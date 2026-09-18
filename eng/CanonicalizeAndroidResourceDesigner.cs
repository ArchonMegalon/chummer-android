using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;

namespace Chummer.Build
{
    // Build-only task. This file is outside the app's Compile glob and must
    // never be shipped in the application. Use Cecil from the admitted SDK.
    public sealed class CanonicalizeAndroidResourceDesigner : Task
    {
        private const string DesignerName = "_Microsoft.Android.Resource.Designer";
        private const long MaximumBytes = 16 * 1024 * 1024;

        [Required]
        public string DesignerFile { get; set; }

        [Required]
        public string IntermediateRoot { get; set; }

        public override bool Execute()
        {
            try
            {
                string root = Path.GetFullPath(IntermediateRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (root.Length == 0)
                    throw new InvalidOperationException("An explicit intermediate directory is required.");
                string file = Path.GetFullPath(DesignerFile);
                string prefix = root + Path.DirectorySeparatorChar;
                StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!file.StartsWith(prefix, comparison)
                    || Path.GetFileName(file) != DesignerName + ".dll")
                    throw new InvalidOperationException("Only the generated resource designer inside obj may be canonicalized.");

                RejectLinks(file, root);
                var info = new FileInfo(file);
                if (info.Length <= 0 || info.Length > MaximumBytes)
                    throw new InvalidOperationException("Generated resource designer has an invalid size.");

                byte[] original = File.ReadAllBytes(file);
                byte[] canonical;
                using (var input = new MemoryStream(original, false))
                using (var assembly = AssemblyDefinition.ReadAssembly(input))
                using (var output = new MemoryStream())
                {
                    if (assembly.Name.Name != DesignerName
                        || assembly.Name.Version != new Version(1, 0, 0, 0)
                        || assembly.Modules.Count != 1
                        || assembly.MainModule.Kind != ModuleKind.Dll
                        || (assembly.MainModule.Attributes & ModuleAttributes.StrongNameSigned) != 0
                        || assembly.MainModule.HasSymbols)
                        throw new InvalidOperationException("Not the SDK's unsigned generated resource designer.");

                    // Retain resource IL, IDs, constants, attributes and public
                    // key. Fix the wall-clock COFF value BEFORE deriving MVID.
                    // Cecil zeros MVID while hashing; the result is idempotent.
                    assembly.Write(output, new WriterParameters
                    {
                        Timestamp = 0,
                        DeterministicMvid = true,
                    });
                    canonical = output.ToArray();
                }

                if (canonical.LongLength > MaximumBytes)
                    throw new InvalidOperationException("Canonical resource designer exceeds its size limit.");
                if (!original.SequenceEqual(canonical))
                {
                    // This private intermediate is not a published artifact.
                    // Any write failure fails the build before it is consumed.
                    File.WriteAllBytes(file, canonical);
                    Log.LogMessage(MessageImportance.Low, "Canonicalized generated resource designer before compilation.");
                }
                return true;
            }
            catch (Exception exception)
            {
                Log.LogError("Resource-designer determinism failed: {0}", exception.Message);
                return false;
            }
        }

        private static void RejectLinks(string file, string root)
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Generated resource designer must not be a link.");
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(file)); directory != null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Generated resource directory must not be a link.");
                if (string.Equals(directory.FullName, root, Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return;
            }
            throw new InvalidOperationException("Generated resource directory is outside its intermediate root.");
        }
    }
}
