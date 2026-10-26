using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.Crusade.ArmyInfo; // ArmyInfoVM, ArmyInfoArmyCartVM, ArmyInfoSquadVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The crusade ARMY WINDOW (<see cref="ArmyInfoVM"/>): opened by the selected army's Army button
    /// (the PC HUD cart's info button, OnInfoRequest) and by Merge (ShowMergeArmy, when another crusader
    /// army stands on the same spot). Tab stops, in the window's order: the army (the shared cart rows
    /// with a live general slot, then Rename and Dismiss army), the second army while merging (the same,
    /// plus previous / next when several are in range), the two move-all-squads arrows, then Split
    /// (create a new army from this one) and Recruit — both only while not merging, as the PC view hides
    /// them — and Close. A squad row opens its action menu: move to the other army (the double-click),
    /// split half / split one / merge into one (the shift / ctrl / alt clicks) and dismiss (its button).
    /// Close = the cart's close button (applies the merge, closes the window); Escape = the window's own
    /// Esc (OnClose: while merging it drops back to the single army, as in the game).
    /// </summary>
    public sealed class GlobalMapArmyInfoScreen : Screen
    {
        public GlobalMapArmyInfoScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_army";
        public override string ScreenName => UIStrings.Instance.CrusadeTexts.Army;
        public override int Layer => 15; // a modal over the world-map base context

        internal static ArmyInfoVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            var vm = rc?.GlobalMapVM?.ArmyInfoVM?.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        private static ArmyInfoArmyCartVM Main() => Live(Vm()?.MainArmyCartVm.Value);
        private static ArmyInfoArmyCartVM Second() => Live(Vm()?.MergeArmyCartVm.Value);
        private static ArmyInfoArmyCartVM Live(ArmyInfoArmyCartVM c) => c != null && !c.IsDisposed ? c : null;

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            var main = Main();
            if (vm == null || main == null) return;
            var second = Second();
            var ct = UIStrings.Instance.CrusadeTexts;

            // ---- the army ----
            b.BeginStop("main").PushContext(main.ArmyName.Value, "group");
            GlobalMapArmyNodes.CartRows(b, Main, "ai:main:", liveGeneral: true, squadActivate: SquadMenu);
            CartButtons(b, Main, "ai:main:");
            b.PopContext();

            // ---- the second army (merging) ----
            if (second != null)
            {
                b.BeginStop("second").PushContext(second.ArmyName.Value, "group");
                GlobalMapArmyNodes.CartRows(b, Second, "ai:second:", liveGeneral: true, squadActivate: SquadMenu);
                CartButtons(b, Second, "ai:second:");
                if (vm.HavePrevArmyMerge.Value)
                    b.AddItem(ControlId.Structural("ai:prev"), GraphNodes.Button(() => Loc.T("crusade.prev_army"), () => Vm()?.PrevMergeArmy()));
                if (vm.HaveNextArmyMerge.Value)
                    b.AddItem(ControlId.Structural("ai:next"), GraphNodes.Button(() => Loc.T("crusade.next_army"), () => Vm()?.NextMergeArmy()));
                b.PopContext();

                // ---- the transfer arrows ----
                b.BeginStop("transfer");
                b.AddItem(ControlId.Structural("ai:tomain"), GraphNodes.Button(
                    () => Loc.T("crusade.move_all_to", new { name = Main()?.ArmyName.Value ?? "" }),
                    () => { Vm()?.MoveSquadsToMainArmy(); SpeakSize(Main()); }));
                b.AddItem(ControlId.Structural("ai:tosecond"), GraphNodes.Button(
                    () => Loc.T("crusade.move_all_to", new { name = Second()?.ArmyName.Value ?? "" }),
                    () => { Vm()?.MoveSquadsToSecondArmy(); SpeakSize(Second()); }));
            }

            // ---- window actions ----
            b.BeginStop("actions");
            bool idle = second == null && vm.SetLeaderVM.Value == null; // the view's CanCreateArmy
            if (idle)
                b.AddItem(ControlId.Structural("ai:split"), GraphNodes.Button(
                    () => UIStrings.Instance.ContextMenu.Split,
                    Split, sound: UISoundType.ArmyManagementUniteArmiesPlay));
            if (idle && vm.CanRecruit.Value)
                b.AddItem(ControlId.Structural("ai:recruit"), GraphNodes.Button(() => ct.Recruit, () => Vm()?.OnRecruit()));
            b.AddItem(ControlId.Structural("ai:close"), GraphNodes.Button(() => Loc.T("action.close"), () => Main()?.OnClose()));
        }

        // Split: the game adds an EMPTY new army as the second cart (merge view) — the Split button then
        // hides, so say what happened and land on the new army's stop. An army still empty when the
        // window closes is deleted by the game, so the player has to move squads into it.
        private static void Split()
        {
            var vm = Vm();
            if (vm == null) return;
            vm.CreateArmy();
            var made = Second();
            if (made == null) return;
            Tts.Speak(Loc.T("crusade.army_created", new { name = made.ArmyName.Value }), interrupt: true);
            Navigation.FocusStop("second");
        }

        // A cart's own buttons: the editable name field and the dismiss-army button.
        private static void CartButtons(GraphBuilder b, Func<ArmyInfoArmyCartVM> get, string k)
        {
            b.AddItem(ControlId.Structural(k + "rename"), GraphNodes.Button(() => Loc.T("crusade.rename"), () =>
            {
                var c = get();
                if (c == null) return;
                ModTextEntryScreen.Open(Loc.T("crusade.rename"), c.ArmyName.Value ?? "", s =>
                {
                    if (!string.IsNullOrEmpty(s)) get()?.SetArmyName(s);
                });
            }));
            b.AddItem(ControlId.Structural(k + "dismiss"), GraphNodes.Button(
                () => Loc.T("crusade.dismiss_army"), () => get()?.DismissAll()));
        }

        // A squad's actions, as the PC squad slot offers them (double-click, shift / ctrl / alt-click, and
        // its dismiss button). The menu re-reads the squad on each pick, so stale rows can't act.
        private static void SquadMenu(ArmyInfoSquadVM cell)
        {
            if (cell == null || cell.IsDisposed || !cell.HasSquad.Value) return;
            var labels = new List<string>();
            var acts = new List<Action>();
            var other = OtherArmyName(cell);
            if (other != null)
            {
                labels.Add(Loc.T("crusade.squad_move", new { name = other }));
                acts.Add(cell.MoveToAnotherArmy);
            }
            if (cell.Count.Value > 1)
            {
                labels.Add(Loc.T("crusade.split_half")); acts.Add(cell.SplitHalf);
                labels.Add(Loc.T("crusade.split_one")); acts.Add(cell.SplitOne);
            }
            labels.Add(Loc.T("crusade.merge_one")); acts.Add(cell.MergeInOne);
            if (cell.CanDismiss.Value)
            {
                labels.Add(UIStrings.Instance.GroupChangerTexts.DismissCharacter); acts.Add(cell.Dismiss);
            }
            ChoiceSubmenuScreen.Open(Loc.T("worldmap.squad", new { name = (string)cell.Squad.Unit.CharacterName, count = cell.Count.Value }),
                labels, -1, i => { if (!cell.IsDisposed) acts[i](); });
        }

        // While merging: the name of the army a squad would move to (null when there's no second army).
        private static string OtherArmyName(ArmyInfoSquadVM cell)
        {
            var main = Main();
            var second = Second();
            if (main == null || second == null || cell.Squad == null) return null;
            if (cell.Squad.Army == main.State.Data) return second.ArmyName.Value;
            if (cell.Squad.Army == second.State.Data) return main.ArmyName.Value;
            return null;
        }

        // After a move-all: where the squads ended up ("Crusader Army I, 5 squads").
        private static void SpeakSize(ArmyInfoArmyCartVM cart)
        {
            if (cart?.State?.Data == null) return;
            Tts.Speak(Loc.T("crusade.army_squads", new { name = cart.ArmyName.Value, count = cart.State.Data.Squads.Count }), interrupt: true);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.OnClose());
        }
    }
}
