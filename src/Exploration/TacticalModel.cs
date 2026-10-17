using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Armies;                              // ArmyFaction
using Kingmaker.Armies.TacticalCombat;               // TacticalCombatHelper
using Kingmaker.Armies.TacticalCombat.Grid;          // TacticalCombatGrid
using Kingmaker.Armies.TacticalCombat.Parts;         // UnitPartTacticalCombat
using Kingmaker.EntitySystem.Entities;               // UnitEntityData
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Read-only facts about a crusade (tactical) battle, straight from the game's own model: the 9 x 7
    /// <see cref="TacticalCombatGrid"/> (3 m square cells, column x along world X, row y along world Z, a
    /// cell holds one squad or an obstacle), each squad's <see cref="UnitPartTacticalCombat"/> (side, head
    /// count), and whose turn it is (the initiative tracker's current unit — the squad the action bar and
    /// the click handlers act for). Shared by the grid cursor and its readout.
    /// </summary>
    internal static class TacticalModel
    {
        public static bool Active => TacticalCombatHelper.IsActive;

        public static TacticalCombatGrid Grid => TacticalCombatGrid.Instance;

        public static Vector2Int Size => Grid.Size;

        /// <summary>The squad whose turn it is (what the action bar is for), or null between turns.</summary>
        public static UnitEntityData ActingSquad
        {
            get
            {
                var vm = Game.Instance?.RootUiContext?.TacticalCombatVM;
                var cur = vm?.TacticalCombatInitiativeTrackerVM?.Value?.CurrentUnit?.Value;
                return cur?.UnitData;
            }
        }

        public static UnitPartTacticalCombat Part(UnitEntityData u) => u?.Get<UnitPartTacticalCombat>();

        /// <summary>Whether a squad fights for the player's army (the crusade), by its army faction.</summary>
        public static bool IsOwn(UnitEntityData u)
        {
            var p = Part(u);
            return p != null && p.Faction == ArmyFaction.Crusaders;
        }

        public static bool IsValid(Vector2Int c) => Grid.IsValid(c);

        public static Vector2Int Clamp(Vector2Int c)
            => new Vector2Int(Mathf.Clamp(c.x, 0, Size.x - 1), Mathf.Clamp(c.y, 0, Size.y - 1));

        /// <summary>The cell's centre in world space.</summary>
        public static Vector3 CellCenter(Vector2Int c) => Grid.IndexToPosition(c);

        /// <summary>The cell under a world point.</summary>
        public static Vector2Int CellAt(Vector3 world) => Clamp(Grid.PositionToIndex(world));

        /// <summary>The live squad standing in a cell (null if empty or its occupant is dead).</summary>
        public static UnitEntityData SquadAt(Vector2Int c)
        {
            if (!IsValid(c)) return null;
            var u = Grid[c].Unit;
            return u != null && !u.State.IsDead ? u : null;
        }

        public static bool ObstacleAt(Vector2Int c) => IsValid(c) && Grid[c].HasObstacle;

        /// <summary>The cell a squad stands on (its bottom-left cell for a large squad), or null.</summary>
        public static Vector2Int? CellOf(UnitEntityData u) => u != null ? Grid.GetActualCell(u) : null;

        /// <summary>The cell to start on / recenter to: the acting squad, else the first of our squads,
        /// else the middle of the field.</summary>
        public static Vector2Int HomeCell
        {
            get
            {
                var c = CellOf(ActingSquad);
                if (c.HasValue) return c.Value;
                foreach (var u in Squads()) if (IsOwn(u)) { var o = CellOf(u); if (o.HasValue) return o.Value; }
                return new Vector2Int(Size.x / 2, Size.y / 2);
            }
        }

        // ---- the camera's frame: everything the player hears and steps is relative to the view ----

        /// <summary>The battle camera's yaw (degrees; the game's own camera rig). Up on screen = this way.</summary>
        public static float CameraYaw
        {
            get
            {
                var rig = Game.Instance?.UI?.GetCameraRig();
                return rig != null ? rig.transform.eulerAngles.y : 0f;
            }
        }

        /// <summary>The camera's yaw snapped to the grid: 0 = looking along +row (world +Z), 1 = along
        /// +column (+X), 2 = along -row, 3 = along -column. Steps and coordinates follow this.</summary>
        public static int CameraQuarter => ((Mathf.RoundToInt(CameraYaw / 90f) % 4) + 4) % 4;

        // The grid steps for screen-up (forward) and screen-right, per camera quarter.
        private static readonly Vector2Int[] Fwd = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
        private static readonly Vector2Int[] Rgt = { new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(0, 1) };

        /// <summary>A screen-relative step (right, up) as a grid step, for the camera as it faces now.</summary>
        public static Vector2Int ScreenStep(int right, int up)
        {
            int q = CameraQuarter;
            return Rgt[q] * right + Fwd[q] * up;
        }

        /// <summary>The cell's place on SCREEN: column from the left, row from the bottom (0-based), and
        /// how many columns the view shows across — so coordinates read as the camera shows the field.</summary>
        public static Vector2Int ScreenCell(Vector2Int c)
        {
            int w = Size.x, h = Size.y;
            switch (CameraQuarter)
            {
                case 1: return new Vector2Int(h - 1 - c.y, c.x);
                case 2: return new Vector2Int(w - 1 - c.x, h - 1 - c.y);
                case 3: return new Vector2Int(c.y, w - 1 - c.x);
                default: return c;
            }
        }

        /// <summary>A world offset in the camera's ear frame (x = right, z = ahead), for panning.</summary>
        public static void ToEar(ref float dx, ref float dz)
        {
            float r = CameraYaw * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            float ex = dx * c - dz * s, ez = dx * s + dz * c;
            dx = ex; dz = ez;
        }

        /// <summary>Every live squad on the field (both armies).</summary>
        public static IEnumerable<UnitEntityData> Squads()
        {
            var seen = new HashSet<UnitEntityData>();
            foreach (var cell in Grid.Cells)
            {
                var u = cell.Unit;
                if (u != null && !u.State.IsDead && seen.Add(u)) yield return u;
            }
        }
    }
}
