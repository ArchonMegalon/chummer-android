import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DOCUMENT_SERVICE = ROOT / "src/Chummer.Android/Platforms/Android/AndroidDocumentService.cs"
IMAGE_SERVICE = ROOT / "src/Chummer.Android/Platforms/Android/AndroidImageDocumentService.cs"
PRINT_SERVICE = ROOT / "src/Chummer.Android/Platforms/Android/AndroidSystemService.cs"


class AndroidDocumentProviderResponsivenessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.document_source = DOCUMENT_SERVICE.read_text(encoding="utf-8")
        cls.image_source = IMAGE_SERVICE.read_text(encoding="utf-8")
        cls.print_source = PRINT_SERVICE.read_text(encoding="utf-8")

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
            "ReadAsync",
            "WriteAsync",
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
            "ReadAsync",
            "WriteAsync",
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
        self.assertIn("content.ReadAsync(buffer, cancellationToken)", self.document_source)
        self.assertIn("destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)", self.document_source)
        self.assertIn("FlushAsync(cancellationToken)", self.document_source)
        self.assertIn("await using Stream source", self.document_source)
        self.assertIn("await using Stream destination", self.document_source)
        self.assertIn("await using (Stream source", self.image_source)
        self.assertIn("CryptographicOperations.ZeroMemory", self.document_source)
        self.assertIn("CryptographicOperations.ZeroMemory", self.image_source)

    def test_bound_save_rechecks_after_picker_before_scheduling_provider_io(self) -> None:
        save = self.document_source.split("public async Task<bool> SaveAsAsync", 1)[1].split(
            "private static async Task<AndroidDocument>", 1
        )[0]
        guard = "EnsureOutputCurrent(isOriginalContextCurrent, cancellationToken);"
        first = save.index(guard)
        picker = save.index("await DocumentIntentBroker.LaunchAsync")
        second = save.index(guard, first + len(guard))
        schedule = save.index("DocumentProviderWorkScheduler.RunAsync")
        self.assertLess(first, picker)
        self.assertLess(picker, second)
        self.assertLess(second, schedule)
        self.assertIn("content, isOriginalContextCurrent, token", save)

    def test_bound_writer_checks_before_truncation_and_between_read_and_write(self) -> None:
        writer = self.document_source.split("private static async Task WriteDocumentAsync", 1)[1].split(
            "private static void EnsureOutputCurrent", 1
        )[0]
        guard = "EnsureOutputCurrent(isOriginalContextCurrent, cancellationToken);"
        self.assertLess(writer.index(guard), writer.index('OpenOutputStream(uri, "wt")'))
        loop = writer.split("while (true)", 1)[1]
        first = loop.index(guard)
        read = loop.index("await content.ReadAsync")
        second = loop.index(guard, first + len(guard))
        write = loop.index("await destination.WriteAsync")
        self.assertLess(first, read)
        self.assertLess(read, second)
        self.assertLess(second, write)
        self.assertIn("ArrayPool<byte>.Shared.Rent(32 * 1024)", writer)
        self.assertIn("finally", writer)
        self.assertIn("CryptographicOperations.ZeroMemory(buffer)", writer)
        self.assertNotIn("CopyToAsync", writer)

    def test_print_owns_unique_cache_file_and_checks_delayed_android_callbacks(self) -> None:
        self.assertIn('"chummer-print-" + Guid.NewGuid().ToString("N") + ".pdf"', self.print_source)
        self.assertNotIn("Path.Combine(FileSystem.CacheDirectory, safeName)", self.print_source)
        self.assertIn("MainThread.InvokeOnMainThreadAsync", self.print_source)
        self.assertIn("if (!handedOff) RemovePrintCache(pdfPath);", self.print_source)
        layout = self.print_source.split("public override void OnLayout", 1)[1].split(
            "public override void OnWrite", 1
        )[0]
        write = self.print_source.split("public override void OnWrite", 1)[1].split(
            "public override void OnFinish", 1
        )[0]
        self.assertIn("!_isOriginalContextCurrent()", layout)
        self.assertIn("OnLayoutCancelled", layout)
        self.assertIn("!_isOriginalContextCurrent()", write)
        loop = write.split("while ((read = input.Read(buffer)) > 0)", 1)[1]
        self.assertLess(loop.index("!_isOriginalContextCurrent()"), loop.index("output.Write"))
        self.assertIn("OnWriteCancelled", loop)
        self.assertIn("CryptographicOperations.ZeroMemory(buffer)", write)


if __name__ == "__main__":
    unittest.main()
