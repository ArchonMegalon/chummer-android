using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationBasicsAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true);
            var saved = PrepareActualFinalizationReadyContext(runtime);
            await HydrateFinalizationOwnerAsync(runtime, owners, saved);
            var original = runtime.Coordinator.State;
            string originalJson = JsonSerializer.Serialize(original);
            Require(Guid.TryParse(original.Rules!.Settings, out _),
                "SETUP: use the real canonical GUID-backed creation profile.");
            var stateProperty = runtime.Presenter.GetType().GetProperty("State")!;
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                foreach ((string locale, string title, string method, string notice) in new[]
                {
                    ("en-GB", "Rules & sources", "Priority", "Source books follow this runner's saved profile. You cannot change them on this screen yet."),
                    ("de-AT", "Regeln & Quellen", "Priorität", "Die Quellenbücher richten sich nach dem gespeicherten Profil dieses Runners. Hier kannst du sie noch nicht ändern."),
                    ("es-MX", "Reglas y fuentes", "Prioridad", "Los libros siguen el perfil guardado de este runner. Todavía no puedes cambiarlos en esta pantalla.")
                })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    var page = new CreationBasicsPage(runtime.Coordinator);
                    Refresh(page);
                    var elements = IssuedElements(page).ToArray();
                    string[] text = elements.OfType<Label>().Select(label => label.Text ?? "").ToArray();
                    Require(!text.Any(value => value.Contains(original.Rules.Settings, StringComparison.Ordinal)
                        || value.Contains(original.CreationWizard!.SourceDigest[..19], StringComparison.Ordinal)),
                        "Creation basics exposes a technical profile ID or digest: " + locale);
                    Require(page.Title == title && text.Contains(method) && text.Contains(notice),
                        "Creation basics is not using the selected language: " + locale);
                    Require(elements.Any(e => e.AutomationId == "creation-basics-authority")
                        && !elements.OfType<InputView>().Any() && !elements.OfType<Picker>().Any(),
                        "Read-only setup offered an unsupported profile/sourcebook edit.");
                    Require(!text.Any(value => value.Contains("settings.xml", StringComparison.Ordinal)
                        || value.Contains("bootstrap", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("sha256:", StringComparison.Ordinal)),
                        "Developer diagnostics remain on the normal setup page.");
                    Console.WriteLine("PASS real Creation basics labels and read-only scope: " + locale);
                }

                foreach (string settings in new[] { "My table rules", Guid.Parse(original.Rules.Settings).ToString("N") })
                {
                    stateProperty.SetValue(runtime.Presenter, original with { Rules = original.Rules with { Settings = settings } });
                    var page = new CreationBasicsPage(runtime.Coordinator);
                    Refresh(page);
                    Require(IssuedElements(page).OfType<Label>().Any(label => label.Text == settings)
                        == !Guid.TryParse(settings, out _),
                        "Human profile names must remain visible, opaque profile IDs must not.");
                }
                stateProperty.SetValue(runtime.Presenter, original);
                Console.WriteLine("PASS readable profile name retained; compact GUID hidden");

                foreach (var invalid in new[]
                {
                    original with { Profile = null },
                    original with { Profile = original.Profile! with { Created = true } },
                    original with { Rules = null },
                    original with { WorkspaceId = null },
                    original with { CreationWizard = null },
                    original with { CreationWizard = original.CreationWizard! with { WorkspaceId = "different-workspace" } },
                    original with { CreationWizard = original.CreationWizard! with { WorkspaceRevision = original.ContentRevision + 1 } }
                })
                {
                    stateProperty.SetValue(runtime.Presenter, invalid);
                    var page = new CreationBasicsPage(runtime.Coordinator);
                    Refresh(page);
                    var elements = IssuedElements(page).ToArray();
                    Require(elements.Any(e => e.AutomationId == "creation-basics-authority-unavailable")
                        && !elements.Any(e => e.AutomationId == "creation-basics-authority"),
                        "Unbound or finalized state was presented as current Creation setup.");
                }
                Console.WriteLine("PASS 7 existing Creation basics fail-closed boundaries");
            }
            finally
            {
                stateProperty.SetValue(runtime.Presenter, original);
                CultureInfo.CurrentUICulture = culture;
            }
            Require(JsonSerializer.Serialize(runtime.Coordinator.State) == originalJson,
                "Display-only labels changed projected rules or identity.");
            var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
            Require(FinalizationDocumentDigest(cold) == FinalizationDocumentDigest(saved),
                "Opening Creation basics changed the saved runner.");
            ui.AssertHealthy();
        });

        static void Refresh(CreationBasicsPage page) => typeof(CreationBasicsPage)
            .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .Invoke(page, null);
    }
}
