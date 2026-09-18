# Android resource reproducibility

This is a build correction, not signing, publication or device qualification.

Two independent Preview 12 builds exposed physical intermediate paths in
`resources.pb` and time-dependent resource-designer identities. A standalone
probe using the admitted Android SDK 36.1.69 reproduces the latter even with
`Deterministic=true`: its generator requests a deterministic MVID but leaves
the PE timestamp at build time. The writer includes that timestamp in the MVID
hash. See the [SDK generator](https://github.com/dotnet/android/blob/36.1.69/src/Xamarin.Android.Build.Tasks/Tasks/GenerateResourceDesignerAssembly.cs)
and [Cecil writer](https://github.com/jbevain/cecil/blob/master/Mono.Cecil/AssemblyWriter.cs).

`eng/AndroidDeterministicResources.targets` makes two Release-only corrections:

- AAPT's `--exclude-sources` omits protobuf source-location metadata. Resource
  IDs, values and configurations remain intact.
- After resource generation and before Csc/linking/AOT, the SDK's Cecil writer
  emits the generated designer with timestamp zero and a content-derived MVID.
  The task is restricted to the expected unsigned intermediate, rejects links,
  and does not rewrite an unchanged output. It never rewrites an APK or AAB.

The task source is outside the app's compilation directory. Debug/proof APKs,
dependency pins, independent whole-AAB equality, approval and signing gates are
unchanged. A changed Android tree still requires fresh qualification.

Run `python3 -m unittest discover -s tests -p test_android_resource_reproducibility.py -v`
in the admitted builder. The tests execute the real SDK generator, Cecil, Csc,
and AAPT. They cover time/root variation, unchanged resource getters/constants,
resource-change sensitivity, identical consumer PE/PDB output, idempotence,
out-of-root/link/malformed input rejection, and unchanged Debug behavior.
Missing exact Android tools cause an explicit skip, never a reproducibility
claim. These small probes do not establish whole-AAB equality; two full builds
and the existing independent comparison remain necessary.
