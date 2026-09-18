"""Compile a tiny project graph with the real SDK; never build/sign an Android package."""

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]
ROUTING = REPO / "eng/ReleaseRestoreRouting.props"
SOURCE_LINK_KIND = "cc110556-a091-4d38-9fec-25ab9a351a6a"

# Use the SDK's metadata reader on actual emitted PE/PDB files, not a handwritten
# PDB decoder or source-text assertions. The inspector is itself the tiny app.
INSPECTOR = r'''
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

GC.KeepAlive(ProbeDependency.Value);
var results = new List<object>();
for (int index = 0; index < args.Length; index += 2)
{
    using var peStream = File.OpenRead(args[index]);
    using var pe = new PEReader(peStream);
    var entry = pe.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.CodeView);
    var cv = pe.ReadCodeViewDebugDirectoryData(entry);
    using var pdbStream = File.OpenRead(args[index + 1]);
    using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
    var reader = provider.GetMetadataReader();
    var documents = reader.Documents.Select(h => reader.GetString(reader.GetDocument(h).Name)).ToArray();
    var sourceLinks = reader.CustomDebugInformation
        .Select(h => reader.GetCustomDebugInformation(h))
        .Where(info => reader.GetGuid(info.Kind) == new Guid("cc110556-a091-4d38-9fec-25ab9a351a6a"))
        .Select(info => Encoding.UTF8.GetString(reader.GetBlobBytes(info.Value))).ToArray();
    results.Add(new { codeViewPath = cv.Path, guid = cv.Guid, age = cv.Age,
                      documents, sourceLinks });
}
Console.WriteLine(JsonSerializer.Serialize(results));
'''

GENERATED = '''
internal static class GeneratedProbe
{
    internal static string Source => Capture();
    private static string Capture([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
'''


