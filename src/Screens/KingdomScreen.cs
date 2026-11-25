using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.GameModes;
using Kingmaker.Globalmap.View;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.UI;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.Kingdom;
using UnityEngine;
using WrathAccess.Input;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    internal sealed class KingdomScreen : Screen
    {
        private enum Tab { Events, Decrees, Map, Campaign }

        private static readonly IReadOnlyList<InputCategory> Categories =
            new[] { InputCategory.Kingdom, InputCategory.UI };
        private static readonly IReadOnlyList<InputCategory> MapCategories =
            new[] { InputCategory.Kingdom, InputCategory.KingdomMap, InputCategory.UI };
        private static readonly IReadOnlyList<InputCategory> GridCategories =
            new[] { InputCategory.SettlementGrid, InputCategory.Kingdom, InputCategory.UI };
        private static readonly IReadOnlyList<InputCategory> CampaignFocused =
            new[] { InputCategory.Kingdom, InputCategory.UI, InputCategory.Exploration, InputCategory.WorldMap };
        private static readonly IReadOnlyList<InputCategory> CampaignUnfocused =
            new[] { InputCategory.Kingdom, InputCategory.Exploration, InputCategory.WorldMap, InputCategory.UI };
        private static readonly Tab[] Tabs = { Tab.Events, Tab.Decrees, Tab.Map, Tab.Campaign };

        private readonly WorldMapNavigation _campaign = new WorldMapNavigation();
        private bool _wasInCampaign;
        private bool _wasInBuilder;
        private Tab? _tabAfterBuilder;
        private ControlId _returnToCard;

        public KingdomScreen() { Wrap = true; }

        public override string Key => "ctx.kingdom";
        public override string ScreenName => CampaignOpen ? TabName(Tab.Campaign) : Loc.T("screen.kingdom");
        public override bool Exclusive => true;
        public override bool StartUnfocused => CampaignOpen;
        public override IReadOnlyList<InputCategory> InputCategories =>
            CampaignOpen ? (Navigation.HasFocus ? CampaignFocused : CampaignUnfocused)
            : HasBuilderGridFocus ? GridCategories : HasMapFocus ? MapCategories : Categories;
        public override bool AllowsTypeahead => !HasMapFocus && !CampaignOpen;
        public override object InitialFocusStop => InBuilder ? KingdomBuilderNodes.GridStop : StopForTab(CurrentTab());

        internal static bool MapOpen => Game.Instance?.CurrentMode == GameModeType.Kingdom
            && KingdomState.Instance?.NavigationType == KingdomNavigationType.LocalMap;
        internal static bool CampaignOpen => Game.Instance?.RootUiContext?.IsKingdom == true && !InBuilder
            && KingdomState.Instance?.NavigationType == KingdomNavigationType.Army;
        internal static bool CampaignMapReady => CampaignOpen && !Loading
            && Game.Instance.CurrentMode == GameModeType.Kingdom
            && GlobalMapView.Instance != null;
        internal static bool CampaignInteractive => CampaignMapReady
            && Game.Instance.GlobalMapController?.SelectedArmy != null;
        internal static bool HasMapFocus => ScreenManager.Current is KingdomScreen && MapOpen && !Loading
            && Equals(Navigation.FocusedStopKey, KingdomMapNodes.MapStop);
        internal static bool HasBuilderGridFocus => ScreenManager.Current is KingdomScreen && InBuilder
            && Equals(Navigation.FocusedStopKey, KingdomBuilderNodes.GridStop);

        private static object StopForTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.Events: return KingdomEventScreen.AvailableStop;
                case Tab.Decrees: return KingdomDecreeNodes.FilterStop;
                case Tab.Map: return KingdomMapNodes.MapStop;
                default: return null;
            }
        }

        private static bool InBuilder => Game.Instance?.RootUiContext?.IsCityBuilder == true;
        private static bool Loading => LoadingProcess.Instance?.IsLoadingScreenActive == true
            || Game.Instance?.KingdomController?.IsKingdomLoading == true;

        public override bool IsActive() => InBuilder || Game.Instance?.RootUiContext?.IsKingdom == true;

        private Tab CurrentTab()
        {
            switch (KingdomState.Instance?.NavigationType)
            {
                case KingdomNavigationType.LocalMap: return Tab.Map;
                case KingdomNavigationType.Army: return Tab.Campaign;
                case KingdomNavigationType.Project:
                case KingdomNavigationType.ProjectRankUp:
                case KingdomNavigationType.ProjectDevelopment:
                case KingdomNavigationType.ProjectEconomy:
                case KingdomNavigationType.ProjectArmy:
                case KingdomNavigationType.ProjectCommand:
                case KingdomNavigationType.ProjectRelics:
                case KingdomNavigationType.ProjectOther: return Tab.Decrees;
                default: return Tab.Events;
            }
        }

        internal void StepTab(int direction)
        {
            var mode = Game.Instance?.CurrentMode;
            if (_tabAfterBuilder.HasValue || Loading
                || (mode != GameModeType.Kingdom && mode != GameModeType.KingdomSettlement))
                return;

            // The builder is opened from a settlement on the Map tab.
            int current = System.Array.IndexOf(Tabs, InBuilder ? Tab.Map : CurrentTab());
            var next = Tabs[(current + direction + Tabs.Length) % Tabs.Length];
            if (mode == GameModeType.KingdomSettlement)
            {
                var back = Object.FindObjectOfType<CityBuilderUIBack>();
                if (back == null) return;
                _tabAfterBuilder = next;
                // Ctrl+Tab explicitly switches to kingdom management even from a builder opened
                // directly on the world map. Regular Back still uses the original return route.
                var state = KingdomState.Instance;
                if (state.ExitSettlementToGlobalMap)
                {
                    state.ExitSettlementToGlobalMap = false;
                    state.ReturnPoint = null;
                }
                back.BackToMap();
                return;
            }

            Object.FindObjectOfType<KingdomUIEventWindow>()?.OnCrossPressed();
            Object.FindObjectOfType<KingdomUISettlementWindow>()?.OnCloseClick();
            Object.FindObjectOfType<KingdomRegionWindow>()?.OnCloseClick();
            SelectTab(next);
        }

        private void SelectTab(Tab tab)
        {
            var state = KingdomState.Instance;
            if (state == null) return;
            switch (tab)
            {
                case Tab.Events: state.NavigationType = KingdomNavigationType.Kingdom; break;
                case Tab.Decrees: state.NavigationType = KingdomNavigationType.Project; break;
                case Tab.Campaign: state.NavigationType = KingdomNavigationType.Army; break;
                default: state.NavigationType = KingdomNavigationType.LocalMap; break;
            }
            if (tab == Tab.Events || tab == Tab.Decrees || tab == Tab.Campaign) Tts.Speak(TabName(tab));
            SyncCampaign();
            if (tab != Tab.Campaign) Navigation.FocusStop(StopForTab(tab));
        }

        public override void OnFocus()
        {
            if (ScreenManager.PreviousFocused is KingdomEventScreen details)
                _returnToCard = details.LastCardId;
            else
                base.OnFocus();
        }

        public override void OnUpdate()
        {
            SyncCampaign();
            if (CampaignInteractive) _campaign.Update();

            if (!Loading && _wasInBuilder != InBuilder)
            {
                _wasInBuilder = InBuilder;
                Navigation.FocusStop(InitialFocusStop);
            }
            // The builder returns via a scene load. Wait for the new kingdom view before changing tabs.
            if (_tabAfterBuilder is Tab tab && !Loading && Game.Instance.CurrentMode == GameModeType.Kingdom
                && KingdomState.Instance?.KingdomManagementActive == true
                && Game.Instance.RootUiContext?.KingdomVM != null)
            {
                _tabAfterBuilder = null;
                SelectTab(tab);
            }
            if (_returnToCard != null)
            {
                var currentTab = CurrentTab();
                if (currentTab == Tab.Events || currentTab == Tab.Decrees) Navigation.FocusNode(_returnToCard);
                _returnToCard = null;
            }
        }

        private void SyncCampaign()
        {
            var campaign = CampaignOpen;
            if (campaign && !_wasInCampaign)
            {
                _campaign.Reset();
                Navigation.FocusStop(null);
                Navigation.Blur();
            }
            else if (!campaign && _wasInCampaign)
                _campaign.Clear();
            _wasInCampaign = campaign;
        }

        public override void OnPush() => KingdomWorldMap.Reset();

        public override void OnPop()
        {
            _wasInBuilder = false;
            _wasInCampaign = false;
            _campaign.Clear();
        }

        public override void Build(GraphBuilder b)
        {
            var tab = CurrentTab();
            if (InBuilder)
            {
                KingdomBuilderNodes.Build(b);
                BuildSettlementToolbar(b);
                return;
            }
            if (tab == Tab.Events || tab == Tab.Decrees)
            {
                if (!KingdomEventScreen.BuildLists(b, tab == Tab.Decrees)) return;
            }
            else if (tab == Tab.Map)
                KingdomMapNodes.Build(b);
            else if (CampaignMapReady)
            {
                GlobalMapSpellNodes.Build(b);
                if (CampaignInteractive) _campaign.Build(b);
            }

            GlobalMapToolbarNodes.Build(b, kingdom: true);
        }

        private static string TabName(Tab tab) => Loc.T("kingdom." + tab.ToString().ToLowerInvariant());

        private void BuildSettlementToolbar(GraphBuilder b)
        {
            b.BeginStop("toolbar").PushContext(Loc.T("worldmap.toolbar"));
            b.AddItem(ControlId.Structural("kingdom:settlement"), GraphNodes.Text(
                () => KingdomState.Instance?.CurrentSettlement?.Name ?? ""));
            BuildResource(b, KingdomResource.Finances, r => r.Finances);
            BuildResource(b, KingdomResource.Materials, r => r.Materials);
            BuildResource(b, KingdomResource.Favors, r => r.Favors);
            b.AddItem(ControlId.Structural("kingdom:builder_back"), GraphNodes.Button(
                () => Loc.T("kingdom.builder_back"), Back));
            b.AddItem(ControlId.Structural("kingdom:game_menu"), GraphNodes.Button(
                () => Loc.T("hud.game_menu"), OpenGameMenu));
            b.PopContext();
        }

        private static void BuildResource(GraphBuilder b, KingdomResource resource, System.Func<KingdomResourcesAmount, int> value)
        {
            b.AddItem(ControlId.Structural("kingdom:resource:" + resource), GraphNodes.Text(
                () => Loc.T("worldmap.value_line", new
                {
                    name = KingdomUIRoot.Instance.GetResourceElement(resource).Name,
                    value = value(KingdomState.Instance.Resources),
                })));
        }

        private void Back()
        {
            if (_tabAfterBuilder.HasValue || Loading) return;
            if (InBuilder)
                Object.FindObjectOfType<CityBuilderUIBack>()?.BackToMap();
            else if (KingdomEventScreen.HasOpenCard()
                || Object.FindObjectOfType<KingdomUISettlementWindow>() != null
                || Object.FindObjectOfType<KingdomRegionWindow>() != null)
                Game.Instance.UI.EscManager.OnEscPressed();
            else
                OpenGameMenu();
        }

        private static void OpenGameMenu()
        {
            if (!Loading)
                EventBus.RaiseEvent(delegate (IEscMenuHandler h) { h.HandleOpen(); });
        }

        internal static void StepFocusedTab(int direction)
        {
            if (ScreenManager.Current is KingdomScreen screen)
                screen.StepTab(direction);
            else if (ScreenManager.Current is KingdomMapDetailScreen || ScreenManager.Current is KingdomBuildingScreen)
                foreach (var s in ScreenManager.Stack)
                    if (s is KingdomScreen kingdom)
                    {
                        if (ScreenManager.Current is KingdomBuildingScreen building)
                            building.InvokeAction(ActionIds.Back);
                        kingdom.StepTab(direction);
                        break;
                    }
        }

        internal static void StepBuilderGrid(int direction)
        {
            if (HasBuilderGridFocus && Navigation.Active is GraphNavigator navigator)
                navigator.RegionJump(direction);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Back());
        }
    }
}
