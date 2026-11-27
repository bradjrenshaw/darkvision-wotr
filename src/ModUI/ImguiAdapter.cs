using System;
using System.Collections.Generic;
using HarmonyLib;
using WrathAccess.ModUI.Imgui;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.ModUI
{
    /// <summary>
    /// Turns a captured IMGUI tree into our navigation graph. This class IS the generic reader — the
    /// rules that work for any mod with no knowledge of it — and the base every mod-specific adapter
    /// derives from. Special-casing a mod is done at whichever depth it needs:
    ///
    /// <list type="bullet">
    /// <item><b>Tweak</b>: override a hook — <see cref="Hide"/> a noise control, <see cref="Clean"/> a
    /// label, name an icon in <see cref="ImageLabel"/>, mark <see cref="IsHeading"/>.</item>
    /// <item><b>Restructure</b>: override <see cref="Emit"/> / <see cref="EmitRow"/> for the nodes you
    /// care about and fall through to base for the rest; or override <see cref="Build"/> to lay a whole
    /// region out by hand (its own Tab stops, a list read from the mod's data instead of its pixels).</item>
    /// <item><b>See more</b>: override <see cref="InstallProbes"/> to hook the mod's own widget
    /// functions, for controls the generic recorder can't see or to bracket regions in named scopes
    /// (see the probe API on <see cref="ImguiCapture"/>).</item>
    /// </list>
    ///
    /// The generic rules, in one paragraph: a horizontal layout group is a ROW; arrows move left/right
    /// inside it and up/down between rows. Static text in a row is not a stop of its own — text before
    /// an unlabelled control NAMES it ("Width" + a slider), text after a control DESCRIBES it (a
    /// toggle's help line), and a row of nothing but text reads as one line. A toolbar / selection
    /// grid becomes a grid of radio options. Controls a mod draws without Unity's primitives arrive as
    /// plain text until someone writes a probe for them.
    /// </summary>
    internal class ImguiAdapter
    {
        /// <summary>The mod this adapter is for (its Unity Mod Manager id), or "generic".</summary>
        public virtual string Id => "generic";

        /// <summary>Hook the mod's own code. Called when a mod window first opens and again whenever
        /// one opens later (mods can load late): make it idempotent.</summary>
        public virtual void InstallProbes(Harmony harmony) { }

        /// <summary>Does this adapter want to present this scope (a region some probe bracketed)?
        /// Mod-level routing is by <see cref="Id"/>; this is for scopes of the adapter's own making.</summary>
        public virtual bool Claims(ImguiNode scope) => false;

        /// <summary>Present one region — a surface's root, a scope, or the slice of a host window that
        /// belongs to this adapter's mod.</summary>
        public virtual void Build(ModUiContext ctx, ImguiNode region) => EmitChildren(ctx, region);

        /// <summary>Where focus should land when the host follows the player into this mod's options
        /// (they just expanded it). Null = the host's own options stop; an adapter that lays its mod
        /// out as several Tab stops names the one to start on. Read AFTER <c>Build</c>.</summary>
        public virtual object LandingStop => null;

        /// <summary>Present ONE node as itself (a horizontal group stays a row), where
        /// <see cref="Build(ModUiContext, ImguiNode)"/> presents a region's CONTENTS.</summary>
        public void BuildNode(ModUiContext ctx, ImguiNode node) => Emit(ctx, node);

        /// <summary>Present a run of sibling nodes (a host hands a mod "everything after my header").</summary>
        public virtual void Build(ModUiContext ctx, IList<ImguiNode> nodes, int from, int to)
        {
            for (int i = from; i < to && i < nodes.Count; i++) Emit(ctx, nodes[i]);
        }

        // =====================================================================================
        // Hooks for light special-casing
        // =====================================================================================

        /// <summary>Drop a node (and everything under it) from the presentation.</summary>
        protected virtual bool Hide(ImguiNode n) => false;

        /// <summary>The spoken form of a control's text. The input already has rich-text tags
        /// stripped; override to drop decoration glyphs, expand abbreviations, etc.</summary>
        protected virtual string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.IndexOf('\n') >= 0 ? text.Replace('\n', ' ').Trim() : text;
        }

        /// <summary>The name of a control that shows only a picture. Default: the texture's own name
        /// (often meaningful — "SettingsNormal"), else "unlabelled".</summary>
        protected virtual string ImageLabel(ImguiNode n)
            => !string.IsNullOrEmpty(n.Image) ? n.Image : Loc.T("modui.unlabelled");

        /// <summary>Read this static text as a heading.</summary>
        protected virtual bool IsHeading(ImguiNode n) => false;

        // =====================================================================================
        // Generic emission
        // =====================================================================================

        protected virtual void EmitChildren(ModUiContext ctx, ImguiNode n)
        {
            if (n.Children == null) return;
            for (int i = 0; i < n.Children.Count; i++) Emit(ctx, n.Children[i]);
        }

        protected virtual void Emit(ModUiContext ctx, ImguiNode n)
        {
            if (Hide(n)) return;
            switch (n.Kind)
            {
                case ImguiKind.Scope:
                {
                    var other = ModAdapters.ForScope(n);
                    if (other != null && !ReferenceEquals(other, this)) other.Build(ctx, n);
                    else EmitScope(ctx, n);
                    return;
                }
                case ImguiKind.Horizontal:
                {
                    var line = Lists.Rent();
                    try
                    {
                        if (CollectLine(n, line)) EmitRow(ctx, line);
                        else EmitChildren(ctx, n); // holds multi-line columns: read them in order
                    }
                    finally { Lists.Return(line); }
                    return;
                }
                case ImguiKind.Root:
                case ImguiKind.Window:
                case ImguiKind.Vertical:
                case ImguiKind.Scroll:
                    EmitChildren(ctx, n);
                    return;
                default:
                {
                    var line = Lists.Rent();
                    try
                    {
                        line.Add(n);
                        EmitRow(ctx, line);
                    }
                    finally { Lists.Return(line); }
                    return;
                }
            }
        }

        /// <summary>A scope nobody claimed: its label (if any) becomes a spoken context level.</summary>
        protected virtual void EmitScope(ModUiContext ctx, ImguiNode scope)
        {
            string label = Clean(scope.ScopeLabel);
            if (label.Length > 0) ctx.B.PushContext(label);
            try { EmitChildren(ctx, scope); }
            finally { if (label.Length > 0) ctx.B.PopContext(); }
        }

        /// <summary>Flatten a layout subtree into the controls of ONE line, in draw order. False when
        /// it isn't one line (a column holding several rows, or a scope — those keep their structure).
        /// Mods wrap single controls in their own vertical groups to nudge alignment (ModKit's slider
        /// is five one-control columns), so a vertical with exactly one line counts as inline.</summary>
        protected virtual bool CollectLine(ImguiNode n, List<ImguiNode> into)
        {
            if (!n.IsContainer)
            {
                into.Add(n);
                return true;
            }
            switch (n.Kind)
            {
                case ImguiKind.Horizontal:
                    for (int i = 0; i < n.Children.Count; i++)
                        if (!CollectLine(n.Children[i], into)) return false;
                    return true;
                case ImguiKind.Vertical:
                case ImguiKind.Scroll:
                    return n.Children.Count == 1 && CollectLine(n.Children[0], into);
                default:
                    return false;
            }
        }

        private struct Cell
        {
            public ImguiNode Node;
            public string Label;
            public string Description;
            public bool IsText;
        }

        /// <summary>Present the controls of one line as one navigation row (see the class summary
        /// for the naming rules).</summary>
        protected virtual void EmitRow(ModUiContext ctx, List<ImguiNode> line)
        {
            // One scratch list for the common (non-nested) case: a build walks hundreds of rows.
            bool scratch = !_cellsBusy;
            var cells = scratch ? _cells : new List<Cell>(line.Count);
            _cellsBusy = true;
            try { EmitRow(ctx, line, cells); }
            finally
            {
                cells.Clear();
                if (scratch) _cellsBusy = false;
            }
        }

        private static readonly List<Cell> _cells = new List<Cell>(16);
        private static bool _cellsBusy;

        private void EmitRow(ModUiContext ctx, List<ImguiNode> line, List<Cell> cells)
        {
            string pending = null;        // static text not yet attached to anything
            ImguiNode pendingNode = null; // its first node (the text cell's identity)
            string rowName = null;        // the row's first static text: names later unlabelled controls

            for (int i = 0; i < line.Count; i++)
            {
                var n = line[i];
                if (Hide(n)) continue;
                if (!n.IsInteractive)
                {
                    string t = Clean(n.Text);
                    if (t.Length == 0) continue; // a picture or a spacer
                    // The text right after a slider is its readout ("960 px"): the slider's value
                    // part reads it live (see Control), so it is not repeated as a description.
                    if (pending == null && cells.Count > 0 && cells[cells.Count - 1].Node.Kind == ImguiKind.Slider
                        && ReferenceEquals(ReadoutNode(cells[cells.Count - 1].Node), n))
                        continue;
                    if (pending == null) { pending = t; pendingNode = n; }
                    else pending = pending + ", " + t;
                    if (rowName == null) rowName = t;
                    continue;
                }

                var cell = new Cell { Node = n };
                string own = n.Kind == ImguiKind.Button || n.Kind == ImguiKind.Toggle ? Clean(n.Text) : "";
                if (own.Length > 0)
                {
                    // A control that names itself: text before it belongs to whatever came earlier.
                    cell.Label = own;
                    if (pending != null) Attach(cells, pendingNode, pending);
                }
                else if (n.Kind == ImguiKind.Button && !string.IsNullOrEmpty(n.Image))
                {
                    cell.Label = ImageLabel(n);
                    if (pending != null) Attach(cells, pendingNode, pending);
                }
                else
                {
                    // An unlabelled control: the text just before it is its name.
                    cell.Label = pending ?? rowName ?? Clean(n.Anchor);
                    if (string.IsNullOrEmpty(cell.Label)) cell.Label = ImageLabel(n);
                }
                pending = null;
                pendingNode = null;
                cells.Add(cell);
            }
            if (pending != null) Attach(cells, pendingNode, pending);
            if (cells.Count == 0) return;

            // "Hotkey:  [None]" — a caption and ONE self-named control: the caption is that control's
            // name too ("Hotkey, None, button"), not a stop of its own. (A caption before several
            // controls stays a cell: it heads the group.)
            if (cells.Count == 2 && cells[0].IsText && !cells[1].IsText && cells[1].Node.Kind != ImguiKind.Choice)
            {
                var only = cells[1];
                only.Label = cells[0].Label + ", " + only.Label;
                cells.Clear();
                cells.Add(only);
            }

            // Emit: plain cells share a row; a choice breaks out into its own grid of options.
            int i0 = 0;
            while (i0 < cells.Count)
            {
                if (cells[i0].Node.Kind == ImguiKind.Choice)
                {
                    EmitChoice(ctx, cells[i0].Node, cells[i0].Label);
                    i0++;
                    continue;
                }
                int i1 = i0;
                while (i1 < cells.Count && cells[i1].Node.Kind != ImguiKind.Choice) i1++;
                bool row = i1 - i0 > 1;
                if (row) ctx.StartRow();
                int added = 0;
                for (int k = i0; k < i1; k++)
                {
                    var c = cells[k];
                    var id = ctx.Id(c.Node, c.IsText ? "text" : null);
                    if (id == null) continue;
                    var vt = c.IsText ? TextCell(c.Node, c.Label) : Control(ctx, c.Node, c.Label, c.Description);
                    if (vt == null) continue;
                    ctx.B.AddItem(id, vt);
                    added++;
                }
                if (row)
                {
                    if (added > 0) ctx.EndRow();
                    else ctx.Recover();
                }
                i0 = i1;
            }
        }

        // Static text with nowhere better to go: it describes the control before it, or (no control
        // yet) stands as a text cell of its own.
        private static void Attach(List<Cell> cells, ImguiNode node, string text)
        {
            if (cells.Count > 0 && !cells[cells.Count - 1].IsText)
            {
                var prev = cells[cells.Count - 1];
                prev.Description = prev.Description == null ? text : prev.Description + ", " + text;
                cells[cells.Count - 1] = prev;
                return;
            }
            cells.Add(new Cell { Node = node, Label = text, IsText = true });
        }

        // =====================================================================================
        // Controls → graph nodes
        // =====================================================================================

        protected virtual NodeVtable TextCell(ImguiNode n, string text)
            => IsHeading(n) ? GraphNodes.Heading(() => text) : GraphNodes.Text(() => text);

        /// <summary>The graph node for one interactive control. <paramref name="label"/> and
        /// <paramref name="description"/> are what the row rules worked out; state is read live.</summary>
        protected virtual NodeVtable Control(ModUiContext ctx, ImguiNode n, string label, string description)
        {
            var surface = ctx.Surface;
            var live = ModUiContext.Live(surface, n);
            Func<bool> enabled = () => { var c = live(); return c == null || c.Enabled; };
            string tip = Clean(ImguiCapture.Plain(n.Tooltip));
            string desc = string.IsNullOrEmpty(description) ? tip
                : (string.IsNullOrEmpty(tip) ? description : description + ". " + tip);
            NodeVtable vt;

            switch (n.Kind)
            {
                case ImguiKind.Button:
                    vt = GraphNodes.Button(() => label, () => ModUiContext.Click(surface, n), enabled);
                    break;

                case ImguiKind.Toggle:
                {
                    bool disclosure = n.Disclosure;
                    // The new state arrives a frame or two after the click (the mod flips it in its own
                    // handler), so it is spoken by the LIVE watch, never predicted here.
                    Func<string> value = () =>
                    {
                        var c = live();
                        if (c == null) return null;
                        if (disclosure) return Loc.T(c.On ? "value.expanded" : "value.collapsed");
                        return Loc.T(c.On ? "value.on" : "value.off");
                    };
                    vt = new NodeVtable
                    {
                        ControlType = disclosure ? ControlTypes.Group : ControlTypes.Toggle,
                        Announcements = new List<NodeAnnouncement>
                        {
                            GraphNodes.LabelPart(() => label),
                            new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                            GraphNodes.DisabledPart(enabled),
                        },
                        SearchText = () => label,
                        OnActivate = () =>
                        {
                            if (!enabled()) return;
                            UiSound.Play(Kingmaker.UI.UISoundType.SettingsSwitchToggle);
                            ModUiContext.Click(surface, n);
                        },
                    };
                    break;
                }

                case ImguiKind.Slider:
                {
                    // Prefer the mod's own readout beside the slider ("960 px", "1.50x") — it carries
                    // the unit and the mod's rounding; fall back to the raw number.
                    Func<string> value = () =>
                    {
                        var c = live();
                        if (c == null) return null;
                        var readout = ReadoutNode(c);
                        return readout != null ? Clean(readout.Text) : FormatValue(c.Value, c.Min, c.Max);
                    };
                    vt = new NodeVtable
                    {
                        ControlType = ControlTypes.Slider,
                        Announcements = new List<NodeAnnouncement>
                        {
                            GraphNodes.LabelPart(() => label),
                            new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                            GraphNodes.DisabledPart(enabled),
                        },
                        SearchText = () => label,
                        OnAdjust = (sign, large) =>
                        {
                            var c = live();
                            if (c == null || !c.Enabled) return;
                            ModUiContext.SetValue(surface, c, Step(c, sign, large));
                        },
                    };
                    break;
                }

                case ImguiKind.TextField:
                {
                    Func<string> value = () =>
                    {
                        var c = live();
                        if (c == null) return null;
                        if (c.Password) return Loc.T("modui.hidden_text");
                        return string.IsNullOrEmpty(c.Content) ? Loc.T("value.blank") : c.Content;
                    };
                    vt = new NodeVtable
                    {
                        Announcements = new List<NodeAnnouncement>
                        {
                            GraphNodes.LabelPart(() => label),
                            new NodeAnnouncement(() => Loc.T("role.edit"), kind: AnnouncementKinds.Role),
                            new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                            GraphNodes.DisabledPart(enabled),
                        },
                        SearchText = () => label,
                        OnActivate = () =>
                        {
                            var c = live();
                            if (c == null || !c.Enabled) return;
                            var target = c;
                            Screens.ModTextEntryScreen.Open(label, c.Password ? "" : c.Content,
                                text => ModUiContext.SetText(surface, target, text));
                        },
                    };
                    break;
                }

                default:
                    return null;
            }

            if (!string.IsNullOrEmpty(desc))
            {
                ((List<NodeAnnouncement>)vt.Announcements).Add(GraphNodes.TooltipPart(() => desc));
                vt.OnTooltip = () => Tts.Speak(desc);
            }
            return vt;
        }

        /// <summary>A toolbar / selection grid: one radio option per entry, laid out in the grid's
        /// own columns (up/down keeps the column). A name, when the row supplied one, is the context
        /// spoken on entering the group.</summary>
        protected virtual void EmitChoice(ModUiContext ctx, ImguiNode n, string label)
        {
            if (n.Options == null || n.Options.Length == 0) return;
            var surface = ctx.Surface;
            var live = ModUiContext.Live(surface, n);
            int count = n.Options.Length;
            int cols = Math.Max(1, Math.Min(n.Columns, count));
            bool named = !string.IsNullOrEmpty(label);
            if (named) ctx.B.PushContext(label);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (cols > 1 && i % cols == 0) ctx.StartRow(n.Key);
                    var id = ctx.Id(n, "o" + i);
                    if (id != null)
                    {
                        int index = i;
                        string text = Clean(ImguiCapture.Plain(n.Options[i]));
                        if (text.Length == 0) text = Loc.T("modui.unlabelled");
                        ctx.B.AddItem(id, GraphNodes.ChoiceOption(
                            () => text,
                            () => { var c = live(); return c != null && c.Selected == index; },
                            () => ModUiContext.Select(surface, n, index),
                            GraphNodes.Position(i + 1, count)));
                    }
                    if (cols > 1 && (i % cols == cols - 1 || i == count - 1)) ctx.EndRow();
                }
            }
            finally
            {
                ctx.Recover();
                if (named) ctx.B.PopContext();
            }
        }

        // =====================================================================================
        // Value helpers
        // =====================================================================================

        /// <summary>The static text drawn immediately after a slider, when it looks like a value
        /// readout (has a digit) — null otherwise. Looks past the single-control wrappers mods put
        /// around a slider.</summary>
        protected static ImguiNode ReadoutNode(ImguiNode slider)
        {
            var n = slider;
            while (n.Parent != null && n.Parent.Children.Count == 1) n = n.Parent;
            var parent = n.Parent;
            if (parent == null || parent.Kind != ImguiKind.Horizontal) return null;
            int i = parent.Children.IndexOf(n);
            if (i < 0 || i + 1 >= parent.Children.Count) return null;
            var next = parent.Children[i + 1];
            while (next.Children != null && next.Children.Count == 1) next = next.Children[0];
            if (next.IsContainer || next.IsInteractive || !next.HasText) return null;
            for (int k = 0; k < next.Text.Length; k++)
                if (char.IsDigit(next.Text[k])) return next;
            return null;
        }

        private static bool Whole(float v) => Math.Abs(v - (float)Math.Round(v)) < 0.0001f;

        protected static string FormatValue(float value, float min, float max)
        {
            if (Whole(min) && Whole(max) && Math.Abs(max - min) >= 2f)
                return Math.Round(value).ToString(System.Globalization.CultureInfo.CurrentCulture);
            return value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        }

        // Consecutive presses arrive faster than the mod redraws, so each press steps from the value
        // the previous one ASKED for, not from the (stale) value last captured.
        private static ImguiTarget _stepTarget;
        private static float _stepValue;
        private static int _stepFrame = -1000;

        private static float Step(ImguiNode c, int sign, bool large)
        {
            float lo = Math.Min(c.Min, c.Max), hi = Math.Max(c.Min, c.Max);
            float range = hi - lo;
            float step;
            if (Whole(c.Min) && Whole(c.Max) && range >= 2f && range <= 200f) step = large ? Math.Max(1f, (float)Math.Round(range / 10f)) : 1f;
            else step = range / (large ? 10f : 100f);
            int frame = UnityEngine.Time.frameCount;
            float from = c.Value;
            var target = c.Target;
            if (frame - _stepFrame < 30 && target.Equals(_stepTarget)) from = _stepValue;
            // A slider drawn right-to-left (max < min) still increases toward its Max.
            float dir = c.Max >= c.Min ? sign : -sign;
            float v = Math.Max(lo, Math.Min(hi, from + dir * step));
            _stepTarget = target;
            _stepValue = v;
            _stepFrame = frame;
            return v;
        }

        /// <summary>Scratch lists for the row collector (a build walks hundreds of rows; don't
        /// allocate a list for each, ten times a second).</summary>
        protected static class Lists
        {
            private static readonly Stack<List<ImguiNode>> _free = new Stack<List<ImguiNode>>();

            public static List<ImguiNode> Rent() => _free.Count > 0 ? _free.Pop() : new List<ImguiNode>(8);

            public static void Return(List<ImguiNode> l)
            {
                l.Clear();
                _free.Push(l);
            }
        }
    }
}
