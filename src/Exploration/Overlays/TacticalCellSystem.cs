using System.Collections.Generic;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The crusade battle grid's readout — a Tactical-scoped system over <see cref="Cursor.Tactical"/>:
    /// what stands in the cell under the cursor (a squad: its name, whose side, head count, and whether it
    /// is the squad acting now; an obstacle; or nothing), then the cell's coordinate. Columns are letters
    /// left to right (A–I), rows numbers bottom to top (1–7), chessboard style, so a cell can be named
    /// and found again. Read on every grid step and on demand; the sighted see all of this on the field.
    /// </summary>
    internal sealed class TacticalCellSystem : OverlaySystem
    {
        public override string Name => "Battle grid readout";
        public override string Key => "tactical_cell";
        public override OverlayScope Scope => OverlayScope.Tactical;
        public override IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;
        protected override bool MovingNow(Overlay overlay) => Cursor.Tactical.MovingRecently;

        public override IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (!Enabled || !TacticalModel.Active || ctx.Trigger == ReadoutTrigger.Settle) yield break;
            if (!(Cursor.Tactical.Space is TacticalSpace space)) yield break;
            var cell = space.Cell;
            yield return new OverlayAnnouncement(ctx.Want, Message.Raw(Contents(cell)), OverlayAnnouncement.OrderContents);
            yield return new OverlayAnnouncement(ctx.Want, Message.Raw(Coordinate(cell)), OverlayAnnouncement.OrderContents + 1);
        }

        /// <summary>The cell's contents as one phrase.</summary>
        public static string Contents(UnityEngine.Vector2Int cell)
        {
            var squad = TacticalModel.SquadAt(cell);
            if (squad != null)
            {
                var parts = new List<string> { squad.CharacterName };
                parts.Add(Loc.T(TacticalModel.IsOwn(squad) ? "tactical.own" : "tactical.enemy"));
                var part = TacticalModel.Part(squad);
                if (part != null) parts.Add(Loc.T("tactical.count", new { count = part.Count }));
                // The bar over the squad: the front creature's hit points (the head count is above).
                parts.Add(Loc.T("tactical.hp", new { current = squad.HPLeft, max = squad.MaxHP }));
                if (squad == TacticalModel.ActingSquad) parts.Add(Loc.T("tactical.acting"));
                // The game's attack tooltip for this squad (or the aimed spell's), when our squad is acting.
                var prediction = TacticalPrediction.For(squad);
                if (prediction.HasValue) parts.Add(TacticalPrediction.Speak(prediction.Value));
                else if (!TacticalModel.IsOwn(squad) && TacticalPrediction.OurActor != null
                    && Kingmaker.Game.Instance.SelectedAbilityHandler?.SelectedAbility == null)
                    parts.Add(Loc.T("tactical.out_of_reach"));
                return string.Join(", ", parts);
            }
            if (TacticalModel.ObstacleAt(cell)) return Loc.T("tactical.obstacle");
            // The game highlights where the acting squad can move: say so on an empty cell in reach.
            var reach = Kingmaker.Game.Instance.SelectedAbilityHandler?.SelectedAbility == null ? TacticalPrediction.Reachable() : null;
            return reach != null && reach.Contains(cell)
                ? Loc.T("tactical.empty") + ", " + Loc.T("tactical.in_reach")
                : Loc.T("tactical.empty");
        }

        /// <summary>"C5": column letter (left to right on screen), row number (bottom to top on screen).</summary>
        public static string Coordinate(UnityEngine.Vector2Int cell)
        {
            var sc = TacticalModel.ScreenCell(cell); // columns left to right and rows bottom to top AS SEEN
            return Loc.T("tactical.coordinate", new { column = ((char)('A' + sc.x)).ToString(), row = sc.y + 1 });
        }
    }
}
