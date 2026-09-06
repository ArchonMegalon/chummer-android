using Chummer.Android.Platform;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    private static async Task Main()
    {
        await PersistedBindingNeverContainsPrivateKeyMaterialAsync();
        await SignatureVerifiesAgainstTheBoundPublicKeyAsync();
        await AliasesAreScopedToInstallationIdentityAsync();
        AliasesRequireTheExactBoundedShape();
        await GrantBindingIsExactAsync();
        await LegacyPrivateKeyIsRemovedWithoutBeingReadAsync();
        await MissingKeyRequiresExplicitRelinkingAsync();
        await InvalidatedKeyRequiresExplicitRelinkingAsync();
        await InvalidatedKeyCleanupMustFinishBeforeReplacementAsync();
        await PendingIdentityIsBoundToTheExactInstallationAsync();
        await TamperedBindingCannotRedirectSigningOrDeletionAsync();
        await TamperedPublicKeyFailsClosedAsync();
        await UnsupportedBindingSchemaFailsClosedAsync();
        await HostileSignerCannotReturnUnverifiableProofAsync();
        await HostileSignerCannotReturnNullProofAsync();
        await InterruptedCreationRemovesPartialAuthorityAsync();
        await CancellationAfterPersistentCreationRemovesTheOrphanAsync();
        await FailedPostCreationDeleteRetainsCleanupSelectorAsync();
        await PartialBindingRecoveryRemovesTheOrphanedKeyAsync();
        await UnlinkDeletesKeyAuthorityAndMetadataAsync();
        await UnlinkFailureRemovesMetadataAndSurfacesTheCleanupFailureAsync();
        await SelectorReadCancellationPreservesTheOnlyCleanupRouteAsync();
        await SelectorReadFailurePreservesTheOnlyCleanupRouteAsync();
        await SelectorDeleteCancellationRetainsARecoverableOutcomeAsync();
        Console.WriteLine("Android account-link key authority tests passed: 24");
    }

    private static async Task PersistedBindingNeverContainsPrivateKeyMaterialAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);

        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        await authority.BindGrantAsync(identity.InstallationId, "grant-one");

        Require(
            typeof(IAndroidDeviceKeyStore).GetMembers().All(static member =>
                !member.Name.Contains("Private", StringComparison.OrdinalIgnoreCase)
                && !member.Name.Contains("Export", StringComparison.OrdinalIgnoreCase)),
            "The production signer abstraction must expose neither private keys nor an export operation.");
        Require(
            metadata.Values.All(static value => !value.Contains("private", StringComparison.OrdinalIgnoreCase)),
            "Secure metadata must not contain a private-key field.");
        Require(
            metadata.Values.All(static value => !value.Contains("pkcs8", StringComparison.OrdinalIgnoreCase)),
            "Secure metadata must not contain serialized PKCS#8 material.");
        Require(
            metadata.Values.Any(value => value.Contains(identity.Alias, StringComparison.Ordinal)),
            "The persisted binding must retain the non-secret Keystore alias.");
        using JsonDocument binding = JsonDocument.Parse(metadata.Values.Single(static value => value.StartsWith('{')));
        Require(
            binding.RootElement.GetProperty("publicKey").GetString() == identity.PublicKey,
            "The persisted binding must retain the server-facing public key.");
    }

    private static async Task SignatureVerifiesAgainstTheBoundPublicKeyAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        byte[] payload = "chummer.install-link.remote-callback.v1\nproof"u8.ToArray();

        byte[] signature = await authority.SignAsync(identity, payload);
        using RSA verifier = RSA.Create();
        byte[] publicKey = Convert.FromBase64String(identity.PublicKey);
        verifier.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);

        Require(bytesRead == publicKey.Length, "The bound public key must use the v2 SPKI wire format.");
        Require(
            verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "The Android proof signature must verify with RSA/SHA-256 and PKCS#1 v1.5.");
        CryptographicOperations.ZeroMemory(payload);
        CryptographicOperations.ZeroMemory(signature);
        CryptographicOperations.ZeroMemory(publicKey);
    }

    private static async Task AliasesAreScopedToInstallationIdentityAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity first = await authority.StartOrResumeExplicitLinkAsync();
        await authority.RemoveAsync();
        AndroidAccountLinkKeyIdentity second = await authority.StartOrResumeExplicitLinkAsync();

        Require(first.InstallationId != second.InstallationId, "A fresh link must create a fresh installation identity.");
        Require(first.Alias != second.Alias, "Different installations must never share a Keystore alias.");
        Require(
            first.Alias == AndroidAccountLinkKeyAuthority.AliasForInstallation(first.InstallationId),
            "The first alias must be derived from its installation scope.");
        Require(
            second.Alias == AndroidAccountLinkKeyAuthority.AliasForInstallation(second.InstallationId),
            "The second alias must be derived from its installation scope.");
    }

    private static void AliasesRequireTheExactBoundedShape()
    {
        const string installationId = "android-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        string valid = AndroidAccountLinkKeyAuthority.AliasForInstallation(installationId);
        Require(
            AndroidAccountLinkKeyAuthority.IsExpectedAlias(valid),
            "A derived alias must satisfy the exact Android Keystore scope.");

        foreach (string hostile in new[]
                 {
                     AndroidAccountLinkKeyAuthority.AliasPrefix + new string('a', 63),
                     AndroidAccountLinkKeyAuthority.AliasPrefix + new string('a', 65),
                     AndroidAccountLinkKeyAuthority.AliasPrefix + new string('A', 64),
                     AndroidAccountLinkKeyAuthority.AliasPrefix + new string('g', 64),
                     AndroidAccountLinkKeyAuthority.AliasPrefix + "../" + new string('a', 61)
                 })
        {
            Require(
                !AndroidAccountLinkKeyAuthority.IsExpectedAlias(hostile),
                "A malformed, unbounded, or non-lowercase alias must fail closed.");
        }
    }

    private static async Task GrantBindingIsExactAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();

        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.RequireLinkedIdentityAsync(identity.InstallationId),
            "An unbound pending key must not authorize a linked grant.");
        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.BindGrantAsync("android-foreign", "grant-one"),
            "A server grant for another installation must fail closed.");

        await authority.BindGrantAsync(identity.InstallationId, "grant-one");
        AndroidAccountLinkKeyIdentity linked = await authority.RequireLinkedIdentityAsync(identity.InstallationId);
        Require(linked.GrantId == "grant-one", "The public-key metadata must bind the exact grant identity.");
    }

    private static async Task MissingKeyRequiresExplicitRelinkingAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity original = await authority.StartOrResumeExplicitLinkAsync();
        keys.RemoveWithoutAuthority(original.Alias);

        AndroidDeviceRelinkRequiredException failure = await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.RequirePendingIdentityAsync(original.InstallationId),
            "A missing Keystore key must fail closed.");
        Require(failure.Availability == AndroidDeviceKeyAvailability.Missing, "Missing-key state must remain explicit.");
        Require(keys.CreatedCount == 1, "A readiness check must never regenerate a missing key.");

        AndroidAccountLinkKeyIdentity replacement = await authority.StartOrResumeExplicitLinkAsync();
        Require(keys.CreatedCount == 2, "Only the explicit link entry point may create the replacement key.");
        Require(replacement.InstallationId != original.InstallationId, "Relinking must use a new installation identity.");
    }

    private static async Task LegacyPrivateKeyIsRemovedWithoutBeingReadAsync()
    {
        MemoryMetadataStore metadata = new();
        metadata.SetRaw(
            AndroidAccountLinkKeyAuthority.LegacyPrivateKeyStorageKey,
            "hostile-exported-pkcs8-private-key-material");
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);

        await authority.RemoveLegacyPrivateKeyAsync();

        Require(
            metadata.ReadCount(AndroidAccountLinkKeyAuthority.LegacyPrivateKeyStorageKey) == 0,
            "Legacy exported private-key material must be deleted without reading it into application memory.");
        Require(
            !metadata.Contains(AndroidAccountLinkKeyAuthority.LegacyPrivateKeyStorageKey),
            "Legacy exported private-key material must be removed unconditionally.");
    }

    private static async Task InvalidatedKeyRequiresExplicitRelinkingAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity original = await authority.StartOrResumeExplicitLinkAsync();
        keys.Invalidate(original.Alias);

        AndroidDeviceRelinkRequiredException failure = await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.SignAsync(original, "proof"u8.ToArray()),
            "An invalidated key must never sign.");
        Require(
            failure.Availability == AndroidDeviceKeyAvailability.Invalidated,
            "Invalidation must remain distinguishable from a missing key.");
        Require(keys.CreatedCount == 1, "A failed signature must never rotate identity implicitly.");

        AndroidAccountLinkKeyIdentity replacement = await authority.StartOrResumeExplicitLinkAsync();
        Require(keys.CreatedCount == 2, "Explicit relinking must create one replacement key.");
        Require(replacement.Alias != original.Alias, "An invalidated key alias must not regain authority.");
    }

    private static async Task InvalidatedKeyCleanupMustFinishBeforeReplacementAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity original = await authority.StartOrResumeExplicitLinkAsync();
        keys.Invalidate(original.Alias);
        keys.FailDeleteAlias = original.Alias;

        await RequireThrowsAsync<CryptographicException>(
            () => authority.StartOrResumeExplicitLinkAsync(),
            "A replacement key must not be created while invalidated-alias cleanup is failing.");
        Require(keys.CreatedCount == 1, "Failed invalidation cleanup must not create parallel authority.");
        Require(keys.Contains(original.Alias), "The failed invalidated-key deletion must remain observable.");
        Require(
            metadata.Contains(AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey),
            "Failed invalidation cleanup must retain the exact alias tombstone.");

        keys.FailDeleteAlias = null;
        AndroidAccountLinkKeyIdentity replacement = await authority.StartOrResumeExplicitLinkAsync();

        Require(keys.CreatedCount == 2, "A later explicit retry may create one replacement key.");
        Require(!keys.Contains(original.Alias), "The invalidated alias must be deleted before replacement.");
        Require(replacement.Alias != original.Alias, "The replacement must use fresh installation authority.");
        Require(
            !metadata.Contains(AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey),
            "Successful invalidation recovery must clear its cleanup tombstone.");
    }

    private static async Task PendingIdentityIsBoundToTheExactInstallationAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();

        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.RequirePendingIdentityAsync("android-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            "Pending state from another installation must never receive a device proof.");
        Require(keys.SignCount == 0, "An installation mismatch must fail before Android Keystore signing.");

        AndroidAccountLinkKeyIdentity resumed = await authority.RequirePendingIdentityAsync(identity.InstallationId);
        Require(resumed == identity, "The exact current installation may resume its pending link.");
    }

    private static async Task TamperedBindingCannotRedirectSigningOrDeletionAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        const string foreignInstallationId = "android-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        string foreignAlias = AndroidAccountLinkKeyAuthority.AliasForInstallation(foreignInstallationId);
        await keys.CreateAsync(foreignAlias);
        metadata.SetRaw(
            AndroidAccountLinkKeyAuthority.BindingStorageKey,
            SerializeBinding(identity.InstallationId, foreignAlias, identity.PublicKey, grantId: null));

        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.RequirePendingIdentityAsync(identity.InstallationId),
            "A tampered alias must not redirect signing authority.");
        await authority.RemoveAsync();

        Require(!keys.Contains(identity.Alias), "Recovery must delete the key derived from the stored installation.");
        Require(keys.Contains(foreignAlias), "Hostile metadata must not select an unrelated alias for deletion.");
        await keys.DeleteAsync(foreignAlias);
    }

    private static async Task TamperedPublicKeyFailsClosedAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        const string foreignInstallationId = "android-BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        AndroidDevicePublicKey foreign = await keys.CreateAsync(
            AndroidAccountLinkKeyAuthority.AliasForInstallation(foreignInstallationId));
        metadata.SetRaw(
            AndroidAccountLinkKeyAuthority.BindingStorageKey,
            SerializeBinding(identity.InstallationId, identity.Alias, foreign.PublicKey!, grantId: null));

        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.SignAsync(identity, "proof"u8.ToArray()),
            "A public-key substitution must fail before signing.");
        Require(keys.SignCount == 0, "Tampered public metadata must fail before Android Keystore signing.");
    }

    private static async Task UnsupportedBindingSchemaFailsClosedAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        metadata.SetRaw(
            AndroidAccountLinkKeyAuthority.BindingStorageKey,
            JsonSerializer.Serialize(new
            {
                version = 2,
                identity.InstallationId,
                identity.Alias,
                identity.PublicKey,
                grantId = (string?)null,
                injectedPrivateKey = "must-not-be-accepted"
            }));

        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.RequirePendingIdentityAsync(identity.InstallationId),
            "Unknown binding fields must not be accepted under the current schema version.");
        Require(keys.SignCount == 0, "A hostile binding schema must fail before signing.");
    }

    private static async Task HostileSignerCannotReturnUnverifiableProofAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new() { CorruptSignatures = true };
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();

        AndroidDeviceRelinkRequiredException failure = await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.SignAsync(identity, "proof"u8.ToArray()),
            "The authority must independently verify every device-key signature.");

        Require(
            failure.Availability == AndroidDeviceKeyAvailability.Invalidated,
            "An unverifiable signature must require explicit relinking.");
        Require(keys.SignCount == 1, "The hostile signer fixture must have been exercised exactly once.");
    }

    private static async Task HostileSignerCannotReturnNullProofAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new() { ReturnNullSignature = true };
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();

        AndroidDeviceRelinkRequiredException failure = await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.SignAsync(identity, "proof"u8.ToArray()),
            "The authority must reject a hostile null signer result.");

        Require(
            failure.Availability == AndroidDeviceKeyAvailability.Invalidated,
            "A null signer result must require explicit relinking.");
    }

    private static async Task InterruptedCreationRemovesPartialAuthorityAsync()
    {
        MemoryMetadataStore metadata = new()
        {
            FailNextSetKey = AndroidAccountLinkKeyAuthority.InstallationIdStorageKey
        };
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);

        await RequireThrowsAsync<IOException>(
            () => authority.StartOrResumeExplicitLinkAsync(),
            "A failed metadata commit must fail the link attempt.");

        Require(keys.KeyCount == 0, "A failed metadata commit must delete the newly created key.");
        Require(metadata.Values.Count == 0, "A failed metadata commit must remove every partial binding.");
    }

    private static async Task CancellationAfterPersistentCreationRemovesTheOrphanAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new() { CancelAfterPersistentCreate = true };
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);

        await RequireThrowsAsync<OperationCanceledException>(
            () => authority.StartOrResumeExplicitLinkAsync(),
            "Cancellation reported after persistent generation must fail the link attempt.");

        Require(keys.CreatedCount == 1, "The hostile backend must persist one key before cancellation.");
        Require(keys.KeyCount == 0, "Post-generation cancellation must not orphan a Keystore alias.");
        Require(metadata.Values.Count == 0, "Post-generation cancellation must not leave partial metadata.");
    }

    private static async Task FailedPostCreationDeleteRetainsCleanupSelectorAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new()
        {
            CancelAfterPersistentCreate = true,
            FailAllDeletes = true
        };
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);

        await RequireThrowsAsync<CryptographicException>(
            () => authority.StartOrResumeExplicitLinkAsync(),
            "A failed cleanup after persistent generation must remain visible.");

        Require(keys.KeyCount == 1, "The hostile backend must retain the key whose deletion failed.");
        Require(
            metadata.Contains(AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey),
            "A failed post-creation delete must retain the exact cleanup selector.");

        keys.FailAllDeletes = false;
        await authority.RemoveAsync();
        Require(keys.KeyCount == 0, "A later cleanup attempt must delete the post-creation orphan.");
        Require(metadata.Values.Count == 0, "Successful recovery must remove the cleanup tombstone.");
    }

    private static async Task PartialBindingRecoveryRemovesTheOrphanedKeyAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity original = await authority.StartOrResumeExplicitLinkAsync();
        metadata.RemoveRaw(AndroidAccountLinkKeyAuthority.InstallationIdStorageKey);

        AndroidAccountLinkKeyIdentity replacement = await authority.StartOrResumeExplicitLinkAsync();

        Require(!keys.Contains(original.Alias), "Recovery must delete a key named by a partial binding.");
        Require(replacement.InstallationId != original.InstallationId, "Partial recovery must create a fresh identity.");
        Require(keys.KeyCount == 1, "Only the recovered identity may retain signing authority.");
    }

    private static async Task UnlinkDeletesKeyAuthorityAndMetadataAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        await authority.BindGrantAsync(identity.InstallationId, "grant-one");

        await authority.RemoveAsync();

        Require(!keys.Contains(identity.Alias), "Unlink must delete the non-exportable key entry.");
        Require(metadata.Values.Count == 0, "Unlink must remove the installation and public binding metadata.");
        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.SignAsync(identity, "proof"u8.ToArray()),
            "A removed identity must never retain signing authority.");
    }

    private static async Task UnlinkFailureRemovesMetadataAndSurfacesTheCleanupFailureAsync()
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        await authority.BindGrantAsync(identity.InstallationId, "grant-one");
        metadata.SetRaw(AndroidAccountLinkKeyAuthority.LegacyPrivateKeyStorageKey, "legacy-secret");
        keys.FailDeleteAlias = identity.Alias;

        await RequireThrowsAsync<CryptographicException>(
            () => authority.RemoveAsync(),
            "A Keystore deletion failure must remain visible to the caller.");

        Require(keys.Contains(identity.Alias), "The hostile fixture must retain the key whose deletion failed.");
        Require(
            metadata.Contains(AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey),
            "Failed key deletion must retain a bounded cleanup tombstone.");
        Require(
            metadata.Values.All(static value => !value.Contains("grant-one", StringComparison.Ordinal)),
            "Cleanup failure must invalidate the linked grant immediately.");
        await RequireThrowsAsync<AndroidDeviceRelinkRequiredException>(
            () => authority.RequireLinkedIdentityAsync(identity.InstallationId),
            "A tombstoned identity must not retain linked request authority.");

        keys.FailDeleteAlias = null;
        await authority.RemoveAsync();
        Require(!keys.Contains(identity.Alias), "A later cleanup attempt must delete the retained alias.");
        Require(metadata.Values.Count == 0, "Successful retry must remove the cleanup tombstone.");
    }

    private static async Task SelectorReadCancellationPreservesTheOnlyCleanupRouteAsync()
    {
        foreach (string selector in CleanupSelectorKeys())
        {
            (MemoryMetadataStore metadata, MemoryDeviceKeyStore keys, AndroidAccountLinkKeyAuthority authority,
                AndroidAccountLinkKeyIdentity identity) = await CreateSingleSelectorFixtureAsync(selector);
            metadata.CancelAfterGetKey = selector;

            await RequireThrowsAsync<OperationCanceledException>(
                () => authority.RemoveAsync(),
                $"Cancellation while reading {selector} must abort cleanup.");

            Require(keys.Contains(identity.Alias), "An unread selector must never orphan the surviving key.");
            Require(metadata.Contains(selector), "The only cleanup selector must survive an ambiguous read.");
            metadata.CancelAfterGetKey = null;
            await authority.RemoveAsync();
            Require(keys.KeyCount == 0, "A later readable cleanup must delete the retained key.");
            Require(metadata.Values.Count == 0, "A later readable cleanup must remove its selectors.");
        }
    }

    private static async Task SelectorReadFailurePreservesTheOnlyCleanupRouteAsync()
    {
        foreach (string selector in CleanupSelectorKeys())
        {
            (MemoryMetadataStore metadata, MemoryDeviceKeyStore keys, AndroidAccountLinkKeyAuthority authority,
                AndroidAccountLinkKeyIdentity identity) = await CreateSingleSelectorFixtureAsync(selector);
            metadata.FailAfterGetKey = selector;

            await RequireThrowsAsync<IOException>(
                () => authority.RemoveAsync(),
                $"Failure while reading {selector} must abort cleanup.");

            Require(keys.Contains(identity.Alias), "A failed selector read must retain the surviving key.");
            Require(metadata.Contains(selector), "A failed read must retain the only durable cleanup selector.");
            metadata.FailAfterGetKey = null;
            await authority.RemoveAsync();
            Require(keys.KeyCount == 0, "A later readable cleanup must delete the retained key.");
        }
    }

    private static async Task SelectorDeleteCancellationRetainsARecoverableOutcomeAsync()
    {
        foreach (string selector in CleanupSelectorKeys())
        {
            MemoryMetadataStore metadata = new();
            MemoryDeviceKeyStore keys = new();
            AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
            AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
            await authority.BindGrantAsync(identity.InstallationId, "grant-one");
            if (!string.Equals(
                    selector,
                    AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey,
                    StringComparison.Ordinal))
            {
                keys.FailDeleteAlias = identity.Alias;
            }
            metadata.CancelAfterRemoveKey = selector;

            await RequireThrowsAsync<OperationCanceledException>(
                () => authority.RemoveAsync(),
                $"Cancellation while deleting {selector} must remain visible.");

            bool aliasRemains = keys.Contains(identity.Alias);
            Require(
                !aliasRemains || metadata.Contains(AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey),
                "A canceled selector deletion must retain a tombstone whenever the key remains.");
            metadata.CancelAfterRemoveKey = null;
            keys.FailDeleteAlias = null;
            await authority.RemoveAsync();
            Require(keys.KeyCount == 0, "A retry after selector-delete cancellation must finish key cleanup.");
            Require(metadata.Values.Count == 0, "A retry after selector-delete cancellation must finish metadata cleanup.");
        }
    }

    private static IReadOnlyList<string> CleanupSelectorKeys()
        =>
        [
            AndroidAccountLinkKeyAuthority.InstallationIdStorageKey,
            AndroidAccountLinkKeyAuthority.BindingStorageKey,
            AndroidAccountLinkKeyAuthority.CleanupTombstoneStorageKey
        ];

    private static async Task<(
        MemoryMetadataStore Metadata,
        MemoryDeviceKeyStore Keys,
        AndroidAccountLinkKeyAuthority Authority,
        AndroidAccountLinkKeyIdentity Identity)> CreateSingleSelectorFixtureAsync(string selector)
    {
        MemoryMetadataStore metadata = new();
        MemoryDeviceKeyStore keys = new();
        AndroidAccountLinkKeyAuthority authority = new(keys, metadata);
        AndroidAccountLinkKeyIdentity identity = await authority.StartOrResumeExplicitLinkAsync();
        if (string.Equals(
                selector,
                AndroidAccountLinkKeyAuthority.InstallationIdStorageKey,
                StringComparison.Ordinal))
        {
            metadata.RemoveRaw(AndroidAccountLinkKeyAuthority.BindingStorageKey);
        }
        else if (string.Equals(
                     selector,
                     AndroidAccountLinkKeyAuthority.BindingStorageKey,
                     StringComparison.Ordinal))
        {
            metadata.RemoveRaw(AndroidAccountLinkKeyAuthority.InstallationIdStorageKey);
        }
        else
        {
            keys.FailDeleteAlias = identity.Alias;
            await RequireThrowsAsync<CryptographicException>(
                () => authority.RemoveAsync(),
                "The tombstone-only fixture must observe a failed key deletion.");
            keys.FailDeleteAlias = null;
            Require(metadata.Values.Count == 1, "The fixture must retain only its cleanup tombstone.");
        }

        Require(metadata.Contains(selector), "The hostile fixture must retain its selected cleanup route.");
        return (metadata, keys, authority, identity);
    }

    private static string SerializeBinding(
        string installationId,
        string alias,
        string publicKey,
        string? grantId)
        => JsonSerializer.Serialize(new
        {
            version = 2,
            installationId,
            alias,
            publicKey,
            grantId
        });

    private static async Task<T> RequireThrowsAsync<T>(Func<Task> action, string message)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class MemoryMetadataStore : IAndroidAccountLinkKeyMetadataStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _readCounts = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Values => _values.Values;

        public string? FailNextSetKey { get; set; }

        public string? CancelAfterGetKey { get; set; }

        public string? FailAfterGetKey { get; set; }

        public string? CancelAfterRemoveKey { get; set; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _readCounts[key] = ReadCount(key) + 1;
            string? value = _values.GetValueOrDefault(key);
            if (string.Equals(CancelAfterGetKey, key, StringComparison.Ordinal))
            {
                throw new OperationCanceledException("Injected cancellation after selector read.");
            }
            if (string.Equals(FailAfterGetKey, key, StringComparison.Ordinal))
            {
                throw new IOException("Injected failure after selector read.");
            }
            return Task.FromResult(value);
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(FailNextSetKey, key, StringComparison.Ordinal))
            {
                FailNextSetKey = null;
                throw new IOException("Injected metadata write failure.");
            }
            _values[key] = value;
            return Task.CompletedTask;
        }

        public bool Contains(string key) => _values.ContainsKey(key);

        public int ReadCount(string key) => _readCounts.GetValueOrDefault(key);

        public void SetRaw(string key, string value) => _values[key] = value;

        public void RemoveRaw(string key) => _values.Remove(key);

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            if (string.Equals(CancelAfterRemoveKey, key, StringComparison.Ordinal))
            {
                throw new OperationCanceledException("Injected cancellation after selector removal.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryDeviceKeyStore : IAndroidDeviceKeyStore, IDisposable
    {
        private readonly Dictionary<string, RSA> _keys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _invalidated = new(StringComparer.Ordinal);

        public int CreatedCount { get; private set; }

        public int SignCount { get; private set; }

        public int KeyCount => _keys.Count;

        public bool CorruptSignatures { get; init; }

        public bool ReturnNullSignature { get; init; }

        public bool CancelAfterPersistentCreate { get; init; }

        public string? FailDeleteAlias { get; set; }

        public bool FailAllDeletes { get; set; }

        public Task<AndroidDevicePublicKey> CreateAsync(
            string alias,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RSA key = RSA.Create(2048);
            _keys.Add(alias, key);
            CreatedCount++;
            if (CancelAfterPersistentCreate)
            {
                throw new OperationCanceledException(
                    "Injected cancellation after persistent key generation.",
                    cancellationToken);
            }
            return Task.FromResult(new AndroidDevicePublicKey(
                AndroidDeviceKeyAvailability.Available,
                Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
        }

        public Task<AndroidDevicePublicKey> GetPublicKeyAsync(
            string alias,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_invalidated.Contains(alias))
            {
                return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));
            }

            return Task.FromResult(_keys.TryGetValue(alias, out RSA? key)
                ? new AndroidDevicePublicKey(
                    AndroidDeviceKeyAvailability.Available,
                    Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))
                : new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Missing));
        }

        public Task<byte[]> SignAsync(
            string alias,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_invalidated.Contains(alias))
            {
                throw new AndroidDeviceRelinkRequiredException(
                    AndroidDeviceKeyAvailability.Invalidated,
                    "Test key invalidated.");
            }

            if (!_keys.TryGetValue(alias, out RSA? key))
            {
                throw new AndroidDeviceRelinkRequiredException(
                    AndroidDeviceKeyAvailability.Missing,
                    "Test key missing.");
            }

            SignCount++;
            if (ReturnNullSignature)
            {
                return Task.FromResult<byte[]>(null!);
            }

            byte[] signature = key.SignData(
                payload.Span,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            if (CorruptSignatures)
            {
                signature[0] ^= 0xff;
            }

            return Task.FromResult(signature);
        }

        public Task DeleteAsync(string alias, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailAllDeletes || string.Equals(FailDeleteAlias, alias, StringComparison.Ordinal))
            {
                throw new CryptographicException("Injected Android Keystore deletion failure.");
            }
            if (_keys.Remove(alias, out RSA? key))
            {
                key.Dispose();
            }
            _invalidated.Remove(alias);
            return Task.CompletedTask;
        }

        public bool Contains(string alias) => _keys.ContainsKey(alias);

        public void Invalidate(string alias) => _invalidated.Add(alias);

        public void RemoveWithoutAuthority(string alias)
        {
            if (_keys.Remove(alias, out RSA? key))
            {
                key.Dispose();
            }
            _invalidated.Remove(alias);
        }

        public void Dispose()
        {
            foreach (RSA key in _keys.Values)
            {
                key.Dispose();
            }
            _keys.Clear();
            _invalidated.Clear();
        }
    }
}
