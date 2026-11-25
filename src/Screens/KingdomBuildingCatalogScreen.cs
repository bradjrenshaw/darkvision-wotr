using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.GameModes;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.Settlements;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using Kingmaker.UI.Settlement;
using WrathAccess.Input;
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using Object = UnityEngine.Object;

namespace WrathAccess.Screens
{
    internal sealed class KingdomBuildingCatalogScreen : Screen
    {
        private readonly SettlementState _settlement;
        private readonly BuildNaviElement _buildTab;
        private readonly bool _openedBuildTab;

        private KingdomBuildingCatalogScreen(SettlementState settlement, BuildNaviElement buildTab)
        {
            _settlement = settlement;
            _buildTab = buildTab;
            _openedBuildTab = !buildTab.Select;
        }

        internal static bool InSettlement(SettlementState settlement) =>
            Game.Instance?.CurrentMode == GameModeType.KingdomSettlement
            && ReferenceEquals(KingdomState.Instance?.CurrentSettlement, settlement);

        internal static void Open(SettlementState settlement)
        {
            if (!(ScreenManager.Current is KingdomScreen parent) || !InSettlement(settlement)) return;
            var buildTab = Object.FindObjectOfType<BuildNaviElement>();
            if (buildTab != null)
                parent.PushChild(new KingdomBuildingCatalogScreen(settlement, buildTab));
        }

        internal static void CloseActive()
        {
            foreach (var screen in ScreenManager.Stack)
                if (screen is KingdomScreen && screen.ActiveChild is KingdomBuildingCatalogScreen catalog)
                    screen.RemoveChild(catalog);
        }

        internal bool CatalogOpen => InSettlement(_settlement) && _buildTab != null && _buildTab.Select;

        public override string Key => "overlay.kingdom_catalog";
        public override string ScreenName => Loc.T("kingdom.build_catalog");
        public override object InitialFocusStop => "catalog";
        public override bool IsActive() => false; // child of the settlement builder
        public override bool Exclusive => true;

        public override void OnPush()
        {
            if (_openedBuildTab) SwitchBuildTab(true);
        }

        public override void OnUpdate()
        {
            if (!CatalogOpen) Close();
        }

        public override void OnPop()
        {
            if (_openedBuildTab && CatalogOpen) SwitchBuildTab(false);
        }

        private static void SwitchBuildTab(bool enabled) =>
            EventBus.RaiseEvent<ISettlementUISwitchModChanged>(h => h.OnSwitchMod(enabled));

        public override void Build(GraphBuilder b)
        {
            if (!CatalogOpen) return;
            var buildings = _settlement.PossibleBuildingsByLevel.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var sheet = new GraphSheet(b.BeginStop("catalog"), "kingdom:catalog:");
            sheet.Region(Loc.T("kingdom.buildings"),
                new[] { Loc.T("kingdom.building_price"), Loc.T("kingdom.building_slots"), Loc.T("kingdom.building_status") });
            foreach (var building in buildings)
            {
                Action details = () => OpenDetails(building);
                var row = GraphNodes.Button(() => Name(building), () => Select(building),
                    () => CatalogOpen && _settlement.CanBuild(building));
                row.OnTooltip = row.OnSecondary = details;
                sheet.RowAt(row, building, new[]
                {
                    DetailCell(1, () => Price(building), details),
                    DetailCell(2, () => Footprint(building), details),
                    DetailCell(3, () => Status(building), details),
                });
            }
            if (buildings.Length == 0)
                sheet.Line(GraphNodes.Text(() => Loc.T("kingdom.catalog_empty")));
            sheet.Finish();

            b.BeginStop("catalog_resources").PushContext(Loc.T("kingdom.catalog_resources"));
            foreach (var resource in KingdomBuildingNodes.Resources)
                b.AddItem(ControlId.Structural("kingdom:catalog_resource:" + resource), GraphNodes.Text(() =>
                    KingdomBuildingNodes.ResourceLine(KingdomState.Instance.Resources, resource)));
            b.AddItem(ControlId.Structural("kingdom:catalog_slots"), GraphNodes.Text(() =>
                Loc.T("kingdom.catalog_slots_left", new { free = _settlement.SlotsLeft, total = _settlement.SlotCount })));
            b.AddItem(ControlId.Structural("kingdom:catalog_buy"), GraphNodes.Button(
                () => UIStrings.Instance.CrusadeTexts.BuyResource,
                GlobalMapBuyResourcesScreen.OpenSettlementPurchase, sound: null));
            b.AddItem(ControlId.Structural("kingdom:catalog_close"), GraphNodes.Button(() => Loc.T("action.close"), Close));
            b.PopContext();
        }

