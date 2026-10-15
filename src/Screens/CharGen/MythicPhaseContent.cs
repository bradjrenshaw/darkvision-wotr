using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Root.Strings; // UIStrings.CharacterSheet.MythicLevel (the level bar's wording)
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Mythic;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.ChupaChupses; // FeatureProgressionChupaChupsVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.MythicInfo; // MythicInfoProgressionVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Mythic phase (the level-up wizard in its Mythic build mode): mirrors the game's
    /// CharGenMythicPhaseDetailedPCView — the mythic path list, the selection's info panel, and the
    /// path's rank progression. Nothing else of the view is content: the doll room and the mythic-level
    /// visual selector only dress the preview model.
    ///   • Paths — the game's selector items in ITS order (selectable first, then unlocked, then locked;
    ///     CharGenMythicSelectorPCView.EntityComparer). A path the unit can't take is shown but not
    ///     interactable, as the game draws it (lineart, Locked layer): unlocked-but-unavailable reads
    ///     "prerequisites not met" like the class list, not-unlocked reads "locked". Space = the item's
    ///     own hover template (what the game shows on hover), selecting = SetSelectedFromView, which
    ///     the VM turns into LevelUpController.SelectClass.
    ///   • Details — the InfoSectionView content: the phase's InfoVM template for the selection
    ///     (TooltipTemplateMythicClass: description, prerequisites…), keyed per selection.
    ///   • Progression — MythicInfoProgressionView: the path's name, the level bar's current mythic
    ///     level, then the ability chains as a grid (one row per chain, one column per mythic level —
    ///     the game draws a column per chain with each ability at its level; same cells, transposed to
    ///     read row-wise). Space on a cell drills into the feature.
    /// </summary>
    public sealed class MythicPhaseContent : CharGenPhaseContent<CharGenMythicPhaseVM>
    {
        public MythicPhaseContent(CharGenMythicPhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            var selector = Phase.MythicSelector.Value;
            if (selector != null)
            {
                int i = 0;
                foreach (var item in Sorted(selector.EntitiesCollection))
                {
                    var it = item;
                    b.AddItem(ControlId.Referenced(it, k + "path:" + i), GraphNodes.SelectionItem(it,
                        () => it.DisplayName ?? "",
                        tooltip: () => it.TooltipTemplate(),
                        available: () => it.IsAvailibleToSelect,
                        extraParts: new[]
                        {
                            // The game's two non-selectable looks: a locked frame (path not unlocked) and
                            // the grayed lineart (unlocked, prerequisites unmet). Announced as state.
                            new NodeAnnouncement(() => !it.IsUnlocked ? Loc.T("state.locked")
                                : !it.IsAvailibleToSelect ? Loc.T("state.prerequisites_not_met") : null,
                                live: true, kind: AnnouncementKinds.Value),
                        }));
                    i++;
                }
            }

            // The info panel for the selection, re-keyed when the selection changes (list focus stays).
            var sel = Phase.SelectedMythicVM.Value;
            string dk = k + "detail:" + (sel?.GetHashCode() ?? 0) + ":";
            var tpl = Phase.InfoVM != null ? Phase.InfoVM.CurrentTooltip : null;
            if (tpl != null)
            {
                b.BeginStop("details").PushContext(Loc.T("chargen.details"), role: null, positions: false);
                TooltipFlowBuilder.Emit(b, dk, tpl, includeEmptyNotice: false);
                b.PopContext();
            }

            EmitProgression(b, dk + "prog:");
        }

        // The game's list order: selectable paths first, then unlocked ones, then locked (stable within).
        private static IEnumerable<CharGenMythicSelectorItemVM> Sorted(IEnumerable<CharGenMythicSelectorItemVM> items)
            => items.Where(x => x != null)
                .OrderByDescending(x => x.IsAvailibleToSelect)
                .ThenByDescending(x => x.IsUnlocked);

        private void EmitProgression(GraphBuilder b, string pk)
        {
            var prog = Phase.MythicProgressionVM;
            var chains = prog?.ProgressionStrings;
            if (chains == null || chains.Count == 0) return;

            int maxLevel = 0;
            foreach (var chain in chains)
                foreach (var ch in chain)
                    if (ch != null && ch.MaxLevel > maxLevel) maxLevel = ch.MaxLevel;
            if (maxLevel <= 0) return;

            b.BeginStop("progression").PushContext(Loc.T("chargen.progression"));
            var sheet = new GraphSheet(b, pk);

            // The panel's header: the path's name, and the level bar read as its current mythic level
            // (the game's own "Mythic level {0}" wording from the Mythic window).
            var name = prog.MythicName;
            if (!string.IsNullOrEmpty(name)) sheet.Line(GraphNodes.Heading(() => prog.MythicName));
            var levelBar = prog.LevelProgressionVM;
            if (levelBar != null)
                sheet.Line(GraphNodes.Text(() => TextUtil.StripRichText(
                    string.Format(UIStrings.Instance.CharacterSheet.MythicLevel, levelBar.CurrentLevel.Value))));

            // The grid: columns = mythic levels (the ruler the game lays the chains along).
            var cols = new string[maxLevel];
            for (int l = 1; l <= maxLevel; l++) cols[l - 1] = Loc.T("chargen.level_col", new { n = l });
            sheet.Region("", cols);
            foreach (var chain in chains)
            {
                if (chain == null || chain.Count == 0) continue;
                string header = null;
                bool mixed = false;
                var cells = new List<KeyValuePair<int, NodeVtable>>();
                foreach (var ch in chain)
                {
                    if (ch == null) continue;
                    if (!string.IsNullOrEmpty(ch.Name))
                    {
                        if (header == null) header = ch.Name;
                        else if (header != ch.Name) mixed = true;
                    }
                    if (ch.Level >= 1 && ch.Level <= maxLevel) cells.Add(new KeyValuePair<int, NodeVtable>(ch.Level, Cell(ch)));
                }
                var rowName = mixed ? Loc.T("chargen.features") : (header ?? Loc.T("chargen.feature"));
                sheet.RowAt(GraphNodes.Text(() => rowName), null, cells);
            }
            sheet.Finish();
            b.PopContext();
        }

        // One ability at its level: name (+ rank when the game shows one); Space = the feature write-up.
        private static NodeVtable Cell(FeatureProgressionChupaChupsVM ch)
        {
            var text = ch.Name ?? "";
            if (ch.HasRank && !string.IsNullOrEmpty(ch.Rank)) text += " " + ch.Rank;
            return GraphNodes.Text(() => text, () => ch.Tooltip);
        }
    }
}
