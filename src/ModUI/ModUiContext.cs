using System;
using System.Collections.Generic;
using WrathAccess.ModUI.Imgui;
using WrathAccess.UI.Graph;

namespace WrathAccess.ModUI
{
    /// <summary>
    /// What an adapter builds INTO, for one surface during one graph build: the graph builder, the
    /// surface whose captured tree is being presented, and the plumbing every adapter needs — stable
    /// control ids, live re-reads of a control from the newest capture, and injected actions.
    ///
    /// Nodes of the captured tree are only good for the build they came from. Anything that outlives
    /// the build (announcement closures, action handlers) must capture the control's
    /// <see cref="ImguiTarget"/> and go through <see cref="Live"/>.
    /// </summary>
    internal sealed class ModUiContext
    {
        public readonly GraphBuilder B;
        public ImguiSurface Surface;

        private readonly HashSet<string> _ids = new HashSet<string>();
        private bool _rowOpen;

        public ModUiContext(GraphBuilder b) { B = b; }

        /// <summary>The graph id for a captured control (plus an optional part, e.g. one option of a
        /// choice). Null when that id was already used in this build — the caller skips the node
        /// rather than crash the whole screen on a duplicate.</summary>
        public ControlId Id(ImguiNode n, string part = null)
        {
            string key = "mui:" + Surface.Key + "|" + n.Key + (part != null ? "|" + part : "");
            return _ids.Add(key) ? ControlId.Structural(key) : null;
        }

        /// <summary>A graph id for something the adapter made up (a synthesized row, a heading).</summary>
        public ControlId Id(string key)
        {
            key = "mui:" + (Surface != null ? Surface.Key : "") + "|" + key;
            return _ids.Add(key) ? ControlId.Structural(key) : null;
        }

        // ---- rows (tracked so a build that throws can't leave one open) ----

        public void StartRow(object rowKey = null)
        {
            if (_rowOpen) return;
            B.StartRow(rowKey);
            _rowOpen = true;
        }

        public void EndRow()
        {
            if (!_rowOpen) return;
            _rowOpen = false;
            B.EndRow();
        }

        /// <summary>After a failed build: close whatever is open so the builder can still finish.</summary>
        public void Recover()
        {
            if (!_rowOpen) return;
            _rowOpen = false;
            try { B.EndRow(); } catch { /* an empty row: nothing was added, nothing to keep */ }
        }

        // ---- live state + actions ----

        /// <summary>A reader that re-finds a control in the surface's NEWEST capture each call.</summary>
        public static Func<ImguiNode> Live(ImguiSurface surface, ImguiNode node)
        {
            var target = node.Target;
            return () => surface.Find(target);
        }

        public static void Click(ImguiSurface surface, ImguiNode node)
            => Send(surface, node, ImguiVerb.Click);

        public static void SetText(ImguiSurface surface, ImguiNode node, string text)
            => Send(surface, node, ImguiVerb.SetText, text: text);

        public static void SetValue(ImguiSurface surface, ImguiNode node, float value)
            => Send(surface, node, ImguiVerb.SetValue, value: value);

        public static void Select(ImguiSurface surface, ImguiNode node, int index)
            => Send(surface, node, ImguiVerb.Select, index: index);

        private static void Send(ImguiSurface surface, ImguiNode node, ImguiVerb verb,
            string text = null, float value = 0f, int index = 0)
        {
            if (surface == null || node == null) return;
            ImguiCapture.Request(surface, node, verb, text, value, index, ok =>
            {
                // The control was redrawn differently (or not at all) before the action could land.
                if (!ok) Tts.Speak(Loc.T("modui.gone"));
            });
        }
    }
}
