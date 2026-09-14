# Offline release source tests

`build-release.sh` must run the complete `unittest` suite and the explicit
parametrized pytest source module before producing an unsigned AAB. These tests
do not assert physical-device, signing, upload, or publication authority.

Supply two explicit, build-user-owned, non-writable-by-others input directories:

- `CHUMMER_ANDROID_RELEASE_TEST_BOOTSTRAP_DIR`: exactly the two files in
  `eng/release-test-bootstrap.lock.json` (PyPA `get-pip.py` and the pinned pip wheel).
- `CHUMMER_ANDROID_RELEASE_TEST_WHEELHOUSE`: exactly the eleven wheels whose
  hashes are listed in `tests/requirements-ci.txt`.

Fetch these public inputs outside the credential-free, network-disabled builder.
The helper verifies bounded byte snapshots before execution; mutable download
URLs are never execution authority. It uses PyPA's supported `get-pip.py` process
and ordinary pip installation, not a custom wheel installer or the experimental
pip zip application. Official references:
[pip installation](https://pip.pypa.io/en/stable/installation/) and
[offline get-pip usage](https://github.com/pypa/get-pip/blob/main/README.md).

The coherent workspace must additionally expose the exact clean `chummer5a`
source-test oracle pinned by the API-36 workflow, including all five inventory
source roots listed there. This is a test oracle, not an APK dependency. Missing
oracle data fails the test runner; it never falls back to `/docker/chummer5a`.

The runner creates a fresh private `venv --without-pip` below the existing private
release input directory. Bootstrap and dependency installation use only copied,
hash-verified offline artifacts. It does not need system pip or ensurepip. Tests
have a separate explicit environment and the trusted SDK on PATH, so MSBuild
tests do not silently skip because dotnet is absent. Tests run with `-I -E -B`;
site initialization is limited to this fresh venv without system/user packages.
The production validator wrappers retain `-I -E -S` unchanged.

Pytest plugin autoload, caller Python hooks and cache output are disabled. The
fresh venv and its input copies are removed on success or exception. This helper
does not enforce host networking itself: its enclosing governed container still
must have no network, credentials, signing keys, or Docker socket.
