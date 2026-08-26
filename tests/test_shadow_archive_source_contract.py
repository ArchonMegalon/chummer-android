import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "src" / "Chummer.Android"


class ShadowArchiveSourceContractTests(unittest.TestCase):
    def test_archive_is_the_first_visible_phone_destination(self) -> None:
        shell = (PROJECT / "MainShell.cs").read_text(encoding="utf-8")
        routes = (PROJECT / "PhoneShellRoutes.cs").read_text(encoding="utf-8")
        phone = shell[shell.index("private void BuildPhoneShell"):shell.index("private async Task ResolveInitialPhoneRouteAsync")]
        archive = 'CreatePhoneTab<ShadowArchivePage>(services, "Archive", PhoneShellRoutes.Archive'
        runners = 'CreatePhoneTab<RunnersPage>(services, "Runners", PhoneShellRoutes.Runners'
        self.assertIn(archive, phone)
        self.assertLess(phone.index(archive), phone.index(runners))
        self.assertIn('public const string Archive = "archive";', routes)
        self.assertIn('AutomationId = $"phone-destination-{route}"', shell)

    def test_port_uses_presentation_contracts_and_default_composition_fails_closed(self) -> None:
        port = (PROJECT / "Native" / "ShadowArchivePublicCatalogPort.cs").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        for contract in (
            "ShadowArchivePresentationResult",
            "ShadowArchivePublicReaderContract",
            "ShadowArchiveCommunityStatusContract",
            "ShadowArchiveSignalMutation",
            "ShadowArchivePresenter",
        ):
            self.assertIn(contract, port)
        self.assertIn("interface IShadowArchivePublicCatalogPort", port)
        self.assertIn("UnavailableShadowArchivePublicCatalogPort", port)
        self.assertIn("IShadowArchivePresentationClient", port)
        self.assertNotIn("interface IShadowArchiveClientPort", port)
        self.assertIn(
            "AddSingleton<IShadowArchivePresentationClient,",
            program,
        )
        self.assertIn("AddSingleton<ShadowArchivePresenter>()", program)
        self.assertIn("AddSingleton<IShadowArchivePublicCatalogPort,", program)
        self.assertNotIn("HttpClient", port)
        self.assertNotIn("example.com", port)

    def test_cards_show_counts_without_a_vote_action(self) -> None:
        page = (PROJECT / "Native" / "ShadowArchivePage.cs").read_text(encoding="utf-8")
        card = page[page.index("private Border BuildStoryCard"):page.index("private void RenderFailure")]
        self.assertIn('Signals: {story.SignalCount:N0}', card)
        self.assertIn('NativeTheme.PrimaryButton("Read story")', card)
        self.assertNotIn("archive-signal-vote", card)
        self.assertNotIn("CreateSignalCommand", card)

    def test_reader_keeps_downloads_public_and_signal_after_final_chapter(self) -> None:
        page = (PROJECT / "Native" / "ShadowArchivePage.cs").read_text(encoding="utf-8")
        policy = (PROJECT / "Native" / "ShadowArchivePhonePolicy.cs").read_text(encoding="utf-8")
        self.assertIn("bool isAtFinalChapter = _chapterIndex == reader.Chapters.Count - 1", page)
        self.assertIn("if (!isAtFinalChapter)", page)
        self.assertIn("archive-signal-vote", page)
        self.assertIn("archive-signal-retract", page)
        self.assertIn("_story.ViewerIsOwner", page)
        self.assertIn("viewerIsOwner", policy)
        self.assertIn("ShadowArchivePhoneSignalKind.OwnerBlocked", policy)
        self.assertIn("_system.OpenUriAsync(download.DownloadUri)", page)
        self.assertIn("No account is required to read or download.", page)

    def test_truthful_states_are_explicit_and_unrelated_products_are_absent(self) -> None:
        sources = "\n".join(
            (PROJECT / "Native" / name).read_text(encoding="utf-8")
            for name in (
                "ShadowArchivePublicCatalogPort.cs",
                "ShadowArchivePhonePolicy.cs",
                "ShadowArchivePage.cs",
            )
        )
        for state in (
            "Loading",
            "No public stories yet",
            "Offline",
            "Unavailable",
            "AuthenticationRequired",
            "Stale",
            "ModerationHeld",
            "RateLimited",
        ):
            self.assertIn(state, sources)
        for forbidden in ("Rook", "Tough Tongue", "LTD", "character mechanics"):
            self.assertNotIn(forbidden, sources)


if __name__ == "__main__":
    unittest.main()
