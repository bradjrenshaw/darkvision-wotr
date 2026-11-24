using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.GameModes;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.Settlements;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using WrathAccess.Input;
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using Button = UnityEngine.UI.Button;
using Object = UnityEngine.Object;

namespace WrathAccess.Screens
{
    internal static class KingdomBuilderNodes
    {
        internal const string GridStop = "kingdom:building_grid";

        internal static void Scan(GraphDir direction)
        {
            if (!KingdomScreen.HasBuilderGridFocus
                || !(Navigation.Active is GraphNavigator navigator)
                || !(navigator.FocusedNodeId?.Reference is SettlementGridTopology.Slot slot))
                return;

            var render = navigator.CurrentRender;
            var node = render.NodeAt(navigator.FocusedNodeId);
            int steps = 0;
            while (node.Transitions.TryGetValue(direction, out var edge)
                && render.NodeAt(edge.Destination) is GraphNode next
                && next.Id.Reference is SettlementGridTopology.Slot nextSlot)
            {
                node = next;
                slot = nextSlot;
                steps++;
                if (slot.Building != null) break;
            }

            if (steps > 0) Navigation.FocusNode(node.Id, announce: false);
            var level = KingdomState.Instance.CurrentSettlement.Level;
            var slots = slot.Grid.AllSlots.Where(s => s.MinLevel <= level).ToArray();
            int left = slots.Min(s => s.X);
            int bottom = slots.Min(s => s.Y);
            int count = Math.Max(0, steps - 1); // The landing cell is announced separately.
            var target = steps > 0 && slot.Building != null
                ? SlotLabel(slot, left, bottom)
                : Loc.T("kingdom.grid_cell", new { name = Loc.T("kingdom.grid_edge"), cell = Coordinate(slot, left, bottom) });
            Tts.Speak(Loc.T(count == 1 ? "kingdom.grid_skipped_one" : "kingdom.grid_skipped_many", new { count, target }),
                interrupt: true);
        }

        internal static void Build(GraphBuilder b)
        {
            var settlement = KingdomState.Instance?.CurrentSettlement;
            if (settlement == null) return;
            b.BeginStop(GridStop).PushContext(Loc.T("kingdom.building_grid"), positions: false);
            var grids = settlement.Topology.Grids
                .Select(grid => (Grid: grid, Slots: grid.AllSlots.Where(s => s.MinLevel <= settlement.Level).ToArray()))
                .Where(grid => grid.Slots.Length > 0).ToArray();
            var locations = new Dictionary<SettlementBuilding, string>();
            int gridNumber = 0;
            foreach (var (grid, slots) in grids)
            {
                int index = ++gridNumber;
                int left = slots.Min(s => s.X);
                int bottom = slots.Min(s => s.Y);
                var occupied = slots.Where(s => s.Building != null).GroupBy(s => s.Building).ToArray();
                foreach (var building in occupied)
                {
                    var cells = string.Join(", ", building.OrderBy(s => s.Y).ThenBy(s => s.X)
                        .Select(s => Coordinate(s, left, bottom)));
                    locations[building.Key] = Loc.T("kingdom.building_grid_location", new { index, cells });
                }
                var title = Loc.T("kingdom.grid_number", new { index, count = grids.Length });
                var buildings = Loc.T(occupied.Length == 1 ? "kingdom.grid_one_building" : "kingdom.grid_building_count",
                    new { count = occupied.Length });
                b.SetRegion(grid).PushContext(string.Join(", ", title, buildings), positions: false);
                foreach (var slot in slots.OrderByDescending(s => s.Y).ThenBy(s => s.X))
                {
                    var node = slot.Building == null
                        ? GraphNodes.Text(() => SlotLabel(slot, left, bottom))
                        : GraphNodes.Button(() => SlotLabel(slot, left, bottom), () => Open(slot.Building));
                    if (slot.Building != null)
                        node.OnTooltip = () => KingdomBuildingNodes.OpenTooltip(slot.Building);
                    b.AddNode(ControlId.ForObject(slot), node);
                }
                foreach (var row in slots.GroupBy(s => s.Y))
                    ConnectSlots(b, row.OrderBy(s => s.X), GraphDir.Right, GraphDir.Left);
                foreach (var column in slots.GroupBy(s => s.X))
                    ConnectSlots(b, column.OrderByDescending(s => s.Y), GraphDir.Down, GraphDir.Up);
                b.PopContext();
            }
            if (gridNumber == 0)
                b.AddItem(ControlId.Structural("kingdom:no_slots"), GraphNodes.Text(() => Loc.T("kingdom.no_slots")));
            b.PopContext();

            b.BeginStop("kingdom:built").PushContext(Loc.T("kingdom.built"));
            var sheet = new GraphSheet(b, "kingdom:built:");
            sheet.Region(null).Line(GraphNodes.Button(() => Loc.T("kingdom.build_new"),
                () => ScreenManager.Current?.PushChild(new BuildingCatalogScreen())));
            KingdomBuildingNodes.BuildTable(sheet, settlement, Open,
                building => locations.TryGetValue(building, out var location) ? location : null);
            sheet.Finish();
            b.PopContext();
        }

