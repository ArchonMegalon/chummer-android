import unittest

from scripts.extract_apksigner_certificate_sha256 import (
    CertificateDigestError,
    extract_certificate_sha256,
)


LOWER_DIGEST = "12" * 32
UPPER_DIGEST = LOWER_DIGEST.upper()


class ExtractApksignerCertificateSha256Tests(unittest.TestCase):
    def test_accepts_numbered_signer_label_and_normalizes_case(self) -> None:
        label, digest = extract_certificate_sha256(
            f"Signer #1 certificate SHA-256 digest: {UPPER_DIGEST}\n"
        )

        self.assertEqual("numbered", label)
        self.assertEqual(LOWER_DIGEST, digest)

    def test_accepts_sdk_range_signer_label(self) -> None:
        label, digest = extract_certificate_sha256(
            "Signer (minSdkVersion=24, maxSdkVersion=2147483647) "
            f"certificate SHA-256 digest: {LOWER_DIGEST}\n"
        )

        self.assertEqual("sdk-range", label)
        self.assertEqual(LOWER_DIGEST, digest)

    def test_accepts_dev_release_sdk_range_signer_label(self) -> None:
        label, digest = extract_certificate_sha256(
            "Signer (minSdkVersion=36 (dev release=true), maxSdkVersion=2147483647) "
            f"certificate SHA-256 digest: {LOWER_DIGEST}\n"
        )

        self.assertEqual("sdk-range-dev-release", label)
        self.assertEqual(LOWER_DIGEST, digest)

    def test_rejects_missing_digest(self) -> None:
        with self.assertRaises(CertificateDigestError):
            extract_certificate_sha256("Verifies\n")

    def test_rejects_malformed_digest_length(self) -> None:
        with self.assertRaises(CertificateDigestError):
            extract_certificate_sha256(
                f"Signer #1 certificate SHA-256 digest: {LOWER_DIGEST[:-2]}\n"
            )

    def test_rejects_unallowlisted_label(self) -> None:
        with self.assertRaises(CertificateDigestError):
            extract_certificate_sha256(
                f"Source Stamp Signer certificate SHA-256 digest: {LOWER_DIGEST}\n"
            )

    def test_rejects_multiple_accepted_signer_digests(self) -> None:
        with self.assertRaises(CertificateDigestError):
            extract_certificate_sha256(
                f"Signer #1 certificate SHA-256 digest: {LOWER_DIGEST}\n"
                f"Signer #2 certificate SHA-256 digest: {LOWER_DIGEST}\n"
            )


if __name__ == "__main__":
    unittest.main()
