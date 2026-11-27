using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using WrathAccess.ModUI.Imgui;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.ModUI.Hosts
{
    /// <summary>
    /// Unity Mod Manager's window (Ctrl+F10) — where almost every mod's settings live.
    ///
    /// UMM is an optional third-party dependency, so nothing here references it at compile time:
    /// every member is found by reflection once and the host reports not <see cref="Present"/> when
    /// UMM isn't installed. Members used (UnityModManagerNet.UnityModManager, v0.32):
    /// <c>UI.Instance</c>, <c>UI.Opened</c>, <c>UI.ToggleWindow(bool)</c>, <c>UI.tabId</c>,
    /// <c>modEntries</c>, <c>ModEntry.Info/Enabled/Active/ErrorOnLoading</c>, <c>Param.EscapeHotkey</c>.
    /// </summary>
    internal sealed class UmmHost : ModUiHost
    {
        public const string OwnerType = "UnityModManagerNet.UnityModManager+UI";

        public override string Id => "umm";
        public override string Title => Loc.T("modui.umm_title");

        private bool _resolved;
        private Type _umm, _ui;
        private PropertyInfo _instance, _opened;
        private MethodInfo _toggle;
        private FieldInfo _modEntries, _escapeHotkey;
        private object _savedEscape;
        private bool _escapeSwapped;

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "UnityModManager") continue;
                    _umm = asm.GetType("UnityModManagerNet.UnityModManager", false);
                    break;
                }
                if (_umm == null) return;
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                _ui = _umm.GetNestedType("UI", BindingFlags.Public | BindingFlags.NonPublic);
                _instance = _ui?.GetProperty("Instance", any);
                _opened = _ui?.GetProperty("Opened", any);
                _toggle = _ui?.GetMethod("ToggleWindow", any, null, new[] { typeof(bool) }, null);
                _modEntries = _umm.GetField("modEntries", any);
                var param = _umm.GetNestedType("Param", BindingFlags.Public | BindingFlags.NonPublic);
                _escapeHotkey = param?.GetField("EscapeHotkey", any);
                if (_instance == null || _opened == null || _toggle == null)
                {
                    Main.Log?.Error("[modui] Unity Mod Manager found but its UI members were not (version mismatch?)");
                    _umm = null;
                }
            }
            catch (Exception e)
            {
                Main.Log?.Error("[modui] UMM resolve: " + e.Message);
                _umm = null;
            }
        }

        public override bool Present { get { Resolve(); return _umm != null; } }

        private object Ui()
        {
            Resolve();
            if (_umm == null) return null;
            var ui = _instance.GetValue(null, null);
            // A destroyed MonoBehaviour is "null" only through Unity's overloaded ==.
            return ui as UnityEngine.Object != null ? ui : null;
        }

        public override bool IsOpen
        {
            get
            {
                var ui = Ui();
                return ui != null && (bool)_opened.GetValue(ui, null);
            }
        }

        public override void Open()
        {
            var ui = Ui();
            if (ui != null) _toggle.Invoke(ui, new object[] { true });
        }

        public override void Close()
        {
            var ui = Ui();
            if (ui != null) _toggle.Invoke(ui, new object[] { false });
        }

        public override bool Owns(ImguiSurface surface)
            => surface.WindowId >= 0 && surface.Owner == OwnerType;

        // UMM closes its window on Escape by itself. While our screen owns the keyboard, Escape is
        // also "back out of the text box / option list / tooltip I opened" — UMM must not yank the
        // window from under those, so its Escape binding is parked for as long as we are attached
        // (our own Back closes the window).
        public override void OnAttach()
        {
            if (_escapeHotkey == null || _escapeSwapped) return;
            try
            {
                _savedEscape = _escapeHotkey.GetValue(null);
                _escapeHotkey.SetValue(null, Activator.CreateInstance(_escapeHotkey.FieldType));
                _escapeSwapped = true;
            }
            catch (Exception e) { Main.Log?.Error("[modui] UMM escape park: " + e.Message); }
        }

        public override void OnDetach()
        {
            if (!_escapeSwapped) return;
            _escapeSwapped = false;
            try { _escapeHotkey.SetValue(null, _savedEscape); }
            catch (Exception e) { Main.Log?.Error("[modui] UMM escape restore: " + e.Message); }
            _savedEscape = null;
        }

        /// <summary>UMM's mod entries (opaque objects; read with <see cref="ModField"/>).</summary>
        public IList ModEntries()
        {
            Resolve();
            return _modEntries != null ? _modEntries.GetValue(null) as IList : null;
        }

        private static readonly Dictionary<string, MemberInfo> _members = new Dictionary<string, MemberInfo>();

        /// <summary>Read a field or property of a UMM object by name (cached lookup).</summary>
        public static object Member(object obj, string name)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            string key = type.FullName + "." + name;
            MemberInfo m;
            if (!_members.TryGetValue(key, out m))
            {
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                m = (MemberInfo)type.GetField(name, any) ?? type.GetProperty(name, any);
                _members[key] = m;
            }
            try
            {
                if (m is FieldInfo f) return f.GetValue(obj);
                if (m is PropertyInfo p) return p.GetValue(obj, null);
            }
            catch { /* a throwing getter reads as "unknown" */ }
            return null;
        }

        // =====================================================================================
        // Layout. The window, as UMM draws it (v0.32):
        //   Label        "Mod Manager x.y"                    (style "umm h1")
        //   Horizontal   [Mods|Logs|Settings] toolbar, "Filter:", text field, "X"
        //   Scroll       the selected tab's content
        //   Horizontal   icon legend (Mods tab only)
        //   Horizontal   Close, Save (+ Clear, Open detailed log on the Logs tab)
        // and on the Mods tab the scroll holds one "box" per mod: a header row (name button, icon
        // buttons, version, requirements, enable toggle, status icon) followed — for the one expanded
        // mod — by an "Options" heading (style "umm h2") and whatever that mod's OnGUI draws.
        //
        // The shell is laid out by hand as Tab stops; each mod's own content goes to that mod's
        // adapter. Anything that doesn't match the expected shape falls back to the generic reader,
        // so a UMM update degrades to "readable but flat" rather than to nothing.
        // =====================================================================================

        public const string StopTabs = "umm:tabs";
        public const string StopContent = "umm:content";
        public const string StopOptions = "umm:options";
        public const string StopWindow = "umm:window";

        public override object InitialStop => StopContent;

        private readonly Shell _shell = new Shell();
        private string _focusOptionsOf; // mod just expanded from the list: land in its options when they appear

        public override void Build(ModUiContext ctx, List<ImguiSurface> surfaces)
        {
            ImguiSurface main = null;
            for (int i = 0; i < surfaces.Count; i++)
                if (surfaces[i].WindowId == 0) { main = surfaces[i]; break; }
            ImguiNode tabs = null, tabRow = null, scroll = null, buttons = null;
            if (main != null && main.Root != null)
            {
                var kids = main.Root.Children;
                for (int i = 0; i < kids.Count; i++)
                {
                    var c = kids[i];
                    if (c.Kind == ImguiKind.Scroll) { if (scroll == null) scroll = c; }
                    else if (c.Kind == ImguiKind.Horizontal)
                    {
                        var choice = tabs == null ? FirstLeaf(c, ImguiKind.Choice) : null;
                        if (choice != null) { tabs = choice; tabRow = c; }
                        else if (scroll != null) buttons = c; // the LAST row under the content (the legend precedes it)
                    }
                }
            }
            if (tabs == null || tabs.Options == null)
            {
                base.Build(ctx, surfaces); // not the window we know: read it flat
                return;
            }

            ctx.Surface = main;
            var b = ctx.B;

            // ---- stop 1: the tab bar ----
            b.BeginStop(StopTabs);
            {
                var live = ModUiContext.Live(main, tabs);
                ctx.StartRow();
                for (int i = 0; i < tabs.Options.Length; i++)
                {
                    int index = i;
                    string name = tabs.Options[i];
                    var id = ctx.Id(tabs, "o" + i);
                    if (id == null) continue;
                    b.AddItem(id, GraphNodes.Tab(() => name,
                        () => { var c = live(); return c != null && c.Selected == index; },
                        () => ModUiContext.Select(main, tabs, index)));
                }
                ctx.EndRow();
            }

            // ---- stop 2: the tab's content ----
            bool modsTab = tabs.Selected == 0;
            ImguiNode expandedBox = null;
            string expandedId = null, expandedName = null;
            b.BeginStop(StopContent);
            if (scroll != null)
            {
                if (!modsTab) _shell.Build(ctx, scroll);
                else
                {
                    b.PushContext(Loc.T("modui.umm_mods"), Loc.T("role.list"));
                    var list = scroll.Children.Count > 0 ? scroll.Children[0] : null;
                    int rows = 0;
                    for (int i = 0; list != null && list.Children != null && i < list.Children.Count; i++)
                    {
                        var box = list.Children[i];
                        // Mod rows are the vertical boxes; the horizontal one is the column header.
                        if (box.Kind != ImguiKind.Vertical || box.Children == null || box.Children.Count == 0) continue;
                        string modId, modName;
                        if (!ModRow(ctx, main, box, out modId, out modName)) continue;
                        rows++;
                        if (box.Children.Count > 1) { expandedBox = box; expandedId = modId; expandedName = modName; }
                    }
                    if (rows == 0) // no mods installed, or the filter matches none
                    {
                        var id = ctx.Id("umm:nomods");
                        if (id != null) b.AddItem(id, GraphNodes.Text(() => Loc.T("modui.umm_no_mods")));
                    }
                    b.PopContext();
                }
            }

            // ---- stop 3: the expanded mod's own UI, presented by that mod's adapter ----
            if (expandedBox != null)
            {
                b.BeginStop(StopOptions);
                b.PushContext(Loc.T("modui.umm_options", new { mod = expandedName }));
                int start = 1;
                for (int i = 1; i < expandedBox.Children.Count; i++)
                {
                    var c = expandedBox.Children[i];
                    if (c.Kind == ImguiKind.Label && c.Style == "umm h2" && c.Text == "Options") { start = i + 1; break; }
                }
                // Anything UMM itself put above the "Options" heading (the Debug / Reload block).
                for (int i = 1; i < start - 1; i++)
                {
                    var c = expandedBox.Children[i];
                    if (c.Kind == ImguiKind.Label && c.Style == "umm h2") continue;
                    _shell.Build(ctx, expandedBox.Children, i, i + 1);
                }
                ModAdapters.ForMod(expandedId).Build(ctx, expandedBox.Children, start, expandedBox.Children.Count);
                b.PopContext();
                if (_focusOptionsOf != null && _focusOptionsOf == expandedName)
                {
                    _focusOptionsOf = null;
                    WrathAccess.UI.Navigation.FocusStop(StopOptions);
                }
            }

            // ---- stop 4: the window's own controls ----
            b.BeginStop(StopWindow);
            if (modsTab && tabRow != null) _shell.BuildNode(ctx, tabRow); // the filter (the toolbar itself is hidden there)
            if (buttons != null) _shell.BuildNode(ctx, buttons);

            // ---- UMM's pop-up windows (drop-down lists and the like), one stop each ----
            object firstPopup = null;
            for (int i = 0; i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                if (ReferenceEquals(s, main) || s.Root == null) continue;
                ctx.Surface = s;
                object key = "umm:popup:" + s.WindowId;
                if (firstPopup == null) firstPopup = key;
                b.BeginStop(key);
                b.PushContext(Loc.T("modui.popup"));
                _popup.Build(ctx, s.Root);
                b.PopContext();
            }

            // A pop-up is modal in effect: follow focus into it when it appears, and back to where
            // it was opened from when it goes.
            if (firstPopup != null && !_popupOpen)
            {
                _popupOpen = true;
                _popupReturn = WrathAccess.UI.Navigation.FocusedStopKey;
                WrathAccess.UI.Navigation.FocusStop(firstPopup);
            }
            else if (firstPopup == null && _popupOpen)
            {
                _popupOpen = false;
                WrathAccess.UI.Navigation.FocusStop(_popupReturn ?? StopContent);
                _popupReturn = null;
            }
        }

        private readonly Popup _popup = new Popup();
        private bool _popupOpen;
        private object _popupReturn;

        /// <summary>Escape: close an open pop-up first; only then the window.</summary>
        public override void Back()
        {
            if (ClosePopups()) return;
            Close();
        }

        // UI.mPopupList holds every pop-up ever created; the open ones have Opened == true.
        private bool ClosePopups()
        {
            bool any = false;
            try
            {
                const BindingFlags any0 = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var list = _ui?.GetField("mPopupList", any0)?.GetValue(null) as IList;
                if (list == null) return false;
                foreach (var p in list)
                {
                    if (p == null) continue;
                    var opened = p.GetType().GetProperty("Opened", any0);
                    if (opened == null || !(bool)opened.GetValue(p, null)) continue;
                    opened.SetValue(p, false, null);
                    any = true;
                }
            }
            catch (Exception e) { Main.Log?.Error("[modui] UMM popup close: " + e.Message); }
            return any;
        }

        // One mod's header row → [name / expander] [enabled toggle] [home page].
        private bool ModRow(ModUiContext ctx, ImguiSurface surface, ImguiNode box, out string modId, out string modName)
        {
            modId = null;
            modName = null;
            var header = box.Children[0];
            if (header.Kind != ImguiKind.Horizontal) return false;
            _line.Clear();
            Flatten(header, _line);
            ImguiNode name = null, toggle = null, www = null, version = null, requires = null;
            int imageButtons = 0;
            for (int i = 0; i < _line.Count; i++)
            {
                var n = _line[i];
                switch (n.Kind)
                {
                    case ImguiKind.Button:
                        if (n.HasText) { if (name == null) name = n; }
                        else { imageButtons++; www = n; } // the home-page icon is the last icon button
                        break;
                    case ImguiKind.Label:
                        if (!n.HasText) break;
                        if (name == null) name = n;
                        else if (version == null) version = n;
                        else if (requires == null) requires = n;
                        break;
                    case ImguiKind.Toggle:
                        if (toggle == null) toggle = n;
                        break;
                }
            }
            if (name == null) return false;
            modName = name.Text;
            bool expandable = name.Kind == ImguiKind.Button;
            // With options, the first icon button is the gear (same as clicking the name); a second
            // one is the home page. Without options the only icon button is the home page.
            if (expandable && imageButtons < 2) www = null;

            object entry = FindEntry(modName);
            modId = Member(Member(entry, "Info"), "Id") as string ?? modName;

            string details = version != null ? Loc.T("modui.umm_version", new { version = version.Text }) : null;
            string status = Status(entry);
            if (status != null) details = details == null ? status : details + ", " + status;
            if (requires != null && requires.Text != "-")
                details += ", " + Loc.T("modui.umm_requires", new { mods = requires.Text });

            var b = ctx.B;
            string name0 = modName, details0 = details;
            ctx.StartRow("umm:modrow");
            var nameId = ctx.Id(name);
            if (nameId != null)
            {
                if (!expandable)
                    b.AddItem(nameId, GraphNodes.Text(() => details0 == null ? name0 : name0 + ", " + details0));
                else
                {
                    var live = ModUiContext.Live(surface, name);
                    // Expanded = the mod's box holds more than its header row.
                    Func<bool> expanded = () =>
                    {
                        var n = live();
                        var bx = n != null ? BoxOf(n) : null;
                        return bx != null && bx.Children.Count > 1;
                    };
                    b.AddItem(nameId, new NodeVtable
                    {
                        ControlType = ControlTypes.Group,
                        Announcements = new List<NodeAnnouncement>
                        {
                            GraphNodes.LabelPart(() => name0),
                            new NodeAnnouncement(() => Loc.T(expanded() ? "value.expanded" : "value.collapsed"),
                                live: true, kind: AnnouncementKinds.Value),
                            GraphNodes.TooltipPart(() => details0),
                        },
                        SearchText = () => name0,
                        OnActivate = () =>
                        {
                            UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                            _focusOptionsOf = expanded() ? null : name0; // opening: follow into the options
                            ModUiContext.Click(surface, name);
                        },
                    });
                }
            }
            if (toggle != null)
            {
                var id = ctx.Id(toggle);
                if (id != null) b.AddItem(id, _shell.Make(ctx, toggle, Loc.T("modui.umm_enabled"), null));
            }
            if (www != null)
            {
                var id = ctx.Id(www);
                if (id != null) b.AddItem(id, _shell.Make(ctx, www, Loc.T("modui.umm_homepage"), null));
            }
            ctx.EndRow();
            return true;
        }

        private static ImguiNode BoxOf(ImguiNode headerLeaf)
        {
            // name → its inner horizontal → the header row → the mod's box
            var n = headerLeaf;
            while (n != null && !(n.Kind == ImguiKind.Vertical && n.Style == "box")) n = n.Parent;
            return n;
        }

        private object FindEntry(string displayName)
        {
            var entries = ModEntries();
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (Member(Member(e, "Info"), "DisplayName") as string == displayName) return e;
            }
            return null;
        }

        // What the status icon shows, as words (UMM's own legend: Active / Inactive / Need restart / Errors).
        private static string Status(object entry)
        {
            if (entry == null) return null;
            bool active = Member(entry, "Active") as bool? ?? false;
            bool enabled = Member(entry, "Enabled") as bool? ?? false;
            string s = active == enabled
                ? Loc.T(active ? "modui.umm_active" : "modui.umm_inactive")
                : Loc.T("modui.umm_restart");
            if (Member(entry, "ErrorOnLoading") as bool? ?? false) s += ", " + Loc.T("modui.umm_errors");
            if (Member(entry, "NewestVersion") != null) s += ", " + Loc.T("modui.umm_update");
            return s;
        }

        private readonly List<ImguiNode> _line = new List<ImguiNode>();

        private static void Flatten(ImguiNode n, List<ImguiNode> into)
        {
            if (n.Children == null) { into.Add(n); return; }
            for (int i = 0; i < n.Children.Count; i++) Flatten(n.Children[i], into);
        }

        private static ImguiNode FirstLeaf(ImguiNode n, ImguiKind kind)
        {
            if (n.Children == null) return n.Kind == kind ? n : null;
            for (int i = 0; i < n.Children.Count; i++)
            {
                var f = FirstLeaf(n.Children[i], kind);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>A UMM drop-down list: a window of plain buttons, the current pick drawn bold.
        /// Read as radio options so the pick is spoken and focus lands on it.</summary>
        private sealed class Popup : ImguiAdapter
        {
            protected override NodeVtable Control(ModUiContext ctx, ImguiNode n, string label, string description)
            {
                if (n.Kind != ImguiKind.Button) return base.Control(ctx, n, label, description);
                var surface = ctx.Surface;
                var live = ModUiContext.Live(surface, n);
                return GraphNodes.ChoiceOption(() => label,
                    () => { var c = live(); return c != null && c.RawText != null && c.RawText.StartsWith("<b>"); },
                    () => ModUiContext.Click(surface, n));
            }

            protected override bool IsHeading(ImguiNode n) => n.Style == "umm h1";
        }

        /// <summary>The generic reader with UMM's own shell quirks ironed out.</summary>
        private sealed class Shell : ImguiAdapter
        {
            // The tab toolbar has its own stop; don't repeat it beside the filter.
            protected override bool Hide(ImguiNode n) => n.Kind == ImguiKind.Choice && n.Style == "umm button";

            protected override string Clean(string text)
            {
                if (text == "Filter:") return Loc.T("modui.umm_filter");
                if (text == "X") return Loc.T("modui.umm_clear_filter");
                return base.Clean(text);
            }

            protected override bool IsHeading(ImguiNode n) => n.Style == "umm h1" || n.Style == "umm h2" || n.Style == "umm bold";

            public NodeVtable Make(ModUiContext ctx, ImguiNode n, string label, string description)
                => Control(ctx, n, label, description);
        }
    }
}
