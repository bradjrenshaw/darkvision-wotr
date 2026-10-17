using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using UnityEngine;
using WrathAccess.Exploration.Overlays;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The crusade battle's review cycles — the same keys as everywhere else: period = enemy squads, comma =
    /// our squads (Shift goes backward), nearest first from the grid cursor, which does NOT move (look
    /// without moving). Each landing reads the squad like the cell readout plus its coordinate and its
    /// place in the list, and plays the review ping at it, heard from the cursor. Home / Slash then plant
    /// the cursor on it. The selection is kept by squad, so the cycle steps on from it even after squads move.
    /// </summary>
    internal static class TacticalReview
    {
        private static UnitEntityData _selected;

        public static void EnemyNext() => Cycle(false, +1);
        public static void EnemyPrev() => Cycle(false, -1);
        public static void OwnNext() => Cycle(true, +1);
        public static void OwnPrev() => Cycle(true, -1);

        private static Vector2Int CursorCell
            => Overlays.Cursor.Tactical.Space is TacticalSpace s ? s.Cell : TacticalModel.HomeCell;

        private static void Cycle(bool own, int dir)
        {
            if (!TacticalModel.Active) return;
            var from = CursorCell;
            var list = TacticalModel.Squads()
                .Where(u => TacticalModel.IsOwn(u) == own && TacticalModel.CellOf(u).HasValue)
                .OrderBy(u => Distance(from, TacticalModel.CellOf(u).Value))
                .ThenBy(u => TacticalModel.CellOf(u).Value.x)
                .ToList();
            if (list.Count == 0) { Tts.Speak(Loc.T(own ? "tactical.no_own" : "tactical.no_enemies"), interrupt: true); return; }
            int i = _selected != null ? list.IndexOf(_selected) : -1;
            i = i < 0 ? (dir > 0 ? 0 : list.Count - 1) : Mathf.Clamp(i + dir, 0, list.Count - 1);
            _selected = list[i];
            var cell = TacticalModel.CellOf(_selected).Value;
            Tts.Speak(TacticalCellSystem.Contents(cell) + ", " + TacticalCellSystem.Coordinate(cell) + ", "
                + Loc.T("nav.position", new { index = i + 1, count = list.Count }), interrupt: true);
            Ping(cell);
        }

        /// <summary>Home / Slash: plant the grid cursor on the reviewed squad and read the cell.</summary>
        public static void JumpToReview()
        {
            if (!TacticalModel.Active) return;
            var cell = TacticalModel.CellOf(_selected);
            if (_selected == null || _selected.State.IsDead || !cell.HasValue) { Tts.Speak(Loc.T("tactical.no_review"), interrupt: true); return; }
            if (Overlays.Cursor.Tactical.Space is TacticalSpace s) s.SetCell(cell.Value);
            Overlays.Cursor.Tactical.AnnounceCurrent();
        }

        /// <summary>Y: the reviewed squad's stats — the game's own squad tooltip (what hovering it shows:
        /// <c>TooltipTemplateTacticalCombatUnit</c>, the initiative tracker's template), in the tooltip reader.</summary>
        public static void InspectReview()
        {
            if (!TacticalModel.Active) return;
            if (_selected == null || _selected.State.IsDead) { Tts.Speak(Loc.T("tactical.no_review"), interrupt: true); return; }
            // The game's right-click inspect (TacticalCombatInspectVM.OnUnitTacticalCombatRightClick):
            // the squad template in the Info window, read by InfoWindowScreen.
            Kingmaker.UI.MVVM._VM.Tooltip.Utils.TooltipHelper.ShowInfo(
                new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateTacticalCombatUnit(_selected));
        }

        /// <summary>I: click the reviewed squad, as a mouse click on it would.</summary>
        public static void ClickReview()
        {
            if (!TacticalModel.Active) return;
            var cell = TacticalModel.CellOf(_selected);
            if (_selected == null || _selected.State.IsDead || !cell.HasValue) { Tts.Speak(Loc.T("tactical.no_review"), interrupt: true); return; }
            Click(cell.Value);
        }

        /// <summary>Enter: click the cell under the grid cursor.</summary>
        public static void ClickCursorCell()
        {
            if (!TacticalModel.Active) return;
            Click(CursorCell);
        }

        // The game's battle-ground click (TacticalCombatClickGroundHandler): its GetPriority gates first —
        // the acting squad must be ours, the battle idle, the action unspent — then its OnClick, which on a
        // squad cell picks the cell to strike from and attacks (ranged squads shoot in place), and on an
        // empty cell moves as far as the squad's movement allows. The click point leans from the clicked
        // cell's centre toward the acting squad, so an attack approaches from the near side (the game
        // chooses the side by where on the target the pointer sits).
        private static void Click(Vector2Int cell)
        {
            // A spell or ability waiting for its target (picked on a HUD bar): this click aims it, through
            // the game's own selected-ability handler (its target rules: a squad for unit spells, the cell
            // for area ones). The squad's view stands in for "the thing under the pointer".
            var aim = Kingmaker.Game.Instance?.SelectedAbilityHandler;
            if (aim?.SelectedAbility != null)
            {
                var squad = TacticalModel.SquadAt(cell);
                var go = squad?.View != null ? squad.View.gameObject : null;
                if (!aim.OnClick(go, TacticalModel.CellCenter(cell), 0)) Tts.Speak(Loc.T("tactical.cant_target"), interrupt: true);
                return;
            }
            var data = Kingmaker.Game.Instance.TacticalCombat?.Data;
            var actor = data?.Turn?.Unit;
            if (data == null || actor == null || !actor.IsDirectlyControllable) { Tts.Speak(Loc.T("tactical.not_your_turn"), interrupt: true); return; }
            if (data.IsBusy || data.Turn.StandardActionUsed || actor.Commands.HasUnfinished()) { Tts.Speak(Loc.T("tactical.busy"), interrupt: true); return; }
            var at = TacticalModel.CellCenter(cell);
            var toward = actor.Position - at; toward.y = 0f;
            if (toward.sqrMagnitude > 1e-4f) at += toward.normalized * 0.5f;
            bool ok = new Kingmaker.Armies.TacticalCombat.Controllers.TacticalCombatClickGroundHandler().OnClick(null, at, 0);
            if (!ok) Tts.Speak(Loc.T("tactical.cant_act"), interrupt: true);
        }

        // Chebyshev cells: the grid allows diagonal steps, so a diagonal neighbour is one cell away.
        private static int Distance(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        // The review ping at the squad, heard from the cursor's cell, through the engaged overlay's sonar
        // sound pick (a straight line: nothing on the open field blocks sight). Silent pick = no ping.
        private static void Ping(Vector2Int cell)
        {
            var overlay = OverlayManager.ActiveOverlay ?? OverlaySettingsRegistry.DefaultOverlay;
            var stem = overlay?.Get<SonarSystem>()?.ReviewStem("straight");
            if (stem == null) return;
            var from = TacticalModel.CellCenter(CursorCell);
            var at = TacticalModel.CellCenter(cell);
            SonarSystem.PlayTacticalAt(Path.Combine(OverlayAudio.Dir, stem + ".wav"), from, at, overlay?.Get<SonarSystem>());
        }
    }
}
