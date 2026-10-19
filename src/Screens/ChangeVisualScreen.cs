using System.Collections.Generic;
using Kingmaker; // Game
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI.MVVM._VM.ChangeVisual; // ChangeVisualVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// PLACEHOLDER for the item appearance (transmog) window — opened by talking to the golem. Not
    /// accessible yet; this screen only says so and lets the player leave: Escape and the Close button
    /// call <see cref="ChangeVisualVM.Close"/>, what the PC view's own close button is wired to
    /// (ChangeVisualPCView.BindViewImplementation). Without it the window took the keyboard with no way out.
    /// Name = the game's own window header.
    /// </summary>
    public sealed class ChangeVisualScreen : Screen
    {
        public override string Key => "ctx.changevisual";
        public override string ScreenName
        {
            get
            {
                var s = (string)UIStrings.Instance?.ChangeVisual?.ChangeVisualHeaderText;
                return string.IsNullOrEmpty(s) ? Loc.T("screen.change_visual") : s;
            }
        }
        public override int Layer => 16; // over the in-game screen, like the other in-game windows
        public override bool Exclusive => true;

        public override bool IsActive() => Game.Instance?.RootUiContext?.ChangeVisualIsShown ?? false;

        private static ChangeVisualVM Vm() => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ChangeVisualVM?.Value;

        public override void Build(GraphBuilder b)
        {
            b.BeginStop("message").AddItem(ControlId.Structural("changevisual:message"),
                GraphNodes.Text(() => Loc.T("changevisual.not_accessible")));
            b.BeginStop("close").AddItem(ControlId.Structural("changevisual:close"),
                GraphNodes.Button(() => Loc.T("action.close"), () => Vm()?.Close()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }
    }
}
