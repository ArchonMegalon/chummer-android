"""Real SDK import-order regression; no restore, compile, signing or app execution."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]
ROUTING = REPO / "eng/ReleaseRestoreRouting.props"


class ReleaseRestoreMsbuildTests(unittest.TestCase):
    def test_external_paths_are_captured_early_without_bypassing_directory_props(self):
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")
        with tempfile.TemporaryDirectory(prefix="release SDK routing ") as temporary:
            root = Path(temporary)
            child_home = root / "cli"
            child_home.mkdir()
            environment = {
                "PATH": os.pathsep.join((str(Path(dotnet).parent), "/usr/bin", "/bin")),
                "DOTNET_CLI_HOME": str(child_home),
                "TMPDIR": str(root), "LANG": "C.UTF-8", "DOTNET_NOLOGO": "1",
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
                "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
                "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "MSBUILDDISABLENODEREUSE": "1",
                "DOTNET_PROCESSOR_COUNT": "1",
            }
            (root / "Directory.Build.props").write_text(
                "<Project><PropertyGroup><RoutingProbeDirectoryImported>true</RoutingProbeDirectoryImported>"
                "</PropertyGroup></Project>", encoding="utf-8")
            properties = ("BaseIntermediateOutputPath,MSBuildProjectExtensionsPath,NuGetLockFilePath,"
                          "RoutingProbeDirectoryImported,_InitialMSBuildProjectExtensionsPath")
            for name in ("Probe.App", "Probe.Dependency"):
                directory = root / name
                directory.mkdir()
                project = directory / (name + ".csproj")
                project.write_text(
                    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    '<TargetFramework>net10.0</TargetFramework></PropertyGroup>'
                    '<Target Name="RoutingProbe" DependsOnTargets="_CheckForInvalidConfigurationAndPlatform" />'
                    '</Project>', encoding="utf-8")
                for hook, routed in (("CustomBeforeMicrosoftCommonProps", True),
                                     ("CustomBeforeDirectoryBuildProps", True),
                                     ("CustomBeforeDirectoryBuildProps", False)):
                    with self.subTest(project=name, hook=hook, routed=routed):
                        command = [dotnet, "msbuild", str(project), "-nologo", "-verbosity:quiet", "-m:1",
                                   "-nodeReuse:false", "-target:RoutingProbe", "-getProperty:" + properties,
                                   "-p:" + hook + "=" + str(ROUTING), "-p:Configuration=Release"]
                        if routed:
                            command.extend(("-p:ChummerReleaseIntermediateRoot=" + str(root / "intermediate"),
                                            "-p:ChummerReleaseLockRoot=" + str(root / "locks")))
                        result = subprocess.run(command, cwd=root, env=environment, stdin=subprocess.DEVNULL,
                                                capture_output=True, text=True, timeout=30, check=False)
                        if hook == "CustomBeforeMicrosoftCommonProps":
                            self.assertNotEqual(0, result.returncode)
                            self.assertIn("MSB3540", result.stdout + result.stderr)
                            continue
                        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                        observed = json.loads(result.stdout)["Properties"]
                        self.assertEqual("true", observed["RoutingProbeDirectoryImported"])
                        if routed:
                            intermediate = str(root / "intermediate" / name) + "/"
                            self.assertEqual(intermediate, observed["BaseIntermediateOutputPath"])
                            self.assertEqual(intermediate, observed["MSBuildProjectExtensionsPath"])
                            self.assertEqual(intermediate, observed["_InitialMSBuildProjectExtensionsPath"])
                            self.assertEqual(str(root / "locks" / (name + ".packages.lock.json")),
                                             observed["NuGetLockFilePath"])
                        else:
                            self.assertEqual(str(directory / "obj") + "/", observed["MSBuildProjectExtensionsPath"])
                            self.assertEqual(observed["MSBuildProjectExtensionsPath"],
                                             observed["_InitialMSBuildProjectExtensionsPath"])
                            self.assertEqual("", observed["NuGetLockFilePath"])
            self.assertFalse(list(root.rglob("project.assets.json")))
            self.assertFalse(list(root.rglob("*.dll")))


if __name__ == "__main__":
    unittest.main()
