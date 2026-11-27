using System.Collections.Generic;
using WrathAccess.ModUI.Imgui;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.ModUI.Mods
{
    /// <summary>
    /// ToyBox (Unity Mod Manager id <c>0ToyBox0</c>). Built entirely on ModKit, so sections, sliders,
    /// check boxes and hotkeys come from <see cref="ModKitAdapter"/>; what is ToyBox's own is the shape
    /// of its window:
    ///
    /// <code>
    ///   notes, "Maximize Window"         (only at some window widths / on the main menu)
    ///   a grid of ~17 tab buttons        (ModKit's TabBar: a SelectionGrid…)
    ///   a box holding the selected tab   (…followed by a "box" vertical)
    /// </code>
    ///
    /// read here as three Tab stops: the notes stay in the mod manager's options stop, then the tabs
    /// as a plain list, then the selected tab's page (its sections collapsed). Focus lands on the tab
    /// list when ToyBox is expanded: it says which page is showing, and Tab goes into it.
    /// </summary>
    internal sealed class ToyBoxAdapter : ModKitAdapter
    {
        public const string StopTabs = "toybox:tabs";
        public const string StopPage = "toybox:page";

        public override string Id => "0ToyBox0";

        private object _landing;
        public override object LandingStop => _landing;

        public override void Build(ModUiContext ctx, IList<ImguiNode> nodes, int from, int to)
        {
            _landing = null;
            IdPrefix = "";
            Given.Clear();
            if (to > nodes.Count) to = nodes.Count;
            // The tab bar is the first selection grid drawn directly in ToyBox's area. Its page box
            // follows — unless the page drew nothing (several do on the main menu), in which case
            // the capture has already dropped the empty box, so the box cannot be what identifies it.
            int tab = -1;
            for (int i = from; i < to; i++)
            {
                var n = nodes[i];
                if (n.Kind == ImguiKind.Choice && n.Options != null && n.Options.Length > 1) { tab = i; break; }
            }
            if (tab < 0)
            {
                // Not the tabbed window (the first-run consent page, an error page): read it as it comes.
                base.Build(ctx, nodes, from, to);
                return;
            }

            var b = ctx.B;
            var surface = ctx.Surface;
            var tabs = nodes[tab];

            // Whatever ToyBox says above its tabs stays in the stop the host opened.
            base.Build(ctx, nodes, from, tab);

            b.BeginStop(StopTabs);
            b.PushContext(Loc.T("modui.tabs"), Loc.T("role.list"));
            var live = ModUiContext.Live(surface, tabs);
            int count = tabs.Options.Length;
            for (int i = 0; i < count; i++)
            {
                var id = ctx.Id(tabs, "o" + i);
                if (id == null) continue;
                int index = i;
                string name = TabName(tabs, i);
                b.AddItem(id, GraphNodes.Tab(() => name,
                    () => { var c = live(); return c != null && c.Selected == index; },
                    () => ModUiContext.Select(surface, tabs, index)));
            }
            b.PopContext();

            b.BeginStop(StopPage);
            string page = tabs.Selected >= 0 && tabs.Selected < count ? TabName(tabs, tabs.Selected) : "";
            IdPrefix = page; // sections keep their expanded state per page
            NamePickers(nodes, tab + 1, to, tabs.Selected >= 0 && tabs.Selected < count ? tabs.Options[tabs.Selected] : "");
            if (page.Length > 0) b.PushContext(page);
            try
            {
                if (tab + 1 < to) base.Build(ctx, nodes, tab + 1, to);
                else
                {
                    var id = ctx.Id("toybox:empty");
                    if (id != null) b.AddItem(id, GraphNodes.Text(() => Loc.T("modui.toybox_empty_page")));
                }
            }
            finally
            {
                ctx.Recover();
                if (page.Length > 0) b.PopContext();
                IdPrefix = "";
            }
            _landing = StopTabs;
        }

        // ToyBox writes prices with a coin glyph: "0⊙" is "costs nothing", "1k⊙ - 2k⊙" a
        // price band (its item categories include one per band). A screen reader says nothing for
        // the glyph, or "circled dot".
        protected override string Clean(string text)
        {
            const char coin = '⊙';
            int i = string.IsNullOrEmpty(text) ? -1 : text.IndexOf(coin);
            if (i < 0) return base.Clean(text);
            string low = text.Substring(0, i).Trim(), rest = text.Substring(i + 1);
            if (rest.StartsWith(" - ", System.StringComparison.Ordinal))
            {
                int j = rest.IndexOf(coin);
                if (j > 3)
                    return base.Clean(Loc.T("modui.toybox_price_range", new { low, high = rest.Substring(3, j - 3).Trim() }) + rest.Substring(j + 1));
            }
            return base.Clean(Loc.T("modui.toybox_price", new { amount = low }) + rest);
        }

        // A few pages open with selection grids that have no caption at all. Which is which is only
        // known from the page (matched on ToyBox's own English tab names; in another language the
        // grids simply stay nameless, announced by their pick).
        private void NamePickers(IList<ImguiNode> nodes, int from, int to, string rawTab)
        {
            string tab = ImguiCapture.Plain(rawTab);
            string[] keys;
            if (tab == "Search 'n Pick") keys = new[] { "modui.toybox_type", "modui.toybox_category", "modui.toybox_character" };
            else if (tab == "Party") keys = new[] { "modui.toybox_party_filter" };
            else if (tab == "Enchantment") keys = new[] { "modui.toybox_item_type", "modui.toybox_ench_type", "modui.toybox_item" };
            else return;
            int found = 0;
            for (int i = from; i < to && found < keys.Length; i++) NameChoices(nodes[i], keys, ref found);
        }

        private void NameChoices(ImguiNode n, string[] keys, ref int found)
        {
            if (found >= keys.Length) return;
            if (n.Children == null)
            {
                if (n.Kind == ImguiKind.Choice) Given[n] = Loc.T(keys[found++]);
                else if (n.Kind == ImguiKind.TextField && n.Anchor == "Categories") Given[n] = Loc.T("modui.toybox_category_search");
                return;
            }
            for (int i = 0; i < n.Children.Count; i++) NameChoices(n.Children[i], keys, ref found);
        }

        // A text box ToyBox draws at the start of a line without a caption is a search or filter box.
        protected override string Unnamed(ImguiNode t, bool firstOnLine)
            => t.Kind == ImguiKind.TextField && firstOnLine ? Loc.T("modui.search") : base.Unnamed(t, firstOnLine);

        private string TabName(ImguiNode tabs, int i)
        {
            string s = Clean(ImguiCapture.Plain(tabs.Options[i]));
            return s.Length > 0 ? s : Loc.T("modui.unlabelled");
        }
    }
}
