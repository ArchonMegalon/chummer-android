from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]
CONTRACT = REPO / "src/Chummer.Android/Platform/IAndroidImageDocumentService.cs"
IMPLEMENTATION = REPO / "src/Chummer.Android/Platforms/Android/AndroidImageDocumentService.cs"
RUNNER_DOCUMENTS = REPO / "src/Chummer.Android/Platforms/Android/AndroidDocumentService.cs"
BROKER = REPO / "src/Chummer.Android/Platforms/Android/DocumentIntentBroker.cs"
ACTIVITY = REPO / "src/Chummer.Android/Platforms/Android/MainActivity.cs"
BOOTSTRAP = REPO / "src/Chummer.Android/MauiProgram.cs"
MANIFEST = REPO / "src/Chummer.Android/Platforms/Android/AndroidManifest.xml"
MUGSHOT_PAGE = REPO / "src/Chummer.Android/Native/CareerMugshotPage.cs"


class AndroidImageDocumentServiceContractTests(unittest.TestCase):
    def test_image_picker_is_distinct_content_uri_and_image_mime_only(self) -> None:
        contract = CONTRACT.read_text(encoding="utf-8")
        implementation = IMPLEMENTATION.read_text(encoding="utf-8")
        runner_documents = RUNNER_DOCUMENTS.read_text(encoding="utf-8")

        for marker in (
            "interface IAndroidImageDocumentService",
            "AndroidImageDocumentCandidate",
            "OpenValidatedAsync",
            'string.Equals(uri.Scheme, "content"',
            'normalized.StartsWith("image/"',
            'normalized, "image/*"',
        ):
            self.assertIn(marker, contract)
        for marker in (
            "class AndroidImageDocumentService",
            "Intent.ActionOpenDocument",
            'intent.SetType("image/*")',
            "DocumentIntentBroker.ImageOpenRequestCode",
            "resolver.GetType(uri)",
            "AndroidImageDocumentValidation.IsImageMediaType",
        ):
            self.assertIn(marker, implementation)
        self.assertNotIn("IAndroidImageDocumentService", runner_documents)
        self.assertNotIn("ImageOpenRequestCode", runner_documents)
        self.assertNotIn('SetType("image/*")', runner_documents)

    def test_bytes_pixels_decode_and_identity_all_fail_closed(self) -> None:
        contract = CONTRACT.read_text(encoding="utf-8")
        implementation = IMPLEMENTATION.read_text(encoding="utf-8")
        for marker in (
            "MaximumEncodedBytes = 16 * 1024 * 1024",
            "MaximumPixelDimension = 10_000",
            "MaximumPixelCount = 32_000_000",
            "encodedBytes.IsEmpty",
            "encodedBytes.Length > MaximumEncodedBytes",
            "Convert.ToBase64String(encodedBytes)",
            "SHA256.HashData(encodedBytes)",
        ):
            self.assertIn(marker, contract)
        for marker in (
            "ResolveDeclaredSize",
            "ReadBoundedAsync",
            "destination.Length + read > AndroidImageDocumentValidation.MaximumEncodedBytes",
            "InJustDecodeBounds = true",
            "BitmapFactory.DecodeByteArray",
            "Bitmap.Config.Argb8888",
            "InPremultiplied = true",
            "decoded.Config != Bitmap.Config.Argb8888",
            "!decoded.IsPremultiplied",
            "CryptographicOperations.ZeroMemory(encodedBytes)",
        ):
            self.assertIn(marker, implementation)

    def test_cancel_broker_registration_and_no_media_permission_are_explicit(self) -> None:
        implementation = IMPLEMENTATION.read_text(encoding="utf-8")
        broker = BROKER.read_text(encoding="utf-8")
        activity = ACTIVITY.read_text(encoding="utf-8")
        bootstrap = BOOTSTRAP.read_text(encoding="utf-8")
        manifest = MANIFEST.read_text(encoding="utf-8")

        self.assertIn("if (uri is null)", implementation)
        self.assertIn("return null;", implementation)
        self.assertIn("ImageOpenRequestCode = 6413", broker)
        self.assertIn("DocumentIntentBroker.ImageOpenRequestCode", activity)
        self.assertIn(
            "AddSingleton<IAndroidImageDocumentService, AndroidImageDocumentService>()",
            bootstrap,
        )
        self.assertNotIn("READ_MEDIA_IMAGES", manifest)
        self.assertNotIn("READ_EXTERNAL_STORAGE", manifest)

    def test_unresolved_chummer5_codec_gate_prevents_false_add_claim(self) -> None:
        contract = CONTRACT.read_text(encoding="utf-8")
        page = MUGSHOT_PAGE.read_text(encoding="utf-8")
        for marker in (
            "AndroidMugshotStorageCodecParity",
            "IsExactChummer5StorageEncodingAvailable = false",
            "SavedImageQuality",
            "GDI+ PNG/JPEG encoding",
            "callers must not persist a candidate as a mugshot",
        ):
            self.assertIn(marker, contract)
        self.assertNotIn("CareerMugshotAddRequest", page)
        self.assertNotIn("ApplyCareerMugshotAddAsync", page)
        self.assertNotIn('AutomationId = "career-mugshot-add"', page)


if __name__ == "__main__":
    unittest.main()
