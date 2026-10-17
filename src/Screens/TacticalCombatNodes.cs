using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (the game's own wait / total defence names)
using Kingmaker.UI.MVVM._VM.TacticalCombat;
using Kingmaker.UI.MVVM._VM.TacticalCombat.ActionBar;
using Kingmaker.UI.MVVM._VM.TacticalCombat.InitiativeTracker;
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlot
using WrathAccess.Exploration;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The crusade battle's HUD, as three tab stops after the grid (which owns the movement keys while
    /// nothing is focused): the ACTING squad's action bar (its abilities, then the Wait and Total Defense
    /// buttons the bar carries), the GENERAL's action bar (mana, the spells on the bar, then the spellbook
    /// the bar's side tab opens), and the INITIATIVE tracker (this round in order, then the next).
    /// Everything reads live from the game's own VMs on <c>TacticalCombatVM</c>; slots activate through
    /// the slot VM's own click (an aimed spell then waits for a target — Enter on a grid cell).
    /// </summary>
    internal static class TacticalCombatNodes
    {
        private static TacticalCombatVM Vm => Game.Instance?.RootUiContext?.TacticalCombatVM;

        public static void Build(GraphBuilder b)
        {
            var vm = Vm;
            if (vm == null) return;
            UnitBar(b, vm.UnitActionBarVM.Value);
            LeaderBar(b, vm.LeaderActionBarVM.Value, vm.OwnLeaderVM.Value, vm.EnemyLeaderVM.Value);
            Tracker(b, vm.TacticalCombatInitiativeTrackerVM.Value);
            Toolbar(b, vm);
        }

        // ---- the corner buttons + the hospital bar ----
        // The battle's own toggles and retreat, with their hover tooltips, and the infirmary bar (how many
        // fallen troops will recover after the battle) — one stop, in the order they sit on screen.
        private static void Toolbar(GraphBuilder b, TacticalCombatVM vm)
        {
            var ct = UIStrings.Instance.CrusadeTexts;
            b.BeginStop("toolbar").PushContext(Loc.T("tactical.toolbar"), "list");
            var hospital = vm.HospitalProgressBarVM.Value;
            if (hospital != null)
                b.AddItem(ControlId.Structural("tc:bar:hospital"), GraphNodes.Text(
                    () => Loc.T("tactical.hospital", new { current = hospital.CurrentHospitalValue.Value, max = hospital.MaxHospitalValue.Value }),
                    () => hospital.TooltipTemplate()));
            b.AddItem(ControlId.Structural("tc:bar:auto"), WithTooltip(GraphNodes.Toggle(
                () => ct.AutoCombatTooltipName, () => vm.IsAutoCombat.Value, vm.ToggleAutoCombat),
                () => new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateSimple(ct.AutoCombatTooltipName, ct.AutoCombatTooltipDesc)));
            b.AddItem(ControlId.Structural("tc:bar:fast"), WithTooltip(GraphNodes.Toggle(
                () => ct.AccelerationTooltipName, () => vm.IsAccelerated.Value, vm.ToggleAcceleration),
                () => new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateSimple(ct.AccelerationTooltipName, ct.AccelerationTooltipDesc)));
            b.AddItem(ControlId.Structural("tc:bar:inspect"), WithTooltip(GraphNodes.Toggle(
                () => Loc.T("tactical.inspect_toggle"), () => vm.IsInspect.Value, vm.SwitchInspect),
                () => new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateGlossary("OpenTacticalCombatInspect")));
            b.AddItem(ControlId.Structural("tc:bar:retreat"), WithTooltip(GraphNodes.Button(
                () => ct.RetreatTooltipName, vm.FleeFromBattle), // the game's own confirmation follows
                () => new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateSimple(ct.RetreatTooltipName, ct.RetreatTooltipDesc)));
            b.PopContext();
        }

        // ---- the acting squad's bar ----
        private static void UnitBar(GraphBuilder b, UnitCrusadeActionBarVM bar)
        {
            if (bar == null) return;
            var squad = TacticalModel.ActingSquad;
            b.BeginStop("unitbar").PushContext(squad != null
                ? Loc.T("tactical.unit_bar", new { name = squad.CharacterName })
                : Loc.T("tactical.unit_bar_none"), "list");
            int i = 0;
            foreach (var s in bar.Slots)
            {
                if (s == null || s.IsEmpty) { i++; continue; }
                b.AddItem(ControlId.Structural("tc:unit:" + i), Slot(s));
                Variants(b, s, "tc:unit:" + i);
                i++;
            }
            b.AddItem(ControlId.Structural("tc:unit:hold"), WithTooltip(GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.WaitTooltipName, bar.OnHold, () => bar.CanHold.Value),
                () => bar.HoldButtonTooltipTemplate));
            b.AddItem(ControlId.Structural("tc:unit:defense"), WithTooltip(GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.TotalDefenseTooltipName, bar.OnDefense),
                () => bar.DefenseButtonTooltipTemplate));
            b.PopContext();
        }

        // ---- the general's bar + spellbook ----
        private static void LeaderBar(GraphBuilder b, LeaderCrusadeActionBarVM bar, TacticalCombatLeaderVM leader, TacticalCombatLeaderVM enemy)
        {
            if (bar == null || leader == null) return;
            b.BeginStop("leaderbar").PushContext(Loc.T("tactical.leader_bar", new { name = leader.GetLeaderName() }), "list");
            b.AddItem(ControlId.Structural("tc:leader:mana"), GraphNodes.Text(
                () => Loc.T("worldmap.size_line", new { name = (string)UIStrings.Instance.CrusadeTexts.MagicReserves, current = leader.CurrentManaValue.Value, max = leader.GetLeaderMaxMana() })));
            // The portrait's click: the general's card (level, stats, skills) in its own window.
            b.AddItem(ControlId.Structural("tc:leader:card"), GraphNodes.Button(() => Loc.T("tactical.leader_card"), leader.RequestLeaderInfo));
            var onBar = new HashSet<MechanicActionBarSlot>();
            int i = 0;
            foreach (var s in bar.Slots)
            {
                if (s == null || s.IsEmpty) { i++; continue; }
                onBar.Add(s.MechanicActionBarSlot);
                b.AddItem(ControlId.Structural("tc:leader:" + i), Slot(s));
                Variants(b, s, "tc:leader:" + i);
                i++;
            }
            // The spellbook (the bar's side tab): every spell the general knows, bar or not.
            if (bar.RitualBookSlots != null && bar.RitualBookSlots.Count > 0)
            {
                b.PushContext(Loc.T("tactical.spellbook"), "list");
                int j = 0;
                foreach (var s in bar.RitualBookSlots)
                {
                    if (s != null && !s.IsEmpty) b.AddItem(ControlId.Structural("tc:book:" + j), Slot(s));
                    j++;
                }
                b.PopContext();
            }
            // The opposing general's panel, when the enemy army has one: name, mana, card.
            if (enemy != null)
            {
                b.PushContext(Loc.T("tactical.enemy_leader", new { name = enemy.GetLeaderName() }), "list");
                b.AddItem(ControlId.Structural("tc:enemy:mana"), GraphNodes.Text(
                    () => Loc.T("worldmap.size_line", new { name = (string)UIStrings.Instance.CrusadeTexts.MagicReserves, current = enemy.CurrentManaValue.Value, max = enemy.GetLeaderMaxMana() })));
                b.AddItem(ControlId.Structural("tc:enemy:card"), GraphNodes.Button(() => Loc.T("tactical.leader_card"), enemy.RequestLeaderInfo));
                b.PopContext();
            }
            b.PopContext();
        }

        // ---- initiative ----
        private static void Tracker(GraphBuilder b, TacticalCombatInitiativeTrackerVM tr)
        {
            if (tr == null) return;
            b.BeginStop("initiative").PushContext(Loc.T("tactical.initiative", new { round = tr.RoundNumber.Value }), "list");
            int i = 0;
            foreach (var u in tr.CurrentRoundUnits)
            {
                if (u?.UnitData != null) b.AddItem(ControlId.Structural("tc:init:" + i), TrackerEntry(u));
                i++;
            }
            if (tr.NextRoundUnits != null && tr.NextRoundUnits.Count > 0)
            {
                b.PushContext(Loc.T("tactical.next_round", new { round = tr.RoundNumber.Value + 1 }), "list");
                int j = 0;
                foreach (var u in tr.NextRoundUnits)
                {
                    if (u?.UnitData != null) b.AddItem(ControlId.Structural("tc:next:" + j), TrackerEntry(u));
                    j++;
                }
                b.PopContext();
            }
            b.PopContext();
        }

        // A squad in the turn order: name, side, head count, acting now; Enter moves the grid cursor onto
        // it; Space = its stats (the tracker's own tooltip).
        private static NodeVtable TrackerEntry(TacticalCombatInitiativeTrackerUnitBaseVM u)
        {
            Func<string> label = () =>
            {
                var parts = new List<string> { u.UnitName.Value };
                parts.Add(Loc.T(u.IsEnemy.Value ? "tactical.enemy" : "tactical.own"));
                parts.Add(Loc.T("tactical.count", new { count = u.UnitCount.Value }));
                if (u.IsCurrent.Value) parts.Add(Loc.T("tactical.acting"));
                var cell = TacticalModel.CellOf(u.UnitData);
                if (cell.HasValue) parts.Add(WrathAccess.Exploration.Overlays.TacticalCellSystem.Coordinate(cell.Value));
                return string.Join(", ", parts);
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement> { GraphNodes.LabelPart(label) },
                SearchText = () => u.UnitName.Value,
                OnActivate = () =>
                {
                    var cell = TacticalModel.CellOf(u.UnitData);
                    if (!cell.HasValue) return;
                    if (WrathAccess.Exploration.Overlays.Cursor.Tactical.Space is WrathAccess.Exploration.Overlays.TacticalSpace s) s.SetCell(cell.Value);
                    Tts.Speak(Loc.T("tactical.cursor_moved", new { cell = WrathAccess.Exploration.Overlays.TacticalCellSystem.Coordinate(cell.Value) }), interrupt: true);
                },
                OnTooltip = () => { var t = u.TooltipTemplate(); if (t != null) TooltipScreen.Open(t); },
            };
        }

        // One battle action-bar slot (unit ability or general's spell): title, uses left, greyed state;
        // Enter = the slot VM's own click (a spell that needs aiming then waits for Enter on a grid cell;
        // a slot with variants opens them first). Space = the slot's tooltip.
        private static NodeVtable Slot(ActionBarSlotVM vm)
        {
            Func<MechanicActionBarSlot> slot = () => vm.MechanicActionBarSlot;
            Func<bool> enabled = () => slot() != null && slot().IsPossibleActive();
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => slot()?.GetTitle() ?? ""),
                    new NodeAnnouncement(() =>
                    {
                        var s = slot();
                        var count = s?.GetCountText(s.GetResource());
                        return string.IsNullOrEmpty(count) ? null : Loc.T("tactical.uses", new { count });
                    }, kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => vm.IsActive.Value ? Loc.T("value.on") : null, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(enabled),
                },
                SearchText = () => slot()?.GetTitle() ?? "",
                OnActivate = () =>
                {
                    if (!enabled()) { Tts.Speak(Loc.T("tactical.slot_unavailable"), interrupt: true); return; }
                    bool hadVariants = vm.HasConvert.Value;
                    vm.OnClick();
                    if (hadVariants && vm.ConvertedVM.Value != null && !vm.ConvertedVM.Value.IsDisposed)
                        Tts.Speak(Loc.T("tactical.variants_open"), interrupt: false);
                    // An aimed ability is now waiting for its target: say how to give it.
                    // Leave the HUD so the movement keys and Enter work the grid for the aim.
                    if (Game.Instance?.SelectedAbilityHandler?.SelectedAbility != null)
                    {
                        Navigation.Blur();
                        Tts.Speak(Loc.T("tactical.choose_target"), interrupt: false);
                    }
                },
                OnTooltip = () => { var t = slot()?.GetTooltipTemplate(); if (t != null) TooltipScreen.Open(t); },
            };
        }

        // A slot with variants opens them as a small bar of their own when clicked (the game's convert
        // popup): list them right under it while it's open; picking one acts like any slot.
        private static void Variants(GraphBuilder b, ActionBarSlotVM s, string k)
        {
            var conv = s.ConvertedVM.Value;
            if (conv == null || conv.IsDisposed || conv.Slots == null || conv.Slots.Count == 0) return;
            b.PushContext(Loc.T("tactical.variants"), "list");
            for (int v = 0; v < conv.Slots.Count; v++)
            {
                var vs = conv.Slots[v];
                if (vs != null && !vs.IsEmpty) b.AddItem(ControlId.Structural(k + ":v:" + v), Slot(vs));
            }
            b.PopContext();
        }

        private static NodeVtable WithTooltip(NodeVtable vt, Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> tpl)
        {
            vt.OnTooltip = () => { var t = tpl(); if (t != null) TooltipScreen.Open(t); };
            return vt;
        }
    }
}
