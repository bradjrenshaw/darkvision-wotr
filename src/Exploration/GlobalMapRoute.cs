using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Globalmap.State;
using Kingmaker.Globalmap.View;
using WrathAccess.Exploration.Overlays;

namespace WrathAccess.Exploration
{
    internal static class GlobalMapRoute
    {
        public static void AnnounceDistance() => Announce(directions: false);
        public static void AnnounceDirections() => Announce(directions: true);

        private static void Announce(bool directions)
        {
            if (!GlobalMapModel.CanReview) return;
            var map = GlobalMapView.Instance;
            var traveler = Game.Instance?.GlobalMapController?.SelectedTraveler;
            var paths = map?.State?.PathManager;
            if (traveler == null || paths == null) return;

            var target = GlobalMapCursor.RouteTarget();
            if (target == null || target.Blueprint == null)
            {
                Tts.Speak(Loc.T("worldmap.route_no_node"), interrupt: true);
                return;
            }

            var name = ConnectedStep.ShortLabel(target);
            if (traveler.Location == target.Blueprint)
            {
                Tts.Speak(directions ? Loc.T("worldmap.route_here", new { name })
                    : Distance(name, 0, 0, traveler is GlobalMapArmyState), interrupt: true);
                return;
            }

            var route = paths.CalculateTravelerPathToLocation(traveler, target.Blueprint);
            string text;
            if (route == null || route.Path.Count == 0)
                text = Loc.T("worldmap.no_route", new { name });
            else if (directions)
                text = Directions(map, route) ?? Loc.T("worldmap.no_route", new { name });
            else
                text = Distance(name, route.Path.Count, paths.GetArmyPathCost(route, traveler.Position),
                    traveler is GlobalMapArmyState);
            Tts.Speak(text, interrupt: true);
        }

        private static string Distance(string name, int nodes, int points, bool army)
        {
            var count = Loc.T(nodes == 1 ? "worldmap.route_one_node" : "worldmap.route_nodes", new { nodes });
            var cost = Loc.T(points == 1 ? "worldmap.route_one_point" : "worldmap.route_points", new { points });
            return Loc.T(army ? "worldmap.route_distance" : "worldmap.route_distance_party", new { name, count, cost });
        }

        private static string Directions(GlobalMapView map, GlobalMapTravelData route)
        {
            var steps = new List<string>();
            foreach (var leg in route.Path)
            {
                var edge = map.GetEdgeView(leg.Blueprint);
                if (edge == null || edge.Point1 == null || edge.Point2 == null) return null;
                var from = leg.Direction > 0 ? edge.Point1 : edge.Point2;
                var to = leg.Direction > 0 ? edge.Point2 : edge.Point1;
                float? start = steps.Count == 0 && route.From.Edge == leg.Blueprint
                    ? route.From.EdgePosition : (float?)null;
                var direction = ConnectedStep.RouteDirection(edge, from, to, start);
                steps.Add(Loc.T("worldmap.node_road", new { direction, dest = ConnectedStep.ShortLabel(to) }));
            }
            return string.Join(". ", steps);
        }
    }
}
