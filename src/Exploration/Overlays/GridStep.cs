using UnityEngine;
using WrathAccess.Input; // OsKeyboard (typematic cadence)

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The crusade battle's one movement mode: a cell of the game's battle grid per press (two keys
    /// together = a diagonal, which the grid allows), on the user's typematic cadence while held — the same
    /// held-vector polling as <see cref="TileStep"/>, but over the grid's own cells instead of a tile size
    /// from settings. Running into the field's edge doesn't move and says so. The landing readout is the
    /// engaged overlay's job (<see cref="TacticalCellSystem"/>).
    /// </summary>
    internal sealed class GridStep : MovementMode
    {
        private readonly MovementSlot _slot;
        public GridStep(MovementSlot slot) { _slot = slot; }

        public override string Name => "Grid stepping";
        public override MovementSlot Slot => _slot;
        public override AnnouncementContext Context => AnnouncementContext.Tile;

        private bool _holding;
        private float _nextStep;

        public override void Idle(Cursor cursor) => _holding = false;

        public override void Tick(float dt, Cursor cursor)
        {
            if (!OverlayManager.Active || !(cursor.Space is TacticalSpace space)) { _holding = false; return; }
            cursor.HeldVector(_slot, out int dx, out int dz);
            if (dx == 0 && dz == 0) { _holding = false; return; }
            float stretch = (dx != 0 && dz != 0) ? 1.41421356f : 1f;
            float now = Time.unscaledTime;
            if (!_holding)
            {
                _holding = true;
                _nextStep = now + OsKeyboard.InitialDelay;
                Step(dx, dz, space, cursor);
            }
            else if (now >= _nextStep)
            {
                _nextStep = now + OsKeyboard.RepeatInterval * stretch;
                Step(dx, dz, space, cursor);
            }
        }

        private void Step(int dx, int dz, TacticalSpace space, Cursor cursor)
        {
            var from = space.Cell;
            var to = from + TacticalModel.ScreenStep(dx, dz); // up = the way the camera looks
            if (!TacticalModel.IsValid(to))
            {
                Tts.Speak(Loc.T("tactical.edge"), interrupt: true);
                return;
            }
            space.SetCell(to);
            cursor.Announce(Context, ReadoutTrigger.Step);
        }

        public override void Recenter(Cursor cursor)
        {
            if (cursor.Space is TacticalSpace space) space.SetCell(TacticalModel.HomeCell); // the acting squad's cell
        }
    }
}
