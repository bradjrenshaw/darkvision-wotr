using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Armies; // ArmyFaction
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.State; // GlobalMapArmyState
using Kingmaker.Globalmap.View;
using Kingmaker.UI.MVVM._VM.GlobalMap;
using UnityEngine;
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Read-only facade over the world map (<see cref="GlobalMapView"/>) — the world-map analogue of
    /// <see cref="WorldModel"/>. Exposes the party (traveler) position and the map's points categorised by
    /// type. The map lays its points out on the same XZ plane areas use (Y is flat), so <see cref="Geo"/>'s
    /// bearing/distance carry over directly. Armies, edges, and live diffing arrive in later increments.
    /// </summary>
    internal static class GlobalMapModel
    {
        internal static GlobalMapVM CurrentVM
        {
            get
            {
                var rc = Game.Instance?.RootUiContext;
                return rc?.IsKingdom == true ? rc.KingdomVM : rc?.GlobalMapVM;
            }
        }

        public static bool Active
        {
            get
            {
                var g = Game.Instance;
                return g != null && g.RootUiContext != null && g.RootUiContext.IsGlobalMap
                    && GlobalMapView.Instance != null;
            }
        }

        /// <summary>The regular world map or Campaign is ready for travel input.
        /// The movement cursor and Continue wait while a rest, dialogue, or battle owns input.</summary>
        public static bool Interactive
            => Game.Instance?.CurrentMode == Kingmaker.GameModes.GameModeType.GlobalMap
                || KingdomScreen.CampaignInteractive;

        /// <summary>Travel is paused MID-JOURNEY awaiting a Continue — the game stopped the party (a discovery
        /// / event) and the next move resumes it. Enter on the cursor resumes via
        /// <see cref="GlobalMapActions.ResumeTravel"/>. Gated on <see cref="Interactive"/>: travel is also
        /// "paused" during a rest / book event, but the answer there is the overlay, not Continue.</summary>
        public static bool TravelPaused
        {
            get
            {
                if (!Interactive) return false;
                var c = Game.Instance.GlobalMapController;
                return c != null && c.TravelsPaused && c.SelectedTraveler != null && c.SelectedTraveler.TravelData != null;
            }
        }

        /// <summary>The party's pawn position on the map (the selected traveler's view).</summary>
        public static Vector3 TravelerPos
        {
            get
            {
                var t = Game.Instance?.GlobalMapController?.SelectedTraveler;
                return (t != null && t.View != null) ? t.View.Position : Vector3.zero;
            }
        }

        /// <summary>The point the party is currently standing on (null mid-travel).</summary>
        public static BlueprintGlobalMapPoint CurrentLocation
            => Game.Instance?.GlobalMapController?.SelectedTraveler?.Location;

        /// <summary>The view of the current location (for its edges / connected points), or null.</summary>
        public static GlobalMapPointView CurrentLocationView
        {
            get
            {
                var bp = CurrentLocation;
                if (bp == null || GlobalMapView.Instance == null) return null;
                var st = GlobalMapView.Instance.State?.GetPointState(bp);
                return st != null ? st.View : null;
            }
        }

        public static IEnumerable<GlobalMapPointView> Points
            => GlobalMapView.Instance != null ? GlobalMapView.Instance.Points : Enumerable.Empty<GlobalMapPointView>();

        /// <summary>The crusade's war camp sits on this point (Act 2+). The camp is a pawn the game
        /// parks on an ordinary, UNNAMED road junction and moves by script; sighted players see the
        /// camp model there and click it to enter. To us that makes the junction a PLACE: it is named
        /// with the game's own camp label and listed with the locations.</summary>
        public static bool IsWarCamp(GlobalMapPointView p)
        {
            var st = GlobalMapView.Instance != null ? GlobalMapView.Instance.State : null;
            return p != null && st != null && (bool)st.WarcampEnabled && st.WarCampLocation != null
                && p.Blueprint == st.WarCampLocation;
        }

        /// <summary>Revealed, enterable places (named locations, plus the war camp's junction) — what
        /// the player browses and travels to.</summary>
        public static IEnumerable<GlobalMapPointView> Locations
            => Points.Where(p => p != null && p.State != null && p.State.IsRevealed
                && p.Blueprint != null && (p.Blueprint.Type == GlobalMapPointType.Location || IsWarCamp(p)));

        /// <summary>Revealed road junctions — the VISIBLE crossroads markers. Both waypoint kinds count once
        /// revealed: a SYSTEM waypoint (where a road bends or forks) gets the very same junction dot as a
        /// plain Waypoint on reveal (WaypointVisualMesh), a click collider, and the same click → location
        /// panel → travel (verified live 2026-10-17). UNREVEALED system waypoints have no visual and stay
        /// out of every list (the connected mode still stops at them: the drawn road visibly bends there).
        /// The junction the war camp stands on is a location instead.</summary>
        public static IEnumerable<GlobalMapPointView> Junctions
            => Points.Where(p => p != null && p.State != null && p.State.IsRevealed
                && p.Blueprint != null && p.Blueprint.Type.IsWaypoint() && !IsWarCamp(p));

        /// <summary>An invisible routing node (system waypoint) — part of the road, not a place. Roads are
        /// read THROUGH these: the next place along a road is the first non-system point.</summary>
        public static bool IsSystemNode(GlobalMapPointView p)
            => p != null && p.Blueprint != null && p.Blueprint.Type == GlobalMapPointType.SystemWaypoint && !IsWarCamp(p);

        /// <summary>Revealed armies on the current map. Empty until the crusade is active (Act 2+) — on every
        /// other map the game clears the army list, so this is naturally inert in Act 1.</summary>
        public static IEnumerable<GlobalMapArmyState> Armies
        {
            get
            {
                var st = GlobalMapView.Instance != null ? GlobalMapView.Instance.State : null;
                if (st == null || st.Armies == null) return Enumerable.Empty<GlobalMapArmyState>();
                return st.Armies.Where(a => a != null && a.IsRevealed && a.Data != null);
            }
        }

        /// <summary>Revealed enemy (demon) armies — the <c>.</c> cycle.</summary>
        public static IEnumerable<GlobalMapArmyState> EnemyArmies
            => Armies.Where(a => a.Data.Faction == ArmyFaction.Demons);

        /// <summary>Revealed allied (crusader/player) armies — the <c>,</c> cycle.</summary>
        public static IEnumerable<GlobalMapArmyState> AllyArmies
            => Armies.Where(a => a.Data.Faction == ArmyFaction.Crusaders);
    }
}
