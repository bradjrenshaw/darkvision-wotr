using System;
using System.Collections.Generic;
using WrathAccess.ModUI;
using WrathAccess.ModUI.Imgui;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Another mod's window, made navigable. Active exactly while a <see cref="ModUiHost"/> window is
    /// open (Unity Mod Manager's, the game's native-mod window) — however it was opened, by our menu
    /// or by the mod manager's own hotkey. The content is whatever <see cref="ImguiCapture"/> last
    /// recorded of that window, laid out by the host and the mods' adapters (see <see cref="ModUi"/>).
    ///
    /// Exclusive and focus-mode: a mod window is modal for a sighted player too (the mod manager
    /// blocks the game's UI under it), and its letters must not fire game or exploration hotkeys.
    /// Escape closes the window.
    /// </summary>
    public sealed class ModUiScreen : Screen
    {
        public override string Key => "overlay.modui";
        public override int Layer => 45; // above everything of ours: the mod window draws over it all
        public override bool Exclusive => true;
        public override bool IsActive() => ModUi.ActiveHost != null;
        public override string ScreenName => ModUi.ActiveHost?.Title;
        public override object InitialFocusStop => ModUi.ActiveHost?.InitialStop;

        private bool _priorFocus;
        private readonly List<ImguiSurface> _surfaces = new List<ImguiSurface>();

        public override void OnPush()
        {
            _priorFocus = FocusMode.Active;
            FocusMode.Set(true);
        }

        public override void OnPop() { FocusMode.Set(_priorFocus); }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ModUi.ActiveHost?.Back());
        }

        public override void Build(GraphBuilder b)
        {
            var host = ModUi.ActiveHost;
            if (host == null) return;
            _surfaces.Clear();
            foreach (var s in ImguiCapture.Surfaces)
                if (s.Alive && s.Root != null && host.Owns(s)) _surfaces.Add(s);
            if (_surfaces.Count == 0) return; // nothing captured yet — the first repaint is a frame away
            _surfaces.Sort((x, y) => x.WindowId.CompareTo(y.WindowId));
            var ctx = new ModUiContext(b);
            try { host.Build(ctx, _surfaces); }
            catch (Exception e)
            {
                // Someone else's UI, redrawn every frame, read by pattern: a surprise must cost one
                // render, not the screen.
                ctx.Recover();
                Main.Log?.Error("[modui] build (" + host.Id + "): " + e);
            }
        }
    }
}
