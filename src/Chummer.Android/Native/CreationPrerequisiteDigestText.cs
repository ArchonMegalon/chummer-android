using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal static class CreationPrerequisiteDigestText
{
    private const string Sha256Prefix = "sha256:";
    private const int DisplayHexLength = 12;

    public static string CanonicalPrefix(string? digest)
    {
        if (!CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(digest))
            return "unavailable";

        return digest![Sha256Prefix.Length..(Sha256Prefix.Length + DisplayHexLength)];
    }
}
