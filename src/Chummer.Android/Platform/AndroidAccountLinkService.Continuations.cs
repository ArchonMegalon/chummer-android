using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Platform;

public sealed partial class AndroidAccountLinkService : IAndroidWorkspaceContinuationTransport
{
    private const int MaximumContinuationBytes = 512 * 1024;
    private const int MaximumContinuationRequestBytes = 768 * 1024;
    private const int MaximumContinuationRows = 200;
    private const string ContinuationUnavailable = "complete-continuation-unavailable";
    private static readonly UTF8Encoding ContinuationUtf8 = new(false, true);
    // Match the existing signed account transport's depth/escaping; the opaque
    // Core envelope keeps PascalCase even inside the camelCase Hub carrier.
    private static readonly JsonSerializerOptions ContinuationWireOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64
    };
    private static readonly JsonSerializerOptions ContinuationProjectionOptions = new(ContinuationWireOptions)
    {
        IgnoreReadOnlyProperties = true
    };

    public async Task<IReadOnlyList<AndroidWorkspaceContinuationItem>> ListContinuationsAsync(
        OwnerContextStamp originalOwner, CancellationToken cancellationToken = default)
    {
        AndroidAccountOwnerState expected = RequireContinuationOwner(originalOwner);
        StoredGrant grant = await ReadContinuationGrantAsync(expected, cancellationToken);
        AndroidAccountLinkRequestAuthority authority = CreateContinuationAuthority(expected, grant, () => { });
        using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
            "/api/v2/install-linking/continuation/workspaces/list",
            new InstallationGrantRequest(grant.InstallationId), authority, cancellationToken);
        RequireContinuationOwnerCurrent(expected);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("The original linked account cannot read continuations.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException("The complete online workspace list is unavailable.");
        JsonElement body = await _httpTransport.ReadJsonAsync<JsonElement>(response, cancellationToken);
        RequireContinuationOwnerCurrent(expected);
        try
        {
            RejectContinuationWireDuplicates(body);
            JsonElement rows = body.GetProperty("snapshots");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > MaximumContinuationRows)
                throw new JsonException();
            List<AndroidWorkspaceContinuationItem> items = new(rows.GetArrayLength());
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                AndroidWorkspaceContinuationItem item = ReadContinuationRow(row, originalOwner.Owner.NormalizedValue);
                if (!ids.Add(item.Character.WorkspaceId)) throw new JsonException();
                items.Add(item);
            }
            RequireContinuationOwnerCurrent(expected);
            return items.OrderByDescending(item => item.Character.UpdatedAtUtc).ToArray();
        }
        catch (Exception error) when (IsContinuationDataError(error))
        {
            // Do not echo a hostile/private payload or partially return a list.
            throw new InvalidDataException("The complete online workspace list is invalid or exceeds its bounds.");
        }
    }

    public async Task<AndroidWorkspaceContinuationWriteResult> UpsertContinuationAsync(
        OwnerContextStamp originalOwner, WorkspaceContinuationExport continuation,
        long expectedRemoteRevision, string? expectedServerToken,
        CancellationToken cancellationToken = default)
    {
        bool proofReleased = false;
        bool knownRejected = false;
        AndroidAccountOwnerState? expected = null;
        try
        {
            expected = RequireContinuationOwner(originalOwner);
            if (expectedRemoteRevision < 0 || expectedRemoteRevision == long.MaxValue
                || (expectedServerToken is not null && !IsContinuationToken(expectedServerToken))
                || (expectedRemoteRevision > 0 && expectedServerToken is null))
                return new(AndroidWorkspaceContinuationWriteOutcome.Conflict);

            // Capture once before any await. Never reread a caller-owned list or
            // object when constructing the projection, digest or signed body.
            byte[] encoded = WorkspaceContinuationCodec.Encode(continuation, MaximumContinuationBytes);
            if (!WorkspaceContinuationCodec.TryDecodeCandidate(encoded, MaximumContinuationBytes, out var captured)
                || captured.Snapshot.OwnerId != originalOwner.Owner.NormalizedValue)
                return new(AndroidWorkspaceContinuationWriteOutcome.Unavailable);
            using JsonDocument envelope = JsonDocument.Parse(encoded, new JsonDocumentOptions { MaxDepth = 128 });
            WorkspaceDocumentSnapshot workspace = captured.Snapshot.Workspace;
            if (workspace.LastUpdatedUtc == default)
                return new(AndroidWorkspaceContinuationWriteOutcome.Unavailable);

            StoredGrant grant = await ReadContinuationGrantAsync(expected, cancellationToken);
            JsonElement request = CaptureContinuationRequest(new
            {
                installationId = grant.InstallationId,
                workspaceId = workspace.Id.Value,
                rulesetId = workspace.Document.RulesetId,
                format = workspace.Document.Format.ToString(),
                schemaVersion = workspace.Document.SchemaVersion,
                payloadKind = workspace.Document.PayloadKind,
                payload = workspace.Document.Content,
                updatedAtUtc = workspace.LastUpdatedUtc,
                originInstallationId = grant.InstallationId,
                // Display summaries are not continuation authority. No XML/rule
                // projection is reconstructed in this transport-only adapter.
                name = (string?)null, alias = (string?)null, metatype = (string?)null,
                buildMethod = (string?)null, createdVersion = (string?)null, appVersion = (string?)null,
                karma = 0m, nuyen = 0m, created = false,
                expectedRemoteRevision,
                expectedServerToken,
                workspaceSnapshot = (JsonElement?)null,
                workspaceSnapshotDigest = (string?)null,
                workspaceContinuation = envelope.RootElement,
                workspaceContinuationDigest = captured.SnapshotDigest
            });
            AndroidAccountLinkRequestAuthority authority = CreateContinuationAuthority(expected, grant,
                () => proofReleased = true);
            using HttpResponseMessage response = await _httpTransport.PostJsonAsync(
                "/api/v2/install-linking/continuation/workspaces/upsert", request, authority, cancellationToken);
            knownRejected = (int)response.StatusCode is >= 400 and < 500;
            RequireContinuationOwnerCurrent(expected);
            // Deliberately do not use SendLinkedAsync: 409 is a data CAS conflict,
            // not account revocation, and must never clear or rotate credentials.
            if (response.StatusCode == HttpStatusCode.Conflict || (int)response.StatusCode == 428)
                return new(AndroidWorkspaceContinuationWriteOutcome.Conflict);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(AndroidWorkspaceContinuationWriteOutcome.Unauthorized);
            if (!response.IsSuccessStatusCode)
                return new(AndroidWorkspaceContinuationWriteOutcome.Unavailable,
                    UnknownRemoteOutcome: !knownRejected);

            JsonElement body = await _httpTransport.ReadJsonAsync<JsonElement>(response, cancellationToken);
            RequireContinuationOwnerCurrent(expected);
            RejectContinuationWireDuplicates(body);
            AndroidWorkspaceContinuationItem stored = ReadContinuationRow(body.GetProperty("snapshot"),
                originalOwner.Owner.NormalizedValue);
            if (stored.Continuation is null || stored.Continuation.SnapshotDigest != captured.SnapshotDigest
                || stored.Character.WorkspaceId != workspace.Id.Value)
                throw new JsonException();
            AndroidWorkspaceContinuationWriteOutcome outcome;
            if (stored.RemoteRevision == expectedRemoteRevision && stored.ServerToken == expectedServerToken)
                outcome = AndroidWorkspaceContinuationWriteOutcome.AlreadyCurrent;
            else if (stored.RemoteRevision == expectedRemoteRevision + 1 && stored.ServerToken != expectedServerToken)
                outcome = AndroidWorkspaceContinuationWriteOutcome.Applied;
            else
                throw new JsonException();
            RequireContinuationOwnerCurrent(expected);
            return new(outcome, stored.RemoteRevision, stored.ServerToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(AndroidWorkspaceContinuationWriteOutcome.Unauthorized,
                UnknownRemoteOutcome: proofReleased && !knownRejected);
        }
        catch (Exception error) when (IsContinuationDataError(error) || error is HttpRequestException
            or IOException or OperationCanceledException or CryptographicException)
        {
            // Signing/dispatch may already have happened. This result is not a
            // retry grant; the caller must list and review before a new mutation.
            return new(expected is not null && !ReferenceEquals(OwnerAuthority.Capture(), expected)
                    ? AndroidWorkspaceContinuationWriteOutcome.Unauthorized
                    : AndroidWorkspaceContinuationWriteOutcome.Unavailable,
                UnknownRemoteOutcome: proofReleased && !knownRejected);
        }
    }

    private AndroidAccountOwnerState RequireContinuationOwner(OwnerContextStamp stamp)
    {
        AndroidAccountOwnerState? current = OwnerAuthority.Capture();
        if (!stamp.IsValid || current is null || current.DeviceLocal
            || !AndroidAccountOwnerKey.TryCreate(current.SubjectId, out string ownerKey)
            || stamp != new OwnerContextStamp(new OwnerScope(ownerKey), current.Issuer, current.Revision))
            throw new UnauthorizedAccessException("The original linked account owner is no longer current.");
        return current;
    }

    private void RequireContinuationOwnerCurrent(AndroidAccountOwnerState expected)
    {
        if (!ReferenceEquals(OwnerAuthority.Capture(), expected))
            throw new UnauthorizedAccessException("The original linked account owner is no longer current.");
    }

    private async Task<StoredGrant> ReadContinuationGrantAsync(AndroidAccountOwnerState expected,
        CancellationToken cancellationToken)
    {
        RequireContinuationOwnerCurrent(expected);
        // ReadStoredGrantAsync owns the asynchronous credential writer gate.
        // Never retain the synchronous, non-reentrant Core owner lease here.
        StoredGrant? grant = await ReadStoredGrantAsync(cancellationToken);
        RequireContinuationOwnerCurrent(expected);
        if (grant is null || grant.InstallationId != expected.InstallationId
            || grant.GrantId != expected.GrantId || grant.SubjectId != expected.SubjectId)
            throw new UnauthorizedAccessException("The original linked account credentials are unavailable.");
        cancellationToken.ThrowIfCancellationRequested();
        return grant;
    }

    private AndroidAccountLinkRequestAuthority CreateContinuationAuthority(AndroidAccountOwnerState expected,
        StoredGrant grant, Action releaseProof)
        => new(grant.InstallationId, grant.GrantId, grant.AccessToken,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), async (canonical, token) =>
            {
                RequireContinuationOwnerCurrent(expected);
                byte[] signature = await _keyAuthority.SignAsync(grant.Identity, canonical, token);
                try
                {
                    RequireContinuationOwnerCurrent(expected);
                    token.ThrowIfCancellationRequested();
                    releaseProof();
                    return signature;
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(signature);
                    throw;
                }
            });

    private static AndroidWorkspaceContinuationItem ReadContinuationRow(JsonElement row, string ownerId)
    {
        string id = ContinuationString(row, "workspaceId");
        string ruleset = ContinuationString(row, "rulesetId");
        string format = ContinuationString(row, "format");
        string payload = ContinuationString(row, "payload");
        DateTimeOffset updated = row.GetProperty("updatedAtUtc").GetDateTimeOffset();
        if (id.Length is 0 or > 128 || ruleset.Length is 0 or > 64 || format.Length is 0 or > 32
            || payload.Length > MaximumContinuationBytes || updated == default)
            throw new JsonException();
        JsonElement summary = row.TryGetProperty("summary", out var summaryValue) ? summaryValue : default;
        AndroidOnlineCharacter character = new(id, ruleset, format, payload, updated,
            ContinuationSummary(summary, "name"), ContinuationSummary(summary, "alias"),
            ContinuationSummary(summary, "metatype"));
        JsonElement? full = ContinuationOptional(row, "workspaceContinuation");
        JsonElement? digest = ContinuationOptional(row, "workspaceContinuationDigest");
        if (full is null && digest is null)
            return new(character, null, null, null, ContinuationUnavailable);
        if (full is not { ValueKind: JsonValueKind.Object } envelope || digest is null)
            throw new JsonException();
        string raw = envelope.GetRawText();
        long combinedBytes = ContinuationUtf8.GetByteCount(raw) + (long)ContinuationUtf8.GetByteCount(payload);
        if (ContinuationOptional(row, "workspaceSnapshot") is { } legacy)
            combinedBytes += ContinuationUtf8.GetByteCount(legacy.GetRawText());
        if (ContinuationUtf8.GetByteCount(raw) > MaximumContinuationBytes
            || combinedBytes > MaximumContinuationRequestBytes
            || !WorkspaceContinuationCodec.TryDecodeCandidate(ContinuationUtf8.GetBytes(raw), MaximumContinuationBytes,
                out var candidate)
            || candidate.Snapshot.OwnerId != ownerId || candidate.SnapshotDigest != digest.Value.GetString())
            throw new JsonException();
        WorkspaceDocumentSnapshot workspace = candidate.Snapshot.Workspace;
        if (workspace.Id.Value != id || workspace.Document.RulesetId != ruleset
            || !Enum.TryParse(format, out WorkspaceDocumentFormat parsedFormat) || workspace.Document.Format != parsedFormat
            || workspace.Document.SchemaVersion != row.GetProperty("schemaVersion").GetInt32()
            || workspace.Document.PayloadKind != ContinuationString(row, "payloadKind")
            || workspace.Document.Content != payload || !workspace.LastUpdatedUtc.EqualsExact(updated))
            throw new JsonException();
        long revision = row.GetProperty("remoteRevision").GetInt64();
        string serverToken = ContinuationString(row, "serverToken");
        if (revision <= 0 || !IsContinuationToken(serverToken)) throw new JsonException();
        ValidateContinuationLegacyProjection(row, workspace);
        return new(character, candidate, revision, serverToken);
    }

    private static void ValidateContinuationLegacyProjection(JsonElement row, WorkspaceDocumentSnapshot workspace)
    {
        JsonElement? projection = ContinuationOptional(row, "workspaceSnapshot");
        JsonElement? digest = ContinuationOptional(row, "workspaceSnapshotDigest");
        if (projection is null && digest is null) return;
        // Compare Core's actual state, including both revisions and all auxiliary
        // history. This compatibility projection is never decoded into shadow DTOs.
        if (projection is null || digest is null
            || ContinuationUtf8.GetByteCount(projection.Value.GetRawText()) > MaximumContinuationBytes
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(workspace, ContinuationProjectionOptions), projection.Value)
            || digest.Value.GetString() != ComputeContinuationLegacyProjectionDigest(projection.Value))
            throw new JsonException();
    }

    private static string ComputeContinuationLegacyProjectionDigest(JsonElement projection)
    {
        // Existing Hub public-projection transport identity, not a rule validator.
        using ContinuationWriteBuffer buffer = new(MaximumContinuationBytes);
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", "canonical-core-workspace-snapshot-json-sha256-v1");
            writer.WritePropertyName("snapshot");
            WriteContinuationCanonicalProjection(projection, writer);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void WriteContinuationCanonicalProjection(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteContinuationCanonicalProjection(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in value.EnumerateArray()) WriteContinuationCanonicalProjection(item, writer);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    private static JsonElement CaptureContinuationRequest(object payload)
    {
        using ContinuationWriteBuffer buffer = new(MaximumContinuationRequestBytes);
        JsonSerializer.Serialize(buffer, payload, ContinuationWireOptions);
        buffer.Position = 0;
        using JsonDocument document = JsonDocument.Parse(buffer, new JsonDocumentOptions { MaxDepth = 64 });
        return document.RootElement.Clone();
    }

    private static void RejectContinuationWireDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectContinuationWireDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) RejectContinuationWireDuplicates(item);
    }

    private static string ContinuationString(JsonElement value, string name)
        => value.GetProperty(name).GetString() ?? throw new JsonException();

    private static JsonElement? ContinuationOptional(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null ? property : null;

    private static string ContinuationSummary(JsonElement summary, string name)
    {
        if (summary.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return string.Empty;
        string value = ContinuationOptional(summary, name)?.GetString() ?? string.Empty;
        return value.Length <= 256 ? value : throw new JsonException();
    }

    private static bool IsContinuationToken(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsContinuationDataError(Exception error) => error is JsonException
        or InvalidOperationException or ArgumentException or FormatException or OverflowException
        or KeyNotFoundException or NotSupportedException;

    private sealed class ContinuationWriteBuffer(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Position + count > maximumBytes) throw new JsonException("Continuation exceeds its byte bound.");
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Position + buffer.Length > maximumBytes) throw new JsonException("Continuation exceeds its byte bound.");
            base.Write(buffer);
        }
    }
}
