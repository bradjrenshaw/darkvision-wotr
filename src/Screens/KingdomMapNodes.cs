using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.Armies;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Globalmap.View;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.Blueprints;
using Kingmaker.Kingdom.Settlements;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.Kingdom;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using UnityEngine;
using WrathAccess.Exploration;
using WrathAccess.Input;
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using Object = UnityEngine.Object;

namespace WrathAccess.Screens
{
    internal static class KingdomMapNodes
    {
        internal const string MapStop = "kingdom:world_map";

        private static IEnumerable<SettlementState> Settlements =>
            KingdomState.Instance.SettlementsManager.Settlements.Where(s =>
                !s.Destroyed && s.MarkerManager.SettlementMarker != null);

        private static IEnumerable<BlueprintRegion> Regions =>
            KingdomState.Instance.UIView.Regions?.Select(r => r.Blueprint)
            ?? Enumerable.Empty<BlueprintRegion>();

        internal static string Tier(SettlementState settlement) =>
            KingdomUIRoot.Instance.Settlement.GetSettlementType(settlement.Level);

        internal static void Build(GraphBuilder b)
        {
            b.BeginStop(MapStop).AddItem(ControlId.Structural(MapStop),
                GraphNodes.Button(() => Loc.T("screen.world_map"), GlobalMapCursor.Interact, sound: null));

            b.BeginStop("kingdom:regions").PushContext(Loc.T("kingdom.regions"), "list");
            foreach (var region in Regions)
                b.AddItem(ControlId.ForObject(region),
                    GraphNodes.Button(() => region.LocalizedName, () => OpenRegion(region)));
            if (!Regions.Any())
                b.AddItem(ControlId.Structural("kingdom:no_regions"), GraphNodes.Text(() => Loc.T("kingdom.no_regions")));
            b.PopContext();

            b.BeginStop("kingdom:settlements").PushContext(Loc.T("kingdom.settlements"), "list");
            foreach (var settlement in Settlements)
                b.AddItem(ControlId.ForObject(settlement), GraphNodes.Button(
                    () => string.Join(", ", settlement.Name, Tier(settlement)), () => OpenSettlement(settlement)));
            if (!Settlements.Any())
                b.AddItem(ControlId.Structural("kingdom:no_settlements"), GraphNodes.Text(() => Loc.T("kingdom.no_settlements")));
            b.PopContext();
        }

        internal static void OpenAt(Vector3 position, GlobalMapPointView point)
        {
            if (!KingdomScreen.HasMapFocus) return;
            var settlement = point?.Blueprint != null
                ? Settlements.FirstOrDefault(s => s.SettlementLocationRef.Is(point.Blueprint))
                : null;
            if (settlement == null)
                settlement = Settlements.Where(s => Geo.Distance(position, s.MarkerManager.SettlementMarker.transform.position) <= 2f)
                    .OrderBy(s => Geo.Distance(position, s.MarkerManager.SettlementMarker.transform.position)).FirstOrDefault();
            if (settlement != null) { OpenSettlement(settlement); return; }
            var region = RegionObject.Instances.FirstOrDefault(r => r.TitleView != null
                && r.TitleView.isActiveAndEnabled && Geo.Distance(position, r.TitleView.transform.position) <= 2f
                && Regions.Contains(r.Blueprint));
            if (region != null) { OpenRegion(region.Blueprint); return; }
            Tts.Speak(Loc.T("kingdom.map_read_only"));
        }

        private static void OpenSettlement(SettlementState settlement)
        {
            if (!KingdomScreen.MapOpen) return;
            settlement.MarkerManager.SettlementMarker?.HandleClick();
        }

        private static void OpenRegion(BlueprintRegion region)
        {
            if (KingdomScreen.MapOpen)
                EventBus.RaiseEvent<IKingdomRegionClickHandler>(h => h.OnRegionClick(region));
        }
    }

    internal sealed class KingdomMapDetailScreen : Screen
    {
        private static readonly System.Reflection.FieldInfo SettlementField =
            AccessTools.Field(typeof(KingdomUISettlementWindow), "m_Settlement");
        private static readonly System.Reflection.FieldInfo RegionField =
            AccessTools.Field(typeof(KingdomRegionWindow), "m_BlueprintRegion");
        private static readonly InputCategory[] Categories = { InputCategory.UI, InputCategory.Kingdom };

        private KingdomUISettlementWindow _settlementWindow;
        private KingdomRegionWindow _regionWindow;

        public override string Key => "overlay.kingdom_map_detail";
        public override int Layer => 15;
        public override bool Exclusive => true;
        public override IReadOnlyList<InputCategory> InputCategories => Categories;

        public override bool IsActive()
        {
            _settlementWindow = KingdomScreen.MapOpen ? Object.FindObjectOfType<KingdomUISettlementWindow>() : null;
            _regionWindow = KingdomScreen.MapOpen && _settlementWindow == null
                ? Object.FindObjectOfType<KingdomRegionWindow>() : null;
            return _settlementWindow != null || _regionWindow != null;
        }