        private static string Name(BlueprintSettlementBuilding building) => TextUtil.StripRichText(building.Name);

        private static KeyValuePair<int, NodeVtable> DetailCell(int column, Func<string> label, Action details)
        {
            var node = GraphNodes.Text(label);
            node.OnTooltip = details;
            return new KeyValuePair<int, NodeVtable>(column, node);
        }

        private string Price(BlueprintSettlementBuilding building) =>
            KingdomBuildingNodes.Price(_settlement.GetActualCost(building), "\n");

        private static string Footprint(BlueprintSettlementBuilding building) =>
            Loc.T("kingdom.catalog_footprint", new
            {
                width = building.SlotSizeX, height = building.SlotSizeY, type = KingdomBuildingNodes.SlotType(building),
            });

        private string Status(BlueprintSettlementBuilding building)
        {
            if (_settlement.CanBuild(building)) return Loc.T("kingdom.catalog_available");
            if (!KingdomState.Instance.Resources.IsGreaterOrEqual(_settlement.GetActualCost(building)))
                return Loc.T("kingdom.catalog_insufficient_resources");
            if (_settlement.UnderSiege) return Loc.T("kingdom.catalog_under_siege");
            if (building.SpecialSlot != SettlementState.SpecialSlotType.None)
                return Loc.T("kingdom.catalog_special_slot_unavailable");
            if (_settlement.SlotsLeft < building.SlotCount) return Loc.T("kingdom.catalog_insufficient_slots");
            return Loc.T("state.disabled");
        }

        private void OpenDetails(BlueprintSettlementBuilding building)
        {
            var lines = new List<string>
            {
                building.FullDescription,
                Loc.T("kingdom.building_price"),
                Price(building),
                Footprint(building),
                Loc.T("kingdom.catalog_build_time", new { days = building.BuildTime }),
                Status(building),
            };
            lines.AddRange(KingdomBuildingNodes.Changes(building.StatChanges));
            TooltipScreen.Open(new TooltipTemplateSimple(Name(building), string.Join("\n", lines)));
        }

        private void Select(BlueprintSettlementBuilding building)
        {
            if (!CatalogOpen || !_settlement.CanBuild(building)) return;
            UIDraggedBuilding.Create(building);
            var drag = UIDraggedBuilding.Current;
            if (drag != null)
                PushChild(new KingdomBuildingPlacementScreen(_settlement, building, drag));
        }

