using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Designers;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.Blueprints;
using Kingmaker.Kingdom.UI;
using Kingmaker.UI.Common;
using Kingmaker.UI.Kingdom;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    internal static class KingdomDecreeNodes
    {
        internal const string FilterStop = "decrees.filters";

        internal static void BuildFilters(GraphBuilder b)
        {
            var state = KingdomState.Instance;
            var view = state?.UIView;
            if (view == null) return;

            var texts = KingdomUIRoot.Instance.Texts;
            var filters = new (KingdomNavigationType Type, string Label)[]
            {
                (KingdomNavigationType.Project, texts.NavigationProjectTypeAll),
                (KingdomNavigationType.ProjectRankUp, texts.NavigationProjectTypeRankUp),
                (KingdomNavigationType.ProjectDevelopment, texts.NavigationProjectTypeDevelopment),
                (KingdomNavigationType.ProjectEconomy, texts.NavigationProjectTypeEconomy),
                (KingdomNavigationType.ProjectArmy, texts.NavigationProjectTypeArmy),
                (KingdomNavigationType.ProjectCommand, texts.NavigationProjectTypeCommand),
                (KingdomNavigationType.ProjectRelics, texts.NavigationProjectTypeRelics),
                (KingdomNavigationType.ProjectOther, texts.NavigationProjectTypeOther),
            };

            var active = view.ActiveProjects ?? Enumerable.Empty<KingdomEventUIView>();
            var finished = view.FinishedProjects ?? Enumerable.Empty<KingdomEventUIView>();
            var cards = active.Concat(finished).Where(card => card.IsProject)
                .GroupBy(card => (object)card.HistoryEntry ?? card).Select(group => group.First()).ToArray();

            b.BeginStop(FilterStop).PushContext(Loc.T("kingdom.decree_filters"), "list");
            foreach (var filter in filters)
            {
                var members = filter.Type == KingdomNavigationType.Project ? cards
                    : cards.Where(card => UIUtility.GetKingdomNavigationType(card.ProjectBlueprint.ProjectType) == filter.Type);
                int available = 0, progress = 0, completed = 0, unread = 0;
                foreach (var card in members)
                {
                    if (card.IsAvailable) available++;
                    if (card.IsInProgress) progress++;
                    if (card.IsFinished) completed++;
                    if (KingdomNaviElement.NeedToShowEvent(card)) unread++;
                }

                var node = GraphNodes.ChoiceOption(() => filter.Label, () => state.NavigationType == filter.Type,
                    () => state.NavigationType = filter.Type);
                node.Announcements = new List<NodeAnnouncement>(node.Announcements)
                {
                    new NodeAnnouncement(() => Loc.T("kingdom.decree_counts", new { available, progress, completed })),
                    new NodeAnnouncement(() => unread > 0 ? Loc.T("kingdom.decree_unread_count", new { count = unread }) : null),
                };
                b.AddItem(ControlId.Structural(("decrees:filter", filter.Type)), node);
            }
            b.PopContext();
        }

        internal static void BuildRequirements(GraphBuilder b, KingdomEventUIView ev, bool canIssue)
        {
            var project = ev.ProjectBlueprint;
            var state = KingdomState.Instance;
            var root = KingdomUIRoot.Instance;
            var stat = root.GetStatElement(root.GetStatType(project.GetDefaultResolutionType()));
            if (stat != null)
                b.AddItem(Id(ev, "stat"), GraphNodes.Text(
                    () => Loc.T("kingdom.decree_stat", new { name = stat.Name }),
                    () => new TooltipTemplateKingdomStat(stat)));

            bool blocked = false;
            if (!project.HasLeaderAvailable())
            {
                blocked = true;
                b.AddItem(Id(ev, "leader"), GraphNodes.Text(() => root.Texts.KingdomNoLeaderAvailable));
            }

            var balances = ResourceAmounts(state.Resources).ToDictionary(entry => entry.Resource, entry => entry.Amount);
            foreach (var cost in ResourceAmounts(ev.Cost).Where(entry => entry.Amount > 0))
            {
                int have = balances[cost.Resource];
                int missing = cost.Amount - have;
                if (missing > 0) blocked = true;
                b.AddItem(KingdomEventScreen.DetailId(ev, cost.Resource), GraphNodes.Text(
                    () => Requirement(ResourceName(cost.Resource), cost.Amount, have),
                    () => new TooltipTemplateKingdomResource(cost.Resource)));
            }

            var inventory = GameHelper.GetPlayerCharacter().Inventory;
            var stash = Game.Instance.Player.SharedStash;
            int itemRow = 0;
            foreach (var cost in project.GetComponents<EventItemCost>())
                foreach (var item in cost.Items)
                {
                    int have = inventory.Count(item) + stash.Count(item);
                    if (have < cost.Amount) blocked = true;
                    b.AddItem(Id(ev, "item:" + itemRow++), GraphNodes.Text(
                        () => Requirement(item.Name, cost.Amount, have),
                        () => new TooltipTemplateItem(item)));
                }

            if (ev.RulerTimeRequired > 0)
            {
                int days = ev.RulerTimeRequired;
                b.AddItem(Id(ev, "time"), GraphNodes.Text(() => TimeAdvance(days)));
                if (!KingdomTimelineManager.CanAdvanceTime())
                {
                    blocked = true;
                    b.AddItem(Id(ev, "time_blocked"), GraphNodes.Text(() => Loc.T("kingdom.decree_time_blocked")));
                }
            }

            if (!canIssue && !blocked && ev.Task != null)
                b.AddItem(Id(ev, "blocked"), GraphNodes.Text(() => Loc.T("kingdom.decree_blocked")));
        }

        private static string Requirement(string name, int required, int have)
        {
            int missing = required - have;
            return missing > 0
                ? Loc.T("kingdom.decree_requirement_missing", new { name, required, have, missing })
                : Loc.T("kingdom.decree_requirement", new { name, required, have });
        }

        internal static string IssueSummary(KingdomEventUIView ev)
        {
            var charges = CostParts(ev.Cost).Concat(ItemParts(ev)).ToArray();
            var cost = charges.Length == 0 ? Loc.T("kingdom.decree_no_cost")
                : Loc.T("kingdom.decree_cost", new { costs = string.Join(", ", charges) });
            return ev.RulerTimeRequired > 0 ? cost + ". " + TimeAdvance(ev.RulerTimeRequired) : cost;
        }

        internal static string RefundSummary(KingdomEventUIView ev)
        {
            var returns = CostParts(ev.Cost * KingdomRoot.Instance.ProjectRefundFactor).Concat(ItemParts(ev)).ToArray();
            return returns.Length == 0 ? null : Loc.T("kingdom.decree_refund", new { costs = string.Join(", ", returns) });
        }

        private static string TimeAdvance(int days) => Loc.T("kingdom.decree_time", new { days });
        private static ControlId Id(KingdomEventUIView ev, string row) => KingdomEventScreen.DetailId(ev, "decree:" + row);
        private static string ResourceName(KingdomResource resource) => KingdomUIRoot.Instance.GetResourceElement(resource).Name;

        private static IEnumerable<string> CostParts(KingdomResourcesAmount amount)
            => ResourceAmounts(amount).Where(entry => entry.Amount > 0)
                .Select(entry => Loc.T("crusade.price", new { count = entry.Amount, name = ResourceName(entry.Resource) }));

        private static IEnumerable<string> ItemParts(KingdomEventUIView ev)
            => ev.ProjectBlueprint.GetComponents<EventItemCost>().SelectMany(cost => cost.Items
                .Select(item => Loc.T("crusade.price", new { count = cost.Amount, name = (string)item.Name })));

        private static IEnumerable<(KingdomResource Resource, int Amount)> ResourceAmounts(KingdomResourcesAmount amount)
        {
            yield return (KingdomResource.Finances, amount.Finances);
            yield return (KingdomResource.Materials, amount.Materials);
            yield return (KingdomResource.Favors, amount.Favors);
        }
    }
}