        private static void ConnectSlots(GraphBuilder b, IEnumerable<SettlementGridTopology.Slot> slots,
            GraphDir forward, GraphDir backward)
        {
            ControlId previous = null;
            foreach (var slot in slots)
            {
                var id = ControlId.ForObject(slot);
                if (previous != null)
                {
                    b.Connect(previous, forward, id);
                    b.Connect(id, backward, previous);
                }
                previous = id;
            }
        }

        private static string SlotLabel(SettlementGridTopology.Slot slot, int left, int bottom)
        {
            var name = slot.Building != null ? KingdomBuildingNodes.Name(slot.Building) : Loc.T("kingdom.empty_slot");
            var view = slot.GetSlotObject();
            if (view != null && view.SpecialType != SettlementState.SpecialSlotType.None)
                name = string.Join(", ", name, KingdomUIRoot.Instance.Settlement.GetSlotsName(view.SpecialType));
            return Loc.T("kingdom.grid_cell", new { name, cell = Coordinate(slot, left, bottom) });
        }

        private static string Coordinate(SettlementGridTopology.Slot slot, int left, int bottom) =>
            TextUtil.GridCoordinate(slot.X - left, slot.Y - bottom);

        private static void Open(SettlementBuilding building)
        {
            var window = Object.FindObjectOfType<SettlementBuildingWindow>();
            if (Game.Instance.CurrentMode == GameModeType.KingdomSettlement && building != null
                && ReferenceEquals(KingdomState.Instance.CurrentSettlement, building.Owner) && window != null)
                window.OnBuildClick(building);
        }

        private sealed class BuildingCatalogScreen : Screen
        {
            public override string Key => "overlay.kingdom_catalog";
            public override bool IsActive() => false; // opened as a child
            public override string ScreenName => Loc.T("kingdom.build_catalog");
            public override void Build(GraphBuilder b) =>
                b.AddItem(ControlId.Structural(Key), GraphNodes.Text(() => Loc.T("kingdom.catalog_todo")));