        private void Close() => ParentScreen?.RemoveChild(this);

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }
    }

    internal sealed class KingdomBuildingPlacementScreen : Screen
    {
        private static readonly InputCategory[] Categories = { InputCategory.SettlementGrid, InputCategory.UI, InputCategory.Kingdom };
        private readonly SettlementState _settlement;
        private readonly BlueprintSettlementBuilding _building;
        private readonly UIDraggedBuilding _drag;
        private SettlementGridTopology.Slot _preview;

        internal KingdomBuildingPlacementScreen(SettlementState settlement, BlueprintSettlementBuilding building,
            UIDraggedBuilding drag)
        {
            _settlement = settlement;
            _building = building;
            _drag = drag;
        }

        private bool Placing => ParentScreen is KingdomBuildingCatalogScreen catalog && catalog.CatalogOpen && _drag != null
            && ReferenceEquals(UIDraggedBuilding.Current, _drag);

        public override string Key => "overlay.kingdom_placement";
        public override string ScreenName => Loc.T("kingdom.placement_title", new { name = (string)_building.Name });
        public override bool IsActive() => false; // child of the catalog
        public override bool Exclusive => true;
        public override bool AllowsTypeahead => false;
        public override IReadOnlyList<InputCategory> InputCategories => Categories;

        internal static bool HasGridFocus => ScreenManager.Current is KingdomBuildingPlacementScreen
            && Equals(Navigation.FocusedStopKey, KingdomBuilderNodes.GridStop);

        public override void OnPush()
        {
            // Drive the native placement preview from graph focus instead of the mouse position.
            _drag.enabled = false;
        }

        public override void OnUpdate()
        {
            if (!Placing)
            {
                Close();
                return;
            }
            if (Navigation.Active is GraphNavigator nav && nav.FocusedNodeId?.Reference is SettlementGridTopology.Slot slot
                && !ReferenceEquals(_preview, slot) && slot.GetSlotObject() is BuildingSlot view && view != null)
            {
                _preview = slot;
                _drag.PlaceInSlot(view);
                EventBus.RaiseEvent<ISettlementBuildSlotHover>(h => h.OnSlotHover(view));
            }
        }

        public override void OnPop()
        {
            if (_drag != null && ReferenceEquals(UIDraggedBuilding.Current, _drag))
                UIDraggedBuilding.Clear();
        }

        public override void Build(GraphBuilder b)
        {
            if (!Placing) return;
            KingdomBuilderNodes.BuildGrid(b, _settlement, (slot, left, bottom) =>
                GraphNodes.Button(() => SlotLabel(slot, left, bottom), () => Place(slot), () => CanPlace(slot), sound: null));
            b.BeginStop("placement_actions");
            b.AddItem(ControlId.Structural("kingdom:placement_cancel"),
                GraphNodes.Button(() => Loc.T("action.cancel"), Close));
        }

        private bool CanPlace(SettlementGridTopology.Slot slot)
        {
            var view = slot.GetSlotObject();
            return Placing && _settlement.CanBuild(_building) && view != null && view.Visible
                && view.CanBuildHere(_building);
        }

        private string SlotLabel(SettlementGridTopology.Slot slot, int left, int bottom)
        {
            var label = KingdomBuilderNodes.SlotLabel(slot, left, bottom);
            var footprint = slot.GetRect(_building).ToArray();
            string status;
            if (CanPlace(slot))
            {
                var cells = string.Join(", ", footprint.OrderBy(s => s.Y).ThenBy(s => s.X)
                    .Select(s => KingdomBuilderNodes.Coordinate(s, left, bottom)));
                status = Loc.T("kingdom.placement_fits", new { cells });
            }
            else if (footprint.Any(s => s == null || s.MinLevel > _settlement.Level || s.GetSlotObject() == null))
                status = Loc.T("kingdom.placement_off_grid");
            else if (footprint.FirstOrDefault(s => s.Building != null) is SettlementGridTopology.Slot occupied)
                status = Loc.T("kingdom.placement_occupied", new { cell = KingdomBuilderNodes.Coordinate(occupied, left, bottom) });
            else if (footprint.Any(s => s.GetSlotObject().SpecialType != _building.SpecialSlot))
                status = Loc.T("kingdom.placement_wrong_slot");
            else
                status = Loc.T("kingdom.placement_invalid");
            return string.Join(", ", label, status);
        }

        private void Place(SettlementGridTopology.Slot slot)
        {
            if (!CanPlace(slot)) return;
            EventBus.RaiseEvent<ISettlementSlotHandler>(h => h.OnSlotClick(slot.GetSlotObject()));
            if (slot.Building?.Blueprint != _building) return;
            Tts.Speak(Loc.T("kingdom.placement_started", new { name = (string)_building.Name }));
            Close();
        }

        private void Close() => ParentScreen?.RemoveChild(this);

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.cancel"), _ => Close());
        }
    }
}
