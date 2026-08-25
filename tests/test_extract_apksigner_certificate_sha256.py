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

    def test_rejects_accepted_plus_source_stamp_digest(self) -> None:
        with self.assertRaises(CertificateDigestError):
            extract_certificate_sha256(
                f"Signer #1 certificate SHA-256 digest: {LOWER_DIGEST}\n"
                f"Source Stamp Signer certificate SHA-256 digest: {LOWER_DIGEST}\n"
            )

    def test_rejects_accepted_plus_unrecognized_certificate_digest(self) -> None:
        with self.assertRaises(CertificateDigestError):
            extract_certificate_sha256(
                f"Signer #1 certificate SHA-256 digest: {LOWER_DIGEST}\n"
                f"Future Signer certificate SHA-256 digest: {LOWER_DIGEST}\n"
            )

    def test_rejection_reports_only_sanitized_label_and_digest_length(self) -> None:
        try:
            extract_certificate_sha256(
                f"Future/Signer certificate SHA-256 digest: {UPPER_DIGEST}\n"
            )
        except CertificateDigestError as error:
            message = str(error)
        else:
            self.fail("unallowlisted label unexpectedly succeeded")

        self.assertIn(
            "Future?Signer (digest_length=64, digest_class=hex)", message
        )
        self.assertNotIn(LOWER_DIGEST, message.lower())
        self.assertNotIn(UPPER_DIGEST, message)

    def test_malformed_digest_remains_redacted_and_fail_closed(self) -> None:
        malformed_digest = "ab" * 31
        try:
            extract_certificate_sha256(
                f"Signer #1 certificate SHA-256 digest: {malformed_digest}\n"
            )
        except CertificateDigestError as error:
            message = str(error)
        else:
            self.fail("malformed digest unexpectedly succeeded")

        self.assertIn(
            "Signer #1 (digest_length=62, digest_class=hex)", message
        )
        self.assertNotIn(malformed_digest, message)

    def test_colon_delimited_digest_diagnostic_is_classified_and_redacted(self) -> None:
        colon_digest = ":".join(["ab"] * 32)
        try:
            extract_certificate_sha256(
                f"Signer #1 certificate SHA-256 digest: {colon_digest}\n"
            )
        except CertificateDigestError as error:
            message = str(error)
        else:
            self.fail("unallowlisted digest representation unexpectedly succeeded")

        self.assertIn(
            "Signer #1 (digest_length=95, "
            "digest_class=colon-delimited-hex-bytes)",
            message,
        )
        self.assertNotIn(colon_digest, message)


if __name__ == "__main__":
    unittest.main()
