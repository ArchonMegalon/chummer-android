using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

/// <summary>
/// Phone catalog projection. The binding and story identity come from the reviewed Presentation
/// contracts; the Android host adds only the list-card signal count and the already-resolved
/// viewer/owner relationship needed to prevent a self-vote.
/// Hub follow-up contract: the public catalog response must supply, per immutable publication,
/// publicationLanguage { languageEditionId, languageTag, displayName, rulesetId, sourceId,
/// sourceDigest } and one-or-more archetypes { archetypeId, displayName, rulesetId, sourceId,
/// sourceDigest }. Query filters must use the stable IDs and return the applied filter binding.
/// Until that reviewed Hub contract exists, the default port below remains unavailable.
/// </summary>
public sealed record ShadowArchivePublicStoryCardViewModel(
    string Title,
    string Summary,
    ShadowArchiveStoryIdentityViewModel Identity,
    int SignalCount,
    bool ViewerIsOwner,
    ShadowArchiveCatalogMetadataViewModel Metadata,
    ShadowArchiveBindingViewModel Binding);

public sealed record ShadowArchivePublicCatalogViewModel(
    IReadOnlyList<ShadowArchivePublicStoryCardViewModel> Stories,
    ShadowArchiveViewerContext Viewer);

public sealed record ShadowArchiveCatalogFilterSelection(
    string? LanguageEditionId,
    string? ArchetypeId);

public sealed record ShadowArchiveCatalogFilterOption(
    string Id,
    string DisplayName);

public sealed record ShadowArchiveFilteredCatalogViewModel(
    IReadOnlyList<ShadowArchivePublicStoryCardViewModel> Stories,
    IReadOnlyList<ShadowArchiveCatalogFilterOption> LanguageEditions,
    IReadOnlyList<ShadowArchiveCatalogFilterOption> Archetypes,
    ShadowArchiveCatalogFilterSelection Selection);

/// <summary>
/// Pure catalog filtering over exact Hub-projected facet identities. It fails closed on missing,
/// cross-edition, duplicate, or unbound metadata and never classifies prose with a model.
/// </summary>
public static class ShadowArchiveCatalogFilterPolicy
{
    public static ShadowArchiveFilteredCatalogViewModel Project(
        ShadowArchivePublicCatalogViewModel catalog,
        ShadowArchiveCatalogFilterSelection selection)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ShadowArchivePublicStoryCardViewModel[] stories = catalog.Stories?.ToArray()
            ?? throw new InvalidOperationException("Stories catalog rows are required.");
        foreach (ShadowArchivePublicStoryCardViewModel story in stories)
            ValidateMetadata(story);

        ShadowArchiveCatalogFilterOption[] languages = stories
            .Select(static story => story.Metadata.PublicationLanguage)
            .GroupBy(static language => language.LanguageEditionId, StringComparer.Ordinal)
            .Select(group =>
            {
                ShadowArchivePublicationLanguageEditionViewModel[] values = group.ToArray();
                if (values.Select(static value => (
                        value.LanguageTag,
                        value.DisplayName,
                        value.RulesetId,
                        value.SourceId,
                        value.SourceDigest))
                    .Distinct().Count() != 1)
                {
                    throw new InvalidOperationException(
                        "A language edition ID resolved to conflicting source metadata.");
                }
                return new ShadowArchiveCatalogFilterOption(group.Key, values[0].DisplayName);
            })
            .OrderBy(static option => option.DisplayName, StringComparer.Ordinal)
            .ThenBy(static option => option.Id, StringComparer.Ordinal)
            .ToArray();

        ShadowArchiveCatalogFilterOption[] archetypes = stories
            .SelectMany(static story => story.Metadata.Archetypes)
            .GroupBy(static archetype => archetype.ArchetypeId, StringComparer.Ordinal)
            .Select(group =>
            {
                ShadowArchiveEditionArchetypeViewModel[] values = group.ToArray();
                if (values.Select(static value => (
                        value.DisplayName,
                        value.RulesetId,
                        value.SourceId,
                        value.SourceDigest))
                    .Distinct().Count() != 1)
                {
                    throw new InvalidOperationException(
                        "An archetype ID resolved to conflicting source metadata.");
                }
                return new ShadowArchiveCatalogFilterOption(group.Key, values[0].DisplayName);
            })
            .OrderBy(static option => option.DisplayName, StringComparer.Ordinal)
            .ThenBy(static option => option.Id, StringComparer.Ordinal)
            .ToArray();

