using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Kingmaker; // Game
using Kingmaker.Blueprints.Credits; // BlueprintCreditsGroup
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.UI.ServiceWindow.Credits; // CreditsUIWindow
using TMPro;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Accessible mirror of the game's credits window (<see cref="CreditsUIWindow"/>, a legacy UIWindow —
    /// no VM; opened from the main menu's Credits entry). The window is a paged book: a tab per credits
    /// group (icon-only on screen; we speak the group's header), a left page (header + message), a right
    /// page of rows built from <c>m_Pages[m_CurrentPage]</c> exactly as <c>OneColum.Append</c> reads them
    /// (header / person + role / text), previous / next page, a play/pause toggle for the auto-advance,
    /// and the search box. Every action drives the window's own handlers (SelectCreditsGroup = the tab
    /// click's OnSelect, OnPrevPage / OnNextPage, OnPlayerSwitch, SearchPerson via OnFind, OnButtonClose).
    /// The auto-advance (a page every 3 s) is paused on open so pages don't change mid-read; the toggle
    /// resumes it. While auto-advancing, m_CurrentPage runs one ahead of the screen during each 3 s wait,
    /// so the DISPLAYED page is read off the window's own "N / M" label. Layout: Tab-stops sections,
    /// page, controls, close.
    /// </summary>
    public sealed class CreditsScreen : Screen
    {
        public CreditsScreen() { Wrap = true; }

        public override string Key => "ctx.credits";
        public override string ScreenName => UIStrings.Instance?.MainMenu?.Credits ?? Loc.T("screen.credits");
        public override int Layer => 30; // over the main menu (and the ending's context)
        public override bool Exclusive => true;

        public override bool IsActive() => Window()?.IsShow ?? false;

        private static CreditsUIWindow Window() => Game.Instance?.UI?.CreditsUI;

        // The window's page state lives in protected fields (no public accessors).
        private static readonly FieldInfo F_Group = typeof(CreditsUIWindow).GetField("m_CurrentGroup", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo F_Pages = typeof(CreditsUIWindow).GetField("m_Pages", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo F_Page = typeof(CreditsUIWindow).GetField("m_CurrentPage", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo F_Search = typeof(CreditsUIWindow).GetField("m_SearchField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo F_PageLabel = typeof(CreditsUIWindow).GetField("m_PageLabel", BindingFlags.Instance | BindingFlags.NonPublic);

        private static BlueprintCreditsGroup CurrentGroup(CreditsUIWindow w) => F_Group?.GetValue(w) as BlueprintCreditsGroup;
        private static List<string> Pages(CreditsUIWindow w) => F_Pages?.GetValue(w) as List<string>;

        // The page on screen: the window's "N / M" label (set with the page it shows), else m_CurrentPage.
        private static int CurrentPage(CreditsUIWindow w)
        {
            var label = (F_PageLabel?.GetValue(w) as TextMeshProUGUI)?.text;
            int slash = label != null ? label.IndexOf('/') : -1;
            if (slash > 0 && int.TryParse(label.Substring(0, slash).Trim(), out var n)) return n - 1;
            return F_Page != null ? (int)F_Page.GetValue(w) : 0;
        }
        private static TMP_InputField SearchField(CreditsUIWindow w) => F_Search?.GetValue(w) as TMP_InputField;

        // Pause and restart the window's page routine on the page it shows, already paused — the way its
        // own tab click (SelectCreditsGroup) restarts it. A bare pause would still flip once: the routine
        // only checks the pause after its current 3 s wait.
        public override void OnPush()
        {
            var w = Window();
            var g = w != null ? CurrentGroup(w) : null;
            if (w == null) return;
            w.Pause.Value = true;
            if (g == null) return;
            int page = CurrentPage(w);
            w.StopAllCoroutines();
            w.StartCoroutine(w.DoCreditsGroup(g, page, -1));
        }

        // A group's spoken name: its header (what the tab's icon stands for); the closing group has
        // none, so its left-page text.
        private static string GroupName(BlueprintCreditsGroup g)
        {
            var h = TextUtil.StripRichText((string)g.HeaderText);
            return string.IsNullOrEmpty(h) ? TextUtil.StripRichText((string)g.LeftPageText) : h;
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            var group = CurrentGroup(w);

            // ---- sections: the bookmark tabs ----
            b.BeginStop("sections").PushContext(Loc.T("credits.sections"), "list");
            int gi = 0;
            foreach (var r in w.Groups)
            {
                var g = r?.Get();
                if (g == null) continue;
                b.AddItem(ControlId.Structural("credits:group:" + gi++), GraphNodes.Tab(
                    () => GroupName(g), () => CurrentGroup(Window()) == g,
                    () => Window()?.SelectCreditsGroup(g)));
            }
            b.PopContext();

            // ---- the page: left-page message, then the right page's rows ----
            if (group != null)
            {
                b.BeginStop("page").PushContext(GroupName(group), "list");
                var header = TextUtil.StripRichText((string)group.HeaderText);
                var message = TextUtil.StripRichText((string)group.LeftPageText);
                int ri = 0;
                if (!string.IsNullOrEmpty(message) && message != header)
                {
                    var m = message;
                    b.AddItem(ControlId.Structural("credits:row:" + ri++), GraphNodes.Text(() => m));
                }
                foreach (var row in PageRows(group, Pages(w), CurrentPage(w)))
                {
                    var t = row;
                    b.AddItem(ControlId.Structural("credits:row:" + ri++), GraphNodes.Text(() => t));
                }
                b.PopContext();
            }

            // ---- page controls: previous, the page label, next, auto-play, search ----
            b.BeginStop("controls");
            b.AddItem(ControlId.Structural("credits:prev"), GraphNodes.Button(
                () => Loc.T("credits.prev_page"), () => TurnPage(-1), () => CurrentPage(Window()) != 0));
            b.AddItem(ControlId.Structural("credits:pagelabel"), GraphNodes.Text(() => PageLabel(Window())));
            b.AddItem(ControlId.Structural("credits:next"), GraphNodes.Button(
                () => Loc.T("credits.next_page"), () => TurnPage(1),
                () => { var x = Window(); var p = x != null ? Pages(x) : null; return p != null && CurrentPage(x) != p.Count - 1; }));
            b.AddItem(ControlId.Structural("credits:play"), GraphNodes.Toggle(
                () => Loc.T("credits.autoplay"), () => !(Window()?.Pause.Value ?? true), () => Window()?.OnPlayerSwitch()));
            b.AddItem(ControlId.Structural("credits:search"), GraphNodes.Button(
                () => SearchLabel(Window()), () => OpenSearch()));

            b.BeginStop("close").AddItem(ControlId.Structural("credits:close"),
                GraphNodes.Button(() => Loc.T("action.close"), () => Window()?.OnButtonClose()));
        }

        // The right page's rows, read the way OneColum.Append reads each line: a header, a person (with the
        // group's role lookup), a text block — a line may carry more than one.
        private static IEnumerable<string> PageRows(BlueprintCreditsGroup group, List<string> pages, int page)
        {
            if (pages == null || page < 0 || page >= pages.Count) yield break;
            using (var reader = new StringReader(pages[page]))
            {
                string line;
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    var header = TextUtil.StripRichText(PageGenerator.ReadHeader(line));
                    if (!string.IsNullOrEmpty(header)) yield return header;
                    var person = TextUtil.StripRichText(PageGenerator.ReadPerson(line));
                    if (!string.IsNullOrEmpty(person))
                    {
                        var role = TextUtil.StripRichText(group.RolesData?.GetRole(PageGenerator.ReadRole(line)));
                        yield return string.IsNullOrEmpty(role) ? person : person + ", " + role;
                    }
                    var text = TextUtil.StripRichText(PageGenerator.ReadText(line));
                    if (!string.IsNullOrEmpty(text)) yield return text;
                }
            }
        }

        private static string PageLabel(CreditsUIWindow w)
        {
            var pages = w != null ? Pages(w) : null;
            if (pages == null || pages.Count == 0) return "";
            return Loc.T("credits.page", new { page = CurrentPage(w) + 1, count = pages.Count });
        }

        // The window's own buttons (paused-aware DoPage), then speak where we landed: focus stays on the
        // button, so the new page is otherwise silent.
        private static void TurnPage(int direction)
        {
            var w = Window();
            if (w == null) return;
            if (direction < 0) w.OnPrevPage(); else w.OnNextPage();
            Tts.Speak(PageLabel(w), interrupt: true);
        }

        private static string SearchLabel(CreditsUIWindow w)
        {
            var label = TextUtil.StripRichText((string)UIStrings.Instance?.Credits?.InputfieldSearchText);
            if (string.IsNullOrEmpty(label)) label = Loc.T("credits.search");
            var current = w != null ? SearchField(w)?.text : null;
            return string.IsNullOrEmpty(current) ? label : label + ": " + current;
        }

        // The search box: type a name, then the window's own search (OnFind reads the field). Repeating
        // the same name steps to the next match, as in the game.
        private static void OpenSearch()
        {
            var w = Window();
            var field = w != null ? SearchField(w) : null;
            if (field == null) return;
            ModTextEntryScreen.Open(SearchLabel(null), field.text ?? "", s =>
            {
                var x = Window();
                var f = x != null ? SearchField(x) : null;
                if (f == null || string.IsNullOrEmpty(s)) return;
                f.text = s;
                x.OnFind();
                Tts.Speak(PageLabel(x), interrupt: true);
            });
        }

        // Escape = the window's close (its own Esc hotkey path).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Window()?.OnButtonClose());
        }
    }
}
