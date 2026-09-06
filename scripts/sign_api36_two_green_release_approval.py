#!/usr/bin/env python3
"""Create a short-lived detached approval for one exact two-green receipt.

This command belongs in the protected release-builder environment.  Its output
authorizes release preparation only; it cannot authorize AAB signing, Play
upload, tester mutation, or publication.
"""

from __future__ import annotations

import argparse
import base64
from datetime import UTC, datetime, timedelta
import hashlib
import importlib.util
import inspect
import json
import os
from pathlib import Path
import secrets
import ssl
import stat
import subprocess
import tempfile
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import urljoin, urlsplit
from urllib.request import HTTPRedirectHandler, HTTPSHandler, ProxyHandler, Request, build_opener


REPO_ROOT = Path(__file__).resolve().parents[1]
VERIFIER_PATH = REPO_ROOT / "scripts/verify_api36_two_green_release_eligibility.py"
TWO_GREEN_PATH = REPO_ROOT / "scripts/materialize-api36-two-green-eligibility.py"
KEY_HYGIENE_PATH = REPO_ROOT / "scripts/verify_release_private_key_hygiene.py"


def _load(path: Path, name: str):
    specification = importlib.util.spec_from_file_location(name, path)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"cannot load {name}")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


VERIFIER = _load(VERIFIER_PATH, "android_two_green_release_approval_verifier")
TWO_GREEN = _load(TWO_GREEN_PATH, "android_two_green_release_approval_contract")
KEY_HYGIENE = _load(KEY_HYGIENE_PATH, "android_release_private_key_hygiene")
GITHUB_PROVENANCE_CONTRACT = "chummer.android.github-two-green-provenance/v1"
GITHUB_API_ROOT = "https://api.github.com/"
MAX_GITHUB_JSON_BYTES = 16 * 1024 * 1024
MAX_GITHUB_ARTIFACT_BYTES = 256 * 1024 * 1024
AUTHENTICATED_GITHUB_FILE_INPUTS = {
    "android_root",
    "policy",
    "environment_policy",
    "source_workflow",
    "review_run",
    "review_jobs",
    "review_artifacts",
    "review_aggregate_archive",
    "review_p0_archive",
    "review_pull_request",
    "review_head_pull_requests",
    "review_aggregate_check_run",
    "review_base_commit",
    "review_head_commit",
    "review_event_commit",
    "main_run",
    "main_jobs",
    "main_artifacts",
    "main_aggregate_archive",
    "main_p0_archive",
    "main_commit",
}
SYSTEM_CA_BUNDLE = Path("/etc/ssl/certs/ca-certificates.crt")


def _https_origin(url: str) -> tuple[str, str, int]:
    parsed = urlsplit(url)
    try:
        port = parsed.port
    except ValueError as error:
        raise ValueError("GitHub provenance URL port is invalid") from error
    if (
        parsed.scheme != "https"
        or not parsed.hostname
        or parsed.username
        or parsed.password
        or port not in (None, 443)
    ):
        raise ValueError("GitHub provenance URL is not canonical HTTPS")
    return parsed.scheme, parsed.hostname.lower(), port or 443


class _SafeRedirect(HTTPRedirectHandler):
    max_repeats = 2
    max_redirections = 5

    def redirect_request(self, request, fp, code, msg, headers, new_url):
        redirected = super().redirect_request(request, fp, code, msg, headers, new_url)
        if redirected is None:
            return None
        new_origin = _https_origin(new_url)
        previous_origin = _https_origin(request.full_url)
        if new_origin != previous_origin:
            redirected.remove_header("Authorization")
        return redirected


