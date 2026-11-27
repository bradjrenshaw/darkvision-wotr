using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.GameModes;
using Kingmaker.Globalmap.State;
using Kingmaker.Globalmap.View;
using WrathAccess.Exploration;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    internal sealed class EnemyArmyRadarScreen : Screen
    {
        private static (GlobalMapState Map, GlobalMapArmyState Army) _cursorTarget;
        private readonly GlobalMapState _map;
        private readonly GlobalMapArmyState _ally;
        private readonly EnemyArmyRadar.Contact[] _contacts;
        private bool _sortByAlly;

        private EnemyArmyRadarScreen(GlobalMapState map)
        {
            _map = map;
            // Keep distances stable while the player browses. Opening again takes a fresh scan.
            (_ally, _contacts) = EnemyArmyRadar.Scan(map);
            Wrap = true;
        }

        private static bool WorldMapReady => GlobalMapModel.Active
            && Game.Instance.CurrentMode == GameModeType.GlobalMap
            && LoadingProcess.Instance?.IsLoadingScreenActive != true;

        private static bool AvailableOn(Screen parent) => Game.Instance?.Player?.Chapter >= 3
            && (parent is GlobalMapScreen && WorldMapReady || parent is KingdomScreen && KingdomScreen.CampaignMapReady);

        internal static void Open()
        {
            var parent = ScreenManager.Current;
            var map = GlobalMapView.Instance?.State;
            if (map != null && AvailableOn(parent))
                parent.PushChild(new EnemyArmyRadarScreen(map));
        }

        public override string Key => "overlay.enemy_army_radar";
        public override string ScreenName => Loc.T("radar.title");
        public override bool Exclusive => true;
        public override bool IsActive() => false;
        public override void OnUpdate()
        {
            if (!AvailableOn(ParentScreen) || GlobalMapView.Instance?.State != _map) Close();
        }

        public override void Build(GraphBuilder b)
        {
            b.BeginStop("sort").PushContext(Loc.T("radar.sort"), "group");
            AddSort(b, "settlement", byAlly: false);
            AddSort(b, "army", byAlly: true);
            b.PopContext();

            b.BeginStop("enemies").PushContext(Loc.T("radar.enemies"), "list");
            var contacts = _contacts.Where(c => c.Army.IsRevealed && !c.Army.IsDestroy);
            var sorted = (_sortByAlly && _ally != null
                ? contacts.OrderBy(c => c.AllyMiles).ThenBy(c => c.SettlementMiles)
                : contacts.OrderBy(c => c.SettlementMiles)).ToArray();
            if (sorted.Length == 0)
                b.AddItem(ControlId.Structural("radar:empty"), GraphNodes.Text(() => Loc.T("worldmap.no_enemy_armies")));
            for (int i = 0; i < sorted.Length; i++)
            {
                var contact = sorted[i];
                b.AddItem(ControlId.ForObject(contact.Army), GraphNodes.Button(
                    () => Describe(contact), () => PushChild(new ArmyActions(this, contact.Army)),
                    position: GraphNodes.Position(i + 1, sorted.Length)));
            }
            b.PopContext();
        }

        private void AddSort(GraphBuilder b, string option, bool byAlly)
        {
            var key = "radar.sort_" + option;
            b.AddItem(ControlId.Structural(key), GraphNodes.ChoiceOption(
                () => Loc.T(key), () => _sortByAlly == byAlly, () => _sortByAlly = byAlly));
        }

        private string Describe(EnemyArmyRadar.Contact contact)
        {
            var settlement = contact.Settlement == null ? Loc.T("radar.no_settlement_route")
                : DistanceTo(contact.SettlementMiles, contact.Settlement.Name);
            var ally = _ally == null ? Loc.T("radar.no_ally")
                : DistanceTo(contact.AllyMiles, GlobalMapActions.ArmyName(_ally));
            return Loc.T("radar.contact", new { name = GlobalMapActions.ArmyName(contact.Army), settlement, ally });
        }

        private static string DistanceTo(float miles, string name) => float.IsPositiveInfinity(miles)
            ? Loc.T("worldmap.no_route", new { name })
            : Loc.T("radar.distance_to", new { distance = Geo.MilesStr(miles), name });

        private void Close() => ParentScreen?.RemoveChild(this);

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private void FocusArmy(GlobalMapArmyState army)
        {
            var parent = ParentScreen;
            if (!AvailableOn(parent) || GlobalMapView.Instance?.State != _map || !army.IsRevealed || army.IsDestroy)
                return;

            // Apply the jump after the base world-map screen has restored its cursor.
            _cursorTarget = (_map, army);
            Close();
            if (parent is KingdomScreen)
                Game.Instance.KingdomController.ExitKingdomMode(_map.Blueprint.GlobalMapEnterPoint);
        }

        internal static void PlaceCursorOnReturn()
        {
            if (_cursorTarget.Army == null || !WorldMapReady) return;
            var target = _cursorTarget;
            _cursorTarget = default;
            if (target.Map != GlobalMapView.Instance.State || !target.Army.IsRevealed || target.Army.IsDestroy) return;
            if (GlobalMapCursor.FocusArmy(target.Army)) Navigation.Blur();
        }

        private sealed class ArmyActions : Screen
        {
            private readonly EnemyArmyRadarScreen _radar;
            private readonly GlobalMapArmyState _army;

            internal ArmyActions(EnemyArmyRadarScreen radar, GlobalMapArmyState army)
            {
                _radar = radar;
                _army = army;
            }

            public override string Key => "overlay.enemy_army_radar_actions";
            public override string ScreenName => Loc.T("radar.actions");
            public override bool IsActive() => false;
            public override bool Exclusive => true;
            public override void OnUpdate()
            {
                _radar.OnUpdate();
                if (!_army.IsRevealed || _army.IsDestroy) ParentScreen?.RemoveChild(this);
            }

            public override void Build(GraphBuilder b) =>
                b.AddItem(ControlId.Structural("radar:focus_army"), GraphNodes.Button(
                    () => Loc.T("radar.focus_army", new { name = GlobalMapActions.ArmyName(_army) }),
                    () => _radar.FocusArmy(_army)));

            public override IEnumerable<ElementAction> GetActions()
            {
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => ParentScreen?.RemoveChild(this));
            }
        }
    }
}
