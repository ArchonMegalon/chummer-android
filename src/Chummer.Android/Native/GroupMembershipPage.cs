using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GroupMembershipPage : NativePageBase
{
    private readonly GroupMembershipEditorState _editor;
    private readonly Switch _membership;
    private readonly Button _save;

    public GroupMembershipPage(
        RunnerSessionCoordinator coordinator,
        GroupMembershipEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        CharacterGroupMembershipState state = editor.Membership;
        Title = "Group membership";
        AutomationId = "group-membership-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(state.Created ? "Career runner" : "Creation runner"));
        body.Add(NativeTheme.Title("Group membership"));
        body.Add(NativeTheme.Body(
            state.MagicEnabled
                ? "Match Chummer5's magical-group membership control and its Career Karma expense."
                : state.ResonanceEnabled
                    ? "Match Chummer5's cost-free Resonance network membership control."
                    : "Match Chummer5's saved group-membership value.",
            NativeTheme.Muted));

        _membership = new Switch
        {
            IsToggled = state.GroupMember,
            AutomationId = "group-membership-toggle",
            OnColor = NativeTheme.Signal
        };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel("Group member"), 0, 0);
        row.Add(_membership, 1, 0);
        body.Add(NativeTheme.Card(row));

        if (state.RequiresConfirmation)
        {
            body.Add(NativeTheme.Body(
                state.KarmaCostsExact
                    ? $"This change costs {state.TransitionKarmaCost.ToString(CultureInfo.InvariantCulture)} Karma; {state.AvailableKarma.ToString(CultureInfo.InvariantCulture)} available."
                    : "Read-only: the exact active group-membership Karma costs are unavailable.",
                state.CanChange ? NativeTheme.Muted : NativeTheme.Danger));
        }

        _save = NativeTheme.PrimaryButton(
            state.RequiresConfirmation
                ? $"Spend {state.TransitionKarmaCost.ToString(CultureInfo.InvariantCulture)} Karma & Save"
                : "Save group membership");
        _save.AutomationId = "group-membership-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _membership.IsEnabled = revisionMatches && _editor.Membership.CanChange;
        _save.IsEnabled = revisionMatches && _editor.Membership.CanChange;
    }

    private async Task SaveAsync()
    {
        CharacterGroupMembershipState state = _editor.Membership;
        if (_membership.IsToggled == state.GroupMember)
        {
            await Navigation.PopAsync();
            return;
        }

        bool confirmed = !state.RequiresConfirmation || await DisplayAlertAsync(
            _membership.IsToggled ? "Join group?" : "Leave group?",
            $"Spend {state.TransitionKarmaCost.ToString(CultureInfo.InvariantCulture)} Karma and save this membership change?",
            "Spend & Save",
            "Cancel");
        if (!confirmed)
        {
            _membership.IsToggled = state.GroupMember;
            return;
        }

        await Coordinator.ApplyGroupMembershipEditAsync(new GroupMembershipEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            state,
            _membership.IsToggled,
            confirmed));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
