using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.UI;
using Kingmaker.UI.Kingdom;
using Kingmaker.UI.MVVM._PCView.Kingdom;
using Owlcat.Runtime.UI.Controls.Button;
using TMPro;
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    // Mouse mode uses the legacy card controllers. Read the same hands, window and footer that
    // drive the PC UI, so our actions share the game's visibility, eligibility and confirmations.
    internal sealed class KingdomEventScreen : Screen
    {
        internal const string AvailableStop = "events.available";

        private static readonly FieldInfo PendingBarUpdate = AccessTools.Field(typeof(KingdomEventBarController), "m_NeedUpdate");
        private static readonly FieldInfo SelectedEvent = AccessTools.Field(typeof(KingdomUIEventWindow), "m_KingdomEventView");
        private static readonly FieldInfo SelectedCard = AccessTools.Field(typeof(KingdomUIEventWindow), "m_Cart");
        private static readonly FieldInfo Footer = AccessTools.Field(typeof(KingdomUIEventWindow), "m_Footer");
        private static readonly FieldInfo FooterEvent = AccessTools.Field(typeof(KingdomUIEventWindowFooter), "m_KingdomEventView");
        private static readonly FieldInfo Duration = AccessTools.Field(typeof(KingdomUIEventWindow), "m_Duration");
        private static readonly FieldInfo Repeatable = AccessTools.Field(typeof(KingdomUIEventWindow), "m_Repeatable");
        private static readonly FieldInfo Result = AccessTools.Field(typeof(KingdomUIEventWindow), "m_ResultDescription");
        private static readonly FieldInfo Start = AccessTools.Field(typeof(KingdomUIEventWindowFooter), "m_StartEvent");
        private static readonly FieldInfo StartLabel = AccessTools.Field(typeof(KingdomUIEventWindowFooter), "m_StartEventLabel");
        private static readonly FieldInfo ThroneRoomHint = AccessTools.Field(typeof(KingdomUIEventWindowFooter), "m_GoThroneRoomLabel");
        private static readonly FieldInfo ChoiceLabel = AccessTools.Field(typeof(KingdomUIEventWindowFooterSolution), "m_TextLabel");

        private static readonly (FieldInfo Button, string Label, Action<KingdomUIEventWindowFooter> Press)[] FooterActions =
        {
            (FooterButton("m_Ignore"), "kingdom.event_ignore", footer => footer.OnIgnore()),
            (FooterButton("m_Unignore"), "kingdom.event_unignore", footer => footer.OnUnignore()),
            (FooterButton("m_Cancel"), "kingdom.event_cancel", footer => footer.OnCancelEvent()),
            (FooterButton("m_Drop"), "kingdom.event_abandon", footer => footer.OnDropPressed()),
            (FooterButton("m_ThroneRoom"), null, footer => footer.OnThroneRoom()),
        };

        private static KingdomPCView _view;
        private static KingdomEventBarController _bar;
        private static KingdomUIEventWindow _window;

        internal ControlId LastCardId { get; private set; }
        private bool _wasFinished;

        public KingdomEventScreen() { Wrap = true; }

        public override string Key => "overlay.kingdom_event";
        public override int Layer => 15;
        public override bool Exclusive => true;

        private static bool InKingdom => Game.Instance?.RootUiContext?.IsKingdom == true
            && Game.Instance.RootUiContext.IsCityBuilder == false;

        private static void FindViews()
        {
            if (_view != null) return;
            _view = UnityEngine.Object.FindObjectOfType<KingdomPCView>();
            _bar = _view != null ? _view.GetComponentInChildren<KingdomEventBarController>(true) : null;
            _window = _view != null ? _view.GetComponentInChildren<KingdomUIEventWindow>(true) : null;
        }

        private static FieldInfo FooterButton(string name) => AccessTools.Field(typeof(KingdomUIEventWindowFooter), name);
        private static T Read<T>(FieldInfo field, object view) where T : class => field?.GetValue(view) as T;
        private static bool IsEvent(KingdomEventUIView ev) => ev?.IsCrusadeEvent == true || ev?.KingdomEventBlueprint != null;

        internal static bool HasOpenCard()
        {
            FindViews();
            return _window != null && Read<KingdomEventUIView>(SelectedEvent, _window) != null;
        }

        private static KingdomEventUIView ShownEvent()
        {
            if (!InKingdom) return null;
            FindViews();
            if (_window == null || !_window.isActiveAndEnabled) return null;
            var ev = Read<KingdomEventUIView>(SelectedEvent, _window);
            // The game can open a completed decree even while another kingdom tab is selected.
            return ev?.IsProject == true || (IsEvent(ev) && KingdomState.Instance?.NavigationType == KingdomNavigationType.Kingdom)
                ? ev : null;
        }

        private static KingdomUIEventWindowFooter BoundFooter(KingdomEventUIView ev)
        {
            var footer = Read<KingdomUIEventWindowFooter>(Footer, _window);
            // OnEventSelected changes the identity before the animated window binds its controls.
            return footer != null && ReferenceEquals(Read<KingdomEventUIView>(FooterEvent, footer), ev) ? footer : null;
        }

        public override bool IsActive() => ShownEvent() != null;

        internal static bool BuildLists(GraphBuilder b, bool decrees)
        {
            FindViews();
            // The hand controllers still contain the previous tab's cards until the bar's Update.
            if (_bar == null || PendingBarUpdate?.GetValue(_bar) is true) return false;
            if (decrees) KingdomDecreeNodes.BuildFilters(b);
            BuildList(b, decrees ? "decrees.available" : AvailableStop, "kingdom.events_available", _bar.Available, decrees);
            BuildList(b, decrees ? "decrees.progress" : "events.progress", "kingdom.events_in_progress", _bar.InProgress, decrees);
            BuildList(b, decrees ? "decrees.completed" : "events.completed", "kingdom.events_completed", _bar.Complete, decrees);
            return true;
        }

        private static void BuildList(GraphBuilder b, string stop, string label, KingdomEventHand hand, bool decrees)
        {
            b.BeginStop(stop).PushContext(Loc.T(label), "list");
            bool any = false;
            if (hand != null && hand.CartList != null)
                foreach (var card in hand.CartList)
                {
                    var ev = card != null ? card.KingdomEventUIView : null;
                    if (!(decrees ? ev?.IsProject == true : IsEvent(ev))) continue;
                    any = true;
                    var node = GraphNodes.Button(() => TextUtil.StripRichText(ev.Blueprint.LocalizedName), () =>
                    {
                        if (_window != null && Read<KingdomEventUIView>(SelectedEvent, _window) == null)
                            card.OnClick();
                    }, sound: null);
                    node.Announcements = new List<NodeAnnouncement>(node.Announcements)
                    {
                        new NodeAnnouncement(() => ev.HistoryEntry?.IsShow == false ? Loc.T("kingdom.event_unread") : null, live: true),
                        new NodeAnnouncement(() => TimeRemaining(ev)),
                    };
                    node.OnTooltip = () => TooltipScreen.FollowLinks(ev.Blueprint.LocalizedDescription, null,
                        SimpleTooltip.Make(ev.Blueprint.LocalizedName, ev.Blueprint.LocalizedDescription));
                    b.AddItem(CardId(ev), node);
                }
            if (!any)
                b.AddItem(ControlId.Structural(stop + ":empty"),
                    GraphNodes.Text(() => Loc.T(decrees ? "kingdom.decrees_empty" : "kingdom.events_empty")));
            b.PopContext();
        }

        private static string TimeRemaining(KingdomEventUIView ev)
            => ev.Task?.IsInProgress == true
                ? string.Format(KingdomUIRoot.Instance.Texts.KingdomEventEndTimeFormat, ev.Task.EndsOn - KingdomState.Instance.CurrentDay)
                : null;

        private static ControlId CardId(KingdomEventUIView ev) => ControlId.ForObject((object)ev.HistoryEntry ?? ev);
        internal static ControlId DetailId(KingdomEventUIView ev, object row) => ControlId.Structural((CardId(ev).StructuralKey, row));

        public override void OnPush() => LastCardId = null;

        public override void OnUpdate()
        {
            var ev = ShownEvent();
            if (ev == null) return;
            var id = CardId(ev);
            if (!id.Equals(LastCardId))
            {
                LastCardId = id;
                Navigation.FocusNode(DetailId(ev, "title"));
            }
            else if (ev.IsFinished && !_wasFinished)
            {
                var result = ResultText(ev);
                Navigation.FocusNode(DetailId(ev, string.IsNullOrWhiteSpace(result) ? "completed" : "result"));
            }
            _wasFinished = ev.IsFinished;
        }

        public override void Build(GraphBuilder b)
        {
            var ev = ShownEvent();
            if (ev == null) return;
            var footer = BoundFooter(ev);
            b.BeginStop("details").PushContext(Loc.T("kingdom.event_details"));
            TextRow(b, ev, "title", ev.Blueprint.LocalizedName);
            TextRow(b, ev, "description", ev.Blueprint.LocalizedDescription);
            if (ev.IsProject)
                TextRow(b, ev, "effect", ev.ProjectBlueprint.MechanicalDescription);
            if (ev.IsFinished)
                TextRow(b, ev, "completed", Loc.T("kingdom.events_completed"));
            if (footer != null)
            {
                TextRow(b, ev, "duration", VisibleText(Duration, _window));
                TextRow(b, ev, "repeatable", VisibleText(Repeatable, _window));
                TextRow(b, ev, "throne_room_hint", VisibleText(ThroneRoomHint, footer));
                if (ev.IsProject && ev.IsAvailable)
                    KingdomDecreeNodes.BuildRequirements(b, ev, CanPress(ev, Read<OwlcatButton>(Start, footer)));
                TextRow(b, ev, "result", ResultText(ev));
            }
            if (ev.IsIgnored)
                TextRow(b, ev, "ignored", KingdomUIRoot.Instance.Texts.KingdomEventWasIgnoreLabel);
            if (footer == null)
            {
                b.PopContext();
                return;
            }

            var choices = footer.GetComponentsInChildren<KingdomUIEventWindowFooterSolution>(false);
            foreach (var choice in choices)
                AddChoice(b, ev, choice);
            if (choices.Length == 0 && ev.IsFinished && footer.CurrentEventSolution != null)
            {
                var answer = footer.CurrentEventSolution;
                var node = GraphNodes.Text(() => ChoiceText(answer.SolutionText));
                AddEffects(node, answer.SolutionText);
                b.AddItem(DetailId(ev, "recorded"), node);
            }
            b.PopContext();

            b.BeginStop("actions").PushContext(Loc.T("kingdom.event_actions"));
            AddStart(b, ev, footer, choices.Length == 0);
            foreach (var action in FooterActions)
            {
                var button = Read<OwlcatButton>(action.Button, footer);
                if (button == null || !button.isActiveAndEnabled) continue;
                bool abandonDecree = ev.IsProject && action.Label == "kingdom.event_abandon";
                var node = GraphNodes.Button(
                    () => action.Label == null ? (string)KingdomUIRoot.Instance.Texts.ToThroneRoomLabel
                        : Loc.T(abandonDecree ? "kingdom.decree_abandon" : action.Label),
                    () => action.Press(footer), () => CanPress(ev, button));
                if (abandonDecree)
                    node.Announcements = new List<NodeAnnouncement>(node.Announcements)
                    {
                        new NodeAnnouncement(() => KingdomDecreeNodes.RefundSummary(ev)),
                    };
                b.AddItem(DetailId(ev, action.Button.Name), node);
            }
            var card = Read<KingdomEventHandCartController>(SelectedCard, _window);
            if (CanBrowse(ev, card))
            {
                b.AddItem(DetailId(ev, "previous"), GraphNodes.Button(
                    () => Loc.T(ev.IsProject ? "kingdom.decree_previous" : "kingdom.event_previous"),
                    () => { if (CanBrowse(ev, card)) _window.OnPrevClick(); }, sound: null));
                b.AddItem(DetailId(ev, "next"), GraphNodes.Button(
                    () => Loc.T(ev.IsProject ? "kingdom.decree_next" : "kingdom.event_next"),
                    () => { if (CanBrowse(ev, card)) _window.OnNextClick(); }, sound: null));
            }
            b.AddItem(DetailId(ev, "close"), GraphNodes.Button(() => Loc.T("action.close"), Close, sound: null));
            b.PopContext();
        }

        private static bool CanBrowse(KingdomEventUIView ev, KingdomEventHandCartController card)
            => card != null && ShownEvent() == ev && BoundFooter(ev) != null
                && card.Hand?.CartList?.Count > 1 && card.Hand.CartList.Contains(card);

        private static void AddChoice(GraphBuilder b, KingdomEventUIView ev, KingdomUIEventWindowFooterSolution choice)
        {
            var raw = VisibleText(ChoiceLabel, choice);
            if (string.IsNullOrWhiteSpace(raw)) return;
            Func<bool> enabled = () => choice != null && choice.isActiveAndEnabled && choice.Toggle.interactable && ShownEvent() == ev;
            Func<bool> selected = () => choice != null && choice.Toggle.isOn;
            var node = GraphNodes.ChoiceOption(() => ChoiceText(raw), selected, choice.SetOn);
            var select = node.OnActivate;
            node.OnActivate = () => { if (enabled() && !selected()) select(); };
            node.Announcements = new List<NodeAnnouncement>(node.Announcements) { GraphNodes.DisabledPart(enabled) };
            AddEffects(node, raw);
            b.AddItem(DetailId(ev, choice.EventSolution), node);
        }

        private static void AddStart(GraphBuilder b, KingdomEventUIView ev, KingdomUIEventWindowFooter footer, bool choicesHidden)
        {
            var button = Read<OwlcatButton>(Start, footer);
            if (ev.IsProject && ev.IsAvailable && ev.Task != null)
            {
                var issue = GraphNodes.Button(() => KingdomUIRoot.Instance.Texts.KingdomStartProjectLabel,
                    footer.OnStart, () => CanPress(ev, button));
                issue.Announcements = new List<NodeAnnouncement>(issue.Announcements)
                {
                    new NodeAnnouncement(() => KingdomDecreeNodes.IssueSummary(ev)),
                };
                b.AddItem(DetailId(ev, "start"), issue);
                return;
            }
            if (button == null || !button.isActiveAndEnabled) return;
            var label = VisibleText(StartLabel, footer) ?? KingdomUIRoot.Instance.Texts.KingdomStartEventLabel;
            var answer = choicesHidden ? footer.CurrentEventSolution?.SolutionText : null;
            var node = GraphNodes.Button(
                () => ChoiceText(answer == null || answer == label ? label : label + ", " + answer),
                footer.OnStart, () => CanPress(ev, button));
            AddEffects(node, answer ?? label);
            b.AddItem(DetailId(ev, "start"), node);
        }

        private static void AddEffects(NodeVtable node, string raw)
        {
            // Effects are part of the choice's readout even with optional tooltip speech disabled.
            // Resolve the game links when read, just as the explicit tooltip reader does.
            node.Announcements = new List<NodeAnnouncement>(node.Announcements)
            {
                new NodeAnnouncement(() => string.Join(" ", TooltipLinks.Extract(raw)
                    .SelectMany(link => TooltipFlowBuilder.BodyText(link.Open())).Distinct())),
            };
            node.OnTooltip = () => TooltipScreen.FollowLinks(raw, null);
        }

        private static string ChoiceText(string raw)
            => TextUtil.StripRichText(TooltipLinks.OmitLinkLabels(raw, "Event_"));

        private static string VisibleText(FieldInfo field, object view)
        {
            var text = Read<TMP_Text>(field, view);
            return text != null && text.gameObject.activeInHierarchy ? text.text : null;
        }

        private static string ResultText(KingdomEventUIView ev)
        {
            if (ev.IsProject && ev.IsFinished && (ev.HistoryEntry?.Type is EventHistoryType.FinishedEvent
                or EventHistoryType.RecurrenceResolved or EventHistoryType.RecurrenceTick))
            {
                var recorded = ev.GetResultDescription();
                if (!string.IsNullOrWhiteSpace(recorded)) return recorded;
            }
            return VisibleText(Result, _window);
        }

        private static void TextRow(GraphBuilder b, KingdomEventUIView ev, string key, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var node = GraphNodes.Text(() => TextUtil.StripRichText(raw));
            node.OnTooltip = () => TooltipScreen.FollowLinks(raw, null);
            b.AddItem(DetailId(ev, key), node);
        }

        private static bool CanPress(KingdomEventUIView ev, OwlcatButton button)
            => button != null && button.isActiveAndEnabled && button.Interactable && ShownEvent() == ev;

        private static void Close()
        {
            var ev = ShownEvent();
            if (ev != null && BoundFooter(ev) != null) _window.OnClose();
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }
    }
}
