using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker;                            // Game
using Kingmaker.Blueprints.Root.Strings;    // UINotificationTexts (game-localized notification formats)
using Kingmaker.Kingdom;                    // KingdomState / KingdomUIRoot (stat names, current morale)
using Kingmaker.Kingdom.Blueprints;         // KingdomRoot.Stats (the stat order the game lists)
using Kingmaker.Settings;                   // SettingsRoot (the game's per-category notification toggles)
using Kingmaker.UI.Common;                  // UIUtility alignment texts
using Kingmaker.UI.MVVM._VM.Dialog;         // DialogContextVM
using Kingmaker.UI.MVVM._VM.Dialog.Dialog;  // AnswerVM, DialogNotificationsVM
using Kingmaker.Utility;                    // UIConsts.GetAnswerString
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Shared pieces of the conversation surfaces (ordinary dialogue + book events/interchapters):
    /// the live dialog context lookup, the player-answer node and the notification strip's lines, so
    /// both screens read and behave identically.
    /// </summary>
    internal static class DialogTranscript
    {
        /// <summary>The active dialog context — the SAME <see cref="DialogContextVM"/> (holding DialogVM /
        /// BookEventVM / InterchapterVM) whether the conversation runs in an area (on <c>InGameVM</c>) or on
        /// the world map (on <c>GlobalMapVM</c>, which carries its own context). The dialogue + book-event
        /// screens read from whichever is live, so they work in both places with no other change.</summary>
        public static DialogContextVM Context()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            if (rc == null) return null;
            return rc.InGameVM?.StaticPartVM?.DialogContextVM ?? rc.GlobalMapVM?.DialogContextVM;
        }

        /// <summary>One player answer: the game's own answer formatter (numbered prefix plus skill-check /
        /// alignment / mythic tags, gated by the same dialogue settings, so we surface only what's drawn);
        /// Enter chooses via <see cref="AnswerVM.OnChooseAnswer"/> (the game plays NextDialogLine — no
        /// click of ours); Space resolves the per-stat DC-preview links (glossary falls through). Disabled
        /// while the dialogue window is hidden (a cutscene transition must not let Enter choose through a
        /// hidden window).</summary>
        public static NodeVtable AnswerNode(AnswerVM vm)
        {
            Func<bool> enabled = () => vm != null && vm.Enable.Value && WrathAccess.DialogVisibility.Shown;
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => AnswerText(vm)),
                    GraphNodes.DisabledPart(enabled),
                },
                SearchText = () => AnswerText(vm),
                // The game's DialogAnswerView assigns BuildingHover (81) in code — dialogue choices
                // hover with their own sound, not the default button hover. Mirror it.
                HoverSound = () => WrathAccess.UiSound.Play(Kingmaker.UI.UISoundType.BuildingHover),
                OnActivate = () => { if (enabled()) vm.OnChooseAnswer(); },
                OnTooltip = () => TooltipScreen.FollowLinks(AnswerText(vm),
                    (id, keys) => WrathAccess.UI.Proxies.DialogLinks.ResolveSkillCheck(
                        keys, null, vm?.Answer?.Value?.SkillChecksDC)),
            };
        }

        // The bind name matches the view's own (DialogAnswerView builds "DialogChoice{Index}"). Returns
        // TMP rich text (<link>-wrapped checks); Tts strips that at speak time. Plain fallback for
        // system answers — the game's button reads an UNNUMBERED "Continue".
        public static string AnswerText(AnswerVM vm)
        {
            if (vm == null) return "";
            var bp = vm.Answer?.Value;
            var text = bp != null ? bp.DisplayText : null;
            if (string.IsNullOrEmpty(text)) return Loc.T("label.continue");
            try { return UIConsts.GetAnswerString(bp, "DialogChoice" + vm.Index, vm.Index); }
            catch { return vm.Index + ". " + text; }
        }
        /// <summary>The notification strip (<see cref="DialogNotificationsVM"/>) as speech lines, mirroring
        /// <c>DialogNotificationsView</c>: the same per-category game settings gates (so we surface only
        /// what's drawn) and the same game-localized <see cref="UINotificationTexts"/> formats, colors
        /// stripped. Every category the view has — alignment, revealed locations, items, experience, and
        /// the kingdom/crusade group (stat changes, new events, free buildings, morale, custom lines) under
        /// its one toggle. Call it synchronously inside the <c>OnUpdateCommand</c> subscription: the VM
        /// clears its lists right after firing.</summary>
        public static List<string> Notifications(DialogNotificationsVM n)
        {
            var lines = new List<string>();
            if (n == null) return lines;
            var t = UINotificationTexts.Instance;

            if (SettingsRoot.Game.Dialogs.ShowAlignmentShiftsNotifications)
            {
                foreach (var shift in n.AlignmentShifts)
                    lines.Add(TextUtil.StripRichText(string.Format(t.AlignmentShiftedFormat, "#FFFFFF",
                        UIUtility.GetAlignmentShiftDirectionText(shift.Direction), shift.Value)));
                var cueData = n.CueData;
                if (cueData != null && cueData.NewAlignment.HasValue)
                    lines.Add(TextUtil.StripRichText(string.Format(t.NewAlignmentAfterShiftedFormat, "#FFFFFF",
                        UIUtility.GetAlignmentName(cueData.NewAlignment.Value))));
            }

            if (SettingsRoot.Game.Dialogs.ShowLocationRevealedNotification && n.RevealedLocationNames.Count > 0)
                lines.Add(TextUtil.StripRichText(string.Format(
                    n.RevealedLocationNames.Count < 2 ? t.RevealedLocationFormat : t.RevealedLocationsFormat,
                    string.Join(", ", n.RevealedLocationNames.ToArray()))));

            if (SettingsRoot.Game.Dialogs.ShowItemsReceivedNotification)
            {
                var got = new List<string>();
                var lost = new List<string>();
                foreach (var kv in n.ItemsChanged)
                {
                    if (kv.Key == null || kv.Value == 0) continue;
                    int abs = Math.Abs(kv.Value);
                    var label = abs > 1 ? kv.Key.Name + " \u00d7" + abs : kv.Key.Name;
                    (kv.Value > 0 ? got : lost).Add(label);
                }
                if (got.Count > 0)
                    lines.Add(TextUtil.StripRichText(string.Format(t.ItemsRecievedFormat, string.Join(", ", got.ToArray()))));
                if (lost.Count > 0)
                    lines.Add(TextUtil.StripRichText(string.Format(t.ItemsLostFormat, string.Join(", ", lost.ToArray()))));
            }

            if (SettingsRoot.Game.Dialogs.ShowXPGainedNotification && n.XpGains.Count > 0)
            {
                int sum = 0;
                foreach (var x in n.XpGains) sum += x;
                lines.Add(TextUtil.StripRichText(string.Format(t.XPGainedFormat, sum)));
            }

            if (SettingsRoot.Game.Dialogs.ShowKingdomNotifications)
            {
                // Stat changes, in the game's own stat order, "Name +2, Name -1".
                if (!n.KingdomChange.IsEmpty && KingdomRoot.Instance != null && KingdomUIRoot.Instance != null)
                {
                    var parts = new List<string>();
                    foreach (var stat in KingdomRoot.Instance.Stats)
                    {
                        if (stat == null) continue;
                        int delta = n.KingdomChange[stat.Type];
                        if (delta == 0) continue;
                        var el = KingdomUIRoot.Instance.GetStatElement(stat.Type);
                        parts.Add(((string)el?.Name ?? stat.Type.ToString()) + " " + Signed(delta));
                    }
                    if (parts.Count > 0)
                        lines.Add(TextUtil.StripRichText(string.Format(t.KingdomStatChangedFormat, string.Join(", ", parts.ToArray()))));
                }

                if (n.NewEvents.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var ev in n.NewEvents) if (ev != null) names.Add(ev.DisplayName);
                    if (names.Count > 0) lines.Add(TextUtil.StripRichText(t.KingdomNewEventsLabel + " " + string.Join(", ", names.ToArray())));
                }

                if (n.FreeBuildings.Count > 0)
                {
                    var sb = new StringBuilder((string)t.KingdomFreeBuildingLabel).Append(" ");
                    bool first = true;
                    foreach (var fb in n.FreeBuildings)
                    {
                        if (fb == null || fb.Building == null) continue;
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append(fb.Building.Name).Append(" \u00d7").Append(fb.Count);
                        if (fb.Region != null) sb.Append(" (").Append(fb.Region.LocalizedName).Append(")");
                    }
                    if (!first) lines.Add(TextUtil.StripRichText(sb.ToString()));
                }

                if (n.MoralDelta.Count > 0)
                {
                    var deltas = new List<string>();
                    foreach (var d in n.MoralDelta) deltas.Add(Signed(d));
                    var text = t.KingdomMoraleDeltaLabel + " " + string.Join(", ", deltas.ToArray());
                    var ks = KingdomState.Instance;
                    if (ks != null && ks.MoraleState != null)
                        text += " (" + t.KingdomCurrentMoraleLabel + " " + ks.MoraleState.CurrentValue + ")";
                    lines.Add(TextUtil.StripRichText(text));
                }

                foreach (var c in n.CustomNotifications)
                    if (!string.IsNullOrEmpty(c)) lines.Add(TextUtil.StripRichText(c));
            }

            return lines;
        }

        private static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

    }
}
