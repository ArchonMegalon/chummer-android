using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    // Continue the real bootstrap/prerequisite/attributes/skills/magic saves,
    // including the later Skills revisit. No XML, rules or receipt is fabricated.
    internal static async Task RunAwakenedFinalizationAsync(string contentRoot, string sourceDirectory,
        CharacterWorkspaceId id)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true, creationSkillsSeed: target =>
                {
                    foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                    {
                        string destination = Path.Combine(target, Path.GetRelativePath(sourceDirectory, source));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(source, destination);
                    }
                });
            runtime.Id = id;
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var initial = store.Get(id).Value!;
            var magic = runtime.Services.GetRequiredService<ICharacterCreationMagicResonanceService>().Load(new(id)).Value!;
            var editor = Chummer.Presentation.Overview.CharacterCreationMagicResonanceWorkflow.Project(magic);
            Require(editor.CanEdit && magic.PrerequisiteDraft!.BuildMethod == CharacterCreationBuildMethods.SumToTen,
                "Awakened Career requires the real saved Sum-to-Ten magic draft.");

            await Task.Run(() =>
            {
                var qualities = runtime.Services.GetRequiredService<ICharacterCreationQualitiesService>();
                var quality = qualities.Preview(new(qualities.Load(new(id)).Value!.Binding, [])).Value!;
                Require(qualities.Confirm(new(quality.Binding, [], quality.PreviewDigest,
                    "awakened-career-qualities", Guid.NewGuid(), true)).Outcome == CharacterCreationFoundationOutcomes.Success,
                    "Awakened empty Qualities confirmation failed.");
                var resources = runtime.Services.GetRequiredService<ICharacterCreationResourcesService>();
                var resourceState = resources.Load(new(id)).Value!;
                var zero = resourceState.Options.First(option => option.IsEnabled && option.KarmaInvestment == 0);
                var resource = resources.Preview(new(resourceState.Binding, zero.OptionId)).Value!;
                Require(resources.Confirm(new(resource.Binding, zero.OptionId, resource.PreviewDigest,
                    "awakened-career-resources", true)).Outcome == CharacterCreationResourcesOutcomes.Applied,
                    "Awakened Resources confirmation failed.");
                var gear = runtime.Services.GetRequiredService<ICharacterCreationGearService>();
                var basket = gear.Preview(new(gear.Load(new(id)).Value!.Binding, [])).Value!;
                Require(gear.Confirm(new(basket.Binding, [], basket.PreviewDigest,
                    "awakened-career-gear", true)).Outcome == CharacterCreationGearOutcomes.Applied,
                    "Awakened empty Gear confirmation failed.");
            });
            var before = store.Get(id).Value!;
            Require(before.Document.Content == initial.Document.Content,
                "Draft completion applied character effects before finalization.");
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var loaded = runtime.Coordinator.LoadCreationFinalization().Value!;
            Require(loaded.CanReview && loaded.StartingCashSource is not null,
                "Awakened finalization blocked: " + string.Join(",", loaded.Blockers));
            if (Environment.GetEnvironmentVariable("CHUMMER_AWAKENED_SMOKE_DIRECTORY") is { Length: > 0 } smokeDirectory)
            {
                Require(Path.IsPathFullyQualified(smokeDirectory)
                    && !Directory.Exists(smokeDirectory) && !File.Exists(smokeDirectory),
                    "Awakened smoke export requires a new explicit synthetic directory; select one talent per export.");
                Directory.CreateDirectory(smokeDirectory);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: false);
                }
                Console.WriteLine($"Awakened synthetic smoke fixture: {id.Value}; talent={editor.Talent.Kind}; revision={before.ContentRevision}/{before.SavedRevision}");
            }
            var root = new BuildPage(runtime.Coordinator);
            // Headless MAUI has no scroll handler; supply geometry completion,
            // never rules, review results or route readiness.
            var rootScroll = (ScrollView)root.Content!;
            rootScroll.ScrollToRequested += (_, _) => rootScroll.SendScrollFinished();
            var navigation = new NavigationPage(root);
            var window = new Window(navigation);
            await navigation.PushAsync(new CreationStartingCashPage(runtime.Coordinator, loaded), false);
            await Appear();
            Element<Entry>("creation-starting-cash-roll").Text = loaded.StartingCashSource!.Dice.ToString();
            await Click("creation-starting-cash-preview");
            Require(Current() is CreationFinalizationPage, "Awakened cash choice did not open final review.");
            var review = (CharacterCreationFinalizationReview)typeof(CreationFinalizationPage)
                .GetField("_review", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Current())!;
            Require(review.CanConfirm && JsonSerializer.Serialize(store.Get(id).Value!) == JsonSerializer.Serialize(before),
                "Awakened review is blocked or changed the runner: " + string.Join(",", review.Blockers));
            await Click("creation-finalization-confirm");
            Require(Current() is CreationFinalizationReceiptPage, "Awakened confirmation did not produce a receipt.");
            var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var receipt = saved.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt;
            Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                && receipt.BuildMethod == CharacterCreationBuildMethods.SumToTen
                && runtime.Coordinator.State.Profile?.Created == true,
                "Awakened finalization did not save exactly one Career transition.");
            var xml = XDocument.Parse(saved.Document.Content).Root!;
            Require(string.Equals(xml.Element("created")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                "Saved character is still a Creation draft.");
            CheckSources("powers", "power", editor.Selections.AdeptPowers.Select(power => power.Identity.SourceId));
            CheckSources("spells", "spell", editor.Selections.Spells.Select(spell => spell.SourceId));
            CheckSources("complexforms", "complexform", editor.Selections.ComplexForms.Select(form => form.SourceId));
            foreach (var power in editor.Selections.AdeptPowers)
                Require((int?)xml.Element("powers")!.Elements("power").Single(row =>
                    string.Equals(row.Element("sourceid")?.Value, power.Identity.SourceId, StringComparison.OrdinalIgnoreCase))
                    .Element("rating") == power.Levels, "Saved Adept power lost its selected level.");
            var tradition = editor.Selections.Tradition ?? editor.Selections.Stream;
            Require(tradition is null ? !xml.Elements("tradition").Any()
                : string.Equals(xml.Element("tradition")?.Element("sourceid")?.Value, tradition.SourceId, StringComparison.OrdinalIgnoreCase),
                "Finalization lost or substituted the tradition/stream.");
            if (magic.MysticAdeptPowerPoints is { } purchase)
                Require((int?)xml.Element("magsplitadept") == purchase.PowerPoints,
                    "Finalization lost the Mystic Adept power-point purchase.");
            foreach (var attribute in magic.AttributesDraft!.Attributes.Where(row => row.IsEnabled))
            {
                var savedAttribute = xml.Element("attributes")!.Elements("attribute")
                    .Single(row => row.Element("name")?.Value == attribute.AttributeId);
                Require((int?)savedAttribute.Element("totalvalue") == attribute.Current
                    && (int?)savedAttribute.Element("base") == attribute.PriorityPointsSpent
                    && (int?)savedAttribute.Element("karma") == attribute.KarmaLevels,
                    "Saved attribute does not match the reviewed allocation: " + attribute.AttributeId);
            }
            string savedBytes = JsonSerializer.Serialize(saved);
            await Click("creation-finalization-open-career");
            Require(ReferenceEquals(Current(), root) && runtime.Coordinator.State.Profile?.Created == true,
                "The receipt did not return to the Career runner.");
            await HydrateFinalizationOwnerAsync(runtime, owners, saved, expectedCreated: true);
            var reopened = await runtime.Coordinator.LoadPersistedPriorityTableCreationReceiptAsync(runtime.Coordinator.State, default);
            Require(reopened?.ReceiptDigest == receipt.ReceiptDigest
                && JsonSerializer.Serialize(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!) == savedBytes,
                "Cold Career reread changed the runner or lost its exact receipt.");
            var replay = await runtime.Coordinator.ConfirmCreationFinalizationAsync(review, "awakened-career-stale-replay");
            Require(replay.Outcome != CharacterCreationFinalizationOutcomes.Applied
                && JsonSerializer.Serialize(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!) == savedBytes,
                "An old final review applied another Career transition.");
            IssuedPageLifecycle(Current(), "OnDisappearing");
            Console.WriteLine($"PASS Sum-to-Ten {editor.Talent.Kind}: saved magic + revisited skills → actual-MAUI cash/review/confirm/Career → cold receipt; powers={editor.Selections.AdeptPowers.Count}, spells={editor.Selections.Spells.Count}, forms={editor.Selections.ComplexForms.Count}; revision={saved.ContentRevision}/{saved.SavedRevision}");

            void CheckSources(string collection, string element, IEnumerable<string> sourceIds)
            {
                var expected = sourceIds.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var actual = xml.Element(collection)?.Elements(element).Select(row => row.Element("sourceid")?.Value ?? "")
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
                Require(expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase),
                    "Finalization lost, duplicated or substituted selected " + collection + ": " + JsonSerializer.Serialize(new { expected, actual }));
            }
            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
            T Element<T>(string key) where T : Element => IssuedElements(Current()).OfType<T>().Single(row => row.AutomationId == key);
            async Task Appear()
            {
                if (IssuedPageField<int>(Current(), "_subscribed") == 0)
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(Current(), "OnAppearing")).WaitAsync(TimeSpan.FromSeconds(30));
            }
            async Task Click(string key)
            {
                var previous = Current(); var button = Element<Button>(key);
                Require(button.IsEnabled, "Disabled awakened action: " + key);
                using (var alerts = new IssuedPageAlerts(previous, window))
                {
                    await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked()).WaitAsync(TimeSpan.FromSeconds(45));
                    Require(alerts.Titles.Count == 0, "Unexpected awakened alert: " + string.Join("; ", alerts.Messages));
                }
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0)
                    IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
        });
    }
}
