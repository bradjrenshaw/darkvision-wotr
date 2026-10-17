using System.Collections.Generic;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Armies;                                      // ArmyLeader, LevelBorder
using Kingmaker.Blueprints.Root.Strings;                     // UIStrings
using Kingmaker.PubSubSystem;                                // EventBus, IEscMenuHandler
using Kingmaker.UI.MVVM._VM.TacticalCombat;                  // LeaderCardVM
using Kingmaker.UI.MVVM._VM.TacticalCombat.Result;           // TacticalCombatResultsVM, TacticalCombatResultUnitVM
using WrathAccess.Input;                                     // InputCategory
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A general's card in battle (<see cref="LeaderCardVM"/>, opened by the general's portrait — our own
    /// or the enemy's): name, level and experience, the five stats, magic reserves and skills (the same
    /// rows as the army panels' cards, <see cref="GeneralNodes.Rows"/>), then Close. Escape closes.
    /// </summary>
    public sealed class TacticalLeaderCardScreen : Screen
    {
        public override string Key => "overlay.tactical_leader_card";
        public override string ScreenName => Vm()?.CommonVM?.Name.Value ?? "";
        public override int Layer => 16;
        public override bool Exclusive => true;
        public override IReadOnlyList<InputCategory> InputCategories => UiOnlyList;
        private static readonly IReadOnlyList<InputCategory> UiOnlyList = new[] { InputCategory.UI };

        private static LeaderCardVM Vm() => Game.Instance?.RootUiContext?.TacticalCombatVM?.LeaderCardVM?.Value;
        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            b.BeginStop("card").PushContext(vm.CommonVM?.Name.Value ?? "", "group");
            b.AddItem(ControlId.Structural("tlc:level"), GraphNodes.Text(() =>
            {
                var e = vm.ExpLevel.Value;
                string level = Loc.T("crusade.level", new { level = vm.Level.Value });
                return level + ", " + (e.Last ? Loc.T("crusade.exp_last", new { current = e.Current })
                                              : Loc.T("crusade.exp", new { current = e.Current, max = e.Max }));
            }));
            GeneralNodes.Rows(b, "tlc", vm.StatsVM, vm.SkillsVM);
            b.PopContext();
            b.BeginStop("close").AddItem(ControlId.Structural("tlc:close"), GraphNodes.Button(
                () => UIStrings.Instance.CommonTexts.Close, vm.OnClose));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.OnClose());
        }
    }

    /// <summary>
    /// The battle results (<see cref="TacticalCombatResultsVM"/>) — after a crusade battle, and the
    /// world map's auto-combat prediction (the same window, with a Start manual combat button). Mirrors
    /// the window: the outcome (victory / defeat, retreated), experience and resources gained, both
    /// generals, our losses, the enemy's losses, the troops the infirmary will restore, then Accept
    /// (and Start manual combat when offered). Escape accepts, as the game's Esc does.
    /// </summary>
    public sealed class TacticalResultsScreen : Screen
    {
        public override string Key => "overlay.tactical_results";
        public override string ScreenName => Loc.T("tactical.results");
        public override int Layer => 17;
        public override bool Exclusive => true;
        public override IReadOnlyList<InputCategory> InputCategories => UiOnlyList;
        private static readonly IReadOnlyList<InputCategory> UiOnlyList = new[] { InputCategory.UI };

        private static TacticalCombatResultsVM Vm()
        {
            var rc = Game.Instance?.RootUiContext;
            return rc?.TacticalCombatVM?.TacticalCombatResultsVM?.Value ?? rc?.GlobalMapVM?.AutoCombatResultsVM?.Value;
        }
        public override bool IsActive() { var vm = Vm(); return vm != null && !vm.IsDisposed; }

        private static readonly System.Reflection.FieldInfo NameF = AccessTools.Field(typeof(TacticalCombatResultUnitVM), "m_Name");

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            b.BeginStop("outcome").PushContext(Loc.T("tactical.results"), "list");
            b.AddItem(ControlId.Structural("tr:outcome"), GraphNodes.Text(() => Loc.T(vm.IsWin ? "tactical.victory" : "tactical.defeat")
                + (vm.IsRetreat ? ", " + Loc.T("tactical.retreated") : "")));
            if (vm.IsWin)
                b.AddItem(ControlId.Structural("tr:exp"), GraphNodes.Text(() => Loc.T("tactical.exp_gained", new { amount = vm.CombatExperience })));
            var res = vm.CombatResourceStats != null ? vm.CombatResourceStats.ResourcesOneTime : default;
            if (vm.CombatResourceStats != null && res.Any())
                b.AddItem(ControlId.Structural("tr:res"), GraphNodes.Text(() => Loc.T("tactical.resources",
                    new { finances = res.Finances, materials = res.Materials, favors = res.Favors })));
            b.PopContext();

            if (vm.OurGeneral != null || vm.EnemyGeneral != null)
            {
                b.BeginStop("generals").PushContext(Loc.T("tactical.generals"), "list");
                if (vm.OurGeneral != null)
                    b.AddItem(ControlId.Structural("tr:ours"), GraphNodes.Text(() => GeneralLine(vm.OurGeneral, false, vm)));
                if (vm.EnemyGeneral != null)
                    b.AddItem(ControlId.Structural("tr:theirs"), GraphNodes.Text(() => GeneralLine(vm.EnemyGeneral, true, vm)));
                b.PopContext();
            }

            Squads(b, "ourloss", Loc.T("tactical.our_losses"), vm.CrusadeSide);
            Squads(b, "theirloss", Loc.T("tactical.enemy_losses"), vm.DemonSide);
            Squads(b, "restored", Loc.T("tactical.restored"), vm.Resurrected);

            b.BeginStop("buttons").PushContext("", "list");
            b.AddItem(ControlId.Structural("tr:accept"), GraphNodes.Button(() => UIStrings.Instance.CommonTexts.Accept, vm.Close));
            if (vm.CanStartManualCombat)
                b.AddItem(ControlId.Structural("tr:manual"), GraphNodes.Button(() => UIStrings.Instance.CrusadeTexts.ManualCombatText, vm.StartManualCombat));
            b.PopContext();
        }

        // One squad list (the window's row of portraits with counts): only filled slots with a count, as shown.
        private static void Squads(GraphBuilder b, string k, string title, List<TacticalCombatResultUnitVM> list)
        {
            var rows = new List<TacticalCombatResultUnitVM>();
            if (list != null) foreach (var u in list) if (u != null && !u.IsEmpty && u.Count != 0) rows.Add(u);
            b.BeginStop(k).PushContext(title, "list");
            if (rows.Count == 0)
                b.AddItem(ControlId.Structural("tr:" + k + ":none"), GraphNodes.Text(() => Loc.T("tactical.none")));
            for (int i = 0; i < rows.Count; i++)
            {
                var u = rows[i];
                b.AddItem(ControlId.Structural("tr:" + k + ":" + i), GraphNodes.Text(
                    () => Loc.T("tactical.result_squad", new { name = NameF?.GetValue(u) as string ?? "", count = u.Count })));
            }
            b.PopContext();
        }

        private static string GeneralLine(ArmyLeader g, bool enemy, TacticalCombatResultsVM vm)
        {
            var parts = new List<string> { g.LocalizedName, Loc.T("crusade.level", new { level = g.Level }) };
            if (!enemy && vm.IsWin)
            {
                var e = g.GetLevelBorder();
                parts.Add(e.Last ? Loc.T("crusade.exp_last", new { current = e.Current }) : Loc.T("crusade.exp", new { current = e.Current, max = e.Max }));
            }
            parts.Add(Loc.T("worldmap.size_line", new { name = (string)UIStrings.Instance.CrusadeTexts.MagicReserves, current = g.Stats.CurrentMana, max = (int)g.Stats.MaxMana }));
            if (enemy && vm.IsWin) parts.Add(UIStrings.Instance.CombatTexts.HPDead);
            return string.Join(", ", parts);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }
    }
}
