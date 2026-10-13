using System.Collections.Generic;
using Kingmaker.Globalmap.Blueprints; // GlobalMapEdgeType
using Kingmaker.Globalmap.State;      // GlobalMapArmyState
using Kingmaker.Globalmap.View;
using UnityEngine;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// World-map GRAPH navigation: the roads are the moves and EVERY node is a stop. From the node the
    /// cursor stands on, each road out gets its own compass word (the direction it LEAVES the node — what
    /// a sighted player sees at the junction; never two roads on one word, the map allows at most four
    /// roads per node and the data says a unique assignment within 45° always exists). The first press of
    /// a direction previews it — what is at the road's far end, how long it is, whether that end lies in
    /// a different direction than the road sets off, whether the road is closed or not yet travelled;
    /// pressing the same direction again moves the cursor to the far end and reads the landing through
    /// the engaged lens. The far end may be a place, a visible junction, or one of the game's invisible
    /// system waypoints where a road bends or forks: those are stops too (named "fork" / "bend", with a
    /// hint of the places beyond), so the cursor traces the road the way a sighted eye does. Off any
    /// node, a press first snaps the cursor to the nearest visible point. Roads are filtered by the
    /// current traveler (an army can't take a party-only road); a place has to be revealed to be offered,
    /// the invisible nodes never are "revealed" in any sense the sighted see.
    ///
    /// Diagonals are two arrows: the keys land a frame or two apart, so a press that grows into a
    /// diagonal within a short window refines the SAME gesture — the second press of a diagonal still
    /// moves even though its first key alone previewed a cardinal for a few milliseconds.
    /// </summary>
    internal sealed class ConnectedStep : MovementMode
    {
        private readonly MovementSlot _slot;
        public ConnectedStep(MovementSlot slot) { _slot = slot; }

        public override string Name => "Connected";
        public override MovementSlot Slot => _slot;
        public override AnnouncementContext Context => AnnouncementContext.Point;

        private const float RefineWindow = 0.25f; // seconds: a second arrow within this grows the same gesture
        private const float LeaveFraction = 0.15f; // the stretch of road whose heading names the exit
        private const float NodeRadius = 0.9f;     // a system waypoint's click sphere (0.5) plus the points' padding

        /// <summary>One road out of the anchor: the node at its far end, the compass sector it got, its
        /// heading as it leaves, and its length.</summary>
        private sealed class Exit
        {
            public GlobalMapPointView To;
            public float LeaveDeg;   // map-frame heading over the first stretch of road
            public float Miles;      // geometric road length (spline)
            public bool Locked;      // the road is closed
            public bool Unexplored;  // the party has not travelled the whole road (the game draws only the explored part)
            public int Sector;       // 0 = north … 7 = northwest (map frame)
        }

        // Press state (see the class summary). _armedSector = the exit previewed by the LAST gesture;
        // _gestureArmed = what that was when the current gesture began (so a diagonal refinement compares
        // against the right thing); _anchor = where the preview was made (any move elsewhere voids it).
        private int _lastDx, _lastDz;
        private float _gestureStart;
        private int _armedSector = -1, _gestureArmed = -1;
        private Vector3 _anchor;

        public override void Idle(Cursor cursor) { _lastDx = 0; _lastDz = 0; }
        public override void OnExit(Cursor cursor) { _armedSector = -1; }

        public override void Tick(float dt, Cursor cursor)
        {
            if (!OverlayManager.Active) { _lastDx = 0; _lastDz = 0; return; }
            // A landing made while a modal was up (the travel prompt's decline) reads out now that the
            // map context is back and the lens is engaged again — the readout pipeline needs a lens.
            if (_announcePending) { _announcePending = false; cursor.Announce(Context, ReadoutTrigger.Step); }
            cursor.HeldVector(_slot, out int dx, out int dz);
            if (dx == 0 && dz == 0) { _lastDx = 0; _lastDz = 0; return; }
            if (dx == _lastDx && dz == _lastDz) return; // holding: no repeat, each press is deliberate
            // A chord's keys come UP a frame apart too: the surviving key alone is a release in progress,
            // not a new press — it must neither preview nor re-arm (it would spoil the next diagonal).
            if (Mathf.Abs(dx) + Mathf.Abs(dz) < Mathf.Abs(_lastDx) + Mathf.Abs(_lastDz)) { _lastDx = dx; _lastDz = dz; return; }
            float now = Time.unscaledTime;
            bool refine = (_lastDx != 0 || _lastDz != 0) && now - _gestureStart < RefineWindow;
            if (!refine) { _gestureStart = now; _gestureArmed = _armedSector; }
            _lastDx = dx; _lastDz = dz;
            // Up is map north: the input is already in the map frame, like the exits.
            Press(SectorOf(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg), cursor);
        }

        private void Press(int sector, Cursor cursor)
        {
            var at = cursor.Position;
            if ((at - _anchor).sqrMagnitude > 1e-4f) { _armedSector = -1; _gestureArmed = -1; } // moved elsewhere since
            var anchor = NodeAt(at);
            if (anchor == null)
            {
                // Off the roads: join the graph at the nearest visible point first.
                anchor = Nearest(at);
                if (anchor == null) { Tts.Speak(Loc.T("worldmap.no_points"), interrupt: true); return; }
                Land(anchor, cursor);
                return;
            }
            var exits = Exits(anchor);
            Exit exit = null;
            foreach (var e in exits) if (e.Sector == sector) { exit = e; break; }
            if (exit == null)
            {
                _armedSector = -1;
                Tts.Speak(NoRoad(sector, exits), interrupt: true);
                return;
            }
            if (sector == _gestureArmed)
            {
                _armedSector = -1; _gestureArmed = -1;
                // An UNEXPLORED road out of the traveler's own node can only be walked, not travelled to
                // (the location-select travel needs explored roads) — the game's navigation arrows. Offer
                // the journey, asking first since it costs time; No just moves the cursor. Explored roads
                // never prompt: the arrows are for looking, and travel to known places has its own keys.
                if (exit.Unexplored && TravelerIsOn(anchor)) OfferTravel(exit, cursor);
                else Land(exit.To, cursor);
                return;
            }
            _armedSector = sector;
            _anchor = at;
            Tts.Speak(Preview(exit, anchor), interrupt: true);
        }

        private bool _announcePending;

        private void Land(GlobalMapPointView p, Cursor cursor, bool deferAnnounce = false)
        {
            cursor.Position = p.transform.position;
            _anchor = cursor.Position;
            if (deferAnnounce) _announcePending = true;
            else cursor.Announce(Context, ReadoutTrigger.Step);
        }

        public override void Recenter(Cursor cursor) { cursor.Position = cursor.Reference; _armedSector = -1; }

        // ---- travelling a road (the game's navigation arrows) ----

        private static Kingmaker.Globalmap.State.IGlobalMapTraveler Traveler
            => Kingmaker.Game.Instance?.GlobalMapController?.SelectedTraveler;

        /// <summary>Whether the current traveler is standing on this node (not mid-road).</summary>
        private static bool TravelerIsOn(GlobalMapPointView node)
        {
            var t = Traveler;
            return t != null && t.Location != null && node.Blueprint == t.Location;
        }

        /// <summary>The game's Yes/No modal: travel this road (what clicking its navigation arrow does —
        /// <c>NavigationArrowView.OnClick</c>: <c>CalculatePathByDirection</c> + <c>StartTravel</c>), or
        /// only move the cursor. The route is computed first so the prompt can quote the journey's cost.</summary>
        private void OfferTravel(Exit exit, Cursor cursor)
        {
            var t = Traveler;
            var pm = GlobalMapView.Instance?.State?.PathManager;
            if (t == null || pm == null || exit.To.Blueprint == null) { Land(exit.To, cursor); return; }
            var data = pm.CalculatePathByDirection(t, exit.To.Blueprint);
            if (data == null) { Tts.Speak(Loc.T("worldmap.no_route", new { name = NodeLabel(exit.To) }), interrupt: true); return; }
            string cost = "";
            if (t is GlobalMapArmyState army)
            {
                int points = pm.GetArmyPathCost(data, army.Position);
                if (points > 0) cost = Loc.T("worldmap.travel_cost", new { points });
            }
            else
            {
                var time = pm.GetPlayerPathTime(data);
                if (time > System.TimeSpan.Zero)
                {
                    var period = Kingmaker.Blueprints.Root.BlueprintRoot.Instance.Calendar.GetPeriodString(time);
                    if (!string.IsNullOrEmpty(period)) cost = Loc.T("worldmap.travel_time", new { time = period });
                }
            }
            var text = Loc.T("worldmap.travel_confirm", new { direction = Word(exit.Sector), dest = NodeLabel(exit.To), cost });
            var to = exit.To;
            Kingmaker.PubSubSystem.EventBus.RaiseEvent(delegate(Kingmaker.PubSubSystem.IMessageModalUIHandler w)
            {
                w.HandleOpen(text, Kingmaker.UI.MessageModalBase.ModalType.Dialog,
                    delegate(Kingmaker.UI.MessageModalBase.ButtonType button)
                    {
                        if (button == Kingmaker.UI.MessageModalBase.ButtonType.Yes) Travel(to);
                        else if (button == Kingmaker.UI.MessageModalBase.ButtonType.No) Land(to, cursor, deferAnnounce: true); // no lens while the modal is up
                    });
            });
        }

        /// <summary>Start along the road toward <paramref name="to"/>, with the arrow click's own guards
        /// (no cutscene, not resting) and the map's interactive gate. The route is recomputed live: the
        /// modal may have sat open a while.</summary>
        private static void Travel(GlobalMapPointView to)
        {
            var t = Traveler;
            var pm = GlobalMapView.Instance?.State?.PathManager;
            if (t == null || pm == null || !GlobalMapModel.Interactive) return;
            if (Kingmaker.Game.Instance.CutsceneLock.Active || Kingmaker.Game.Instance.CurrentMode == Kingmaker.GameModes.GameModeType.Rest) return;
            var data = pm.CalculatePathByDirection(t, to.Blueprint);
            if (data == null) { Tts.Speak(Loc.T("worldmap.no_route", new { name = NodeLabel(to) }), interrupt: true); return; }
            t.StartTravel(data, fromClick: true);
            // The route may run on through bends to a real place: name where it actually ends.
            var end = data.To != null && data.To.Location != null ? GlobalMapView.Instance.GetPointView(data.To.Location) : null;
            Tts.Speak(Loc.T("worldmap.traveling", new { name = end != null ? ShortLabel(end) : ShortLabel(to) }));
        }

        // ---- speech ----

        private static string Preview(Exit e, GlobalMapPointView from)
        {
            var parts = new List<string>
            {
                Loc.T("worldmap.exit", new { direction = Word(e.Sector), dest = NodeLabel(e.To) }),
                Geo.MilesStr(e.Miles),
            };
            // An invisible node: say which places the roads beyond it lead to, so the stop means something.
            if (GlobalMapModel.IsSystemNode(e.To))
            {
                var beyond = Beyond(e.To, from);
                if (beyond.Count > 0) parts.Add(Loc.T("worldmap.exit_toward", new { list = string.Join(", ", beyond) }));
            }
            // The road sets off one way but delivers you another: say where the far end really lies.
            float dx = e.To.transform.position.x - from.transform.position.x, dz = e.To.transform.position.z - from.transform.position.z;
            int trueSector = SectorOf(MapFrame.ToMap(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg));
            if (trueSector != e.Sector) parts.Add(Loc.T("worldmap.exit_bears", new { bearing = Word(trueSector) }));
            if (e.Locked) parts.Add(Loc.T("worldmap.exit_locked"));
            else if (e.Unexplored) parts.Add(Loc.T("worldmap.exit_unexplored"));
            return string.Join(", ", parts);
        }

        private static string NoRoad(int sector, List<Exit> exits)
        {
            var s = Loc.T("worldmap.no_exit", new { direction = Word(sector) });
            if (exits.Count == 0) return s;
            var words = new List<string>();
            foreach (var e in exits) words.Add(Word(e.Sector));
            return s + ". " + Loc.T("worldmap.exits", new { list = string.Join(", ", words) });
        }

        /// <summary>A node's spoken name: a place / junction as the point readouts say it; an invisible
        /// node by its shape (fork / bend).</summary>
        public static string NodeLabel(GlobalMapPointView p)
            => GlobalMapModel.IsSystemNode(p) ? Loc.T(RoadCount(p) >= 3 ? "worldmap.fork" : "worldmap.bend") : GlobalMapActions.InPlace(p);

        /// <summary>What an invisible node under the cursor reads as: its shape, then its roads with their
        /// directions and far ends — the in-place readout of a stop the sighted never click (the lens uses
        /// it when no point is under the cursor).</summary>
        public static string DescribeNode(GlobalMapPointView node)
        {
            var roads = new List<string>();
            foreach (var e in Exits(node))
                roads.Add(Loc.T("worldmap.node_road", new { direction = Word(e.Sector), dest = ShortLabel(e.To) }));
            return Loc.T("worldmap.node_here", new { name = NodeLabel(node), list = string.Join(", ", roads) });
        }

        // A far end named briefly for a road list: the place's name, or the invisible node's shape.
        private static string ShortLabel(GlobalMapPointView p)
            => GlobalMapModel.IsSystemNode(p) ? NodeLabel(p) : GlobalMapActions.Name(p);

        private static readonly string[] Words8 =
            { "geo.north", "geo.northeast", "geo.east", "geo.southeast", "geo.south", "geo.southwest", "geo.west", "geo.northwest" };
        private static string Word(int sector) => Loc.T(Words8[sector]);

        private static int SectorOf(float mapDeg)
        {
            int s = Mathf.RoundToInt(MapFrame.Normalize(mapDeg) / 45f) % 8;
            return s < 0 ? s + 8 : s;
        }

        // ---- the graph ----

        /// <summary>The node whose footprint holds <paramref name="at"/>: a visible point by its own click
        /// radius (<see cref="GlobalMapCursor.NearestWithin"/>), else an invisible system waypoint.</summary>
        public static GlobalMapPointView NodeAt(Vector3 at) => GlobalMapCursor.NearestWithin() ?? SystemNodeWithin(at);

        /// <summary>The system waypoint whose footprint holds <paramref name="at"/>, or null.</summary>
        public static GlobalMapPointView SystemNodeWithin(Vector3 at)
        {
            GlobalMapPointView best = null;
            float bd = NodeRadius * NodeRadius;
            foreach (var p in GlobalMapModel.Points)
            {
                if (p == null || !GlobalMapModel.IsSystemNode(p)) continue;
                float d = (p.transform.position - at).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>Nearest revealed visible point (location or junction) to a map position.</summary>
        private static GlobalMapPointView Nearest(Vector3 at)
        {
            GlobalMapPointView best = null;
            float bd = float.MaxValue;
            foreach (var p in GlobalMapModel.Locations) Consider(p, at, ref best, ref bd);
            foreach (var p in GlobalMapModel.Junctions) Consider(p, at, ref best, ref bd);
            return best;
        }

        private static void Consider(GlobalMapPointView p, Vector3 at, ref GlobalMapPointView best, ref float bd)
        {
            if (p == null) return;
            float d = (p.transform.position - at).sqrMagnitude;
            if (d < bd) { bd = d; best = p; }
        }

        /// <summary>Whether the current traveler may use a road (the path manager's own rule: armies
        /// never take party-only roads).</summary>
        private static bool Passable(GlobalMapEdgeView e)
        {
            if (e == null || e.Blueprint == null) return false;
            bool army = Kingmaker.Game.Instance?.GlobalMapController?.SelectedTraveler is GlobalMapArmyState;
            return !army || e.Blueprint.Type != GlobalMapEdgeType.PassableOnlyByPlayer;
        }

        // The roads a player can be offered from a node (a road to an unrevealed place isn't drawn, so
        // an invisible node with one such road looks like a bend, not a fork).
        private static int RoadCount(GlobalMapPointView p)
        {
            int n = 0;
            if (p.Edges != null) foreach (var e in p.Edges) if (Passable(e) && Offerable(e.GetOppositeLocation(p))) n++;
            return n;
        }

        /// <summary>Whether a road's far end may be offered: an invisible node always (it has no look to
        /// be revealed), a place or junction once revealed.</summary>
        private static bool Offerable(GlobalMapPointView p)
            => p != null && p.State != null && (GlobalMapModel.IsSystemNode(p) || p.State.IsRevealed);

        /// <summary>The roads out of <paramref name="from"/>, one exit each to the adjacent node, with a
        /// unique compass sector apiece.</summary>
        private static List<Exit> Exits(GlobalMapPointView from)
        {
            var exits = new List<Exit>();
            if (from.Edges == null) return exits;
            var taken = new HashSet<GlobalMapPointView> { from }; // two roads to one node: the first wins
            foreach (var e in from.Edges)
            {
                if (!Passable(e)) continue;
                var other = e.GetOppositeLocation(from);
                if (!Offerable(other) || !taken.Add(other)) continue;
                var dir = LeaveDirection(e, from);
                if (!dir.HasValue) continue;
                var st = e.State;
                exits.Add(new Exit
                {
                    To = other,
                    LeaveDeg = MapFrame.ToMap(dir.Value),
                    Miles = Length(e),
                    Locked = e.IsLocked,
                    Unexplored = st != null && !st.Revealed,
                });
            }
            AssignSectors(exits);
            return exits;
        }

        /// <summary>The revealed places and junctions the roads beyond an invisible node lead to (through
        /// further invisible nodes), not counting the way back to <paramref name="from"/> — the preview's
        /// "toward …" hint.</summary>
        private static List<string> Beyond(GlobalMapPointView node, GlobalMapPointView from)
        {
            var names = new List<string>();
            var found = new List<GlobalMapPointView>();
            var chain = new HashSet<GlobalMapPointView> { from, node };
            if (node.Edges != null)
                foreach (var e in node.Edges)
                    if (Passable(e)) Collect(node, e, chain, found, 0);
            foreach (var p in found) names.Add(GlobalMapActions.Name(p));
            return names;
        }

        private static void Collect(GlobalMapPointView node, GlobalMapEdgeView edge, HashSet<GlobalMapPointView> chain, List<GlobalMapPointView> into, int depth)
        {
            var other = edge.GetOppositeLocation(node);
            if (other == null || other.State == null || chain.Contains(other) || depth > 16) return;
            if (!GlobalMapModel.IsSystemNode(other))
            {
                if (other.State.IsRevealed && !into.Contains(other)) into.Add(other);
                return;
            }
            chain.Add(other);
            if (other.Edges != null)
                foreach (var e in other.Edges)
                    if (e != edge && Passable(e)) Collect(other, e, chain, into, depth + 1);
        }

        /// <summary>The world heading (degrees, 0 = +Z) the road leaves <paramref name="from"/> on: the
        /// chord over its first stretch of spline, from whichever end the point sits at.</summary>
        private static float? LeaveDirection(GlobalMapEdgeView e, GlobalMapPointView from)
        {
            var spline = e.Spline;
            if (spline == null) return null;
            bool fromStart = e.Point1 == from;
            var a = from.transform.position;
            var b = spline.EvaluatePosition(fromStart ? LeaveFraction : 1f - LeaveFraction);
            float dx = b.x - a.x, dz = b.z - a.z;
            if (dx * dx + dz * dz < 1e-6f)
            {
                var far = e.GetOppositeLocation(from);
                if (far == null) return null;
                dx = far.transform.position.x - a.x; dz = far.transform.position.z - a.z;
            }
            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        /// <summary>A road's geometric length in miles (the spline's; the game's EdgeLength is
        /// travel-time weighted). Falls back to the straight line.</summary>
        private static float Length(GlobalMapEdgeView e)
        {
            var s = e.Spline;
            if (s != null && s.WorldLength > 0.01f) return s.WorldLength;
            var p1 = e.Point1; var p2 = e.Point2;
            return p1 != null && p2 != null ? Geo.Distance(p1.transform.position, p2.transform.position) : 0f;
        }

        // Each exit may take its nearest sector or either neighbour (all within 45° of its heading);
        // among the assignments where no two share a sector, the one with the least total deviation
        // wins. At most four exits × three candidates: a trivial exhaustive search. If (against the
        // measurements) no unique assignment exists, the leftover exits keep their nearest sector.
        private static void AssignSectors(List<Exit> exits)
        {
            int n = exits.Count;
            if (n == 0) return;
            var cand = new int[n][];
            var cost = new float[n][];
            for (int i = 0; i < n; i++)
            {
                float deg = MapFrame.Normalize(exits[i].LeaveDeg);
                int nearest = SectorOf(deg);
                cand[i] = new[] { nearest, (nearest + 1) % 8, (nearest + 7) % 8 };
                cost[i] = new float[3];
                for (int k = 0; k < 3; k++) cost[i][k] = Mathf.Abs(Mathf.DeltaAngle(deg, cand[i][k] * 45f));
                exits[i].Sector = nearest;
            }
            var pick = new int[n]; var best = new int[n]; float bestCost = float.MaxValue;
            Search(0, n, cand, cost, pick, 0f, best, ref bestCost);
            if (bestCost < float.MaxValue)
                for (int i = 0; i < n; i++) exits[i].Sector = cand[i][best[i]];
        }

        private static void Search(int i, int n, int[][] cand, float[][] cost, int[] pick, float sum, int[] best, ref float bestCost)
        {
            if (sum >= bestCost) return;
            if (i == n) { bestCost = sum; System.Array.Copy(pick, best, n); return; }
            for (int k = 0; k < 3; k++)
            {
                int s = cand[i][k];
                bool clash = false;
                for (int j = 0; j < i; j++) if (cand[j][pick[j]] == s) { clash = true; break; }
                if (clash) continue;
                pick[i] = k;
                Search(i + 1, n, cand, cost, pick, sum + cost[i][k], best, ref bestCost);
            }
        }
    }
}