class ReleaseReproducibilityMsbuildTests(unittest.TestCase):
    def test_real_project_graph_has_identical_managed_outputs_across_source_and_scratch_roots(self):
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")
        with tempfile.TemporaryDirectory(prefix="release SDK reproducibility ") as temporary:
            root = Path(temporary)
            home = root / "cli"
            home.mkdir()
            environment = {
                "PATH": os.pathsep.join((str(Path(dotnet).parent), "/usr/bin", "/bin")),
                "HOME": str(home), "DOTNET_CLI_HOME": str(home), "TMPDIR": str(root),
                "LANG": "C.UTF-8", "DOTNET_NOLOGO": "1", "DOTNET_PROCESSOR_COUNT": "1",
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
                "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
                "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "MSBUILDDISABLENODEREUSE": "1",
                "GIT_CONFIG_NOSYSTEM": "1", "GIT_CONFIG_GLOBAL": "/dev/null",
                "GIT_TERMINAL_PROMPT": "0", "GIT_AUTHOR_DATE": "2000-01-01T00:00:00Z",
                "GIT_COMMITTER_DATE": "2000-01-01T00:00:00Z",
            }

            def run(*arguments, cwd=root):
                result = subprocess.run(arguments, cwd=cwd, env=environment, stdin=subprocess.DEVNULL,
                                        capture_output=True, text=True, timeout=90, check=False)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                return result.stdout.strip()

            first = root / "producer checkout"
            first.mkdir()
            (first / ".gitignore").write_text("bin/\nobj/\n", encoding="utf-8")
            (first / "NuGet.Config").write_text(
                '<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8")
            (first / "Directory.Build.props").write_text(
                '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                '<ImplicitUsings>enable</ImplicitUsings><DebugType>portable</DebugType>'
                '</PropertyGroup></Project>', encoding="utf-8")
            (first / "Directory.Build.targets").write_text(
                '<Project><ItemGroup><Compile Include="$(BaseIntermediateOutputPath)ProbeGenerated.cs" />'
                '</ItemGroup></Project>', encoding="utf-8")
            names = ("Probe.App", "Probe.Dependency")
            for name in names:
                directory = first / name
                directory.mkdir()
                body = ('<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>'
                        '<ItemGroup><ProjectReference Include="../Probe.Dependency/Probe.Dependency.csproj" />'
                        '</ItemGroup>') if name == "Probe.App" else ""
                (directory / (name + ".csproj")).write_text(
                    '<Project Sdk="Microsoft.NET.Sdk">' + body + '</Project>', encoding="utf-8")
                source = INSPECTOR if name == "Probe.App" else 'public static class ProbeDependency { public const int Value = 7; }'
                (directory / "Program.cs").write_text(source, encoding="utf-8")
            run("git", "init", "-q", str(first))
            run("git", "-C", str(first), "remote", "add", "origin",
                "https://github.com/chummer-tests/release-repro.git")
            run("git", "-C", str(first), "add", ".")
            run("git", "-C", str(first), "-c", "user.name=Release Repro Probe",
                "-c", "user.email=repro@example.invalid", "commit", "-qm", "fixed probe source")
            commit = run("git", "-C", str(first), "rev-parse", "HEAD")
            second = root / "rebuilder checkout"
            shutil.copytree(first, second)
            results = []
            for source, scratch_name in ((first, "release.PRODUCER"), (second, "release.REBUILDER")):
                # Scratch is outside both Git roots, so CI's SourceRoot mapping
                # cannot hide a missing release PathMap for generated files/PDBs.
                scratch = root / scratch_name
                intermediate = scratch / "intermediate"
                for name in names:
                    directory = intermediate / name
                    directory.mkdir(parents=True)
                    (directory / "ProbeGenerated.cs").write_text(GENERATED, encoding="utf-8")
                properties = [
                    "-p:Configuration=Release", "-p:UseSharedCompilation=false", "-p:BuildInParallel=false",
                    "-p:Deterministic=true", "-p:ContinuousIntegrationBuild=true",
                    "-p:AndroidAotAdditionalArguments=deterministic",
                    "-p:CustomBeforeDirectoryBuildProps=" + str(ROUTING),
                    "-p:ChummerReleaseIntermediateRoot=" + str(intermediate),
                    "-p:ChummerReleaseDeterministicRoot=" + str(scratch),
                    "-p:RestorePackagesPath=" + str(scratch / "packages"), "-p:NuGetAudit=false",
                ]
                project = source / "Probe.App/Probe.App.csproj"
                run(dotnet, "restore", str(project), "--configfile", str(source / "NuGet.Config"),
                    "--disable-parallel", "--verbosity", "quiet", "-m:1", *properties, cwd=source)
                # One app build traverses ProjectReference, exercising global
                # property propagation to the dependency as the release does.
                run(dotnet, "build", str(project), "--no-restore", "--disable-build-servers",
                    "--verbosity", "quiet", "-m:1", *properties, cwd=source)
                outputs = [source / name / "bin/Release/net10.0" / name for name in names]
                metadata = json.loads(run(dotnet, str(outputs[0]) + ".dll",
                                         *[str(path) + suffix for path in outputs for suffix in (".dll", ".pdb")]))
                observed = []
                for name, output, info in zip(names, outputs, metadata):
                    dll, pdb = (Path(str(output) + suffix).read_bytes() for suffix in (".dll", ".pdb"))
                    self.assertEqual(f"/_/release/intermediate/{name}/Release/net10.0/{name}.pdb", info["codeViewPath"])
                    self.assertIn(f"/_/release/intermediate/{name}/ProbeGenerated.cs", info["documents"])
                    self.assertEqual(1, len(info["sourceLinks"]), SOURCE_LINK_KIND)
                    source_link = info["sourceLinks"][0]
                    mappings = json.loads(source_link)["documents"]
                    self.assertTrue(mappings)
                    self.assertTrue(all(f"/{commit}/" in url for url in mappings.values()))
                    self.assertTrue(any(doc.endswith("/Program.cs") and any(
                        doc.startswith(pattern.removesuffix("*")) for pattern in mappings)
                        for doc in info["documents"]))
                    generated_link = intermediate / name / "Release/net10.0" / (name + ".sourcelink.json")
                    self.assertEqual(generated_link.read_text(encoding="utf-8"), source_link)
                    for physical in (source, scratch):
                        self.assertNotIn(str(physical), json.dumps(info))
                        for encoding in ("utf-8", "utf-16le"):
                            self.assertNotIn(str(physical).encode(encoding), dll)
                            self.assertNotIn(str(physical).encode(encoding), pdb)
                    observed.append((dll, pdb, info))
                results.append(observed)
            for name, producer, rebuild in zip(names, *results):
                with self.subTest(project=name):
                    self.assertEqual(producer, rebuild, "DLL, portable PDB, CodeView or SourceLink differs")
                    print(json.dumps({"probe": "release-managed-reproducibility", "project": name,
                                      "dllSha256": hashlib.sha256(producer[0]).hexdigest(),
                                      "pdbSha256": hashlib.sha256(producer[1]).hexdigest(),
                                      "sourceLink": producer[2]["sourceLinks"][0],
                                      "codeViewPath": producer[2]["codeViewPath"], "equal": True}))


if __name__ == "__main__":
    unittest.main()
