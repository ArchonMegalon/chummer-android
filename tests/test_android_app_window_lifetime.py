"""Source guard for the Activity-recreation regression exercised on API 36.

This guards the DI/Window boundary, not device lifecycle execution by itself.
"""
from pathlib import Path
import unittest


PROJECT = Path(__file__).resolve().parents[1] / "src/Chummer.Android"


class AndroidAppWindowLifetimeTests(unittest.TestCase):
    def test_each_window_resolves_a_fresh_visual_root(self):
        app = (PROJECT / "App.xaml.cs").read_text()
        self.assertIn("public App(Func<MainShell> createMainShell)", app)
        self.assertIn("private readonly Func<MainShell> _createMainShell;", app)
        self.assertIn("=> new(_createMainShell());", app)
        self.assertNotIn("private readonly MainShell", app)
        self.assertNotIn("=> new(_mainShell)", app)

    def test_shell_is_transient_but_runner_state_is_not_recreated(self):
        registration = (PROJECT / "MauiProgram.cs").read_text()
        self.assertIn("builder.Services.AddTransient<MainShell>();", registration)
        self.assertIn("builder.Services.AddSingleton<Func<MainShell>>(provider =>", registration)
        self.assertIn("() => provider.GetRequiredService<MainShell>()", registration)
        self.assertNotIn("AddSingleton<MainShell>", registration)
        self.assertIn("builder.Services.AddSingleton<RunnerSessionCoordinator>();", registration)
        self.assertIn("builder.Services.AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>();", registration)


if __name__ == "__main__":
    unittest.main()
