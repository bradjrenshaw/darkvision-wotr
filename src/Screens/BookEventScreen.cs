using System;
using System.Collections.Generic;
using Kingmaker;
using System.Linq;
using Kingmaker.Controllers.Dialog;      // SkillCheckResult
using Kingmaker.DialogSystem.Blueprints; // BlueprintBookPage
using Kingmaker.UI.Common;               // UIUtility.SkillCheckText
using Kingmaker.UI.MVVM._VM.Dialog.BookEvent; // BookEventVM
using Kingmaker.UI.MVVM._VM.Dialog.Interchapter; // InterchapterVM (a BookEventVM subclass)
using UniRx;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A book event (<see cref="BookEventVM"/>) — the illustrated storybook page with a passage of
    /// narrative and numbered choices (e.g. the Areelu vision). It's a <c>DialogType.Book</c> conversation,
    /// so it rides the SAME in-game HUD VM as ordinary dialogue (<c>DialogContextVM.BookEventVM</c>, beside
    /// <c>DialogVM</c>) and reuses the dialogue <c>AnswerVM</c> → <see cref="WrathAccess.UI.Proxies.DialogAnswerButton"/>.
    ///
    /// A page carries several cues (the paragraphs, shown together) plus the answers; we read the whole
    /// passage when a new page appears (keyed on <c>BlueprintBookPage</c>, like the dialogue cue), and the
    /// passage is the first focusable element so you can re-read it. Choosing an answer advances to the next
    /// page in place (new passage + choices) until the book closes.
    ///
    /// Interchapter/epilogue narration (e.g. "Trapped in the Darkness") is the same thing — <see
    /// cref="InterchapterVM"/> derives from BookEventVM, just stored in a separate context slot and carrying
    /// a page <c>Title</c> — so we pick that VM up too and read its title ahead of the passage. The
    /// notification strip the page shows under the passage (experience, items, alignment shifts, crusade
    /// changes — <see cref="DialogTranscript.Notifications"/>) is appended as rows after the paragraphs:
    /// the VM fires it with the page and clears its lists at once, so it is snapshotted in the
    /// subscription and kept per page. The skill-check "choose a character" sub-step is still deferred.
    /// </summary>
    public sealed class BookEventScreen : Screen
    {
        public override string Key => "ctx.bookevent";
        public override string ScreenName => Loc.T("screen.book_event");
        public override int Layer => 15; // over the in-game context + service windows, like dialogue

        // Same hide-not-close pop semantics as dialogue.
        public override bool KeepStateOnPop => true;

        // What we've read so far: the page AND its passage lines. A hub page (the Elysium
        // "Where am I? / Who are you?" questions) re-shows the SAME BlueprintBookPage per answer,
        // appending the reply cue (or replacing the text outright) — keyed on the page alone, those
        // updates went unread while the vanished answer's focus recovery re-read the old paragraph.
        private BlueprintBookPage _spokenPage;
        private readonly List<string> _spokenLines = new List<string>();

        private BookEventVM _subscribedVm;   // the book our notification subscription belongs to
        private IDisposable _notifSub;
        private readonly List<string> _notifLines = new List<string>(); // the current page's strip

        private static BookEventVM Vm()
        {
            // In-area OR world-map context (the global map carries its own DialogContextVM) — see
            // DialogTranscript.Context. Interchapter/epilogue is a BookEventVM subclass in its own slot.
            var ctx = DialogTranscript.Context();
            return ctx?.BookEventVM?.Value ?? ctx?.InterchapterVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { Reset(); }
        // Keep _spokenPage across a hide/re-push (don't re-read the passage); clear only the focus
        // marker so the re-push lands back on the passage top (the pop dropped the graph state).
        public override void OnPop() { if (Vm() == null) Reset(); }
        private void Reset()
        {
            _spokenPage = null;
            _spokenLines.Clear();
            _notifLines.Clear();
            _notifSub?.Dispose();
            _notifSub = null;
            _subscribedVm = null;
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            // A new book = a fresh VM: re-subscribe to its notification strip. The VM CLEARS its lists
            // right after firing the command, so the snapshot must happen synchronously inside it.
            if (vm != _subscribedVm)
            {
                _notifSub?.Dispose();
                _subscribedVm = vm;
                _notifLines.Clear();
                _notifSub = vm.DialogNotifications.OnUpdateCommand.Subscribe(show =>
                {
                    _notifLines.Clear();
                    if (show) _notifLines.AddRange(DialogTranscript.Notifications(vm.DialogNotifications));
                });
            }

            var page = vm.BlueprintBookPage.Value;
            if (page == null) return; // VM exists a frame before the first page is pushed

            var lines = Lines(vm).Select(l => l.Raw).ToList();
            // A new page: read the whole passage. The SAME page with changed lines: read only what is
            // new — the lines past the longest unchanged prefix (an appended reply; a replaced passage
            // reads whole again). Focus lands SILENTLY on the first new line either way (the queued
            // read is the speech); Down reaches the choices, Up re-reads earlier paragraphs.
            int from = page != _spokenPage ? 0 : CommonPrefix(_spokenLines, lines);
            if (page != _spokenPage || from < lines.Count || lines.Count != _spokenLines.Count)
            {
                _spokenPage = page;
                _spokenLines.Clear();
                _spokenLines.AddRange(lines);
                if (from < lines.Count)
                {
                    Navigation.FocusNode(ControlId.Structural(PageKey(vm) + "row:" + from), announce: false);
                    Speak(lines, from);
                }
            }
        }

        private static int CommonPrefix(List<string> a, List<string> b)
        {
            int n = 0;
            while (n < a.Count && n < b.Count && a[n] == b[n]) n++;
            return n;
        }

        // Speak the passage from a line onward, QUEUED (never interrupting — the dialogue rule).
        // Re-reading individual paragraphs is done by arrowing the rows.
        private static void Speak(List<string> lines, int from)
        {
            if (from >= lines.Count) return;
            var part = lines.GetRange(from, lines.Count - from);
            Tts.Speak(TextUtil.StripRichText(string.Join("\n", part.ToArray())), interrupt: false);
        }

        private static string PageKey(BookEventVM vm)
            => "book:" + vm.GetHashCode() + ":" + (vm.BlueprintBookPage.Value?.GetHashCode() ?? 0) + ":";


        // Same shape as ordinary dialogue: the passage rows, then the choices — one stop, no positions.
        // Keys carry the page, so choosing an answer re-keys everything (OnUpdate re-homes silently).
        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null || vm.BlueprintBookPage.Value == null) return;
            string k = PageKey(vm);

            b.PushContext("", role: null, positions: false); // silent positions-off scope (a transcript)
            var lines = Lines(vm);
            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i].Raw;
                var checks = lines[i].Checks;
                b.AddItem(ControlId.Structural(k + "row:" + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new NodeAnnouncement(() => TextUtil.StripRichText(raw)) },
                    // Raw text kept un-stripped so glossary links survive for Space; the check result's
                    // own link resolves against the cue's rolls, as in ordinary dialogue.
                    OnTooltip = () => TooltipScreen.FollowLinks(raw,
                        (id, keys) => WrathAccess.UI.Proxies.DialogLinks.ResolveSkillCheck(keys, checks, null)),
                });
            }

            var answers = vm.Answers.Value;
            if (answers != null)
            {
                int ai = 0;
                foreach (var a in answers)
                {
                    if (a != null)
                        b.AddItem(ControlId.Referenced(a, k + "ans:" + ai), DialogTranscript.AnswerNode(a));
                    ai++;
                }
            }
            b.PopContext();
        }

        /// <summary>One transcript line: its raw (markup-intact) text and, for a line that opens a cue
        /// with a skill check, that cue's rolls (so the check's link can show the roll).</summary>
        private struct Line
        {
            public string Raw;
            public List<SkillCheckResult> Checks;
        }

        // The page's rows: the passage, then the notification strip's lines (where the game draws it).
        private List<Line> Lines(BookEventVM vm)
        {
            var lines = PassageLines(vm);
            foreach (var n in _notifLines) lines.Add(new Line { Raw = n });
            return lines;
        }

        // The page as transcript lines: the interchapter title first (e.g. "Trapped in the Darkness"), then
        // one line per cue paragraph (raw text — kept un-stripped so glossary links survive for Space).
        // A cue's check result ("[Failed a Diplomacy check]") is a runtime prefix the game's cue view
        // composes from the cue's SkillChecks — NOT part of BaseText — so it is prepended the same way
        // (UIUtility.SkillCheckText, as the dialogue screen does) to the cue's first paragraph.
        private static List<Line> PassageLines(BookEventVM vm)
        {
            var lines = new List<Line>();
            if (vm is InterchapterVM ic && !string.IsNullOrWhiteSpace(ic.Title.Value)) lines.Add(new Line { Raw = ic.Title.Value });
            foreach (var cue in vm.Cues)
            {
                var t = cue?.BaseText;
                if (string.IsNullOrWhiteSpace(t)) continue;
                var checks = cue.SkillChecks != null && cue.SkillChecks.Count > 0 ? cue.SkillChecks : null;
                string check = checks != null ? UIUtility.SkillCheckText(checks)?.Trim() : null;
                foreach (var part in t.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    var raw = part.Trim();
                    if (!string.IsNullOrEmpty(check)) { raw = check + " " + raw; check = null; }
                    lines.Add(new Line { Raw = raw, Checks = checks });
                }
            }
            return lines;
        }
    }
}
