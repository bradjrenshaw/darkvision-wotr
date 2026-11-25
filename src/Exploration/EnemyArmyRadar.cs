using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.State;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.Settlements;
using UnityEngine;

namespace WrathAccess.Exploration
{
    internal static class EnemyArmyRadar
    {
        internal sealed class Contact
        {
            internal GlobalMapArmyState Army;
            internal SettlementState Settlement;
            internal float SettlementMiles;
            internal float AllyMiles;
        }

        internal static (GlobalMapArmyState Ally, Contact[] Contacts) Scan(GlobalMapState map)
        {
            var ally = Game.Instance.GlobalMapController.SelectedArmy
                ?? GlobalMapModel.CurrentVM?.ArmiesVM?.Armies.FirstOrDefault()?.Army
                ?? GlobalMapModel.AllyArmies.FirstOrDefault(a => !a.IsDestroy);
            var enemies = GlobalMapModel.EnemyArmies.Where(a => !a.IsDestroy).ToArray();
            if (enemies.Length == 0) return (ally, System.Array.Empty<Contact>());

            var roads = new RoadDistances(map);
            var settlements = KingdomState.Instance.SettlementsManager.Settlements
                .Where(s => !s.Destroyed)
                .Select(s => (Point: s.SettlementLocationRef.Get(), Settlement: s))
                .Where(s => s.Point != null && map.GetPointState(s.Point) != null);
            var toSettlement = roads.Search(settlements.Select(s => (s.Point, 0f, s.Settlement)));
            var allyPosition = ally?.Position;
            var toAlly = roads.Search(roads.Endpoints(allyPosition)
                .Select(e => (e.Point, e.Miles, (SettlementState)null)));

            return (ally, enemies.Select(army =>
            {
                var position = army.Position;
                var settlement = roads.DistanceTo(position, toSettlement);
                var allyMiles = roads.DistanceTo(position, toAlly).Miles;
                if (position?.Edge != null && position.Edge == allyPosition?.Edge)
                    allyMiles = Mathf.Min(allyMiles,
                        position.Edge.Length * Mathf.Abs(position.EdgePosition - allyPosition.EdgePosition));
                return new Contact
                {
                    Army = army, Settlement = settlement.Settlement, SettlementMiles = settlement.Miles,
                    AllyMiles = allyMiles,
                };
            }).ToArray());
        }

        private sealed class RoadDistances
        {
            private readonly Dictionary<BlueprintGlobalMapPoint, List<(BlueprintGlobalMapPoint Point, float Miles)>> _roads
                = new Dictionary<BlueprintGlobalMapPoint, List<(BlueprintGlobalMapPoint, float)>>();

            internal RoadDistances(GlobalMapState map)
            {
                foreach (var reference in map.Blueprint.Edges)
                {
                    var edge = reference.Get();
                    if (edge == null || edge.IsLocked || edge.Type == GlobalMapEdgeType.PassableOnlyByPlayer
                        || edge.Point1 == null || edge.Point2 == null) continue;
                    AddRoad(edge.Point1, edge.Point2, edge.Length);
                    AddRoad(edge.Point2, edge.Point1, edge.Length);
                }
            }

            private void AddRoad(BlueprintGlobalMapPoint from, BlueprintGlobalMapPoint to, float miles)
            {
                if (!_roads.TryGetValue(from, out var exits))
                    _roads.Add(from, exits = new List<(BlueprintGlobalMapPoint, float)>());
                exits.Add((to, miles));
            }

            // Travel positions are fractions of the game's road length. Seed both ends of a road
            // when a traveler is between junctions so either direction can yield the shortest route.
            internal IEnumerable<(BlueprintGlobalMapPoint Point, float Miles)> Endpoints(GlobalMapPosition position)
            {
                if (position?.Location != null)
                    yield return (position.Location, 0f);
                else if (position?.Edge is BlueprintGlobalMapEdge edge)
                {
                    if (edge.Point1 != null) yield return (edge.Point1, edge.Length * position.EdgePosition);
                    if (edge.Point2 != null) yield return (edge.Point2, edge.Length * (1f - position.EdgePosition));
                }
            }

            internal Dictionary<BlueprintGlobalMapPoint, (float Miles, SettlementState Settlement)> Search(
                IEnumerable<(BlueprintGlobalMapPoint Point, float Miles, SettlementState Settlement)> starts)
            {
                var distances = new Dictionary<BlueprintGlobalMapPoint, (float Miles, SettlementState Settlement)>();
                var pending = new SortedSet<(float Miles, int Sequence, BlueprintGlobalMapPoint Point)>();
                int sequence = 0;

                void Visit(BlueprintGlobalMapPoint point, float miles, SettlementState settlement)
                {
                    if (distances.TryGetValue(point, out var previous) && previous.Miles <= miles) return;
                    distances[point] = (miles, settlement);
                    pending.Add((miles, sequence++, point));
                }

                foreach (var start in starts) Visit(start.Point, start.Miles, start.Settlement);
                while (pending.Count > 0)
                {
                    var next = pending.Min;
                    pending.Remove(next);
                    var best = distances[next.Point];
                    if (best.Miles != next.Miles || !_roads.TryGetValue(next.Point, out var exits)) continue;
                    foreach (var exit in exits) Visit(exit.Point, best.Miles + exit.Miles, best.Settlement);
                }
                return distances;
            }

            internal (float Miles, SettlementState Settlement) DistanceTo(GlobalMapPosition position,
                Dictionary<BlueprintGlobalMapPoint, (float Miles, SettlementState Settlement)> distances)
            {
                var nearest = (Miles: float.PositiveInfinity, Settlement: (SettlementState)null);
                foreach (var end in Endpoints(position))
                    if (distances.TryGetValue(end.Point, out var route) && end.Miles + route.Miles < nearest.Miles)
                        nearest = (end.Miles + route.Miles, route.Settlement);
                return nearest;
            }
        }
    }
}
