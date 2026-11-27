using System;
using System.Collections.Generic;
using UnityEngine;

namespace WrathAccess.ModUI.Imgui
{
    /// <summary>What one recorded IMGUI call was. Containers first (they carry children), then leaves.</summary>
    internal enum ImguiKind
    {
        // ---- containers ----
        Root,        // a MonoBehaviour's OnGUI drawn outside any window
        Window,      // the body of a GUI.Window / GUILayout.Window
        Horizontal,  // GUILayout.BeginHorizontal .. EndHorizontal — a ROW
        Vertical,    // GUILayout.BeginVertical .. EndVertical
        Scroll,      // GUILayout.BeginScrollView .. EndScrollView
        Scope,       // a semantic bracket pushed by an adapter probe ("this is mod X's tab Y")

        // ---- leaves ----
        Label,       // GUI.Label
        Box,         // GUI.Box
        Drawn,       // text drawn straight through GUIStyle.Draw by a custom control nobody has a probe for
        Button,      // GUI.Button / RepeatButton
        Toggle,      // GUI.Toggle, or a custom check box / disclosure arrow reported by a probe
        TextField,   // GUI.TextField / TextArea / PasswordField
        Slider,      // GUI.HorizontalSlider / VerticalSlider
        Choice,      // GUI.Toolbar / SelectionGrid — pick one of N
    }

    /// <summary>
    /// One node of a captured IMGUI pass: a layout container or a control. IMGUI keeps no widget tree —
    /// a mod just calls "draw a button here" every frame — so this tree is rebuilt from scratch on every
    /// captured repaint and thrown away with the next one. NEVER hold a node across frames: hold its
    /// <see cref="Target"/> and re-find it in the current snapshot (<see cref="ImguiSurface.Find"/>).
    /// </summary>
    internal sealed class ImguiNode
    {
        public ImguiKind Kind;

        /// <summary>The control's own text with rich-text tags stripped ("" when it has none). This
        /// is what identity uses: mods recolour a label by state (ModKit greys an unticked option,
        /// oranges the selected tab), and a control must stay the same control when it changes colour.</summary>
        public string Text;
        /// <summary>The text exactly as drawn, tags and all — for an adapter that reads meaning from
        /// the markup (red = error).</summary>
        public string RawText;
        public string Tooltip;
        /// <summary>Name of the texture the control shows, when it has one (often the only clue to what
        /// an icon button does).</summary>
        public string Image;
        /// <summary>GUIStyle.name — "box", "button", or whatever the mod's skin calls it. Adapters use it
        /// to tell a heading label from a body label.</summary>
        public string Style;
        /// <summary>Screen-space rectangle (leaves; containers leave it empty).</summary>
        public Rect Rect;
        public bool Enabled = true;

        // Toggle
        public bool On;
        /// <summary>A toggle that expands/collapses a section rather than setting an option.</summary>
        public bool Disclosure;

        // TextField
        /// <summary>What the field currently holds (its <see cref="Text"/> stays empty: a field's
        /// content changes as you type, so it must not be part of the identity).</summary>
        public string Content;
        public bool Multiline;
        public bool Password;

        // Slider
        public float Value, Min, Max;

        // Choice
        public string[] Options;
        public bool[] OptionEnabled;
        public int Selected = -1;
        public int Columns;

        // Scope (containers pushed by adapter probes)
        public string ScopeKind, ScopeId, ScopeLabel;

        /// <summary>Probe payload — whatever the adapter that recorded this node wants to find again.</summary>
        public object Data;

        // ---- identity (see ImguiTarget) ----
        /// <summary>Id of the nearest enclosing <see cref="ImguiKind.Scope"/>, "" at surface level.</summary>
        public string Scope = "";
        /// <summary>For a control with no text of its own: the nearest preceding static text ("the
        /// slider after 'Width'").</summary>
        public string Anchor;
        /// <summary>Which one of the identical (kind, scope, text/anchor) controls this is, in draw order.</summary>
        public int Occurrence;

        public ImguiNode Parent;
        public List<ImguiNode> Children; // null for leaves

        public bool IsContainer => Kind <= ImguiKind.Scope;
        public bool IsInteractive => Kind >= ImguiKind.Button;
        public bool HasText => !string.IsNullOrEmpty(Text);