            public override IEnumerable<ElementAction> GetActions()
            {
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => ParentScreen?.RemoveChild(this));
            }
        }
    }

    internal static class KingdomBuildingNodes
    {
        private static readonly KingdomResource[] Resources =
            { KingdomResource.Finances, KingdomResource.Materials, KingdomResource.Favors };

        internal static string Name(SettlementBuilding building) => TextUtil.StripRichText(building.Blueprint.Name);

        internal static void BuildTable(GraphSheet sheet, SettlementState settlement, Action<SettlementBuilding> open = null,
            Func<SettlementBuilding, string> location = null)
        {
            sheet.Region(null).Line(GraphNodes.Heading(() => Loc.T("kingdom.buildings")));
            var buildings = settlement.Buildings.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            if (buildings.Length == 0)
                sheet.Line(GraphNodes.Text(() => Loc.T("kingdom.no_buildings")));
            else
            {
                var columns = new List<string>();
                if (location != null) columns.Add(Loc.T("kingdom.building_location"));
                columns.AddRange(new[] { Loc.T("kingdom.building_description"), Loc.T("kingdom.building_slots"),
                    Loc.T("kingdom.building_price"), Loc.T("kingdom.building_status") });
                sheet.Region(Loc.T("kingdom.buildings"), columns.ToArray());
                foreach (var building in buildings)
                {
                    var name = open == null
                        ? GraphNodes.Text(() => Name(building))
                        : GraphNodes.Button(() => Name(building), () => open(building));
                    name.OnTooltip = () => OpenTooltip(building);
                    var cells = new List<KeyValuePair<int, NodeVtable>>();
                    if (location != null) cells.Add(Cell(1, building, () => location(building)));
                    int col = cells.Count + 1;
                    cells.AddRange(new[]
                    {
                        Cell(col, building, () => TextUtil.StripRichText(building.Blueprint.Description)),
                        Cell(col + 1, building, () => Slots(building.Blueprint)),
                        Cell(col + 2, building, () => Price(building.Owner.GetActualCost(building.Blueprint))),
                        Cell(col + 3, building, () => Status(building)),
                    });
                    sheet.RowAt(name, building, cells);
                }
            }
        }

        private static KeyValuePair<int, NodeVtable> Cell(int index, SettlementBuilding building, Func<string> text)
        {
            var node = GraphNodes.Text(text);
            node.OnTooltip = () => OpenTooltip(building);
            return new KeyValuePair<int, NodeVtable>(index, node);
        }

        private static string Slots(BlueprintSettlementBuilding blueprint) =>
            string.Join(", ", blueprint.SlotCount, blueprint.SpecialSlot == SettlementState.SpecialSlotType.None
                ? Loc.T("kingdom.standard_slot")
                : KingdomUIRoot.Instance.Settlement.GetSlotsName(blueprint.SpecialSlot));

        private static string Status(SettlementBuilding building) => !building.IsFinished
            ? string.Format(KingdomUIRoot.Instance.Settlement.DaysToBuildSimpleFormat,
                Math.Max(1, building.FinishedOn - KingdomState.Instance.CurrentDay))
            : building.Blueprint.UpgradesTo != null ? Loc.T("kingdom.building_has_upgrade") : Loc.T("kingdom.building_finished");

        internal static string Price(KingdomResourcesAmount amount)
        {
            var parts = ResourceLines(amount).ToArray();
            return parts.Length == 0 ? Loc.T("kingdom.building_free") : string.Join(", ", parts);
        }

        private static IEnumerable<string> ResourceLines(KingdomResourcesAmount amount)
        {
            foreach (var resource in Resources)
            {
                int value = amount.Get(resource);
                if (value != 0)
                    yield return Loc.T("crusade.price", new { count = value, name = KingdomUIRoot.Instance.GetResourceElement(resource).Name });
            }
        }

        private static IEnumerable<string> Changes(KingdomStats.Changes changes)
        {
            if (changes == null) yield break;
            foreach (KingdomStats.Type stat in Enum.GetValues(typeof(KingdomStats.Type)))
            {
                int value = changes[stat];
                if (value != 0)
                    yield return Loc.T("worldmap.value_line", new
                    {
                        name = KingdomUIRoot.Instance.GetStatElement(stat).Name,
                        value = value.ToString("+0;-0"),
                    });
            }
            foreach (var line in ResourceLines(changes.ResourcesPerTurn))
                yield return Loc.T("kingdom.resources_per_turn", new { resources = line });
            foreach (var line in ResourceLines(changes.ResourcesOneTime))
                yield return Loc.T("kingdom.resources_once", new { resources = line });
        }

        internal static void AddChanges(GraphSheet sheet, KingdomStats.Changes changes)
        {
            foreach (string line in Changes(changes))
                sheet.Line(GraphNodes.Text(() => line));
        }

        private static IEnumerable<string> Details(SettlementBuilding building)
        {
            var blueprint = building.Blueprint;
            yield return blueprint.FullDescription;
            yield return Loc.T("worldmap.value_line", new { name = Loc.T("kingdom.building_slots"), value = Slots(blueprint) });
            yield return Status(building);
            foreach (string line in Changes(building.AppliedChanges ?? blueprint.StatChanges)) yield return line;
            if (blueprint.UpgradesTo is BlueprintSettlementBuilding upgrade && upgrade != null)
            {
                yield return Loc.T("kingdom.building_upgrades_to", new
                {
                    name = (string)upgrade.Name,
                    tier = KingdomUIRoot.Instance.Settlement.GetSettlementType(upgrade.MinLevel),
                });
                var extra = new KingdomStats.Changes(upgrade.StatChanges);
                extra.Accumulate(blueprint.StatChanges, -1);
                foreach (string line in Changes(extra)) yield return line;
            }
            yield return Loc.T("kingdom.building_sell_value", new { price = Price(building.Owner.GetSellPrice(blueprint)) });
        }

        internal static void OpenTooltip(SettlementBuilding building)
        {
            if (building != null)
                TooltipScreen.Open(new TooltipTemplateSimple(Name(building), string.Join("\n", Details(building))));
        }

        internal static void BuildDetails(GraphSheet sheet, SettlementBuilding building)
        {
            foreach (string line in Details(building))
            {
                var node = GraphNodes.Text(() => TextUtil.StripRichText(line));
                if (UI.Tooltips.TooltipLinks.Extract(line).Count > 0)
                    node.OnTooltip = () => TooltipScreen.FollowLinks(line, null);
                sheet.Line(node);
            }
        }
    }

    internal sealed class KingdomBuildingScreen : Screen
    {
        private static readonly FieldInfo BuildingField = AccessTools.Field(typeof(SettlementBuildingWindowBase), "Building");
        private static readonly FieldInfo SellBlockField = AccessTools.Field(typeof(SettlementBuildingWindow), "m_SellBlock");
        private static readonly FieldInfo CancelButtonField = AccessTools.Field(SellBlockField.FieldType, "SellCancelButton");
        private static readonly InputCategory[] Categories = { InputCategory.UI, InputCategory.Kingdom };

        private SettlementBuildingWindow _window;
        private SettlementBuilding Building => _window != null ? BuildingField.GetValue(_window) as SettlementBuilding : null;
        private bool Selling => _window != null && CancelButtonField.GetValue(SellBlockField.GetValue(_window)) is Button button
            && button.gameObject.activeInHierarchy;

        public override string Key => "overlay.kingdom_building";
        public override int Layer => 15;
        public override bool Exclusive => true;
        public override IReadOnlyList<InputCategory> InputCategories => Categories;

        public override bool IsActive()
        {
            _window = Game.Instance?.CurrentMode == GameModeType.KingdomSettlement
                ? Object.FindObjectOfType<SettlementBuildingWindow>() : null;
            return Building != null;
        }

        public override void Build(GraphBuilder b)
        {
            var building = Building;
            if (building == null) return;
            var sheet = new GraphSheet(b.BeginStop("kingdom:building_details"), "kingdom:building_details:");
            sheet.Region(null).Line(GraphNodes.Heading(() => KingdomBuildingNodes.Name(building)));
            KingdomBuildingNodes.BuildDetails(sheet, building);
            sheet.Region(null);
            var upgrade = building.Blueprint.UpgradesTo;
            sheet.Line(GraphNodes.Button(() => upgrade == null
                ? UIStrings.Instance.CommonTexts.Upgrade.ToString()
                : Loc.T("kingdom.upgrade_building", new
                {
                    price = KingdomBuildingNodes.Price(building.Owner.GetActualCost(upgrade)),
                }), () => _window.OnUpdateClick(),
                () => ReferenceEquals(Building, building) && !Selling && building.IsFinished
                    && upgrade != null && building.Owner.CanBuildUprgade(upgrade)));
            var sale = GraphNodes.Button(() => Loc.T(Selling ? "kingdom.cancel_sale" : "kingdom.sell_building"),
                () =>
                {
                    if (!ReferenceEquals(Building, building)) return;
                    if (Selling) _window.CancelSold();
                    else ConfirmSale(building);
                });
            sheet.Line(sale);
            sheet.Line(GraphNodes.Button(() => Loc.T("action.close"), Close));
            sheet.Finish();
        }

        private void ConfirmSale(SettlementBuilding building)
        {
            var window = _window;
            var text = Loc.T("kingdom.confirm_sale", new
            {
                name = KingdomBuildingNodes.Name(building),
                price = KingdomBuildingNodes.Price(building.Owner.GetSellPrice(building.Blueprint)),
            });
            EventBus.RaiseEvent<IMessageModalUIHandler>(h => h.HandleOpen(text, MessageModalBase.ModalType.Dialog,
                button =>
                {
                    if (button == MessageModalBase.ButtonType.Yes && window != null
                        && ReferenceEquals(BuildingField.GetValue(window), building))
                        window.Sold();
                }));
        }

        private void Close()
        {
            if (_window == null) return;
            if (Selling) _window.CancelSold();
            _window.Hide();
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }
    }
}
