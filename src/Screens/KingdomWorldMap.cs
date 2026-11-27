using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Armies;
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.State;
using Kingmaker.Globalmap.View;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.Blueprints;
using Kingmaker.Kingdom.Settlements;
using UnityEngine;
using WrathAccess.Exploration;
using WrathAccess.Input;

namespace WrathAccess.Screens
{
    internal static class KingdomWorldMap
    {
        private enum Category { Everything, Locations, Settlements, Armies }

        private static readonly string[] Categories =
            { "worldmap.cat_everything", "worldmap.cat_locations", "kingdom.settlements", "kingdom.map_armies" };
        private static readonly string[] Directions =
            { "geo.north", "geo.northeast", "geo.east", "geo.southeast", "geo.south", "geo.southwest", "geo.west", "geo.northwest" };
        private const string InputPrefix = "kingdom.map_";
        private const float ChordWindow = 0.12f;

        private static KingdomState _kingdom;
        private static SettlementState _lastSettlement;
        // A target is a map point, a settlement, or an army. Keeping the actual target distinguishes
        // an army from a town when they share a position.
        private static object _cursor;
        private static object _review;
        private static Category _category;
        private static (GlobalMapPointView From, GlobalMapPointView To, int Sector) _preview;
        private static bool _arrowsHeld;
        private static ArrowRefinement? _refinement;

        // A cardinal press is immediately audible. Keep its starting state briefly so a second
        // arrow can turn it into a diagonal from the original node, even if the cardinal moved.
        private struct ArrowRefinement
        {
            internal int Sector;
            internal float Deadline;
            internal object Cursor;
            internal SettlementState LastSettlement;
            internal (GlobalMapPointView From, GlobalMapPointView To, int Sector) Preview;
        }

        private static IEnumerable<GlobalMapPointView> Points =>
            GlobalMapView.Instance?.Points ?? Enumerable.Empty<GlobalMapPointView>();

        private static object Current => Position(_cursor).HasValue ? _cursor : Drezen;

        private static SettlementState Drezen => KingdomMapNodes.Settlements
            .FirstOrDefault(s => s.SettlementLocationRef.Get()?.name == "Point_Drezen");

        internal static void Reset()
        {
            if (_kingdom != KingdomState.Instance)
            {
                _kingdom = KingdomState.Instance;
                _lastSettlement = null;
            }
            _cursor = _review = null;
            _category = Category.Everything;
            ClearPreview();
            ResetGesture();
        }

        internal static void RememberSettlement(SettlementState settlement)
        {
            _lastSettlement = settlement;
            _refinement = null;
        }

        internal static void RegisterInput()
        {
            InputAction Bind(string id, string label, KeyCode key, Action action = null,
                bool ctrl = false, bool shift = false)
                => InputManager.Register(InputPrefix + id, label, InputCategory.KingdomMap, action == null ? null : (Action)(() =>
                {
                    if (!KingdomScreen.HasMapFocus) return;
                    _refinement = null;
                    action();
                })).AddBinding(key, ctrl: ctrl, shift: shift);

            void Pair(string id, string label, KeyCode key, Action<int> step)
            {
                Bind("next_" + id, "Next " + label, key, () => step(1)).Repeating();
                Bind("prev_" + id, "Previous " + label, key, () => step(-1), shift: true).Repeating();
            }

            Bind("up", "Map cursor north", KeyCode.UpArrow);
            Bind("down", "Map cursor south", KeyCode.DownArrow);
            Bind("left", "Map cursor west", KeyCode.LeftArrow);
            Bind("right", "Map cursor east", KeyCode.RightArrow);
            Bind("interact", "Interact at map cursor", KeyCode.Return, InteractCursor).AddBinding(KeyCode.KeypadEnter);
            Bind("read", "Read current map node", KeyCode.K, ReadNode);
            Bind("center", "Focus last settlement", KeyCode.C, Recenter);
            Bind("home", "Focus scanner item", KeyCode.Home, JumpToReview).AddBinding(KeyCode.Slash);
            Bind("review", "Interact with map scanner item", KeyCode.I, () => Interact(_review));
            Bind("next_item", "Next map scanner item", KeyCode.PageDown, () => Scan(_category, 1)).Repeating();
            Bind("prev_item", "Previous map scanner item", KeyCode.PageUp, () => Scan(_category, -1)).Repeating();
            Bind("next_category", "Next map scanner category", KeyCode.PageDown, () => StepCategory(1), ctrl: true).Repeating();
            Bind("prev_category", "Previous map scanner category", KeyCode.PageUp, () => StepCategory(-1), ctrl: true).Repeating();
            Pair("location", "location", KeyCode.N, step => Scan(Category.Locations, step));
            Pair("settlement", "settlement", KeyCode.S, step => Scan(Category.Settlements, step));
            Pair("ally", "allied army", KeyCode.Comma, step => Scan(Category.Armies, step, ArmyFaction.Crusaders));
            Pair("enemy", "enemy army", KeyCode.Period, step => Scan(Category.Armies, step, ArmyFaction.Demons));
            Pair("connection", "connected map node", KeyCode.M, ScanConnections);
            Pair("point", "map item", KeyCode.B, step => Scan(Category.Everything, step));
        }

