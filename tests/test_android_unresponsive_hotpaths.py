import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
COORDINATOR = ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
ACCOUNT_SCHEDULER = ROOT / "src/Chummer.Android/Platform/AccountStartupWorkScheduler.cs"
DOCUMENT_BROKER = ROOT / "src/Chummer.Android/Platforms/Android/DocumentIntentBroker.cs"
MAIN_ACTIVITY = ROOT / "src/Chummer.Android/Platforms/Android/MainActivity.cs"
ACCOUNT_CONTRACT = ROOT / "src/Chummer.Android/Platform/IAndroidAccountLinkService.cs"
MORE_PAGE = ROOT / "src/Chummer.Android/Native/MorePage.cs"
ACCOUNT_PRIVACY_PAGE = ROOT / "src/Chummer.Android/Native/AccountPrivacyPage.cs"
CAMPAIGN_PAGE = ROOT / "src/Chummer.Android/Native/CampaignPage.cs"


class AndroidUnresponsiveHotpathTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.coordinator = COORDINATOR.read_text(encoding="utf-8")
        cls.account_scheduler = ACCOUNT_SCHEDULER.read_text(encoding="utf-8")
        cls.document_broker = DOCUMENT_BROKER.read_text(encoding="utf-8")
        cls.main_activity = MAIN_ACTIVITY.read_text(encoding="utf-8")
        cls.account_contract = ACCOUNT_CONTRACT.read_text(encoding="utf-8")
        cls.more_page = MORE_PAGE.read_text(encoding="utf-8")
        cls.account_privacy_page = ACCOUNT_PRIVACY_PAGE.read_text(encoding="utf-8")
        cls.campaign_page = CAMPAIGN_PAGE.read_text(encoding="utf-8")

    def test_remote_account_recovery_does_not_gate_first_shell_settlement(self) -> None:
        initialize = self.coordinator.split(
            "public async Task InitializeAsync", maxsplit=1
        )[1].split(
            "public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync", maxsplit=1
        )[0]

        self.assertNotIn("await _account.InitializeAsync", initialize)
        self.assertIn("_accountInitialization = InitializeAccountInBackgroundAsync();", initialize)
        self.assertLess(
            initialize.index("_initialized = true;"),
            initialize.index("_accountInitialization = InitializeAccountInBackgroundAsync();"),
        )
        self.assertIn("AccountStartupWorkScheduler.RunAsync", initialize)
        self.assertIn("_lifetime.Token", initialize)
        self.assertIn("Task.Run", self.account_scheduler)

    def test_account_actions_fail_closed_while_recovery_is_loading(self) -> None:
        self.assertIn(
            "public bool IsLoading => Status == AndroidAccountLinkStatus.Loading;",
            self.account_contract,
        )
        for page in (self.more_page, self.account_privacy_page, self.campaign_page):
            self.assertIn("link.IsEnabled = !Coordinator.Account.IsLoading;", page)
        self.assertIn(
            "_refreshToolbar.IsEnabled = Coordinator.Account.IsLinked;",
            self.campaign_page,
        )

        begin_link = self.coordinator.split(
            "public async Task BeginAccountLinkAsync", maxsplit=1
        )[1].split("public async Task UnlinkAccountAsync", maxsplit=1)[0]
        self.assertIn("if (_account.Snapshot.IsLoading)", begin_link)
        self.assertLess(
            begin_link.index("if (_account.Snapshot.IsLoading)"),
            begin_link.index("await _account.BeginLinkAsync"),
        )

    def test_picker_completion_and_teardown_are_activity_owned(self) -> None:
        self.assertIn("Activity Owner", self.document_broker)
        self.assertIn("int RequestCode", self.document_broker)
        self.assertIn("ReferenceEquals(pending.Owner, activity)", self.document_broker)
        self.assertIn("pending.RequestCode == requestCode", self.document_broker)
        self.assertIn("Cancel((PendingRequest)state!)", self.document_broker)
        self.assertIn("keep this exact request as a tombstone", self.document_broker)
        self.assertIn(
            "Interlocked.CompareExchange(ref _pending, null, pending)",
            self.document_broker,
        )

        on_destroy = self.main_activity.split(
            "protected override void OnDestroy()", maxsplit=1
        )[1].split("protected override void OnPause()", maxsplit=1)[0]
        on_activity_result = self.main_activity.split(
            "protected override void OnActivityResult", maxsplit=1
        )[1]
        self.assertIn("DocumentIntentBroker.Cancel(this);", on_destroy)
        self.assertIn(
            "DocumentIntentBroker.Complete(this, requestCode, documentUri);",
            on_activity_result,
        )


if __name__ == "__main__":
    unittest.main()
