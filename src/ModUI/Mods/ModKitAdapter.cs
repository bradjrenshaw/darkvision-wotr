using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WrathAccess.Input;
using WrathAccess.ModUI.Imgui;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.ModUI.Mods
{
    /// <summary>
    /// ModKit, the widget library ToyBox is built on (other mods bundle their own copy; every loaded
    /// assembly that contains it gets hooked). Two halves:
    ///
    /// <para><b>Probes</b> on ModKit's own functions, because the pixels alone don't say enough:</para>
    /// <list type="bullet">
    /// <item><c>ModKit.Private.UI.Toggle(Rect, …)</c> — check boxes and disclosure arrows are fully
    /// hand-rolled (hit-tested and painted by hand), so without this there is nothing to press.</item>
    /// <item><c>UI.HStack(title, …)</c> — a titled SECTION. On screen that is only a bold label in a
    /// left-hand column and a divider line; nothing in the layout groups a section's rows.</item>
    /// <item><c>UI.Slider / LogSlider</c> — label, slider, number box, unit and Reset are five
    /// separate controls, and a log slider's position is not its value.</item>
    /// <item><c>UI.EditKeyBind</c> — the little "Bind" button after an action. Its caption changes
    /// with its state and its capture mode reads raw key events from a focused window.</item>
    /// <item><c>UI.ValueAdjuster</c> — a stepper: an arrow button either side of a number.</item>
    /// <item><c>UI.ToggleButton(ref state, title, applyToChildren)</c> — a row of a tree browser: an
    /// unlabelled arrow, an expand-all button and the title as three unrelated controls.</item>
    /// </list>
    ///
    /// <para><b>Presentation</b> of what those probes record (this class claims their scopes, so it
    /// applies inside any mod; a mod built on ModKit derives its adapter from this one):</para>
    /// <list type="bullet">
    /// <item>A section is a collapsed tree group: a page reads as its section names; Right opens one.</item>
    /// <item>Inside a section, ONE CONTROL PER LINE. ModKit packs two or three to a row to save
    /// space; here every control is its own line, with its hotkey (if it has one) to its right.
    /// Outside sections a drawn row is a record (a character, a quest) and stays one row.</item>
    /// <item>A slider is one item: Left/Right step it in the mod's own units, Enter types an exact
    /// value into its number box, Backspace presses its Reset.</item>
    /// <item>A selection grid is one item: Left/Right change the pick, Enter lists the options.</item>
    /// <item>A hotkey is one item: Enter captures a key, Backspace clears it — through ModKit's own
    /// binding table, not its on-screen capture.</item>
    /// </list>
    /// </summary>
    internal class ModKitAdapter : ImguiAdapter
    {
        public const string KindSection = "modkit.section";
        public const string KindSlider = "modkit.slider";
        public const string KindBind = "modkit.keybind";
        public const string KindTreeNode = "modkit.treenode";
        public const string KindAdjuster = "modkit.adjuster";

        public override string Id => "lib.modkit";

        public override bool Claims(ImguiNode scope)
            => scope.ScopeKind != null && scope.ScopeKind.StartsWith("modkit.", StringComparison.Ordinal);

        /// <summary>What a slider probe knows that the drawn controls don't say.</summary>
        internal sealed class SliderInfo
        {
            public float Min, Max;
            public int Decimals;
            public string Units;
            public bool Log;
        }

        /// <summary>The limits of a stepper (an arrow button either side of a number).</summary>
        internal sealed class AdjusterInfo
        {
            public int Increment = 1, Min = int.MinValue, Max = int.MaxValue;
        }

        /// <summary>A hotkey slot: ModKit's identifier for it and the ModKit copy it belongs to.</summary>
        internal sealed class BindInfo
        {
            public string Identifier;
            public Type Ui;
        }

        // =====================================================================================
        // Probes
        // =====================================================================================

        private readonly HashSet<Assembly> _scanned = new HashSet<Assembly>();
        private static readonly Dictionary<Type, GUIContent> _disclosureOn = new Dictionary<Type, GUIContent>();

        public override void InstallProbes(Harmony harmony)
        {
            // The library adapter hooks ModKit once; mod adapters derived from this class must not
            // hook it again (every scope would open twice).
            if (GetType() != typeof(ModKitAdapter)) return;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!_scanned.Add(asm)) continue;
                Type priv, ui;
                try
                {
                    priv = asm.GetType("ModKit.Private.UI", false);
                    ui = asm.GetType("ModKit.UI", false);
                }
                catch { continue; }
                if (priv == null && ui == null) continue;
                string name = asm.GetName().Name;
                int installed = 0;
                var self = typeof(ModKitAdapter);

                if (priv != null)
                    installed += Hook(harmony, name, AccessTools.Method(priv, "Toggle", new[]
                    {
                        typeof(Rect), typeof(GUIContent), typeof(bool), typeof(bool),
                        typeof(GUIContent), typeof(GUIContent), typeof(GUIStyle), typeof(GUIStyle),
                    }), "Toggle(Rect, …)", new HarmonyMethod(self, nameof(TogglePrefix)), new HarmonyMethod(self, nameof(TogglePostfix)), null);

                if (ui != null)
                {
                    var end = new HarmonyMethod(self, nameof(ScopeFinalizer));
                    var f = typeof(float);
                    var rf = typeof(float).MakeByRefType();
                    var opts = typeof(GUILayoutOption[]);

                    installed += Hook(harmony, name,
                        AccessTools.Method(ui, "HStack", new[] { typeof(string), typeof(int), typeof(Action[]) }),
                        "HStack", new HarmonyMethod(self, nameof(SectionPrefix)), null, end);

                    var slider = new HarmonyMethod(self, nameof(SliderPrefix));
                    installed += Hook(harmony, name,
                        AccessTools.Method(ui, "Slider", new[] { typeof(string), rf, f, f, f, typeof(int), typeof(string), opts }),
                        "Slider(title, …)", slider, null, end);
                    installed += Hook(harmony, name,
                        AccessTools.Method(ui, "LogSlider", new[] { typeof(string), rf, f, f, f, typeof(int), typeof(string), opts }),
                        "LogSlider", slider, null, end);
                    installed += Hook(harmony, name,
                        AccessTools.Method(ui, "LogSliderCustomLabelWidth", new[] { typeof(string), rf, f, f, f, typeof(int), typeof(string), typeof(int), opts }),
                        "LogSliderCustomLabelWidth", slider, null, end);
                    installed += Hook(harmony, name,
                        AccessTools.Method(ui, "Slider", new[] { rf, f, f, f, typeof(int), typeof(string), opts }),
                        "Slider(untitled)", new HarmonyMethod(self, nameof(BareSliderPrefix)), null, end);

                    installed += Hook(harmony, name,
                        AccessTools.Method(ui, "EditKeyBind", new[] { typeof(string), typeof(bool), typeof(bool), opts }),
                        "EditKeyBind", new HarmonyMethod(self, nameof(BindPrefix)), null, end);

                    installed += Hook(harmony, name,
                        AccessTools.Method(ui, "ValueAdjuster", new[] { typeof(int).MakeByRefType(), typeof(int), typeof(int), typeof(int), typeof(bool) }),
                        "ValueAdjuster", new HarmonyMethod(self, nameof(AdjusterPrefix)), null, end);

                    // ToggleButton(ref ToggleState, title, Action<ToggleState> applyToChildren, options):
                    // the row of a tree browser. Found by shape — the state enum is ModKit's own.
                    MethodBase tree = null;
                    foreach (var m in ui.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (m.Name != "ToggleButton") continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 4 && ps[0].ParameterType.IsByRef && ps[1].ParameterType == typeof(string)
                            && typeof(Delegate).IsAssignableFrom(ps[2].ParameterType)) { tree = m; break; }
                    }
                    installed += Hook(harmony, name, tree, "ToggleButton(tree row)",
                        new HarmonyMethod(self, nameof(TreeNodePrefix)), null, end);
                }
                Main.Log?.Log("[modui] ModKit probes installed in " + name + ": " + installed);
            }
        }

        private static int Hook(Harmony harmony, string asm, MethodBase target, string what,
            HarmonyMethod prefix, HarmonyMethod postfix, HarmonyMethod finalizer)
        {
            if (target == null)
            {
                Main.Log?.Log("[modui] ModKit in " + asm + " has no " + what + "; that widget reads generically");
                return 0;
            }
            try
            {
                harmony.Patch(target, prefix, postfix, null, finalizer);
                return 1;
            }
            catch (Exception e)
            {
                Main.Log?.Error("[modui] ModKit probe " + what + " in " + asm + ": " + e.Message);
                return 0;
            }
        }

        // ---- check boxes / disclosure arrows ----

        private static void TogglePrefix()
        {
            if (ImguiCapture.Recording) ImguiCapture.Enter();
        }

        private static void TogglePostfix(Rect rect, GUIContent label, bool value, bool isEmpty,
            GUIContent on, MethodBase __originalMethod, ref bool __result)
        {
            if (!ImguiCapture.Recording || !ImguiCapture.Exit()) return;
            var n = ImguiCapture.Leaf(ImguiKind.Toggle, rect, label != null ? label.text : null,
                label != null ? label.tooltip : null);
            if (n == null) return;
            n.On = value;
            n.Disclosure = IsDisclosure(__originalMethod.DeclaringType, on);
            ImguiRequest r;
            if (ImguiCapture.TryTake(n, ImguiVerb.Click, out r))
            {
                __result = true; // the funnel returns "was clicked"; its caller flips the value
                ImguiCapture.Applied(r);
            }
        }

        // A disclosure toggle is the same function called with the arrow glyphs instead of the tick
        // glyphs. Read lazily, from inside the widget's own call — never force another mod's static
        // initialiser to run early.
        private static bool IsDisclosure(Type ui, GUIContent on)
        {
            if (ui == null || on == null) return false;
            GUIContent arrow;
            if (!_disclosureOn.TryGetValue(ui, out arrow))
            {
                try { arrow = ui.GetField("DisclosureOnContent", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as GUIContent; }
                catch { arrow = null; }
                _disclosureOn[ui] = arrow;
            }
            return arrow != null && ReferenceEquals(arrow, on);
        }

        // ---- scopes: each prefix opens one, the shared finalizer closes it (also when the mod throws) ----

        private static void SectionPrefix(string title, out bool __state)
        {
            __state = false;
            if (title == null || !ImguiCapture.Recording) return;
            string plain = ImguiCapture.Plain(title);
            __state = ImguiCapture.BeginScope(KindSection, plain, plain);
        }

        private static void SliderPrefix(string title, float min, float max, int decimals, string units,
            MethodBase __originalMethod, out bool __state)
        {
            __state = false;
            if (!ImguiCapture.Recording) return;
            string plain = ImguiCapture.Plain(title);
            __state = ImguiCapture.BeginScope(KindSlider, plain, plain, new SliderInfo
            {
                Min = min, Max = max, Decimals = decimals, Units = ImguiCapture.Plain(units),
                Log = __originalMethod.Name.StartsWith("Log", StringComparison.Ordinal),
            });
        }

        private static void BareSliderPrefix(float min, float max, int decimals, string units, out bool __state)
        {
            __state = false;
            if (!ImguiCapture.Recording) return;
            __state = ImguiCapture.BeginScope(KindSlider, "", null, new SliderInfo
            {
                Min = min, Max = max, Decimals = decimals, Units = ImguiCapture.Plain(units),
            });
        }

        private static void BindPrefix(string identifier, MethodBase __originalMethod, out bool __state)
        {
            __state = false;
            if (identifier == null || !ImguiCapture.Recording) return;
            __state = ImguiCapture.BeginScope(KindBind, "bind:" + identifier, identifier,
                new BindInfo { Identifier = identifier, Ui = __originalMethod.DeclaringType });
        }

        private static void AdjusterPrefix(int increment, int min, int max, out bool __state)
        {
            __state = false;
            if (!ImguiCapture.Recording) return;
            __state = ImguiCapture.BeginScope(KindAdjuster, "", null,
                new AdjusterInfo { Increment = Math.Max(1, increment), Min = min, Max = max });
        }

        private static void TreeNodePrefix(string title, out bool __state)
        {
            __state = false;
            if (title == null || !ImguiCapture.Recording) return;
            string plain = ImguiCapture.Plain(title);
            __state = ImguiCapture.BeginScope(KindTreeNode, plain, plain);
        }

        private static void ScopeFinalizer(bool __state)
        {
            if (__state) ImguiCapture.EndScope();
        }

        // =====================================================================================
        // Presentation: structure
        // =====================================================================================

        /// <summary>Prefix for the ids of section groups, so the same section name on two pages of a
        /// mod keeps separate expanded/collapsed state. A mod adapter sets it per page.</summary>
        protected string IdPrefix = "";

        private string _skipTitle; // a section's own title label, drawn inside its first row

        private static bool IsKind(ImguiNode n, string kind) => n.Kind == ImguiKind.Scope && n.ScopeKind == kind;

        public override void Build(ModUiContext ctx, ImguiNode region)
        {
            // Reached from another adapter that met one of our scopes in its own walk.
            if (region.Kind == ImguiKind.Scope && Claims(region)) Emit(ctx, region);
            else base.Build(ctx, region);
        }

        public override void Build(ModUiContext ctx, IList<ImguiNode> nodes, int from, int to)
            => EmitRun(ctx, nodes, from, Math.Min(to, nodes.Count));

        protected override void EmitChildren(ModUiContext ctx, ImguiNode n)
        {
            if (n.Children != null) EmitRun(ctx, n.Children, 0, n.Children.Count);
        }

        /// <summary>A run of siblings, with consecutive sections folded into tree groups. An untitled
        /// section right after a titled one is ModKit's way of continuing it (the title column is
        /// simply left blank), so it joins that group.</summary>
        private void EmitRun(ModUiContext ctx, IList<ImguiNode> nodes, int from, int to)
        {
            bool open = false, expanded = false;
            var b = ctx.B;
            try
            {
                for (int i = from; i < to; i++)
                {
                    var n = nodes[i];
                    if (IsKind(n, KindSection) && !Hide(n))
                    {
                        string title = Clean(n.ScopeLabel);
                        if (title.Length == 0)
                        {
                            if (!open || expanded) SectionBody(ctx, n, null);
                            continue;
                        }
                        if (open) { b.EndGroup(); open = false; }
                        var id = ctx.Id("sec:" + IdPrefix + "/" + n.Scope + "/" + title);
                        if (id == null) { SectionBody(ctx, n, title); continue; } // a repeated title: read it flat
                        expanded = b.IsExpanded(id);
                        b.BeginGroup(id, GraphNodes.Group(() => title));
                        open = true;
                        // Nothing is built for a collapsed section: a page is hundreds of controls,
                        // rebuilt several times a second, and only the open ones are ever reachable.
                        if (expanded) SectionBody(ctx, n, title);
                        continue;
                    }
                    if (open) { b.EndGroup(); open = false; }
                    Emit(ctx, n);
                }
            }
            finally
            {
                ctx.Recover();
                if (open) b.EndGroup();
            }
        }

        private int _sectionDepth; // > 0 while presenting the inside of a section

        private void SectionBody(ModUiContext ctx, ImguiNode section, string title)
        {
            if (section.Children == null) return;
            _sectionDepth++;
            try
            {
                for (int i = 0; i < section.Children.Count; i++)
                {
                    _skipTitle = i == 0 ? title : null; // the title label heads the first row only
                    try { EmitRun(ctx, section.Children, i, i + 1); }
                    finally { _skipTitle = null; }
                }
            }
            finally { _sectionDepth--; }
        }

        protected override void Emit(ModUiContext ctx, ImguiNode n)
        {
            if (Hide(n)) return;
            switch (n.Kind)
            {
                case ImguiKind.Scope:
                    if (IsKind(n, KindSection)) { EmitRun(ctx, new[] { n }, 0, 1); return; } // met outside a run (rare)
                    if (!Claims(n)) { base.Emit(ctx, n); return; }
                    EmitLine(ctx, n);
                    return;
                case ImguiKind.Horizontal:
                    EmitLine(ctx, n);
                    return;
                case ImguiKind.Root:
                case ImguiKind.Window:
                case ImguiKind.Vertical:
                case ImguiKind.Scroll:
                    EmitChildren(ctx, n);
                    return;
                default:
                    EmitLine(ctx, n);
                    return;
            }
        }

        private void EmitLine(ModUiContext ctx, ImguiNode n)
        {
            var tokens = Lists.Rent();
            try
            {
                if (CollectTokens(n, tokens)) EmitUnits(ctx, tokens);
                else EmitChildren(ctx, n); // holds columns of several lines: take them in order
            }
            finally
            {
                Lists.Return(tokens);
                _names.Clear();
                _skips.Clear();
            }
        }

        private static bool IsAtomic(ImguiNode n)
            => IsKind(n, KindSlider) || IsKind(n, KindBind) || IsKind(n, KindAdjuster);

        /// <summary>Names a mod adapter gives to controls the mod draws without one (it knows the
        /// page: "the first grid on Search 'n Pick is the blueprint type"). Filled before the page
        /// is presented; the adapter clears it at the start of each build.</summary>
        protected readonly Dictionary<ImguiNode, string> Given = new Dictionary<ImguiNode, string>();

        // What a probe knows about the pieces of a widget, for the line being presented.
        private readonly Dictionary<ImguiNode, string> _names = new Dictionary<ImguiNode, string>();
        private readonly HashSet<ImguiNode> _skips = new HashSet<ImguiNode>();

        /// <summary>The controls of one drawn line, in order, with a slider or a hotkey counted as ONE
        /// thing. False when the subtree is not a single line (a column of several rows, a section).</summary>
        private bool CollectTokens(ImguiNode n, List<ImguiNode> into)
        {
            if (!n.IsContainer || IsAtomic(n))
            {
                into.Add(n);
                return true;
            }
            switch (n.Kind)
            {
                case ImguiKind.Scope:
                    if (!IsKind(n, KindTreeNode)) return false;
                    // ModKit's tree row: an unlabelled disclosure arrow, an expand/collapse-all
                    // button, then the title as a separate label. The arrow IS the row, so it takes
                    // the title and the label is not repeated.
                    for (int i = 0; i < n.Children.Count; i++)
                    {
                        var c = n.Children[i];
                        if (c.Kind == ImguiKind.Toggle && !c.HasText) _names[c] = Clean(n.ScopeLabel);
                        else if (c.Kind == ImguiKind.Label && c.Text == n.ScopeLabel) _skips.Add(c);
                        if (!CollectTokens(c, into)) return false;
                    }
                    return true;
                case ImguiKind.Horizontal:
                    for (int i = 0; i < n.Children.Count; i++)
                        if (!CollectTokens(n.Children[i], into)) return false;
                    return true;
                case ImguiKind.Vertical:
                case ImguiKind.Scroll:
                {
                    if (n.Children.Count == 1) return CollectTokens(n.Children[0], into);
                    // A column inside a row is still one line when only ONE of its entries holds
                    // controls and the rest is text stacked around it (a record's details arrow
                    // with its description underneath). Two entries with controls are two lines.
                    int withControls = 0;
                    for (int i = 0; i < n.Children.Count; i++)
                        if (HasControl(n.Children[i]) && ++withControls > 1) return false;
                    for (int i = 0; i < n.Children.Count; i++)
                        if (!CollectTokens(n.Children[i], into)) return false;
                    return true;
                }
                default:
                    return false;
            }
        }

        private static bool HasControl(ImguiNode n)
        {
            if (n.Children == null) return n.IsInteractive;
            for (int i = 0; i < n.Children.Count; i++)
                if (HasControl(n.Children[i])) return true;
            return false;
        }

        // =====================================================================================
        // Presentation: lines
        //
        // Inside a SECTION (a settings page) every control is its own line: ModKit packs two or
        // three to a row there only to save space, and text after a control is its description.
        // Everywhere else a drawn row is a RECORD — a character and its buttons, a quest and its
        // state — and stays one navigation row, Left/Right between its controls.
        // =====================================================================================

        private struct Unit
        {
            public ImguiNode Node;  // the control (or the text, for IsText)
            public ImguiNode Bind;  // the hotkey drawn after it
            public ImguiNode Edit;  // the number box drawn after a stepper
            public bool Stepper;    // Node is the "<" of a hand-drawn "< 14 >"
            public string Label;
            public string Desc;
            public bool IsText;
        }

        private readonly List<Unit> _units = new List<Unit>(8);
        private bool _unitsBusy;

        private void EmitUnits(ModUiContext ctx, List<ImguiNode> tokens)
        {
            bool scratch = !_unitsBusy;
            var units = scratch ? _units : new List<Unit>(tokens.Count);
            _unitsBusy = true;
            try { EmitUnits(ctx, tokens, units); }
            finally
            {
                units.Clear();
                if (scratch) _unitsBusy = false;
            }
        }

        private static bool HasLetterOrDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsLetterOrDigit(s[i])) return true;
            return false;
        }

        // A button whose caption is only a symbol or a number ("+1", a pencil): on its own it says
        // nothing, so the text drawn before it is its caption ("lvl 6  [+1]").
        private static bool IsWeak(ImguiNode t)
        {
            if (t.Kind != ImguiKind.Button || !t.HasText) return false;
            for (int i = 0; i < t.Text.Length; i++)
                if (char.IsLetter(t.Text[i])) return false;
            return true;
        }

        private bool IsStatic(ImguiNode t) => !t.IsContainer && !t.IsInteractive;

        // For a control with no name and no text before it: the first later text on the line that
        // is not about to name some other unlabelled control.
        private ImguiNode FreeTextAfter(List<ImguiNode> tokens, int i)
        {
            for (int j = i + 1; j < tokens.Count; j++)
            {
                var t = tokens[j];
                if (Hide(t) || _skips.Contains(t) || !IsStatic(t)) continue;
                string s = Clean(t.Text);
                if (s.Length == 0 || !HasLetterOrDigit(s)) continue;
                for (int k = j + 1; k < tokens.Count; k++)
                {
                    var next = tokens[k];
                    if (Hide(next) || _skips.Contains(next)) continue;
                    if (IsStatic(next)) continue;
                    if (OwnLabel(next).Length == 0) return null; // that text names the control after it
                    break;
                }
                return t;
            }
            return null;
        }

        private void EmitUnits(ModUiContext ctx, List<ImguiNode> tokens, List<Unit> units)
        {
            bool rows = _sectionDepth == 0;
            string pending = null;        // static text not yet attached to anything
            ImguiNode pendingNode = null;
            string caption = null;        // text ahead of the line's first control, when that control names itself
            ImguiNode captionNode = null;

            ImguiNode head = null;        // the control that stands for the whole row (see below)

            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (Hide(t) || _skips.Contains(t)) continue;

                // A stepper drawn by hand rather than through ModKit's widget: "<", a number, ">".
                int stepperEnd = StepperAt(tokens, i);
                if (stepperEnd >= i)
                {
                    units.Add(new Unit
                    {
                        // Anchored on its name when it has one: the arrows come and go at the limits.
                        Node = pendingNode ?? (t.IsInteractive ? t : tokens[stepperEnd]),
                        Stepper = true,
                        Label = string.IsNullOrEmpty(pending) ? Unnamed(t, units.Count == 0) : pending,
                    });
                    pending = null;
                    pendingNode = null;
                    i = stepperEnd;
                    continue;
                }

                if (IsStatic(t))
                {
                    string s = Clean(t.Text);
                    if (s.Length == 0 || !HasLetterOrDigit(s)) continue; // a picture, a spacer, a decorative glyph
                    if (_skipTitle != null && s == _skipTitle) { _skipTitle = null; continue; }
                    if (pending == null) { pending = s; pendingNode = t; }
                    else pending = pending + ", " + s;
                    continue;
                }

                bool bind = IsKind(t, KindBind);
                if (bind && pending == null && units.Count > 0)
                {
                    // The hotkey of the control just before it.
                    var prev = units[units.Count - 1];
                    if (!prev.IsText && prev.Bind == null && !IsKind(prev.Node, KindBind))
                    {
                        prev.Bind = t;
                        units[units.Count - 1] = prev;
                        continue;
                    }
                }

                if (t.Kind == ImguiKind.TextField && pending == null && units.Count > 0)
                {
                    // The number box of the stepper just before it.
                    var prev = units[units.Count - 1];
                    if (!prev.IsText && prev.Edit == null && (prev.Stepper || IsKind(prev.Node, KindAdjuster)))
                    {
                        prev.Edit = t;
                        units[units.Count - 1] = prev;
                        continue;
                    }
                }

                var u = new Unit { Node = t };
                string own = OwnLabel(t);
                string paging = own.Length > 0 ? PagingName(tokens, i) : null;
                if (paging != null) own = paging;
                if (own.Length > 0)
                {
                    u.Label = own;
                    if (pending != null)
                    {
                        if (IsWeak(t) && paging == null) u.Label = pending + ", " + own; // its caption
                        else if (units.Count > 0) Describe(units, pending);            // about the control before it
                        else { caption = pending; captionNode = pendingNode; }
                    }
                }
                else if (rows && head == null && t.Kind == ImguiKind.Toggle && t.Disclosure)
                {
                    // A nameless disclosure arrow in a record's row opens THAT record (a feature and
                    // its Remove button, then the arrow): it takes the row's name and leads the row.
                    // The name is the row's FIRST text (the caption, when a button came between);
                    // text right before the arrow (a rating, a type column) is then about the record.
                    u.Label = caption ?? pending;
                    if (caption != null && pending != null) u.Desc = pending;
                    if (string.IsNullOrEmpty(u.Label)) u.Label = Loc.T("modui.details");
                    else head = t;
                    caption = null;
                    captionNode = null;
                }
                else if (t.Kind == ImguiKind.TextField && pending == null && InlineEditor(tokens, i))
                    u.Label = Loc.T("modui.inline_edit");
                else if (t.Kind == ImguiKind.TextField && pending == null && i + 1 < tokens.Count
                         && tokens[i + 1].Kind == ImguiKind.Button && tokens[i + 1].Text == "Search")
                    u.Label = Loc.T("modui.search"); // the box a Search button belongs to
                else
                {
                    // Unlabelled (a grid, a bare number box, an arrow, a hotkey on its own): the text
                    // before it on the line is its name; failing that, text after it that nothing
                    // else needs. Only the SAME line counts — the capture's anchor (the nearest
                    // text drawn before the control, however far back) is for identity, not naming:
                    // a filter bar at the top of a page would be called by the last note above it.
                    u.Label = pending;
                    if (string.IsNullOrEmpty(u.Label) && bind) u.Label = Clean(t.ScopeLabel);
                    if (string.IsNullOrEmpty(u.Label) && (t.Kind == ImguiKind.Toggle || t.Kind == ImguiKind.Button))
                    {
                        var free = FreeTextAfter(tokens, i);
                        if (free != null)
                        {
                            u.Label = Clean(free.Text);
                            _skips.Add(free);
                        }
                    }
                    if (string.IsNullOrEmpty(u.Label)) u.Label = Unnamed(t, units.Count == 0);
                }
                pending = null;
                pendingNode = null;
                units.Add(u);
            }

            if (pending != null)
            {
                if (units.Count > 0) Describe(units, pending);
                else units.Add(new Unit { Node = pendingNode, Label = pending, IsText = true });
            }
            if (caption != null)
            {
                if (units.Count == 1)
                {
                    // "Mouse:  [x] Invert X Axis" — the caption is that one control's name too.
                    var only = units[0];
                    only.Label = caption + ", " + only.Label;
                    units[0] = only;
                }
                else if (rows) units.Insert(0, new Unit { Node = captionNode, Label = caption, IsText = true });
                else
                {
                    var id = ctx.Id(captionNode, "text");
                    if (id != null) ctx.B.AddItem(id, TextCell(captionNode, caption));
                }
            }
            if (units.Count == 0) return;

            if (!rows)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    bool pair = units[i].Bind != null;
                    // A shared key: Up/Down from a hotkey stays in the hotkey column.
                    if (pair) ctx.StartRow("modkit.unit");
                    int n = AddUnit(ctx, units[i]);
                    if (pair) { if (n > 0) ctx.EndRow(); else ctx.Recover(); }
                }
                return;
            }

            // A tree row, or a record with a details arrow, leads with its name (ModKit draws a
            // tree row's Start/Complete buttons first) and carries the row's status and description
            // text, wherever on the line that was drawn.
            for (int k = 0; k < units.Count; k++)
            {
                if (units[k].IsText || units[k].Stepper) continue;
                if (!ReferenceEquals(units[k].Node, head) && !_names.ContainsKey(units[k].Node)) continue;
                var first = units[k];
                units.RemoveAt(k);
                for (int j = 0; j < units.Count; j++)
                {
                    var other = units[j];
                    if (other.IsText || other.Desc == null) continue;
                    first.Desc = first.Desc == null ? other.Desc : first.Desc + ", " + other.Desc;
                    other.Desc = null;
                    units[j] = other;
                }
                units.Insert(0, first);
                break;
            }

            bool row = units.Count > 1 || units[0].Bind != null;
            if (row) ctx.StartRow();
            int added = 0;
            for (int i = 0; i < units.Count; i++) added += AddUnit(ctx, units[i]);
            if (row) { if (added > 0) ctx.EndRow(); else ctx.Recover(); }
        }

        // ModKit's browser pages its results with "-" and "+" beside a "Page: 1 / 6" label.
        private string PagingName(List<ImguiNode> tokens, int i)
        {
            var t = tokens[i];
            if (t.Kind != ImguiKind.Button || !t.HasText) return null;
            string text = t.Text.Trim();
            if (text != "-" && text != "+") return null;
            for (int j = 0; j < tokens.Count; j++)
                if (IsStatic(tokens[j]) && tokens[j].HasText && tokens[j].Text.StartsWith("Page", StringComparison.Ordinal))
                    return Loc.T(text == "-" ? "modui.page_previous" : "modui.page_next");
            return null;
        }

        // A text box followed directly by a cancel / confirm glyph: ModKit's editable label, mid-edit.
        private static bool InlineEditor(List<ImguiNode> tokens, int i)
        {
            if (i + 1 >= tokens.Count) return false;
            var next = tokens[i + 1];
            return next.Kind == ImguiKind.Button && (next.Text == "\u2716" || next.Text == "\u2714");
        }

        private static void Describe(List<Unit> units, string text)
        {
            var prev = units[units.Count - 1];
            if (prev.IsText) prev.Label = prev.Label + ", " + text;
            else prev.Desc = prev.Desc == null ? text : prev.Desc + ", " + text;
            units[units.Count - 1] = prev;
        }

        // The text a control carries itself: a button's or check box's caption, a titled slider's
        // title, or what a probe said this piece of a widget is.
        private string OwnLabel(ImguiNode t)
        {
            string given;
            if (_names.TryGetValue(t, out given) && !string.IsNullOrEmpty(given)) return given;
            if (Given.TryGetValue(t, out given) && !string.IsNullOrEmpty(given)) return given;
            if (IsKind(t, KindSlider)) return Clean(t.ScopeLabel);
            if (t.Kind == ImguiKind.Button || t.Kind == ImguiKind.Toggle)
            {
                string s = Clean(t.Text);
                if (s.Length > 0) return s;
                if (t.Kind == ImguiKind.Button && !string.IsNullOrEmpty(t.Image)) return ImageLabel(t);
            }
            return "";
        }

        /// <summary>What to call a control nothing on its line names. A nameless grid is announced
        /// by its pick alone; a text box after other controls holds the value of what precedes it.</summary>
        protected virtual string Unnamed(ImguiNode t, bool firstOnLine)
        {
            if (t.Kind == ImguiKind.Choice) return "";
            if (t.Kind == ImguiKind.TextField && !firstOnLine) return Loc.T("modui.value");
            return Loc.T("modui.unlabelled");
        }

        // ModKit draws a few buttons as bare glyphs.
        protected override string Clean(string text)
        {
            if (!string.IsNullOrEmpty(text) && text.Length <= 2)
            {
                switch (text)
                {
                    case "\u270E": return Loc.T("modui.glyph_edit");
                    case "\u226B": return Loc.T("modui.glyph_expand_all");
                    case "\u226A": return Loc.T("modui.glyph_collapse_all");
                    case "\u2714": return Loc.T("modui.glyph_confirm");
                    case "\u2716": return Loc.T("modui.glyph_cancel");
                }
            }
            return base.Clean(text);
        }

        /// <summary>Add one unit's item(s) to the builder (inside whatever row is open). Returns how many.</summary>
        private int AddUnit(ModUiContext ctx, Unit u)
        {
            var b = ctx.B;
            if (u.IsText)
            {
                var tid = ctx.Id(u.Node, "text");
                if (tid == null) return 0;
                b.AddItem(tid, TextCell(u.Node, u.Label));
                return 1;
            }

            ControlId id;
            NodeVtable vt;
            if (IsKind(u.Node, KindBind))
            {
                id = BindId(ctx, u.Node);
                vt = id != null ? BindItem(u.Node, u.Label, u.Label, u.Desc) : null;
            }
            else if (IsKind(u.Node, KindSlider))
            {
                id = ctx.Id(First(u.Node));
                vt = id != null ? SliderItem(ctx, u.Node, u.Label, u.Desc) : null;
            }
            else if (u.Stepper || IsKind(u.Node, KindAdjuster))
            {
                id = ctx.Id(u.Node, "step");
                vt = id != null ? AdjusterItem(ctx, u.Node, u.Stepper, u.Edit, u.Label, u.Desc) : null;
            }
            else if (u.Node.Kind == ImguiKind.Choice)
            {
                id = ctx.Id(u.Node);
                vt = id != null ? ChoiceItem(ctx, u.Node, u.Label, u.Desc) : null;
            }
            else
            {
                id = ctx.Id(u.Node);
                vt = id != null ? Control(ctx, u.Node, u.Label, u.Desc) : null;
            }
            if (vt == null) return 0;
            b.AddItem(id, vt);
            if (u.Bind == null) return 1;

            var bindId = BindId(ctx, u.Bind);
            var bindVt = bindId != null
                ? BindItem(u.Bind, Loc.T("modui.hotkey_for", new { name = u.Label }), u.Label, null) : null;
            if (bindVt == null) return 1;
            b.AddItem(bindId, bindVt);
            return 2;
        }

        private static ImguiNode First(ImguiNode n)
        {
            while (n.Children != null && n.Children.Count > 0) n = n.Children[0];
            return n;
        }

        private static ImguiNode Find(ImguiNode n, ImguiKind kind, bool last = false)
        {
            if (n.Children == null) return n.Kind == kind ? n : null;
            if (!last)
            {
                for (int i = 0; i < n.Children.Count; i++)
                {
                    var f = Find(n.Children[i], kind);
                    if (f != null) return f;
                }
            }
            else
            {
                for (int i = n.Children.Count - 1; i >= 0; i--)
                {
                    var f = Find(n.Children[i], kind, true);
                    if (f != null) return f;
                }
            }
            return null;
        }

        // =====================================================================================
        // The optimistic value
        //
        // An injected change lands when the mod next repaints, and the capture after that — a few
        // frames. Until then the item reports the value it ASKED for, so Left/Right speak at once,
        // key repeat steps from the right place, and the real value arriving a moment later is not
        // news. If the mod settles on something else (it clamps, or the control went away) the
        // difference is announced when the window lapses.
        // =====================================================================================

        private static ImguiSurface _optSurface;
        private static ImguiTarget _optTarget;
        private static float _optUntil;
        private static float _optValue;
        private static int _optIndex;

        private static void Expect(ImguiSurface surface, ImguiNode node, float value, int index)
        {
            _optSurface = surface;
            _optTarget = node.Target;
            _optValue = value;
            _optIndex = index;
            _optUntil = Time.unscaledTime + 0.75f;
        }

        private static bool Expecting(ImguiSurface surface, ImguiNode node)
            => ReferenceEquals(_optSurface, surface) && Time.unscaledTime < _optUntil && _optTarget.Equals(node.Target);

        // =====================================================================================
        // Slider: label + slider + number box + unit + Reset, as one item
        // =====================================================================================

        private NodeVtable SliderItem(ModUiContext ctx, ImguiNode scope, string label, string description)
        {
            var info = scope.Data as SliderInfo ?? new SliderInfo();
            var surface = ctx.Surface;
            var slider0 = Find(scope, ImguiKind.Slider);
            var field0 = Find(scope, ImguiKind.TextField);
            var reset0 = Find(scope, ImguiKind.Button, last: true);
            if (slider0 == null) return null;
            var slider = ModUiContext.Live(surface, slider0);
            var field = field0 != null ? ModUiContext.Live(surface, field0) : null;
            var reset = reset0 != null ? ModUiContext.Live(surface, reset0) : null;
            string units = info.Units ?? "";

            Func<bool> enabled = () => { var c = slider(); return c == null || c.Enabled; };
            // The value in the mod's own units: what its number box shows, else worked out from the
            // slider (a log slider's position is 100·log10(value + 1)).
            Func<float?> read = () =>
            {
                var s = slider();
                if (s == null) return null;
                if (Expecting(surface, s)) return _optValue;
                var f = field != null ? field() : null;
                float v;
                if (f != null && float.TryParse(f.Content, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
                return info.Log ? (float)(Math.Pow(10.0, s.Value / 100f) - 1.0) : s.Value;
            };
            Func<string> value = () =>
            {
                var v = read();
                if (v == null) return null;
                string text = v.Value.ToString(CultureInfo.CurrentCulture);
                return units.Length > 0 ? text + " " + units : text;
            };

            var vt = new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(enabled),
                },
                SearchText = () => label,
                StateText = value,
                OnAdjust = (sign, large) =>
                {
                    var s = slider();
                    var cur = read();
                    if (s == null || cur == null || !s.Enabled) return;
                    float next = info.Log ? StepLog(info, cur.Value, sign) : StepLinear(info, cur.Value, sign);
                    if (Math.Abs(next - cur.Value) < 1e-6f) return; // at an end
                    UiSound.Play(Kingmaker.UI.UISoundType.SettingsSliderMove);
                    Expect(surface, s, next, 0);
                    ModUiContext.SetValue(surface, s, info.Log ? 100f * (float)Math.Log10(next + 1f) : next);
                },
            };
            if (field != null)
                vt.OnActivate = () =>
                {
                    var f = field();
                    if (f == null || !f.Enabled) return;
                    Screens.ModTextEntryScreen.Open(label, f.Content, text => ModUiContext.SetText(surface, f, text));
                };
            if (reset != null)
                vt.OnSecondary = () =>
                {
                    var r = reset();
                    if (r == null || !r.Enabled) return;
                    UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                    ModUiContext.Click(surface, r); // the new value announces itself (the live part)
                };
            Tip(vt, description, slider0.Tooltip);
            return vt;
        }

        private static float Clamp(SliderInfo info, double v)
        {
            float lo = Math.Min(info.Min, info.Max), hi = Math.Max(info.Min, info.Max);
            return (float)Math.Max(lo, Math.Min(hi, v));
        }

        // The smallest step the slider's own rounding keeps, widened until the range is at most 200
        // steps (a 0–4 slider with two decimals is not 400 key presses).
        private static float StepLinear(SliderInfo info, float cur, int sign)
        {
            int decimals = Math.Max(0, Math.Min(6, info.Decimals));
            double step = Math.Pow(10.0, -decimals);
            double range = Math.Abs(info.Max - info.Min);
            while (range / step > 200.0) step *= 10.0;
            return Clamp(info, Math.Round(cur + sign * step, decimals));
        }

        // ModKit's log slider shows fewer decimals as the value grows, and re-rounds the value to
        // that many on every repaint. So: step by one unit of the last digit it shows HERE, and if
        // the result would not survive its rounding THERE (0 → 0.1 snaps straight back to 0 on a
        // whole-number slider), try the next coarser digit.
        private static int LogDigits(SliderInfo info, float v)
            => (int)Math.Max(0.0, Math.Min(15.0, info.Decimals + 1.01 - Math.Log10(v + 1f)));

        private static float StepLog(SliderInfo info, float cur, int sign)
        {
            for (int d = LogDigits(info, cur); d >= 0; d--)
            {
                float cand = Clamp(info, Math.Round(cur + sign * Math.Pow(10.0, -d), d));
                if (Math.Abs(cand - cur) < 1e-6f) continue;
                float settled = (float)Math.Round(cand, LogDigits(info, cand));
                if (Math.Abs(settled - cand) > 1e-6f) continue;
                return cand;
            }
            return cur;
        }

        // =====================================================================================
        // Stepper (an arrow either side of a number, optionally a number box after it): one item
        // =====================================================================================

        private static bool IsArrow(ImguiNode t, string arrow)
            => t.Kind == ImguiKind.Button && t.HasText && t.Text.Trim() == arrow;

        // "14", "level: 6", "my lvl: 1": static text that ends in a whole number.
        private static bool TrailingInt(string text, out string prefix, out int value)
        {
            prefix = "";
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int end = text.Length;
            while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
            int start = end;
            while (start > 0 && char.IsDigit(text[start - 1])) start--;
            if (start == end) return false;
            if (start > 0 && text[start - 1] == '-') start--;
            prefix = text.Substring(0, start);
            return int.TryParse(text.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsNumber(ImguiNode t)
        {
            string prefix;
            int v;
            return t.Children == null && !t.IsInteractive && TrailingInt(t.Text, out prefix, out v);
        }

        // A stepper a mod drew by hand: "<", a number, ">" — either arrow missing while the value
        // sits at that limit. Starting at tokens[i]: the index of its last token, or -1.
        private static int StepperAt(List<ImguiNode> tokens, int i)
        {
            int number;
            if (IsArrow(tokens[i], "<") && i + 1 < tokens.Count && IsNumber(tokens[i + 1])) number = i + 1;
            else if (IsNumber(tokens[i]) && i + 1 < tokens.Count && IsArrow(tokens[i + 1], ">")) number = i;
            else return -1;
            return number + 1 < tokens.Count && IsArrow(tokens[number + 1], ">") ? number + 1 : number;
        }

        // The pieces of a hand-drawn stepper, re-found in a later frame. The anchor is something
        // whose identity does not change with the value: the name drawn before it when there is
        // one (an arrow comes and goes at the limits), else an arrow.
        private static void Pieces(ImguiNode anchor, out ImguiNode less, out ImguiNode number, out ImguiNode more)
        {
            less = number = more = null;
            if (anchor.IsInteractive)
            {
                var sibs = anchor.Parent != null ? anchor.Parent.Children : null;
                if (sibs == null) return;
                int i = sibs.IndexOf(anchor);
                int n = IsArrow(anchor, "<") ? i + 1 : i - 1;
                if (n < 0 || n >= sibs.Count || !IsNumber(sibs[n])) return;
                number = sibs[n];
                if (n > 0 && IsArrow(sibs[n - 1], "<")) less = sibs[n - 1];
                if (n + 1 < sibs.Count && IsArrow(sibs[n + 1], ">")) more = sibs[n + 1];
                return;
            }
            // From the name: the first number with an arrow beside it, after the name, in the
            // nearest enclosing group that has one (the name is often wrapped in a column of its own).
            var c = anchor;
            for (int up = 0; up < 3 && c.Parent != null; up++, c = c.Parent)
            {
                var sibs = c.Parent.Children;
                for (int j = sibs.IndexOf(c) + 1; j < sibs.Count; j++)
                {
                    if (!IsNumber(sibs[j])) continue;
                    bool lt = j > 0 && IsArrow(sibs[j - 1], "<"), gt = j + 1 < sibs.Count && IsArrow(sibs[j + 1], ">");
                    if (!lt && !gt) continue;
                    number = sibs[j];
                    if (lt) less = sibs[j - 1];
                    if (gt) more = sibs[j + 1];
                    return;
                }
            }
        }

        /// <param name="anchor">ModKit's stepper scope, or (<paramref name="drawn"/>) the anchor of
        /// one a mod drew by hand (see <see cref="Pieces"/>).</param>
        private NodeVtable AdjusterItem(ModUiContext ctx, ImguiNode anchor, bool drawn, ImguiNode edit, string label, string description)
        {
            var info = anchor.Data as AdjusterInfo ?? new AdjusterInfo();
            var surface = ctx.Surface;
            var live = ModUiContext.Live(surface, anchor);
            var field = edit != null ? ModUiContext.Live(surface, edit) : null;
            Func<ImguiNode, ImguiNode> numberOf = a =>
            {
                if (!drawn) return NumberIn(a);
                ImguiNode less, number, more;
                Pieces(a, out less, out number, out more);
                return number;
            };
            Func<ImguiNode, int, ImguiNode> arrowOf = (a, sign) =>
            {
                if (!drawn) return ArrowIn(a, sign > 0 ? ">" : "<");
                ImguiNode less, number, more;
                Pieces(a, out less, out number, out more);
                return sign > 0 ? more : less;
            };

            // The value as drawn ("level: 6"): the number, and the words in front of it.
            Func<ImguiNode, int?> parse = s =>
            {
                var v = numberOf(s);
                string prefix;
                int n;
                return v != null && TrailingInt(v.Text, out prefix, out n) ? n : (int?)null;
            };
            Func<int?> read = () =>
            {
                var s = live();
                if (s == null) return null;
                if (Expecting(surface, s)) return (int)_optValue;
                return parse(s);
            };
            Func<string> value = () =>
            {
                var s = live();
                if (s == null) return null;
                var v = numberOf(s);
                string prefix = "";
                int n;
                if (v == null || !TrailingInt(v.Text, out prefix, out n)) return null;
                if (Expecting(surface, s)) n = (int)_optValue;
                return Clean(prefix + n.ToString(CultureInfo.CurrentCulture));
            };

            var vt = new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                StateText = value,
                OnAdjust = (sign, large) =>
                {
                    var s = live();
                    var cur = read();
                    if (s == null || cur == null) return;
                    // At a limit the arrow is simply not drawn.
                    var arrow = arrowOf(s, sign);
                    if (arrow == null || !arrow.Enabled) return;
                    long next = (long)cur.Value + sign * info.Increment;
                    next = Math.Max(info.Min, Math.Min(info.Max, next));
                    UiSound.Play(Kingmaker.UI.UISoundType.SettingsSliderMove);
                    Expect(surface, s, next, 0);
                    ModUiContext.Click(surface, arrow);
                },
            };
            if (field != null)
                vt.OnActivate = () =>
                {
                    var f = field();
                    if (f == null || !f.Enabled) return;
                    Screens.ModTextEntryScreen.Open(label, f.Content, text => ModUiContext.SetText(surface, f, text));
                };
            Tip(vt, description, null);
            return vt;
        }

        private static ImguiNode NumberIn(ImguiNode n)
        {
            if (n.Children == null)
            {
                int v;
                return n.HasText && int.TryParse(n.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? n : null;
            }
            for (int i = 0; i < n.Children.Count; i++)
            {
                var f = NumberIn(n.Children[i]);
                if (f != null) return f;
            }
            return null;
        }

        private static ImguiNode ArrowIn(ImguiNode n, string arrow)
        {
            if (n.Children == null)
                return n.Kind == ImguiKind.Button && n.HasText && n.Text.Trim() == arrow ? n : null;
            for (int i = 0; i < n.Children.Count; i++)
            {
                var f = ArrowIn(n.Children[i], arrow);
                if (f != null) return f;
            }
            return null;
        }

        // =====================================================================================
        // Selection grid: one item
        // =====================================================================================

        /// <summary>A pick-one grid as a single adjustable item ("All Attacks Hit, Off, 1 of 6"):
        /// Left/Right change the pick, Enter lists the options.</summary>
        protected NodeVtable ChoiceItem(ModUiContext ctx, ImguiNode n, string label, string description)
        {
            if (n.Options == null || n.Options.Length == 0) return null;
            var surface = ctx.Surface;
            var live = ModUiContext.Live(surface, n);
            Func<bool> enabled = () => { var c = live(); return c == null || c.Enabled; };
            Func<ImguiNode, int> index = c => Expecting(surface, c) ? _optIndex : c.Selected;
            Func<string> value = () =>
            {
                var c = live();
                if (c == null || c.Options == null) return null;
                int i = index(c);
                return i >= 0 && i < c.Options.Length ? OptionText(c, i) : Loc.T("modui.no_selection");
            };
            Func<string> position = () =>
            {
                var c = live();
                if (c == null || c.Options == null) return null;
                int i = index(c);
                return i >= 0 && i < c.Options.Length ? Loc.T("nav.position", new { index = i + 1, count = c.Options.Length }) : null;
            };

            var vt = new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(enabled),
                    new NodeAnnouncement(position, kind: AnnouncementKinds.Position),
                },
                SearchText = () => label.Length > 0 ? label : value(),
                StateText = value,
                OnAdjust = (sign, large) =>
                {
                    var c = live();
                    if (c == null || c.Options == null || !c.Enabled) return;
                    // Nothing selected = a grid of ACTIONS drawn as a selection grid ("+5 Lawful
                    // Good"): arrowing must not fire one. Enter lists them.
                    if (index(c) < 0) return;
                    int i = index(c) + sign;
                    while (i >= 0 && i < c.Options.Length && c.OptionEnabled != null && i < c.OptionEnabled.Length && !c.OptionEnabled[i]) i += sign;
                    if (i < 0 || i >= c.Options.Length) return; // at an end
                    UiSound.Play(Kingmaker.UI.UISoundType.SettingsSliderMove);
                    Expect(surface, c, 0f, i);
                    ModUiContext.Select(surface, c, i);
                },
                OnActivate = () =>
                {
                    var c = live();
                    if (c == null || c.Options == null || !c.Enabled) return;
                    var options = new List<string>(c.Options.Length);
                    for (int i = 0; i < c.Options.Length; i++) options.Add(OptionText(c, i));
                    Screens.ChoiceSubmenuScreen.Open(label, options, Math.Max(0, c.Selected), pick =>
                    {
                        var now = live();
                        if (now != null) ModUiContext.Select(surface, now, pick);
                    });
                },
            };
            Tip(vt, description, n.Tooltip);
            return vt;
        }

        private string OptionText(ImguiNode c, int i)
        {
            string text = Clean(ImguiCapture.Plain(c.Options[i]));
            return text.Length > 0 ? text : Loc.T("modui.unlabelled");
        }

        private void Tip(NodeVtable vt, string description, string tooltip)
        {
            string tip = Clean(ImguiCapture.Plain(tooltip));
            string desc = string.IsNullOrEmpty(description) ? tip
                : (string.IsNullOrEmpty(tip) ? description : description + ". " + tip);
            if (string.IsNullOrEmpty(desc)) return;
            ((List<NodeAnnouncement>)vt.Announcements).Add(GraphNodes.TooltipPart(() => desc));
            vt.OnTooltip = () => Tts.Speak(desc);
        }

        // =====================================================================================
        // Hotkey: read and written through ModKit's binding table
        //
        // ModKit's own capture ("click Bind, then press a key") reads raw key events inside the mod
        // manager's window, which only arrive while that window has IMGUI keyboard focus — and would
        // reach our navigation as well. So the item never clicks the button: it reads the binding
        // from the table, captures a key with our own dialog, and stores it with the same call the
        // button makes (which also saves it).
        // =====================================================================================

        private sealed class BindApi
        {
            public FieldInfo Bindings;
            public MethodInfo Set, Clear;
            public ConstructorInfo NewBind;
            public FieldInfo Key, Ctrl, Alt, Cmd, Shift;
            public bool Ok => Bindings != null && Set != null && Clear != null && NewBind != null && Key != null;
        }

        private static readonly Dictionary<Type, BindApi> _bindApis = new Dictionary<Type, BindApi>();

        private static BindApi ApiFor(Type ui)
        {
            if (ui == null) return null;
            BindApi api;
            if (_bindApis.TryGetValue(ui, out api)) return api;
            api = new BindApi();
            try
            {
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var table = ui.GetNestedType("KeyBindings", BindingFlags.Public | BindingFlags.NonPublic);
                var bind = ui.GetNestedType("KeyBind", BindingFlags.Public | BindingFlags.NonPublic);
                if (table != null && bind != null)
                {
                    api.Bindings = table.GetField("bindings", any);
                    api.Set = table.GetMethod("SetBinding", any, null, new[] { typeof(string), bind }, null);
                    api.Clear = table.GetMethod("ClearBinding", any, null, new[] { typeof(string) }, null);
                    api.NewBind = bind.GetConstructor(any, null, new[]
                    {
                        typeof(string), typeof(KeyCode), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                    }, null);
                    api.Key = bind.GetField("Key", any);
                    api.Ctrl = bind.GetField("Ctrl", any);
                    api.Alt = bind.GetField("Alt", any);
                    api.Cmd = bind.GetField("Cmd", any);
                    api.Shift = bind.GetField("Shift", any);
                }
                if (!api.Ok) Main.Log?.Log("[modui] ModKit key bindings not found in " + ui.Assembly.GetName().Name + "; hotkeys are read-only buttons");
            }
            catch (Exception e) { Main.Log?.Error("[modui] ModKit key bindings: " + e.Message); }
            _bindApis[ui] = api;
            return api;
        }

        private static ControlId BindId(ModUiContext ctx, ImguiNode scope)
            => ctx.Id("bind:" + scope.Scope + "/" + scope.ScopeLabel);

        // The bound combination in our own words (ModKit's is drawn with arrow glyphs), or null.
        private static string BoundText(BindApi api, string identifier)
        {
            try
            {
                var table = api.Bindings.GetValue(null) as IDictionary;
                var bind = table != null && table.Contains(identifier) ? table[identifier] : null;
                if (bind == null) return null;
                var key = (KeyCode)api.Key.GetValue(bind);
                if (key == KeyCode.None) return null;
                bool ctrl = Flag(api.Ctrl, bind), shift = Flag(api.Shift, bind);
                bool alt = Flag(api.Alt, bind) || Flag(api.Cmd, bind);
                return new KeyboardBinding(key, ctrl, shift, alt).DisplayName;
            }
            catch { return null; }
        }

        private static bool Flag(FieldInfo f, object o) => f != null && (bool)f.GetValue(o);

        /// <param name="label">What the item is called ("Rest All hotkey", or the caption it stands under).</param>
        /// <param name="what">The action being bound, for the capture prompt.</param>
        private NodeVtable BindItem(ImguiNode scope, string label, string what, string description)
        {
            var info = scope.Data as BindInfo;
            var api = info != null ? ApiFor(info.Ui) : null;
            if (api == null || !api.Ok) return null;
            string identifier = info.Identifier;
            Func<string> value = () => BoundText(api, identifier) ?? Loc.T("value.not_bound");

            // ModKit lists clashes under the button ("conflicts", then the other actions).
            string conflicts = null;
            if (scope.Children != null)
            {
                var texts = Lists.Rent();
                try
                {
                    Texts(scope, texts);
                    for (int i = 0; i < texts.Count; i++)
                    {
                        string s = Clean(texts[i].Text);
                        if (s.Length > 0) conflicts = conflicts == null ? s : conflicts + ", " + s;
                    }
                }
                finally { Lists.Return(texts); }
            }

            var vt = new NodeVtable
            {
                ControlType = ControlTypes.KeyBinding,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                OnActivate = () =>
                {
                    UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                    Screens.ModUiKeyCaptureScreen.Open(what, (key, ctrl, shift, alt) =>
                    {
                        try
                        {
                            var bind = api.NewBind.Invoke(new object[] { identifier, key, ctrl, alt, false, shift, false });
                            api.Set.Invoke(null, new[] { identifier, bind });
                            return true;
                        }
                        catch (Exception e)
                        {
                            Main.Log?.Error("[modui] ModKit set binding: " + e);
                            return false;
                        }
                    });
                },
                OnSecondary = () =>
                {
                    try
                    {
                        UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                        api.Clear.Invoke(null, new object[] { identifier }); // announces itself (the live part)
                    }
                    catch (Exception e) { Main.Log?.Error("[modui] ModKit clear binding: " + e); }
                },
            };
            Tip(vt, description == null ? conflicts : (conflicts == null ? description : description + ". " + conflicts), null);
            return vt;
        }

        private static void Texts(ImguiNode n, List<ImguiNode> into)
        {
            if (n.Children == null)
            {
                if (!n.IsInteractive && n.HasText) into.Add(n);
                return;
            }
            for (int i = 0; i < n.Children.Count; i++) Texts(n.Children[i], into);
        }
    }
}