class GitHubApiClient:
    def __init__(self, token_file: Path) -> None:
        token_raw = VERIFIER._stable_bytes(
            token_file,
            label="GitHub provenance token",
            limit=16 * 1024,
            owner_only=True,
        )
        try:
            token = token_raw.decode("ascii").strip()
        except UnicodeDecodeError as error:
            raise ValueError("GitHub provenance token is not ASCII") from error
        if not 20 <= len(token) <= 512 or any(character.isspace() for character in token):
            raise ValueError("GitHub provenance token format is invalid")
        self._token = token
        if (
            SYSTEM_CA_BUNDLE.is_symlink()
            or not SYSTEM_CA_BUNDLE.is_file()
            or SYSTEM_CA_BUNDLE.stat().st_uid != 0
            or stat.S_IMODE(SYSTEM_CA_BUNDLE.stat().st_mode) & 0o022
        ):
            raise ValueError("system GitHub provenance CA bundle is not trusted")
        context = ssl.create_default_context(cafile=os.fspath(SYSTEM_CA_BUNDLE))
        self._opener = build_opener(
            ProxyHandler({}),
            HTTPSHandler(context=context),
            _SafeRedirect(),
        )

    def fetch(self, endpoint: str, *, artifact: bool = False) -> bytes:
        if endpoint.startswith(("http:", "https:", "//")) or ".." in endpoint.split("/"):
            raise ValueError("GitHub provenance endpoint is not repository-relative")
        url = urljoin(GITHUB_API_ROOT, endpoint.lstrip("/"))
        if _https_origin(url) != ("https", "api.github.com", 443):
            raise ValueError("GitHub provenance endpoint escaped the canonical API")
        request = Request(
            url,
            headers={
                "Accept": "application/vnd.github+json",
                "Authorization": f"Bearer {self._token}",
                "User-Agent": "chummer-android-protected-release-signer/1",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        limit = MAX_GITHUB_ARTIFACT_BYTES if artifact else MAX_GITHUB_JSON_BYTES
        try:
            with self._opener.open(request, timeout=30) as response:
                _https_origin(response.geturl())
                chunks: list[bytes] = []
                total = 0
                while total <= limit:
                    chunk = response.read(min(1024 * 1024, limit + 1 - total))
                    if not chunk:
                        break
                    chunks.append(chunk)
                    total += len(chunk)
        except (HTTPError, URLError, TimeoutError, OSError) as error:
            raise ValueError("authenticated GitHub provenance fetch failed") from error
        if total > limit:
            raise ValueError("authenticated GitHub provenance response is oversized")
        return b"".join(chunks)


def _private_file(path: Path, label: str, *, outside_repo: bool) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_file():
        raise ValueError(f"{label} must be an absolute regular non-symlink file")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if resolved != path or metadata.st_uid != os.getuid() or stat.S_IMODE(metadata.st_mode) & 0o077:
        raise ValueError(f"{label} must be canonical and owner-only")
    if outside_repo and resolved.is_relative_to(REPO_ROOT):
        raise ValueError(f"{label} must remain outside the repository")
    return resolved


def _private_key(path: Path) -> Path:
    KEY_HYGIENE.verify(REPO_ROOT)
    return KEY_HYGIENE.private_key(path, REPO_ROOT, "release approval private key")


def _write_exclusive(path: Path, raw: bytes) -> None:
    if (
        not path.is_absolute()
        or path.exists()
        or path.is_symlink()
        or not path.parent.is_dir()
        or path.parent.is_symlink()
        or path.parent.resolve(strict=True) != path.parent
        or path.parent.stat().st_uid != os.getuid()
        or stat.S_IMODE(path.parent.stat().st_mode) & 0o077
    ):
        raise ValueError("release approval output must be new in one canonical owner-only directory")
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        0o600,
    )
    try:
        with os.fdopen(descriptor, "wb") as output:
            output.write(raw)
            output.flush()
            os.fsync(output.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def _authenticated_github_replay(
    receipt_raw: bytes,
    receipt: dict[str, object],
    github_token_path: Path,
    github_client: Any | None = None,
) -> tuple[str, str]:
    token_path = _private_file(
        github_token_path, "GitHub provenance token", outside_repo=True
    )
    client = github_client or GitHubApiClient(token_path)
    review_evidence = receipt.get("reviewRun")
    main_evidence = receipt.get("mainRun")
    pull_request_authority = receipt.get("reviewPullRequest")
    if not all(isinstance(value, dict) for value in (
        review_evidence, main_evidence, pull_request_authority
    )):
        raise ValueError("two-green receipt lacks replayable GitHub authority")
    review_identity = review_evidence.get("run")
    main_identity = main_evidence.get("run")
    if not isinstance(review_identity, dict) or not isinstance(main_identity, dict):
        raise ValueError("two-green receipt lacks replayable run authority")
    review_run_id = review_identity.get("id")
    main_run_id = main_identity.get("id")
    pull_request_number = pull_request_authority.get("number")
    review_event_sha = review_evidence.get("p0EventSha")
    if (
        type(review_run_id) is not int or review_run_id <= 0
        or type(main_run_id) is not int or main_run_id <= 0
        or type(pull_request_number) is not int or pull_request_number <= 0
    ):
        raise ValueError("two-green receipt GitHub numeric authority is invalid")
    VERIFIER._sha40(review_event_sha, "two-green review event SHA")
    repository = TWO_GREEN.REPOSITORY

    fetched: dict[str, bytes] = {}

    def fetch_json(
        name: str, endpoint: str, *, expect_object: bool = True
    ) -> Any:
        raw = client.fetch(endpoint, artifact=False)
        fetched[name] = raw
        try:
            value = json.loads(
                raw.decode("utf-8"),
                object_pairs_hook=TWO_GREEN.object_without_duplicates,
                parse_constant=lambda token: (_ for _ in ()).throw(
                    ValueError(f"authenticated GitHub {name} contains {token}")
                ),
            )
        except (UnicodeDecodeError, json.JSONDecodeError, RecursionError) as error:
            raise ValueError(f"authenticated GitHub {name} is not strict JSON") from error
        if expect_object and not isinstance(value, dict):
            raise ValueError(f"authenticated GitHub {name} is not one object")
        return value

    def fetch_archive(name: str, artifact_id: int) -> None:
        fetched[name] = client.fetch(
            f"repos/{repository}/actions/artifacts/{artifact_id}/zip",
            artifact=True,
        )

    run_values: dict[str, dict[str, Any]] = {}
    job_values: dict[str, dict[str, Any]] = {}
    artifact_values: dict[str, dict[str, Any]] = {}
    for role, run_id in (("review", review_run_id), ("main", main_run_id)):
        run = fetch_json(
            f"{role}_run", f"repos/{repository}/actions/runs/{run_id}"
        )
        attempt = run.get("run_attempt")
        if type(attempt) is not int or attempt <= 0:
            raise ValueError("authenticated GitHub run attempt is invalid")
        jobs = fetch_json(
            f"{role}_jobs",
            f"repos/{repository}/actions/runs/{run_id}/attempts/{attempt}/jobs?per_page=100",
        )
        artifacts = fetch_json(
            f"{role}_artifacts",
            f"repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100",
        )
        if jobs.get("total_count") != len(jobs.get("jobs", [])):
            raise ValueError("authenticated GitHub jobs response is paginated or incomplete")
        if artifacts.get("total_count") != len(artifacts.get("artifacts", [])):
            raise ValueError("authenticated GitHub artifacts response is paginated or incomplete")
        run_values[role], job_values[role], artifact_values[role] = run, jobs, artifacts
        for kind, artifact_name in (
            ("aggregate", f"chummer-android-api36-phone-sr5-wizard-aggregate-{run_id}-{attempt}"),
            ("p0", f"chummer-android-p0-pr-authority-{run_id}-{attempt}"),
        ):
            matching = [
                row for row in artifacts.get("artifacts", [])
                if isinstance(row, dict) and row.get("name") == artifact_name
            ]
            if len(matching) != 1 or type(matching[0].get("id")) is not int:
                raise ValueError("authenticated GitHub proof artifact cardinality differs")
            fetch_archive(f"{role}_{kind}_archive", matching[0]["id"])

    review_run = run_values["review"]
    main_run = run_values["main"]
    review_head_sha = VERIFIER._sha40(
        review_run.get("head_sha"), "authenticated review head SHA"
    )
    main_head_sha = VERIFIER._sha40(
        main_run.get("head_sha"), "authenticated main head SHA"
    )
    pull_request = fetch_json(
        "review_pull_request",
        f"repos/{repository}/pulls/{pull_request_number}",
    )
    pull_request_base = pull_request.get("base")
    if not isinstance(pull_request_base, dict):
        raise ValueError("authenticated GitHub pull request base is missing")
    review_base_sha = VERIFIER._sha40(
        pull_request_base.get("sha"), "authenticated pull request base SHA"
    )
    fetch_json(
        "review_head_pull_requests",
        f"repos/{repository}/commits/{review_head_sha}/pulls",
        expect_object=False,
    )
    aggregate_jobs = [
        row for row in job_values["review"].get("jobs", [])
        if isinstance(row, dict) and row.get("name") == "Aggregate exact API 36 phone evidence"
    ]
    if len(aggregate_jobs) != 1 or type(aggregate_jobs[0].get("id")) is not int:
        raise ValueError("authenticated GitHub aggregate job cardinality differs")
    fetch_json(
        "review_aggregate_check_run",
        f"repos/{repository}/check-runs/{aggregate_jobs[0]['id']}",
    )
    fetch_json("review_base_commit", f"repos/{repository}/git/commits/{review_base_sha}")
    fetch_json("review_head_commit", f"repos/{repository}/git/commits/{review_head_sha}")
    fetch_json("review_event_commit", f"repos/{repository}/git/commits/{review_event_sha}")
    fetch_json("main_commit", f"repos/{repository}/git/commits/{main_head_sha}")
    remote_main = fetch_json("remote_main", f"repos/{repository}/git/ref/heads/main")
    remote_object = remote_main.get("object")
    if (
        not isinstance(remote_object, dict)
        or remote_object.get("type") != "commit"
        or remote_object.get("sha") != receipt.get("sourceCommit")
        or main_head_sha != receipt.get("sourceCommit")
    ):
        raise ValueError("authenticated remote main does not equal the release source commit")

    android_root = Path(getattr(client, "android_root", REPO_ROOT)).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix="chummer-two-green-github-provenance-") as directory:
        root = Path(directory)
        paths: dict[str, Path] = {}
        for name, raw in fetched.items():
            path = root / (f"{name}.zip" if name.endswith("_archive") else f"{name}.json")
            path.write_bytes(raw)
            path.chmod(0o600)
            paths[name] = path
        arguments = {
            "android_root": android_root,
            "policy": TWO_GREEN.POLICY_PATH,
            "environment_policy": TWO_GREEN.ENVIRONMENT_POLICY_PATH,
            "source_workflow": android_root / TWO_GREEN.WORKFLOW_PATH,
            "review_run_id": review_run_id,
            "review_pull_request_number": pull_request_number,
            "review_event_sha": review_event_sha,
            "main_run_id": main_run_id,
            **{name: paths[name] for name in AUTHENTICATED_GITHUB_FILE_INPUTS - {
                "android_root", "policy", "environment_policy", "source_workflow"
            }},
        }
        expected_parameters = set(inspect.signature(TWO_GREEN.create_authority).parameters)
        if set(arguments) != expected_parameters:
            raise ValueError("authenticated GitHub replay inputs are incomplete")
        rebuilt = TWO_GREEN.create_authority(**arguments)
    if rebuilt != receipt or TWO_GREEN.pretty_json_bytes(rebuilt) != receipt_raw:
        raise ValueError("two-green receipt does not replay from authenticated GitHub provenance")
    validator_raw = VERIFIER._stable_bytes(
        TWO_GREEN_PATH,
        label="two-green deep provenance validator",
        limit=VERIFIER.MAX_AUTHORITY_BYTES,
        owner_only=False,
    )
    validator_sha256 = hashlib.sha256(validator_raw).hexdigest()
    provenance_binding = {
        "contractName": GITHUB_PROVENANCE_CONTRACT,
        "repository": repository,
        "validatorSha256": validator_sha256,
        "receiptSha256": hashlib.sha256(receipt_raw).hexdigest(),
        "remoteMainCommit": main_head_sha,
        "inputSha256": {
            name: hashlib.sha256(raw).hexdigest()
            for name, raw in sorted(fetched.items())
        },
    }
    return validator_sha256, hashlib.sha256(
        VERIFIER._canonical_json_bytes(provenance_binding)
    ).hexdigest()


def sign(
    receipt_path: Path,
    github_token_path: Path,
    private_key_path: Path,
    output_path: Path,
) -> dict[str, object]:
    receipt_raw = VERIFIER._stable_bytes(
        receipt_path,
        label="two-green eligibility receipt",
        limit=VERIFIER.MAX_RECEIPT_BYTES,
        owner_only=True,
    )
    receipt = VERIFIER._strict_json(receipt_raw, label="two-green eligibility receipt")
    TWO_GREEN.validate_authority(receipt)
    provenance_validator_sha256, provenance_replay_sha256 = _authenticated_github_replay(
        receipt_raw,
        receipt,
        github_token_path,
    )
    generated = datetime.now(UTC).replace(microsecond=0)
    expires = generated + timedelta(hours=6)
    unsigned = VERIFIER.release_approval_unsigned(
        receipt_raw,
        receipt,
        generated_at_utc=generated.isoformat().replace("+00:00", "Z"),
        expires_at_utc=expires.isoformat().replace("+00:00", "Z"),
        challenge_nonce=secrets.token_hex(32),
        provenance_validator_sha256=provenance_validator_sha256,
        provenance_replay_sha256=provenance_replay_sha256,
    )
    private_key = _private_key(private_key_path)
    with tempfile.TemporaryDirectory(prefix="chummer-android-release-approval-sign-") as directory:
        payload = Path(directory) / "payload.json"
        payload.write_bytes(VERIFIER._canonical_json_bytes(unsigned))
        completed = subprocess.run(
            [
                os.fspath(VERIFIER.OPENSSL), "pkeyutl", "-sign", "-inkey",
                os.fspath(private_key), "-rawin", "-in", os.fspath(payload),
            ],
            check=False,
            capture_output=True,
            timeout=20,
            env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
        )
    if completed.returncode != 0 or len(completed.stdout) != 64:
        raise ValueError("release approval signing failed")
    approval = {
        **unsigned,
        "signatureBase64": base64.b64encode(completed.stdout).decode("ascii"),
    }
    raw = (json.dumps(approval, indent=2, sort_keys=True) + "\n").encode("utf-8")
    _write_exclusive(output_path, raw)
    try:
        VERIFIER._verify_release_approval(
            output_path,
            receipt_raw=receipt_raw,
            receipt=receipt,
            now=generated,
        )
    except Exception:
        output_path.unlink(missing_ok=True)
        raise
    return {
        "status": "pass",
        "releasePreparationApproved": True,
        "signingAuthorized": False,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "approvalSha256": hashlib.sha256(raw).hexdigest(),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--github-token-file", required=True, type=Path)
    parser.add_argument("--private-key", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    try:
        result = sign(
            arguments.receipt,
            arguments.github_token_file,
            arguments.private_key,
            arguments.output,
        )
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        result = {
            "status": "fail",
            "releasePreparationApproved": False,
            "signingAuthorized": False,
            "publicationAuthorized": False,
            "googlePlayUploadAuthorized": False,
            "failures": [str(error)],
        }
    print(json.dumps(result, sort_keys=True))
    return 0 if result["status"] == "pass" else 2


if __name__ == "__main__":
    raise SystemExit(main())
