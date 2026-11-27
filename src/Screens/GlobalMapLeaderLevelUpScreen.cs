using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI.MVVM._VM.Crusade.ArmyInfo; // ArmyLeaderSkillVM
using Kingmaker.UI.MVVM._VM.Crusade.LeaderLevelUp; // LeaderLevelUpVM
using WrathAccess.Exploration;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A crusade general's LEVEL-UP window (<see cref="LeaderLevelUpVM"/>; raised by the Level up button
    /// on an army cart's general, ILeaderLevelupHandler). One screen, laid out as the prefab is: the
    /// general's card on the left (the level change, each stat with its rise at the new level, the
    /// skills already known), then the right-hand block — "Choose ability" (pick one; Space = the
    /// skill's tooltip) above the guaranteed ability when the level grants one — then Accept and Close.
    /// Accept needs a pick, as the PC button does (CanConfirm = SelectedSkill != null). Escape closes.
    /// </summary>
    public sealed class GlobalMapLeaderLevelUpScreen : Screen
    {
        public GlobalMapLeaderLevelUpScreen() { Wrap = true; }

        public override string Key => "overlay.worldmap_leader_levelup";
        public override string ScreenName => UIStrings.Instance.CharGen.LevelUp;
        public override int Layer => 18; // over the army window and its general panels

        private static LeaderLevelUpVM Vm()
        {
            var vm = GlobalMapModel.CurrentVM?.LeaderLevelUpVM?.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var ct = UIStrings.Instance.CrusadeTexts;

            // ---- the general's card ----
            b.BeginStop("general").PushContext(vm.LeaderCommonVM.Name.Value ?? "", "group");
            b.AddItem(ControlId.Structural("lu:level"), GraphNodes.Text(
                () => Loc.T("crusade.level_change", new { from = vm.CurrentLevel, to = vm.CurrentLevel + 1 })));
            GeneralNodes.Rows(b, "lu", vm.StatsVM, vm.SkillsVM);
            b.PopContext();

            // ---- choose an ability ----
            if (vm.SkillsToChoose.Count > 0)
            {
                b.BeginStop("choose").PushContext(ct.ChooseAbility, "list");
                for (int i = 0; i < vm.SkillsToChoose.Count; i++)
                    b.AddItem(ControlId.Referenced(vm.SkillsToChoose[i], "lu:choose:" + i), Choice(vm.SkillsToChoose[i]));
                b.PopContext();
            }

            // ---- the guaranteed ability ----
            var g = vm.GuaranteedSkillVM;
            if (g != null)
            {
                b.BeginStop("guaranteed").PushContext(ct.GuaranteedAbilityText, "group");
                b.AddItem(ControlId.Structural("lu:guaranteed"), GraphNodes.Text(() => g.Name.Value, () => g.GetTooltipTemplate()));
                b.PopContext();
            }

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural("lu:accept"), GraphNodes.Button(
                () => UIStrings.Instance.CommonTexts.Accept,
                () => { var v = Vm(); if (v != null && v.SelectedSkill.Value != null) v.Confirm(); }, // Confirm plays its own sound
                () => Vm()?.SelectedSkill.Value != null, sound: null));
            b.AddItem(ControlId.Structural("lu:close"), GraphNodes.Button(() => Loc.T("action.close"), () => Vm()?.Close()));
        }

        // A skill on offer: Enter picks it (the skill view's click), "selected" follows the VM's pick.
        private static NodeVtable Choice(ArmyLeaderSkillVM s)
        {
            var vt = GraphNodes.Button(() => s.Name.Value, () => s.ConfirmAction());
            vt.Announcements = new List<NodeAnnouncement>(vt.Announcements)
            {
                GraphNodes.SelectedPart(() => Vm()?.SelectedSkill.Value == s.Skill),
            };
            vt.OnTooltip = () => { var t = s.GetTooltipTemplate(); if (t != null) TooltipScreen.Open(t); };
            return vt;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }
    }
}