        string? languageId = ExactOptionalId(selection.LanguageEditionId);
        string? archetypeId = ExactOptionalId(selection.ArchetypeId);
        if (languageId is not null && languages.All(option => !string.Equals(option.Id, languageId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The selected publication language edition is unavailable.");
        if (archetypeId is not null && archetypes.All(option => !string.Equals(option.Id, archetypeId, StringComparison.Ordinal)))
            throw new InvalidOperationException("The selected edition-bound archetype is unavailable.");

        ShadowArchivePublicStoryCardViewModel[] filtered = stories
            .Where(story => languageId is null || string.Equals(
                story.Metadata.PublicationLanguage.LanguageEditionId,
                languageId,
                StringComparison.Ordinal))
            .Where(story => archetypeId is null || story.Metadata.Archetypes.Any(archetype =>
                string.Equals(archetype.ArchetypeId, archetypeId, StringComparison.Ordinal)))
            .ToArray();
        return new(filtered, languages, archetypes, new(languageId, archetypeId));
    }

    private static void ValidateMetadata(ShadowArchivePublicStoryCardViewModel story)
    {
        ArgumentNullException.ThrowIfNull(story);
        ShadowArchiveCatalogMetadataViewModel metadata = story.Metadata
            ?? throw new InvalidOperationException("A public story has no authoritative catalog metadata.");
        ShadowArchivePublicationLanguageEditionViewModel language = metadata.PublicationLanguage
            ?? throw new InvalidOperationException("A public story has no publication language edition.");
        if (!Exact(language.LanguageEditionId)
            || !Exact(language.LanguageTag)
            || !Exact(language.DisplayName)
            || !Exact(language.RulesetId)
            || !Exact(language.SourceId)
            || !IsDigest(language.SourceDigest)
            || metadata.Archetypes is null
            || metadata.Archetypes.Count == 0
            || metadata.Archetypes.Select(static value => value.ArchetypeId)
                .Distinct(StringComparer.Ordinal).Count() != metadata.Archetypes.Count)
        {
            throw new InvalidOperationException("A public story has invalid source-bound catalog metadata.");
        }

        foreach (ShadowArchiveEditionArchetypeViewModel archetype in metadata.Archetypes)
        {
            if (archetype is null
                || !Exact(archetype.ArchetypeId)
                || !Exact(archetype.DisplayName)
                || !Exact(archetype.RulesetId)
                || !string.Equals(archetype.RulesetId, language.RulesetId, StringComparison.Ordinal)
                || !Exact(archetype.SourceId)
                || !IsDigest(archetype.SourceDigest))
            {
                throw new InvalidOperationException(
                    "A public story archetype is not bound to its rules edition and source.");
            }
        }
    }

    private static string? ExactOptionalId(string? value)
        => value is null ? null : Exact(value)
            ? value
            : throw new InvalidOperationException("Catalog filter IDs must be exact.");

    private static bool Exact(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool IsDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        ReadOnlySpan<char> digest = value.AsSpan().Trim();
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            digest = digest[7..];
        return digest.Length == 64 && digest.ToString().All(static character => Uri.IsHexDigit(character));
    }
}

/// <summary>
/// Narrow Android host seam for the one missing Presentation capability: public story discovery.
/// It is not a second reader/community/signal client. Those flows use
/// <see cref="ShadowArchivePresenter"/> and <see cref="IShadowArchivePresentationClient"/> exactly.
/// Presentation 6ee4a7f5f has no public-list query contract yet.
/// </summary>
public interface IShadowArchivePublicCatalogPort
{
    Task<ShadowArchivePresentationResult<ShadowArchivePublicCatalogViewModel>> LoadPublicStoriesAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Honest default until the Hub catalog and viewer composition are supplied. It never fabricates
/// a story, count, reader payload, login, vote, leaderboard, reward, or provider response.
/// </summary>
public sealed class UnavailableShadowArchivePublicCatalogPort : IShadowArchivePublicCatalogPort
{
    public Task<ShadowArchivePresentationResult<ShadowArchivePublicCatalogViewModel>> LoadPublicStoriesAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(Unavailable<ShadowArchivePublicCatalogViewModel>());

    private static ShadowArchivePresentationResult<T> Unavailable<T>()
        => new(ShadowArchivePresentationState.Unavailable, default, Error());

    private static ShadowArchiveErrorViewModel Error()
        => new(
            "shadow_archive_android_composition_unavailable",
            "Stories is unavailable in this build. No public story or Signal response was assumed.",
            null,
            null,
            null,
            null,
            null);
}

/// <summary>
/// Exact Presentation transport contract, failed closed until Android receives a reviewed Hub
/// adapter. Returning Unavailable is transport truth; no story, vote, or community payload is
/// synthesized.
/// </summary>
public sealed class UnavailableShadowArchivePresentationClient : IShadowArchivePresentationClient
{
    public Task<ShadowArchiveClientResult<ShadowArchivePublicationPreviewContract>> GetPublicationPreviewAsync(
        ShadowArchivePublicationPreviewQuery query,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchivePublicationPreviewContract>());

    public Task<ShadowArchiveClientResult<ShadowArchivePublicReaderContract>> GetPublicReaderAsync(
        ShadowArchivePublicReaderQuery query,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchivePublicReaderContract>());

    public Task<ShadowArchiveClientResult<ShadowArchiveCommunityStatusContract>> GetCommunityStatusAsync(
        ShadowArchiveCommunityQuery query,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchiveCommunityStatusContract>());

    public Task<ShadowArchiveClientResult<ShadowArchiveCommunityStatusContract>> MutateSignalAsync(
        ShadowArchiveSignalMutation mutation,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchiveCommunityStatusContract>());

    private static ShadowArchiveClientResult<T> Unavailable<T>()
        => new(
            ShadowArchiveClientResultKind.Unavailable,
            ErrorCode: "shadow_archive_android_transport_unavailable",
            SafeMessage: "Stories is unavailable in this build. No public response or Signal change was assumed.");
}
