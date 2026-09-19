using System.Reflection;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

internal static class CommerceSelectionRuntimeTests
{
    public static Task RunAsync()
    {
        foreach (string selector in new[] { "cyberware", "cyberware-grade", "drug-grade", "drug-component" })
        {
            using var fixture = new Fixture();
            ContentPage page = selector switch
            {
                "cyberware" => new Sr5CareerCyberwareCatalogPage(fixture.Coordinator),
                "cyberware-grade" => new Sr5CareerCyberwareGradePage(fixture.Coordinator),
                "drug-grade" => new Sr5CareerCustomDrugGradePage(fixture.Coordinator),
                _ => new Sr5CareerCustomDrugComponentPage(fixture.Coordinator)
            };
            var navigation = CommerceSelectionNavigation.Create();
            ((NavigationProxy)page.Navigation).Inner = (INavigation)navigation;
            page.GetType().GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content;
            Button[] choices = body.Children.OfType<Border>()
                .Select(card => card.Content).OfType<Grid>()
                .SelectMany(row => row.Children.OfType<Button>()).ToArray();
            Require(choices.Length >= 2, selector + ": missing distinct real choices");

            ((IButtonController)choices[0]).SendClicked();
            Require(navigation.PopCalls == 1 && fixture.Writes == 1, selector + ": first choice not saved/popped");
            object? accepted = fixture.CyberCheckpoint ?? (object?)fixture.DrugCheckpoint;
            string acceptedId = selector switch
            {
                "cyberware" => $"career-cyberware-source-{fixture.CyberCheckpoint!.Selection.SourceId.Value:N}",
                "cyberware-grade" => $"career-cyberware-grade-{fixture.CyberCheckpoint!.Selection.GradeId.Value:N}",
                "drug-grade" => $"career-custom-drug-grade-{fixture.DrugCheckpoint!.Selection.GradeId.Value:N}",
                _ => $"career-custom-drug-component-{fixture.DrugCheckpoint!.Selection.Components.Single().ComponentId.Value:N}-{fixture.DrugCheckpoint.Selection.Components.Single().Level}"
            };
            Require(choices[0].AutomationId == acceptedId, selector + ": wrong exact source/grade/effect saved");
            ((IButtonController)choices[1]).SendClicked();
            ((IButtonController)choices[0]).SendClicked();
            Require(navigation.PopCalls == 1 && fixture.Writes == 1, selector + ": overlap replayed save or pop");
            Require(Equals(accepted, fixture.CyberCheckpoint ?? (object?)fixture.DrugCheckpoint),
                selector + ": rejected tap changed the saved draft");
            navigation.Complete(page);
            Require(fixture.WorkspaceWrites == 0, selector + ": selecting a draft must not commit a character");
            Console.WriteLine("PASS exclusive native selector: " + selector);
        }

        foreach (string change in new[] { "workspace", "revision", "character", "catalog", "rules", "draft", "phase" })
        {
            using var fixture = new Fixture();
            if (change == "phase") fixture.PrepareDrugReview();
            var cyber = fixture.Coordinator.LoadCareerCyberwarePurchase();
            var drug = fixture.Coordinator.LoadCareerCustomDrugRecipe();
            fixture.Change(change);
            int writes = fixture.Writes;
            if (change != "rules") // Cyberware has no separate rules digest.
                Reject(() => fixture.Coordinator.UpdateCareerCyberwarePurchaseSelection(
                    cyber, cyber.Selection with { MarkupPercent = 37m }));
            Reject(() => fixture.Coordinator.UpdateCareerCustomDrugRecipeSelection(
                drug, drug.Selection with { Name = "stale-tap" }));
            Require(fixture.CyberCheckpoint?.Selection.MarkupPercent != 37m
                && fixture.DrugCheckpoint?.Selection.Name != "stale-tap"
                && fixture.WorkspaceWrites == 0, change + ": stale selection escaped");
            if (change is "workspace" or "draft" or "phase")
                Require(fixture.Writes == writes, change + ": stale tap wrote a draft");
            Console.WriteLine("PASS stale selector rejection: " + change);
        }

        using (var fixture = new Fixture())
        {
            // Preferences restoration allocates new component lists. Equal
            // values must still be selectable after a service/process restart.
            var initial = fixture.Coordinator.LoadCareerCustomDrugRecipe();
            var selected = fixture.Coordinator.UpdateCareerCustomDrugRecipeSelection(initial,
                initial.Selection with { Components = [new(fixture.FoundationId, 1)] });
            var restarted = new Sr5CareerCustomDrugRecipeService(fixture, fixture, fixture);
            var updated = restarted.UpdateSelection(selected, selected.Selection with { Name = "restored" });
            Require(updated.Selection.Name == "restored"
                && updated.Selection.Components.SequenceEqual(selected.Selection.Components)
                && fixture.WorkspaceWrites == 0, "equal restored selection was rejected or committed");
            Console.WriteLine("PASS value-equal restored component selection");
        }
        Console.WriteLine("Commerce selector regressions passed: 12 scenarios.");
        return Task.CompletedTask;
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Stale selector was accepted.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable, ICharacterCyberwarePurchaseAuthority,
        ICharacterCustomDrugAuthority, ISr5CareerCyberwareWorkspaceStore, ISr5CareerCustomDrugWorkspaceStore,
        ISr5CareerCyberwarePurchaseCheckpointStore, ISr5CareerCustomDrugRecipeCheckpointStore
    {
        private static readonly CharacterWorkspaceId Workspace = new("commerce-selector-runner");
        private string _xml = "<character><created>True</created><nuyen>100000</nuyen><cyberwares/><expenses/></character>";
        private long _revision = 7;
        private string _catalog = new('a', 64);
        private string _rules = new('c', 64);
        private CharacterOverviewState _state;
        public RunnerSessionCoordinator Coordinator { get; }
        public int Writes { get; private set; }
        public int WorkspaceWrites { get; private set; }
        public Sr5CareerCyberwarePurchaseCheckpoint? CyberCheckpoint { get; private set; }
        public Sr5CareerCustomDrugRecipeCheckpoint? DrugCheckpoint { get; private set; }
        private readonly CharacterCyberwareSourceId _source = new(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        private readonly CharacterCyberwareGradeId _grade = new(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        private readonly CharacterCustomDrugGradeId _drugGrade = new(Guid.Parse("33333333-3333-4333-8333-333333333333"));
        public CharacterCustomDrugComponentId FoundationId { get; } = new(Guid.Parse("44444444-4444-4444-8444-444444444444"));

        public Fixture()
        {
            var creation = Program.NewCreationOverview(Workspace, _revision, _revision);
            _state = creation with
            {
                Profile = creation.Profile! with { Created = true },
                Rules = new CharacterRulesSection("SR5", "", "", 0, 0, 0, 0, [])
            };
            Coordinator = new RunnerSessionCoordinator(
                StrictPageProxy.Create<ICharacterOverviewPresenter>(() => _state),
                null!, null!, null!, null!, null!, null!,
                StrictPageProxy.Create<Chummer.Presentation.Shell.IShellPresenter>(),
                null!, null!, null!, null!, null!,
                StrictPageProxy.Create<Chummer.Android.Platform.IAndroidAccountLinkService>(), null!, null!,
                careerCyberwarePurchaseService: new(this, this, this),
                careerCustomDrugRecipeService: new(this, this, this));
        }

        public void Dispose() => Coordinator.Dispose();

        public void Change(string change)
        {
            switch (change)
            {
                case "workspace": _state = _state with { WorkspaceId = new("another-runner") }; break;
                case "revision": _revision++; break;
                case "character": _xml = _xml.Replace("100000", "99999", StringComparison.Ordinal); break;
                case "catalog": _catalog = new('b', 64); break;
                case "rules": _rules = new('d', 64); break;
                case "draft":
                    var cyber = Coordinator.LoadCareerCyberwarePurchase();
                    Coordinator.UpdateCareerCyberwarePurchaseSelection(cyber.Selection with { FreeCost = true });
                    var drug = Coordinator.LoadCareerCustomDrugRecipe();
                    Coordinator.UpdateCareerCustomDrugRecipeSelection(drug.Selection with { Name = "newer" });
                    break;
                case "phase":
                    Coordinator.ReviewCareerCustomDrugRecipe();
                    Coordinator.ReviewCareerCyberwarePurchase();
                    break;
                default: throw new InvalidOperationException(change);
            }
        }

        public void PrepareDrugReview()
        {
            var original = Coordinator.LoadCareerCustomDrugRecipe();
            Coordinator.UpdateCareerCustomDrugRecipeSelection(original.Selection with
                { Name = "ready", Components = [new(FoundationId, 1)] });
        }

        public CharacterCyberwarePurchasePreparation Prepare(string xml, long revision)
        {
            CharacterCyberwarePurchaseGrade[] grades =
            [
                new(_grade, "Standard", 1m, 1m, 0),
                new(new(Guid.Parse("55555555-5555-4555-8555-555555555555")), "Alpha", 1.2m, 0.8m, 2)
            ];
            var entry = new CharacterCyberwarePurchaseCatalogEntry(_source, "Simrig", "Headware",
                "0.1", "[0]", "6", "5000", "SR5", "452", true, "", [], grades);
            return new(true, [], revision, CharacterCyberwarePurchaseRules.ComputeCharacterDigest(xml),
                _catalog, "sr5-test", new('b', 64), 100000m, false, null, null,
                new(false, false, 1m, false, 1m, 2, false, []),
                [entry, entry with { SourceId = new(Guid.Parse("66666666-6666-4666-8666-666666666666")), Name = "Other" }], []);
        }

        public CharacterCyberwarePurchaseQuote Quote(CharacterCyberwarePurchasePreparation preparation, CharacterCyberwarePurchaseSelection selection)
            => CharacterCyberwarePurchaseRules.Quote(preparation, selection);
        public CharacterCyberwarePurchaseCommitResult Commit(string xml, long revision, CharacterCyberwarePurchaseCommand command)
            => throw new InvalidOperationException("Selector must not commit.");
        public CharacterCyberwarePurchaseCommitResult Undo(string xml, long revision, CharacterCyberwarePurchaseUndoCommand command)
            => throw new InvalidOperationException("Selector must not undo.");

        public CharacterCustomDrugPreparation Prepare(string xml, long revision, CharacterCustomDrugContext context)
        {
            var foundation = new CharacterCustomDrugComponentSource(FoundationId, "Foundation",
                CharacterCustomDrugComponentCategory.Foundation, 1, 2, CharacterCustomDrugLegality.Restricted,
                100m, 1, 1, "SR5", "414", new('f', 64), ["source-anchor"],
                [new(1, [], [], [], [], 0, 0, 0, 0, 1), new(2, [], [], [], [], 0, 0, 0, 0, 1)]);
            var grade = new CharacterCustomDrugGrade(_drugGrade, "Standard", 1m, 0, "SR5", new('d', 64), ["grade"]);
            return new(true, [], context, CharacterCustomDrugQuotePurpose.RecipeDefinition, revision,
                CharacterCustomDrugRules.ComputeCharacterDigest(xml), _catalog, _rules, "sr5-test", 100000m,
                new(true, false, false, 8, 100m, 2),
                [grade, grade with { Id = new(Guid.Parse("77777777-7777-4777-8777-777777777777")), Name = "Other" }],
                [foundation]);
        }
        public CharacterCustomDrugQuote Quote(CharacterCustomDrugPreparation preparation, CharacterCustomDrugSelection selection)
            => CharacterCustomDrugRules.Quote(preparation, selection);
        public CharacterCustomDrugCommitResult Commit(string xml, long revision, CharacterCustomDrugContext context, CharacterCustomDrugCommitCommand command)
            => throw new InvalidOperationException("Selector must not commit.");
        public CharacterCustomDrugCommitResult LookupReceipt(string xml, long revision, CharacterCustomDrugContext context, CharacterCustomDrugCommitCommand command)
            => throw new InvalidOperationException("Unexpected receipt lookup.");
        public CharacterCustomDrugCommitResult Undo(string xml, long revision, CharacterCustomDrugContext context, CharacterCustomDrugUndoCommand command)
            => throw new InvalidOperationException("Selector must not undo.");

        Sr5CareerCyberwareWorkspaceSnapshot? ISr5CareerCyberwareWorkspaceStore.Read(CharacterWorkspaceId id)
            => new(id, _revision, _revision, new(_xml, "sr5"));
        Sr5CareerCustomDrugWorkspaceSnapshot? ISr5CareerCustomDrugWorkspaceStore.Read(CharacterWorkspaceId id)
            => new(id, _revision, _revision, new(_xml, "sr5"));
        public Sr5CareerCyberwareWorkspaceWriteResult ReplaceAndCheckpoint(Sr5CareerCyberwareWorkspaceSnapshot expected, string xml)
        { WorkspaceWrites++; throw new InvalidOperationException("Unexpected workspace write."); }
        public Sr5CareerCustomDrugWorkspaceWriteResult ReplaceAndCheckpoint(Sr5CareerCustomDrugWorkspaceSnapshot expected, string xml)
        { WorkspaceWrites++; throw new InvalidOperationException("Unexpected workspace write."); }

        Sr5CareerCyberwarePurchaseCheckpoint? ISr5CareerCyberwarePurchaseCheckpointStore.Read(CharacterWorkspaceId id)
            => CyberCheckpoint;
        Sr5CareerCustomDrugRecipeCheckpoint? ISr5CareerCustomDrugRecipeCheckpointStore.Read(CharacterWorkspaceId id)
            => DrugCheckpoint is { } checkpoint
                ? checkpoint with { Selection = checkpoint.Selection with { Components = checkpoint.Selection.Components.ToArray() } }
                : null;
        public void Write(Sr5CareerCyberwarePurchaseCheckpoint checkpoint) { CyberCheckpoint = checkpoint; Writes++; }
        public void Write(Sr5CareerCustomDrugRecipeCheckpoint checkpoint) { DrugCheckpoint = checkpoint; Writes++; }
        void ISr5CareerCyberwarePurchaseCheckpointStore.Clear(CharacterWorkspaceId id) => CyberCheckpoint = null;
        void ISr5CareerCustomDrugRecipeCheckpointStore.Clear(CharacterWorkspaceId id) => DrugCheckpoint = null;
    }
}

public class CommerceSelectionNavigation : DispatchProxy
{
    private readonly TaskCompletionSource<Page> _pop = new();
    public int PopCalls { get; private set; }
    public static CommerceSelectionNavigation Create()
    {
        INavigation proxy = Create<INavigation, CommerceSelectionNavigation>();
        return (CommerceSelectionNavigation)proxy;
    }
    public void Complete(Page page) => _pop.SetResult(page);
    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => method?.Name switch
        {
            "PopAsync" => Pop(),
            "get_ModalStack" or "get_NavigationStack" => Array.Empty<Page>(),
            _ => throw new InvalidOperationException("Unexpected navigation: " + method?.Name)
        };
    private Task<Page> Pop() { PopCalls++; return _pop.Task; }
}
