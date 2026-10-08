using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Chummer.Android.Native;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCareerMinimalUiAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            await using var runtime = new NativeRewardRuntime(contentRoot);
            await runtime.LoadRunnerAsync();
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var before = store.Get(runtime.Id).Value!;
            CultureInfo prior = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (locale, heading, open, quality) in new[]
                {
                    ("en-GB", "Career", "Open Career", "Change a quality"),
                    ("de-AT", "Karriere", "Karriere öffnen", "Vor- oder Nachteil ändern"),
                    ("es-MX", "Carrera", "Abrir Carrera", "Cambiar una cualidad")
                })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    var dashboard = new BuildPage(runtime.Coordinator);
                    typeof(BuildPage).GetMethod("AddSr5CareerWizardRoute",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, null);
                    string dashboardText = MinimalVisibleText(dashboard);
                    Require(dashboardText.Contains(heading) && dashboardText.Contains(open)
                        && dashboardText.Contains(quality), "Career landing copy is not localized: " + locale);
                    Require(!Regex.IsMatch(dashboardText,
                        "InternalId|SourceId|Core|atomic|four-revision|Direct deep link|Source-bound|Exact chassis"),
                        "Career landing still exposes implementation jargon: " + locale);
                    foreach (string id in new[] { "build-sr5-career-wizard", "build-career-quality",
                        "build-career-skill-group", "build-career-specialization", "build-career-commerce",
                        "build-career-vehicle-workshop" })
                        Require(MinimalVisible(dashboard).OfType<Button>().Single(x => x.AutomationId == id).IsEnabled,
                            "Copy polish removed or disabled a Career destination: " + id);
                    MinimalRequireNoMachineValues(dashboard);

                    var page = new Sr5CareerWizardPage(runtime.Coordinator);
                    await MinimalPrepareAsync(page);
                    var toggle = MinimalVisible(page).OfType<Button>().Single(x =>
                        x.AutomationId == "sr5-career-wizard-details-toggle");
                    Require(!MinimalVisible(page).OfType<Label>().Any(x =>
                        x.AutomationId == "sr5-career-wizard-binding"), "Career diagnostics start expanded.");
                    MinimalRequireNoMachineValues(page);
                    ((IButtonController)toggle).SendClicked();
                    var binding = MinimalVisible(page).OfType<Label>().Single(x =>
                        x.AutomationId == "sr5-career-wizard-binding");
                    string exactBinding = binding.Text;
                    Require(exactBinding.Contains(runtime.Id.Value), "Expanded diagnostics lost the real runner ID.");
                    ((IButtonController)toggle).SendClicked();
                    Require(!binding.IsVisible && binding.Text == exactBinding,
                        "Collapsing diagnostics changed the underlying binding.");
                    MinimalRequireNoMachineValues(page);
                    MinimalRender(page);
                    ((IButtonController)toggle).SendClicked();
                    Require(!binding.IsVisible, "Detached diagnostic button reopened a stale binding.");
                    MinimalRequireNoMachineValues(page);
                    Require(MinimalVisible(page).OfType<Button>().Any(x =>
                        x.AutomationId == "sr5-career-action-after-run" && x.IsEnabled),
                        "Career polish removed the ordinary run-result action.");
                }
            }
            finally { CultureInfo.CurrentUICulture = prior; }
            RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
        });
        Console.WriteLine("PASS Career minimal UI: EN/DE/ES player copy, six destinations, collapsed exact diagnostics, detached-button rejection, unchanged saved runner");
    }
}