        public override void Build(GraphBuilder b)
        {
            var sheet = new GraphSheet(b.BeginStop("kingdom:map_detail"), "kingdom:map_detail:");
            if (_settlementWindow != null && SettlementField.GetValue(_settlementWindow) is SettlementState settlement)
                BuildSettlement(sheet, settlement);
            else if (_regionWindow != null && RegionField.GetValue(_regionWindow) is BlueprintRegion region)
                BuildRegion(sheet, region);
            sheet.Finish();
        }

        private void BuildSettlement(GraphSheet sheet, SettlementState settlement)
        {
            var window = _settlementWindow;
            sheet.Region(null).Line(GraphNodes.Heading(() => string.Join(", ", settlement.Name, KingdomMapNodes.Tier(settlement))));
            if (settlement.ReadyForLevelUp)
                sheet.Line(GraphNodes.Button(() => UIStrings.Instance.CommonTexts.Upgrade,
                    () =>
                    {
                        window.OnUpgradeClick();
                        AnnounceSettlement(window);
                    },
                    () => ReferenceEquals(settlement, SettlementField.GetValue(window)) && settlement.ReadyForLevelUp));
            sheet.Line(GraphNodes.Button(() => Loc.T("kingdom.open_builder"), () => window.OnEnterClick()));

            sheet.Region(null).Line(GraphNodes.Heading(() => Loc.T("kingdom.settlement_stats")));
            var growth = KingdomState.Instance.RecruitsManager.Growth
                .Where(r => settlement.GetUnitGrowthBonus(r.Unit) > 0).ToArray();
            foreach (var recruit in growth)
                sheet.Row(GraphNodes.Text(() => Loc.T("kingdom.recruit_growth", new
                {
                    name = recruit.Unit.CharacterName,
                    count = settlement.GetUnitGrowthBonus(recruit.Unit),
                }), () => new TooltipTemplateSimple(recruit.Unit.CharacterName, recruit.Unit.GetArmyData()?.Description)), recruit.Unit);
            if (growth.Length == 0)
                sheet.Line(GraphNodes.Text(() => Loc.T("kingdom.no_recruit_growth")));

            KingdomBuildingNodes.BuildTable(sheet, settlement);
            sheet.Region(null);
            if (KingdomState.Instance.SettlementsManager.Settlements.Count(s => s.MarkerManager.SettlementMarker != null) > 1)
            {
                sheet.Line(GraphNodes.Button(() => Loc.T("kingdom.previous_settlement"), () =>
                {
                    window.OnPrevPressed();
                    AnnounceSettlement(window);
                }));
                sheet.Line(GraphNodes.Button(() => Loc.T("kingdom.next_settlement"), () =>
                {
                    window.OnNextPressed();
                    AnnounceSettlement(window);
                }));
            }
            sheet.Line(GraphNodes.Button(() => Loc.T("action.close"), Close));
        }

        private void BuildRegion(GraphSheet sheet, BlueprintRegion region)
        {
            var window = _regionWindow;
            sheet.Region(null).Line(GraphNodes.Heading(() => region.LocalizedName));
            sheet.Line(GraphNodes.Text(() => TextUtil.StripRichText(region.ClaimedDescription)));
            KingdomBuildingNodes.AddChanges(sheet, region.StatsWhenClaimed);
            var state = KingdomState.Instance.RegionsManager.GetRegion(region);
            if (state?.IsUpgraded == true && state.Upgrade != null)
            {
                sheet.Region(null).Line(GraphNodes.Heading(() => Loc.T("kingdom.region_upgrade")));
                sheet.Line(GraphNodes.Text(() => TextUtil.StripRichText(state.Upgrade.ChangeDescription)));
                KingdomBuildingNodes.AddChanges(sheet, state.Upgrade.StatsChanges);
            }
            sheet.Region(null);
            if (KingdomState.Instance.UIView.Regions.Count() > 1)
            {
                sheet.Line(GraphNodes.Button(() => Loc.T("kingdom.previous_region"), () =>
                {
                    window.OnNaviPrevClick();
                    AnnounceRegion(window);
                }));
                sheet.Line(GraphNodes.Button(() => Loc.T("kingdom.next_region"), () =>
                {
                    window.OnNaviNextClick();
                    AnnounceRegion(window);
                }));
            }
            sheet.Line(GraphNodes.Button(() => Loc.T("action.close"), Close));
        }

        private void Close()
        {
            if (_settlementWindow != null) _settlementWindow.OnCloseClick();
            else if (_regionWindow != null) _regionWindow.OnCloseClick();
        }

        private static void AnnounceSettlement(KingdomUISettlementWindow window)
        {
            if (SettlementField.GetValue(window) is SettlementState settlement)
                Tts.Speak(string.Join(", ", settlement.Name, KingdomMapNodes.Tier(settlement)));
        }

        private static void AnnounceRegion(KingdomRegionWindow window)
        {
            if (RegionField.GetValue(window) is BlueprintRegion region)
                Tts.Speak(region.LocalizedName);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }
    }
}
