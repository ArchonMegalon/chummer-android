"""Real admitted-SDK probes, not a full APK/AAB or release-eligibility claim."""

import hashlib
import os
from pathlib import Path
import shlex
import shutil
import struct
import subprocess
import tempfile
import time
import unittest
from xml.sax.saxutils import escape
import zipfile


REPO = Path(__file__).resolve().parents[1]
TARGETS = REPO / "eng/AndroidDeterministicResources.targets"


class AndroidResourceReproducibilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dotnet = shutil.which("dotnet")
        if not cls.dotnet:
            raise unittest.SkipTest("dotnet is unavailable")
        sdk = Path(cls.dotnet).resolve().parent
        tasks = sdk / "packs/Microsoft.Android.Sdk.Linux/36.1.69/tools"
        reference_packs = sorted((sdk / "packs/Microsoft.NETCore.App.Ref").glob("10.*/ref/net10.0"))
        if not reference_packs:
            raise unittest.SkipTest(".NET 10 reference pack unavailable")
        references = reference_packs[-1]
        android = Path(os.environ.get("ANDROID_SDK_ROOT", os.environ.get("ANDROID_HOME", "/opt/android-sdk")))
        if not android.is_dir():
            android = Path("/usr/local/lib/android/sdk")
        cls.aapt = android / "build-tools/36.0.0/aapt2"
        cls.platform = android / "platforms/android-36/android.jar"
        if not all(path.is_file() for path in (
            tasks / "Xamarin.Android.Build.Tasks.dll", references / "netstandard.dll",
        )):
            raise unittest.SkipTest("exact Android 36.1.69 probe toolchain unavailable")
        cls.tasks, cls.references = tasks, references

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="resource-repro-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.env = {
            "PATH": os.pathsep.join((str(Path(self.dotnet).parent), "/usr/bin", "/bin")),
            "DOTNET_CLI_HOME": str(self.root / "cli"), "TMPDIR": str(self.root),
            "DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "DOTNET_PROCESSOR_COUNT": "1",
            "DOTNET_CLI_USE_MSBUILD_SERVER": "0", "MSBUILDDISABLENODEREUSE": "1",
            "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true", "LANG": "C.UTF-8",
        }

    def run_process(self, *args, success=True):
        result = subprocess.run([str(arg) for arg in args], cwd=self.root, env=self.env,
                                stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=60)
        if success:
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        else:
            self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        return result

    def msbuild(self, project, target, *properties, success=True):
        # The real SDK resolves this property during a target, not evaluation.
        # Direct task re-entry below supplies the already-resolved equivalent.
        resolved = ["-p:MonoAndroidToolsDirectory=" + str(self.tasks)] if target == "ChummerCanonicalizeResourceDesigner" else []
        return self.run_process(self.dotnet, "msbuild", project, "-nologo", "-v:quiet",
                                "-m:1", "-nodeReuse:false", "-target:" + target,
                                *resolved, *properties, success=success)

    def fixture(self, name, value="0x7f010001"):
        source = self.root / name
        source.mkdir()
        (source / "R.txt").write_text(f"int string app_name {value}\nint id probe_button 0x7f020001\n")
        (source / "resources.xml").write_text('<resources><string name="app_name">Probe</string></resources>')
        project = source / "resource.proj"
        project.write_text(f'''<Project>
          <PropertyGroup>
            <Configuration>Release</Configuration><Deterministic>true</Deterministic>
            <AndroidPackageFormats>aab</AndroidPackageFormats>
            <AndroidUseDesignerAssembly>True</AndroidUseDesignerAssembly>
            <IntermediateOutputPath>$(MSBuildThisFileDirectory)obj/</IntermediateOutputPath>
            <_GenerateResourceDesignerAssemblyOutput>$(IntermediateOutputPath)_Microsoft.Android.Resource.Designer.dll</_GenerateResourceDesignerAssemblyOutput>
          </PropertyGroup>
          <UsingTask TaskName="Xamarin.Android.Tasks.GenerateResourceDesignerAssembly"
                     AssemblyFile="{escape(str(self.tasks))}/Xamarin.Android.Build.Tasks.dll" />
          <UsingTask TaskName="ObserveResourceValues" TaskFactory="RoslynCodeTaskFactory"
                     AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
            <ParameterGroup><File ParameterType="System.String" Required="true" />
              <Values ParameterType="System.String" Output="true" />
              <ResourceTypeName ParameterType="System.String" Output="true" /></ParameterGroup>
            <Task><Using Namespace="System" /><Using Namespace="System.Linq" />
              <Code Type="Fragment" Language="cs"><![CDATA[
                var assembly = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(File));
                var resource = assembly.GetTypes().Single(type => type.Name == "Resource");
                var constant = assembly.GetTypes().Single(type => type.Name == "ResourceConstant");
                ResourceTypeName = resource.FullName;
                Values = assembly.FullName + "|" +
                  resource.GetNestedType("String").GetProperty("app_name").GetValue(null) + "|" +
                  resource.GetNestedType("Id").GetProperty("probe_button").GetValue(null) + "|" +
                  constant.GetNestedType("String").GetField("app_name").GetRawConstantValue() + "|" +
                  constant.GetNestedType("Id").GetField("probe_button").GetRawConstantValue();
              ]]></Code></Task>
          </UsingTask>
          <Import Project="{escape(str(TARGETS))}" />
          <Target Name="_UpdateAndroidResgen">
            <PropertyGroup><MonoAndroidToolsDirectory>{escape(str(self.tasks))}</MonoAndroidToolsDirectory></PropertyGroup>
            <MakeDir Directories="$(IntermediateOutputPath)" />
            <GenerateResourceDesignerAssembly RTxtFile="$(MSBuildThisFileDirectory)R.txt"
                IsApplication="true" DesignTimeBuild="false" Deterministic="true"
                OutputFile="$(_GenerateResourceDesignerAssemblyOutput)"
                TargetFrameworkIdentifier="MonoAndroid" TargetFrameworkVersion="v10.0"
                ProjectDir="$(MSBuildThisFileDirectory)" Resources="$(MSBuildThisFileDirectory)resources.xml"
                ResourceDirectory="$(MSBuildThisFileDirectory)" CaseMapFile="$(MSBuildThisFileDirectory)case.map"
                FrameworkDirectories="{escape(str(self.references))}" AssemblyName="Chummer.ResourceProbe" />
            <Copy SourceFiles="$(_GenerateResourceDesignerAssemblyOutput)" DestinationFiles="$(MSBuildThisFileDirectory)raw.dll" />
            <ObserveResourceValues File="$(_GenerateResourceDesignerAssemblyOutput)">
              <Output TaskParameter="Values" PropertyName="BeforeValues" />
              <Output TaskParameter="ResourceTypeName" PropertyName="ResourceTypeName" /></ObserveResourceValues>
            <WriteLinesToFile File="$(MSBuildThisFileDirectory)before.txt" Lines="$(BeforeValues)" Overwrite="true" />
            <WriteLinesToFile File="$(MSBuildThisFileDirectory)resource-type.txt" Lines="$(ResourceTypeName)" Overwrite="true" />
          </Target>
          <Target Name="CoreCompile" DependsOnTargets="_UpdateAndroidResgen">
            <ObserveResourceValues File="$(_GenerateResourceDesignerAssemblyOutput)">
              <Output TaskParameter="Values" PropertyName="AfterValues" /></ObserveResourceValues>
            <WriteLinesToFile File="$(MSBuildThisFileDirectory)after.txt" Lines="$(AfterValues)" Overwrite="true" />
          </Target>
        </Project>''')
        return project

    def test_real_designer_is_equal_across_paths_and_time_with_unchanged_values(self):
        outputs = []
        raw = []
        for name in ("producer", "rebuilder"):
            project = self.fixture(name)
            self.msbuild(project, "CoreCompile")
            directory = project.parent
            self.assertEqual((directory / "before.txt").read_bytes(), (directory / "after.txt").read_bytes())
            self.assertIn("2130771969|2130837505|2130771969|2130837505", (directory / "after.txt").read_text())
            output = directory / "obj/_Microsoft.Android.Resource.Designer.dll"
            data = output.read_bytes()
            pe = struct.unpack_from("<I", data, 0x3c)[0]
            self.assertEqual(0, struct.unpack_from("<I", data, pe + 8)[0])
            outputs.append(data)
            raw.append((directory / "raw.dll").read_bytes())
            modified = output.stat().st_mtime_ns
            self.msbuild(project, "ChummerCanonicalizeResourceDesigner")
            self.assertEqual(data, output.read_bytes())
            self.assertEqual(modified, output.stat().st_mtime_ns, "idempotent task must not dirty its output")
            if name == "producer":
                time.sleep(2)  # Ensure the SDK's original wall-clock timestamp changes.
        self.assertNotEqual(raw[0], raw[1], "baseline must demonstrate the SDK timestamp defect")
        self.assertEqual(outputs[0], outputs[1])
        changed = self.fixture("changed", "0x7f010002")
        self.msbuild(changed, "CoreCompile")
        self.assertNotEqual(outputs[0], (changed.parent / "obj/_Microsoft.Android.Resource.Designer.dll").read_bytes())
        self.assert_compiler_consumers_repeatable()

    def assert_compiler_consumers_repeatable(self):
        # Exercise real Csc: changing a reference's MVID also changes the app's
        # deterministic PE/PDB identity even when its resource API is unchanged.
        source = self.root / "consumer"
        source.mkdir()
        (source / "NuGet.Config").write_text(
            '<configuration><packageSources><clear /></packageSources></configuration>')
        project = source / "Consumer.csproj"
        project.write_text('''<Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework>
          <AssemblyName>Chummer.ResourceProbe</AssemblyName><Deterministic>true</Deterministic>
          <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
          </PropertyGroup><ItemGroup><Compile Include="Consumer.cs" />
          <Reference Include="_Microsoft.Android.Resource.Designer"><HintPath>$(DesignerReference)</HintPath></Reference>
          </ItemGroup></Project>''')
        resource_type = (self.root / "producer/resource-type.txt").read_text().strip()
        self.assertEqual(resource_type, (self.root / "rebuilder/resource-type.txt").read_text().strip())
        (source / "Consumer.cs").write_text('public static class Consumer { public static int Title => '
                                           + resource_type + '.String.app_name; }')
        digests = {"raw": [], "canonical": []}
        for mode in digests:
            for name in ("producer", "rebuilder"):
                reference = self.root / name / ("raw.dll" if mode == "raw" else "obj/_Microsoft.Android.Resource.Designer.dll")
                scratch = self.root / (mode + "-" + name)
                properties = ["-p:DesignerReference=" + str(reference),
                              "-p:BaseIntermediateOutputPath=" + str(scratch / "obj") + "/",
                              "-p:BaseOutputPath=" + str(scratch / "bin") + "/",
                              "-p:PathMap=" + str(scratch) + "=/_/build",
                              "-p:UseSharedCompilation=false", "-p:BuildInParallel=false", "-p:NuGetAudit=false"]
                self.run_process(self.dotnet, "restore", project, "--configfile", source / "NuGet.Config",
                                 "--disable-parallel", "-v:quiet", "-m:1", *properties)
                self.run_process(self.dotnet, "build", project, "--no-restore", "--disable-build-servers",
                                 "-c", "Release", "-v:quiet", "-m:1", *properties)
                output = scratch / "bin/Release/net10.0/Chummer.ResourceProbe"
                digests[mode].append(tuple(hashlib.sha256(Path(str(output) + suffix).read_bytes()).hexdigest()
                                           for suffix in (".dll", ".pdb")))
        self.assertNotEqual(*digests["raw"], "raw SDK reference identity must demonstrate the compiler propagation")
        self.assertEqual(*digests["canonical"], "app PE and portable PDB must match after correcting their input")

    def test_canonicalizer_rejects_paths_outside_intermediates_and_symlinks(self):
        project = self.fixture("guards")
        self.msbuild(project, "CoreCompile")
        original = project.parent / "obj/_Microsoft.Android.Resource.Designer.dll"
        outside = self.root / "_Microsoft.Android.Resource.Designer.dll"
        shutil.copyfile(original, outside)
        before = outside.read_bytes()
        failure = self.msbuild(project, "ChummerCanonicalizeResourceDesigner",
                               "-p:_GenerateResourceDesignerAssemblyOutput=" + str(outside), success=False)
        self.assertIn("inside obj", failure.stdout + failure.stderr)
        self.assertEqual(before, outside.read_bytes())
        link = project.parent / "obj/link"
        link.symlink_to(self.root, target_is_directory=True)
        failure = self.msbuild(project, "ChummerCanonicalizeResourceDesigner",
                               "-p:_GenerateResourceDesignerAssemblyOutput=" + str(link / outside.name), success=False)
        self.assertIn("must not be a link", failure.stdout + failure.stderr)
        self.assertEqual(before, outside.read_bytes())
        original.write_bytes(b"not a PE assembly")
        failure = self.msbuild(project, "ChummerCanonicalizeResourceDesigner", success=False)
        self.assertIn("Resource-designer determinism failed", failure.stdout + failure.stderr)
        self.assertEqual(b"not a PE assembly", original.read_bytes())

    def test_release_requires_determinism_but_debug_is_unchanged(self):
        project = self.fixture("configuration")
        self.msbuild(project, "CoreCompile", "-p:Configuration=Debug")
        output = project.parent / "obj/_Microsoft.Android.Resource.Designer.dll"
        self.assertEqual((project.parent / "raw.dll").read_bytes(), output.read_bytes())
        original = output.read_bytes()
        failure = self.msbuild(project, "ChummerCanonicalizeResourceDesigner", "-p:Deterministic=false", success=False)
        self.assertIn("requires Deterministic=true", failure.stdout + failure.stderr)
        self.assertEqual(original, output.read_bytes())

    def test_real_aapt_removes_only_source_path_variability(self):
        if not self.aapt.is_file() or not self.platform.is_file():
            self.skipTest("exact build-tools 36.0.0 / API-36 platform unavailable")
        project = self.fixture("aapt-options")
        options = self.run_process(self.dotnet, "msbuild", project, "-nologo", "-v:quiet",
                                   "-getProperty:AndroidAapt2LinkExtraArgs").stdout.strip()
        self.assertEqual(["--exclude-sources"], shlex.split(options))
        debug = self.run_process(self.dotnet, "msbuild", project, "-nologo", "-v:quiet",
                                 "-p:Configuration=Debug", "-getProperty:AndroidAapt2LinkExtraArgs").stdout.strip()
        self.assertEqual("", debug)
        resources = {"raw": [], "canonical": []}
        for name in ("producer-path", "longer-rebuilder-path"):
            source = self.root / name
            values = source / "res/values"
            values.mkdir(parents=True)
            (values / "strings.xml").write_text('<resources><string name="app_name">Same value</string></resources>')
            manifest = source / "AndroidManifest.xml"
            manifest.write_text('<manifest xmlns:android="http://schemas.android.com/apk/res/android" '
                                'package="test.chummer.repro"><uses-sdk android:minSdkVersion="24" />'
                                '<application android:label="@string/app_name" /></manifest>')
            compiled = source / "compiled.zip"
            self.run_process(self.aapt, "compile", "--dir", values.parent, "-o", compiled)
            dumps = []
            for mode in ("raw", "canonical"):
                apk = source / (mode + ".apk")
                extra = shlex.split(options) if mode == "canonical" else []
                self.run_process(self.aapt, "link", "--proto-format", "-I", self.platform,
                                 "--manifest", manifest, "-o", apk, *extra, compiled)
                with zipfile.ZipFile(apk) as archive:
                    resources[mode].append(archive.read("resources.pb"))
                dumps.append(self.run_process(self.aapt, "dump", "resources", apk).stdout)
            source_annotation = " src=" + str(values / "strings.xml") + ":1"
            self.assertEqual(1, dumps[0].count(source_annotation))
            self.assertNotIn(" src=", dumps[1])
            self.assertEqual(dumps[0].replace(source_annotation, ""), dumps[1],
                             "only the exact fixture source annotation may disappear")
            self.assertIn("Same value", dumps[1])
        self.assertNotEqual(*resources["raw"])
        self.assertEqual(*resources["canonical"])
        print("resource-protobuf-sha256=" + hashlib.sha256(resources["canonical"][0]).hexdigest())


if __name__ == "__main__":
    unittest.main()