        public ImguiTarget Target => new ImguiTarget(Kind, Scope, Text, Anchor, Occurrence);

        private string _key;
        /// <summary>The identity as a string — for graph control ids. Built on demand (never per frame).</summary>
        public string Key => _key ?? (_key = Target.ToString());

        public override string ToString() => Kind + " " + Key;
    }

    /// <summary>
    /// The identity of a control ACROSS frames. IMGUI gives controls no stable id, so identity is what a
    /// person would say: "the third 'Add' button in ToyBox's options", "the slider after 'Width'". Stable
    /// against layout shifts elsewhere on the page; an adapter that needs better (a list whose rows
    /// reorder) brackets the rows in scopes with ids of its own.
    /// </summary>
    internal struct ImguiTarget : IEquatable<ImguiTarget>
    {
        public readonly ImguiKind Kind;
        public readonly string Scope;
        public readonly string Text;
        public readonly string Anchor;
        public readonly int Occurrence;

        public ImguiTarget(ImguiKind kind, string scope, string text, string anchor, int occurrence)
        {
            Kind = kind;
            Scope = scope ?? "";
            Text = text ?? "";
            Anchor = anchor ?? "";
            Occurrence = occurrence;
        }

        public bool Equals(ImguiTarget o)
            => Kind == o.Kind && Occurrence == o.Occurrence
               && string.Equals(Text, o.Text, StringComparison.Ordinal)
               && string.Equals(Anchor, o.Anchor, StringComparison.Ordinal)
               && string.Equals(Scope, o.Scope, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ImguiTarget t && Equals(t);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind * 397 + Occurrence;
                h = h * 31 + (Text != null ? Text.GetHashCode() : 0);
                h = h * 31 + (Anchor != null ? Anchor.GetHashCode() : 0);
                h = h * 31 + (Scope != null ? Scope.GetHashCode() : 0);
                return h;
            }
        }

        public override string ToString()
            => Kind + ":" + Scope + ":" + (string.IsNullOrEmpty(Text) ? "@" + Anchor : Text) + "#" + Occurrence;
    }

    /// <summary>
    /// One place IMGUI is drawn: a window (the mod manager's, a mod's own pop-up), or a MonoBehaviour's
    /// bare OnGUI. Persistent — the latest captured tree hangs off <see cref="Root"/>.
    /// </summary>
    internal sealed class ImguiSurface
    {
        /// <summary>"win:OwnerType:windowId" or "root:OwnerType" — stable across sessions.</summary>
        public string Key;
        /// <summary>Full type name of the MonoBehaviour whose OnGUI draws this.</summary>
        public string Owner;
        public int OwnerInstance;
        /// <summary>The GUI.Window id, or -1 for content outside any window.</summary>
        public int WindowId = -1;
        public string Title;

        /// <summary>The latest captured tree (null until the first capture).</summary>
        public ImguiNode Root;
        /// <summary>Frame the tree in <see cref="Root"/> was captured on.</summary>
        public int CapturedFrame = -1;
        /// <summary>Last frame this surface was drawn at all (cheap liveness, tracked on every frame).</summary>
        public int SeenFrame = -1;
        /// <summary>Bumped on every publish — "has anything been captured since I last looked".</summary>
        public int Version;

        public bool Alive => Time.frameCount - SeenFrame <= 3;

        private Dictionary<ImguiTarget, ImguiNode> _index;
        private ImguiNode _indexedRoot;

        /// <summary>Re-find a control in the CURRENT tree by identity; null when it is gone.</summary>
        public ImguiNode Find(ImguiTarget target)
        {
            if (Root == null) return null;
            if (!ReferenceEquals(_indexedRoot, Root))
            {
                if (_index == null) _index = new Dictionary<ImguiTarget, ImguiNode>();
                else _index.Clear();
                Index(Root);
                _indexedRoot = Root;
            }
            ImguiNode n;
            return _index.TryGetValue(target, out n) ? n : null;
        }

        private void Index(ImguiNode n)
        {
            if (n.Children == null)
            {
                _index[n.Target] = n;
                return;
            }
            for (int i = 0; i < n.Children.Count; i++) Index(n.Children[i]);
        }
    }
}
