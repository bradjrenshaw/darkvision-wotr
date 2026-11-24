using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.GameModes;
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
        private enum Tab { Events, Decrees, Map, Campaign, Builder }

        private static readonly IReadOnlyList<InputCategory> Categories =
            new[] { InputCategory.Kingdom, InputCategory.UI };
        private static readonly Tab[] Tabs = { Tab.Events, Tab.Decrees, Tab.Map, Tab.Campaign, Tab.Builder };

        private bool _builderSelected;
        private Tab? _tabAfterBuilder;
        private ControlId _returnToCard;

        public KingdomScreen() { Wrap = true; }

        public override string Key => "ctx.kingdom";
        public override string ScreenName => Loc.T("screen.kingdom");
        public override bool Exclusive => true;
        public override IReadOnlyList<InputCategory> InputCategories => Categories;
        public override object InitialFocusStop => StopForTab(CurrentTab());

        private static object StopForTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.Events: return KingdomEventScreen.AvailableStop;
                case Tab.Decrees: return KingdomDecreeNodes.FilterStop;
                default: return tab;
            }
        }

        private static bool InBuilder => Game.Instance?.RootUiContext?.IsCityBuilder == true;
        private static bool Loading => LoadingProcess.Instance?.IsLoadingScreenActive == true
            || Game.Instance?.KingdomController?.IsKingdomLoading == true;

        public override bool IsActive() => InBuilder || Game.Instance?.RootUiContext?.IsKingdom == true;

        private Tab CurrentTab()
        {
            if (InBuilder) return Tab.Builder;
            switch (KingdomState.Instance?.NavigationType)
            {
                case KingdomNavigationType.LocalMap: return _builderSelected ? Tab.Builder : Tab.Map;
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

            int current = System.Array.IndexOf(Tabs, CurrentTab());
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
            _builderSelected = tab == Tab.Builder;
            switch (tab)
            {
                case Tab.Events: state.NavigationType = KingdomNavigationType.Kingdom; break;
                case Tab.Decrees: state.NavigationType = KingdomNavigationType.Project; break;
                case Tab.Campaign: state.NavigationType = KingdomNavigationType.Army; break;
                default: state.NavigationType = KingdomNavigationType.LocalMap; break;
            }
            if (tab == Tab.Events || tab == Tab.Decrees) Tts.Speak(TabName(tab));
            Navigation.FocusStop(StopForTab(tab));
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
            // The builder returns via a scene load. Wait for the new kingdom view before changing tabs.
            if (_tabAfterBuilder is Tab tab && !Loading && Game.Instance.CurrentMode == GameModeType.Kingdom
                && KingdomState.Instance?.KingdomManagementActive == true
                && Game.Instance.RootUiContext?.KingdomVM != null)
            {
                _tabAfterBuilder = null;
                SelectTab(tab);
            }
            if (KingdomState.Instance?.NavigationType != KingdomNavigationType.LocalMap)
                _builderSelected = false;

            if (_returnToCard != null)
            {
                var currentTab = CurrentTab();
                if (currentTab == Tab.Events || currentTab == Tab.Decrees) Navigation.FocusNode(_returnToCard);
                _returnToCard = null;
            }
        }

        public override void OnPop() => _builderSelected = false;

        public override void Build(GraphBuilder b)
        {
            var tab = CurrentTab();
            if (tab == Tab.Events || tab == Tab.Decrees)
            {
                if (!KingdomEventScreen.BuildLists(b, tab == Tab.Decrees)) return;
            }
            else
            {
                b.BeginStop(tab);
                b.AddItem(ControlId.Structural("kingdom:tab:" + tab), GraphNodes.Text(() => TabName(tab)));
                if (tab == Tab.Builder && !InBuilder)
                    b.AddItem(ControlId.Structural("kingdom:builder_entry"), GraphNodes.Text(() => Loc.T("kingdom.builder_entry")));
            }

            if (InBuilder)
                BuildSettlementToolbar(b);
            else
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
            {
                _builderSelected = false;
                Object.FindObjectOfType<CityBuilderUIBack>()?.BackToMap();
            }
            else if (_builderSelected)
                SelectTab(Tab.Map);
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

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Back());
        }
    }
}
