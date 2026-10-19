using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kingmaker;
using Kingmaker.UI.MVVM;
using Kingmaker.UI.MVVM._VM.ContextMenu;
using Kingmaker.UI.MVVM._VM.MainMenu;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The main menu, our first navigable screen: the sidebar entries (Continue / New Game / Load / …)
    /// from MainMenuSideBarVM as one arrow-navigable list, each confirming to run its entry's command —
    /// letting a blind player start/load a game with our own nav and unlock the downstream screens.
    ///
    /// Graph-native: declared fresh from the live sidebar VM every render; entry identity rides the entry
    /// VMs. The "Main Menu" list context reproduces the old labeled-container announcement on entry.
    ///
    /// Second Tab-stop: the text block under the sidebar (MainMenuView.m_MotivationText — Owlcat's
    /// introductory / advertisement text, from MainMenuVM.GetIntroductoryText) with each of its links as
    /// a button, then the version line (m_VersionText = GameVersion.GetVersion()).
    /// </summary>
    public sealed class MainMenuScreen : Screen
    {
        public override string Key => "ctx.mainmenu";
        public override int Layer => 0;
        // No ScreenName: the sidebar list context announces "Main Menu" via the context diff instead of
        // the screen self-announcing.

        public override bool IsActive()
        {
            var g = Game.Instance;
            if (g == null || g.RootUiContext == null || !g.RootUiContext.IsMainMenu) return false;

            // The sidebar is hidden whenever a main-menu sub-window covers it
            // (New Game setup, character generation, marketing popup).
            // Until those get their own screens, just stop being navigable here.
            var mm = g.RootUiContext.MainMenuVM;
            if (mm == null) return false;
            if (mm.NewGameVM != null) return false;
            if (mm.CharGenContextVM != null && mm.CharGenContextVM.CharGenVM != null
                && mm.CharGenContextVM.CharGenVM.Value != null) return false;
            if (mm.MarketingMessageVM != null && mm.MarketingMessageVM.Value != null) return false;
            return true;
        }


        public override void Build(GraphBuilder b)
        {
            var sidebar = RootUIContext.Instance?.MainMenuVM?.MainMenuSideBarVM;
            if (sidebar == null) return;

            // The PC sidebar view binds these seven; the VM's DLC manager entry has no PC view (the
            // DLC manager is console-only — MainMenuPCView.OpenDLCManager is empty), so it's left out.
            var entries = new List<ContextMenuEntityVM>();
            foreach (var vm in new[]
            {
                sidebar.ContinueVm, sidebar.NewGameVm, sidebar.LoadVm,
                sidebar.OptionsVm, sidebar.CreditVm, sidebar.LicenseVm, sidebar.ExitVm,
            })
                if (vm != null && !vm.IsSeparator) entries.Add(vm);

            b.BeginStop("menu").PushContext(Loc.T("screen.main_menu"), "list");
            for (int i = 0; i < entries.Count; i++)
                b.AddItem(ControlId.Referenced(entries[i], "mainmenu:" + i),
                    GraphNodes.MenuEntry(entries[i], GraphNodes.Position(i + 1, entries.Count)));
            b.PopContext();

            BuildIntroduction(b, RootUIContext.Instance.MainMenuVM);
        }

        // ---- the introductory text: paragraphs as text rows, each paragraph's links as buttons ----

        private struct IntroRow { public string Text; public string Url; }

        // GetIntroductoryText reads a file each call; it's fixed for the menu's lifetime, so parse once
        // per MainMenuVM.
        private static MainMenuVM s_introFor;
        private static List<IntroRow> s_intro;

        private static readonly Regex Link = new Regex("<link=\"([^\"]*)\">(.*?)</link>", RegexOptions.Singleline);
        private static readonly Regex Paragraphs = new Regex(@"\n\s*\n");

        private static void BuildIntroduction(GraphBuilder b, MainMenuVM vm)
        {
            if (vm == null) return;
            if (s_introFor != vm) { s_introFor = vm; s_intro = ParseIntro(vm.GetIntroductoryText()); }

            b.BeginStop("introduction");
            for (int i = 0; i < s_intro.Count; i++)
            {
                var row = s_intro[i];
                if (row.Url == null)
                    b.AddItem(ControlId.Structural("mainmenu:intro:" + i), GraphNodes.Text(() => row.Text));
                else
                    b.AddItem(ControlId.Structural("mainmenu:intro:" + i),
                        GraphNodes.Button(() => row.Text, () => ExternalUrl.Open(row.Url)));
            }
            var version = Kingmaker.GameVersion.GetVersion();
            if (!string.IsNullOrEmpty(version))
                b.AddItem(ControlId.Structural("mainmenu:version"),
                    GraphNodes.Text(() => Loc.T("mainmenu.version", new { version })));
        }

        private static List<IntroRow> ParseIntro(string text)
        {
            var rows = new List<IntroRow>();
            if (string.IsNullOrEmpty(text)) return rows;
            foreach (var para in Paragraphs.Split(text))
            {
                var plain = TextUtil.StripRichText(para);
                if (string.IsNullOrEmpty(plain)) continue;
                rows.Add(new IntroRow { Text = plain });
                foreach (Match m in Link.Matches(para))
                {
                    var label = TextUtil.StripRichText(m.Groups[2].Value);
                    var url = LinkUrl(m.Groups[1].Value);
                    if (!string.IsNullOrEmpty(label) && url != null) rows.Add(new IntroRow { Text = label, Url = url });
                }
            }
            return rows;
        }

        // The URL the game opens for a link id (UIUtility.SetTextLink's click handler, verbatim logic:
        // split on ':' dropping empties; skip a leading http/https key; re-join a "//host" with its scheme;
        // otherwise prefix http://).
        private static string LinkUrl(string linkId)
        {
            var keys = new List<string>();
            foreach (var k in linkId.Split(':')) if (!string.IsNullOrEmpty(k)) keys.Add(k);
            if (keys.Count == 0) return null;
            string url = keys[0];
            if ((url == "http" || url == "https") && keys.Count > 1) url = keys[1];
            if (url.StartsWith("//")) url = keys[0] + ":" + url;
            else if (!url.Contains("http://") && !url.Contains("https://")) url = "http://" + url;
            return url;
        }
    }
}
