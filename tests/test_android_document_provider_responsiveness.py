import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DOCUMENT_SERVICE = ROOT / "src/Chummer.Android/Platforms/Android/AndroidDocumentService.cs"
IMAGE_SERVICE = ROOT / "src/Chummer.Android/Platforms/Android/AndroidImageDocumentService.cs"


class AndroidDocumentProviderResponsivenessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.document_source = DOCUMENT_SERVICE.read_text(encoding="utf-8")
        cls.image_source = IMAGE_SERVICE.read_text(encoding="utf-8")

    def test_picker_launch_stays_before_background_provider_work(self) -> None:
        document_open = self.document_source.split(
            "public async Task<bool> SaveAsAsync", maxsplit=1
        )[0]
        document_save = self.document_source.split(
            "public async Task<bool> SaveAsAsync", maxsplit=1
        )[1].split("private static async Task<AndroidDocument>", maxsplit=1)[0]
        image_open = self.image_source.split(
            "private static async Task<AndroidImageDocumentCandidate>", maxsplit=1
        )[0]

        for operation in (document_open, document_save, image_open):
            self.assertIn("DocumentIntentBroker.LaunchAsync", operation)
            self.assertIn("DocumentProviderWorkScheduler.RunAsync", operation)
            self.assertLess(
                operation.index("DocumentIntentBroker.LaunchAsync"),
                operation.index("DocumentProviderWorkScheduler.RunAsync"),
            )

    def test_content_resolver_ipc_and_stream_io_are_confined_to_worker_methods(self) -> None:
        document_entry_points = self.document_source.split(
            "private static async Task<AndroidDocument>", maxsplit=1
        )[0]
        image_entry_point = self.image_source.split(
            "private static async Task<AndroidImageDocumentCandidate>", maxsplit=1
        )[0]
        blocking_operations = (
            "TakePersistableUriPermission",
            "OpenInputStream",
            "OpenOutputStream",
            ".Query(",
            ".GetType(uri)",
            "CopyToAsync",
            "FlushAsync",
        )

        for operation in blocking_operations:
            self.assertNotIn(operation, document_entry_points)
            self.assertNotIn(operation, image_entry_point)

        document_worker = self.document_source.split(
            "private static async Task<AndroidDocument>", maxsplit=1
        )[1]
        image_worker = self.image_source.split(
            "private static async Task<AndroidImageDocumentCandidate>", maxsplit=1
        )[1]
        for operation in (
            "TakePersistableUriPermission",
            "OpenInputStream",
            "OpenOutputStream",
            ".Query(",
            ".GetType(uri)",
            "CopyToAsync",
            "FlushAsync",
        ):
            combined_workers = document_worker + image_worker
            self.assertIn(operation, combined_workers)

    def test_saf_grants_limits_cancellation_and_cleanup_remain_explicit(self) -> None:
        self.assertIn("private const int MaxDocumentBytes = 8 * 1024 * 1024;", self.document_source)
        self.assertIn("ActivityFlags.GrantPersistableUriPermission", self.document_source)
        self.assertIn("ActivityFlags.GrantReadUriPermission", self.document_source)
        self.assertIn("ActivityFlags.GrantWriteUriPermission", self.document_source)
        self.assertIn("ActivityFlags.GrantPersistableUriPermission", self.image_source)
        self.assertIn("ActivityFlags.GrantReadUriPermission", self.image_source)
        self.assertIn("ReadBoundedAsync(source, cancellationToken)", self.document_source)
        self.assertIn("CopyToAsync(destination, cancellationToken)", self.document_source)
        self.assertIn("FlushAsync(cancellationToken)", self.document_source)
        self.assertIn("await using Stream source", self.document_source)
        self.assertIn("await using Stream destination", self.document_source)
        self.assertIn("await using (Stream source", self.image_source)
        self.assertIn("CryptographicOperations.ZeroMemory", self.document_source)
        self.assertIn("CryptographicOperations.ZeroMemory", self.image_source)


if __name__ == "__main__":
    unittest.main()