        internal static void Tick()
        {
            if (!KingdomScreen.HasMapFocus || !FocusMode.Active)
            {
                ResetGesture();
                ClearPreview();
                return;
            }
            int Axis(string positive, string negative) =>
                (InputManager.Held(InputPrefix + positive) ? 1 : 0) - (InputManager.Held(InputPrefix + negative) ? 1 : 0);
            UpdateDirection(Axis("right", "left"), Axis("up", "down"), Time.unscaledTime);
        }

        private static void UpdateDirection(int dx, int dz, float now)
        {
            if (dx == 0 && dz == 0)
            {
                ResetGesture();
                return;
            }
            int sector = Sector(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
            bool diagonal = dx != 0 && dz != 0;
            if (!_arrowsHeld)
            {
                _arrowsHeld = true;
                if (!diagonal)
                    _refinement = new ArrowRefinement
                    {
                        Sector = sector, Deadline = now + ChordWindow, Cursor = _cursor,
                        LastSettlement = _lastSettlement, Preview = _preview,
                    };
                Step(sector);
                return;
            }
            if (!(_refinement is ArrowRefinement start) || sector == start.Sector) return;

            _refinement = null;
            int difference = (sector - start.Sector + Directions.Length) % Directions.Length;
            if (!diagonal || now > start.Deadline || (difference != 1 && difference != Directions.Length - 1))
                return;
            _cursor = start.Cursor;
            _lastSettlement = start.LastSettlement;
            _preview = start.Preview;
            Step(sector);
        }

        private static void ResetGesture()
        {
            _arrowsHeld = false;
            _refinement = null;
        }

        private static void ClearPreview() => _preview = default;

        private static void Step(int sector)
        {
            var current = Current;
            var from = PointFor(current);
            var exits = Exits(from);
            var exit = exits.FirstOrDefault(e => e.Sector == sector);
            if (exit == null)
            {
                ClearPreview();
                var noRoad = Loc.T("worldmap.no_exit", new { direction = Loc.T(Directions[sector]) });
                Tts.Speak(noRoad + ". " + DescribeExits(exits), interrupt: true);
                return;
            }

            var target = TargetAt(exit.To);
            var region = RegionAt(target);
            var crossing = region != RegionAt(current) ? RegionName(region) : null;
            var name = WithRegion(Name(target), crossing);
            if (_preview.From == from && _preview.To == exit.To && _preview.Sector == sector)
            {
                Place(target);
                Tts.Speak(name, interrupt: true);
                return;
            }

            _preview = (from, exit.To, sector);
            var preview = Loc.T("worldmap.node_road", new { direction = Loc.T(Directions[sector]), dest = name })
                + ", " + Geo.MilesStr(exit.Miles);
            Tts.Speak(preview, interrupt: true);
        }

        private static void Place(object target)
        {
            _cursor = target;
            if (target is SettlementState settlement) _lastSettlement = settlement;
            ClearPreview();
        }

        private static void Recenter()
        {
            Place(Position(_lastSettlement).HasValue ? _lastSettlement : Drezen);
            ReadNode();
        }

        private static void JumpToReview()
        {
            if (!Position(_review).HasValue)
            {
                Tts.Speak(Loc.T("worldmap.scan_none"), interrupt: true);
                return;
            }
            Place(_review);
            ReadNode();
        }

        private static void ReadNode()
        {
            var target = Current;
            var description = target == null ? Loc.T("worldmap.cursor_empty")
                : WithRegion(Name(target), RegionName(RegionAt(target)) ?? Loc.T("kingdom.map_unknown_region"))
                    + ". " + DescribeExits(Exits(PointFor(target)));
            Tts.Speak(description, interrupt: true);
        }

        internal static void InteractCursor()
        {
            if (KingdomScreen.HasMapFocus) Interact(Current);
        }

        private static void Interact(object target)
        {
            if (!Position(target).HasValue) return;
            if (target is SettlementState settlement)
                KingdomMapNodes.OpenSettlement(settlement);
            else if (target is GlobalMapArmyState)
                Tts.Speak(Name(target), interrupt: true);
            else if (target is GlobalMapPointView point && point.Blueprint.Type != GlobalMapPointType.Location)
                Tts.Speak(DescribeExits(Exits(point)), interrupt: true);
        }

        private static void StepCategory(int direction)
        {
            _category = (Category)(((int)_category + direction + Categories.Length) % Categories.Length);
            _review = null;
            var label = Loc.T(Categories[(int)_category]);
            Scan(_category, 1, categoryAnnouncement: label);
        }

        private static void Scan(Category category, int direction, ArmyFaction? faction = null, string categoryAnnouncement = null)
        {
            var settlements = KingdomMapNodes.Settlements.ToArray();
            IEnumerable<object> targets = Enumerable.Empty<object>();
            if (category == Category.Everything || category == Category.Locations)
                targets = Points.Where(p => p?.Blueprint != null && p.State?.IsRevealed == true
                    && (category == Category.Everything || p.Blueprint.Type == GlobalMapPointType.Location)
                    && !settlements.Any(s => s.SettlementLocationRef.Is(p.Blueprint)));
            if (category == Category.Everything || category == Category.Settlements)
                targets = targets.Concat(settlements);
            if (category == Category.Everything || category == Category.Armies)
            {
                var armies = GlobalMapView.Instance?.State?.Armies;
                if (armies != null)
                    targets = targets.Concat(armies.Where(a => a?.IsRevealed == true && a.Data != null
                        && (!faction.HasValue || a.Data.Faction == faction.Value)));
            }
            var emptyKey = faction == ArmyFaction.Crusaders ? "worldmap.no_ally_armies"
                : faction == ArmyFaction.Demons ? "worldmap.no_enemy_armies" : null;
            var empty = emptyKey != null ? Loc.T(emptyKey)
                : Loc.T("worldmap.scan_empty", new { cat = Loc.T(Categories[(int)category]) });
            ScanTargets(targets, direction, empty, categoryAnnouncement);
        }

        private static void ScanConnections(int direction)
        {
            var exits = Exits(PointFor(Current));
            ScanTargets(exits.Select(e => TargetAt(e.To)), direction, Loc.T("worldmap.no_connections"));
        }

        private static void ScanTargets(IEnumerable<object> targets, int direction, string empty, string categoryAnnouncement = null)
        {
            var from = Position(Current) ?? Vector3.zero;
            var sorted = targets.Select(target => (target, position: Position(target)))
                .Where(item => item.position.HasValue)
                .OrderBy(item => Geo.Distance(from, item.position.Value)).ToList();
            if (sorted.Count == 0)
            {
                Tts.Speak(empty, interrupt: true);
                return;
            }
            int previous = sorted.FindIndex(item => ReferenceEquals(item.target, _review));
            int index = previous < 0 ? (direction > 0 ? 0 : sorted.Count - 1)
                : Mathf.Clamp(previous + direction, 0, sorted.Count - 1);
            var itemAtIndex = sorted[index];
            _review = itemAtIndex.target;
            if (_review is SettlementState settlement) RememberSettlement(settlement);
            string text = string.Join(", ", Name(_review), Geo.Bearing(from, itemAtIndex.position.Value),
                Loc.T("nav.position", new { index = index + 1, count = sorted.Count }));
            Tts.Speak(categoryAnnouncement == null ? text : categoryAnnouncement + ". " + text, interrupt: true);
        }

        private static object TargetAt(GlobalMapPointView point)
            => (object)KingdomMapNodes.Settlements.FirstOrDefault(s => s.SettlementLocationRef.Is(point.Blueprint)) ?? point;

        private static GlobalMapPointView PointFor(object target)
        {
            switch (target)
            {
                case GlobalMapPointView point: return point;
                case SettlementState settlement:
                    return GlobalMapView.Instance?.GetPointView(settlement.SettlementLocationRef.Get());
                case GlobalMapArmyState army:
                    if (army.Location != null) return GlobalMapView.Instance?.GetPointView(army.Location);
                    var position = Position(army);
                    return position.HasValue ? Points.Where(p => p?.Blueprint != null)
                        .OrderBy(p => Geo.Distance(position.Value, p.transform.position)).FirstOrDefault() : null;
                default: return null;
            }
        }

        private static Vector3? Position(object target)
        {
            switch (target)
            {
                case GlobalMapPointView point when point != null: return point.transform.position;
                case SettlementState settlement when !settlement.Destroyed:
                    var marker = settlement.MarkerManager.SettlementMarker;
                    return marker != null ? marker.transform.position : (Vector3?)null;
                case GlobalMapArmyState army when army.IsRevealed && army.Data != null:
                    if (army.View != null) return army.View.Position;
                    if (army.Location != null)
                        return GlobalMapView.Instance?.GetPointView(army.Location)?.transform.position;
                    break;
            }
            return null;
        }

        private static RegionId RegionAt(object target)
            => (target as SettlementState)?.Region?.Blueprint?.Id ?? PointFor(target)?.Blueprint?.Region ?? RegionId.None;

        private static string RegionName(RegionId id)
            => RegionObject.Instances.FirstOrDefault(r => r != null && r.Blueprint?.Id == id)?.Blueprint.LocalizedName;

        private static string WithRegion(string name, string region)
            => string.IsNullOrEmpty(region) ? name : Loc.T("kingdom.map_in_region", new { name, region });

        private static string Name(object target)
        {
            switch (target)
            {
                case SettlementState settlement: return settlement.Name;
                case GlobalMapArmyState army:
                    var armyName = (string)army.Data.ArmyName;
                    return string.IsNullOrEmpty(armyName) ? Loc.T("worldmap.army_fallback") : armyName;
                case GlobalMapPointView point when point.Blueprint.Type == GlobalMapPointType.SystemWaypoint:
                    return Loc.T(Exits(point).Count >= 3 ? "worldmap.fork" : "worldmap.bend");
                case GlobalMapPointView point when point.State.IsRevealed:
                    var pointName = (string)point.Blueprint.Name;
                    return string.IsNullOrEmpty(pointName) ? Loc.T("worldmap.junction") : pointName;
                default: return Loc.T("worldmap.unseen_place");
            }
        }

        private sealed class Exit
        {
            internal GlobalMapPointView To;
            internal float Heading;
            internal float Miles;
            internal int Sector;
        }

        private static List<Exit> Exits(GlobalMapPointView from)
        {
            var exits = new List<Exit>();
            if (from == null || from.Edges == null) return exits;
            var seen = new HashSet<GlobalMapPointView> { from };
            foreach (var edge in from.Edges)
            {
                if (edge?.State == null) continue;
                var to = edge.GetOppositeLocation(from);
                if (to?.Blueprint == null || to.State == null) continue;
                // Follow map knowledge, irrespective of which party or army is selected.
                if (!to.State.IsRevealed && to.Blueprint.Type != GlobalMapPointType.SystemWaypoint
                    && edge.State.Explored1 <= 0f && edge.State.Explored2 <= 0f) continue;
                if (!seen.Add(to)) continue;
                var start = from.transform.position;
                var spline = edge.Spline;
                var end = spline?.EvaluatePosition(edge.Point1 == from ? 0.15f : 0.85f) ?? to.transform.position;
                if (Geo.IsHere(start, end)) end = to.transform.position;
                float heading = MapFrame.ToMap(Mathf.Atan2(end.x - start.x, end.z - start.z) * Mathf.Rad2Deg);
                float miles = spline != null && spline.WorldLength > 0f
                    ? spline.WorldLength : Geo.Distance(start, to.transform.position);
                exits.Add(new Exit { To = to, Heading = heading, Miles = miles, Sector = Sector(heading) });
            }
            // Give each road its own arrow. Prefer the assignment with the smallest total deflection
            // from the headings at the junction, so two roads that start close together stay reachable.
            if (exits.Count <= Directions.Length)
            {
                var assigned = new int[exits.Count];
                float best = float.PositiveInfinity;
                AssignSectors(exits, assigned, 0, 0, 0f, ref best);
            }
            return exits;
        }

        private static void AssignSectors(List<Exit> exits, int[] assigned, int index, int used, float cost, ref float best)
        {
            if (cost >= best) return;
            if (index == exits.Count)
            {
                best = cost;
                for (int i = 0; i < exits.Count; i++) exits[i].Sector = assigned[i];
                return;
            }
            for (int sector = 0; sector < Directions.Length; sector++)
            {
                int bit = 1 << sector;
                if ((used & bit) != 0) continue;
                assigned[index] = sector;
                float turn = Mathf.Abs(Mathf.DeltaAngle(exits[index].Heading, sector * 45f));
                AssignSectors(exits, assigned, index + 1, used | bit, cost + turn, ref best);
            }
        }

        private static int Sector(float heading) => Mathf.RoundToInt(MapFrame.Normalize(heading) / 45f) % 8;

        private static string DescribeExits(List<Exit> exits)
        {
            if (exits.Count == 0) return Loc.T("worldmap.no_connections");
            var roads = exits.OrderBy(e => e.Sector).Select(e =>
                Loc.T("worldmap.node_road", new { direction = Loc.T(Directions[e.Sector]), dest = Name(TargetAt(e.To)) }));
            return Loc.T("worldmap.exits", new { list = string.Join(", ", roads) });
        }
    }
}
