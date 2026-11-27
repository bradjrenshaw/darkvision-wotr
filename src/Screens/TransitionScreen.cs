using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.Transition;
using WrathAccess.Exploration;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>Destination picker for multi-entrance maps opened from an area or the world map.</summary>
    public sealed class TransitionScreen : Screen
    {
        public override string Key => "ctx.transition";
        public override string ScreenName
        {
            get
            {
                var name = TextUtil.StripRichText(Vm()?.Name);
                return string.IsNullOrWhiteSpace(name) ? Loc.T("screen.destinations") : name;
            }
        }

        public override int Layer => 15;
        public override bool Exclusive => true;

        private static TransitionVM Vm() =>
            Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.TransitionVM?.Value
            ?? GlobalMapModel.CurrentVM?.TransitionVM?.Value;

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            b.BeginStop("destinations");
            bool hasDestinations = false;
            foreach (var entry in vm.EntryVms)
            {
                if (!entry.IsVisible.Value) continue;
                hasDestinations = true;
                var node = GraphNodes.Button(() => DestinationLabel(entry), () =>
                {
                    if (ReferenceEquals(vm, Vm()) && entry.IsVisible.Value)
                        entry.Enter();
                });
                if (entry.Attention.Value)
                    node.OnTooltip = () => TooltipScreen.Open(entry.GetTooltipTemplate());
                b.AddItem(ControlId.ForObject(entry), node);
            }

            if (!hasDestinations)
                b.AddItem(ControlId.Referenced(vm, "empty"), GraphNodes.Text(() =>
                    TextUtil.StripRichText(UIStrings.Instance.CommonTexts.AlushinyrraTransitionEmpty)));

            b.AddItem(ControlId.Referenced(vm, "close"),
                GraphNodes.Button(() => Loc.T("action.close"), Close));
        }

        private static string DestinationLabel(TransitionEntryVM entry)
        {
            var name = TextUtil.StripRichText(entry.Name.Value);
            return entry.Attention.Value ? name + ", " + Loc.T("transition.linked_quests") : name;
        }

        private static void Close() => Vm()?.Close();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }
    }
}
