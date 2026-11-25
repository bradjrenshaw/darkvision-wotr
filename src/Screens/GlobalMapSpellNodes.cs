using System;
using System.Linq;
using Kingmaker;
using Kingmaker.Crusade.GlobalMagic.Actions;
using Kingmaker.Crusade.GlobalMagic.Executors;
using Kingmaker.Crusade.GlobalMagic.SpellsManager;
using Kingmaker.Settings;
using Kingmaker.UI;
using Kingmaker.UI.Common;
using Kingmaker.UI.MVVM._VM.GlobalMap.ActionBar;
using Kingmaker.Globalmap.View;
using WrathAccess.Exploration;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    internal static class GlobalMapSpellNodes
    {
        internal static GlobalMapActionBarVM ActiveBar
        {
            get
            {
                if (Game.Instance?.RootUiContext?.IsKingdom == true
                    ? !KingdomScreen.CampaignMapReady
                    : !GlobalMapModel.Active) return null;

                if (SettingsRoot.Difficulty.AutoCrusade) return null;
                var bar = GlobalMapModel.CurrentVM?.GlobalMapActionBarVM;
                return bar?.IsEnable.Value == true ? bar : null;
            }
        }

        internal static void Build(GraphBuilder b)
        {
            var bar = ActiveBar;
            if (bar == null) return;
            // IsEnable can stay true after the last spell is removed. Empty vanilla slots add no stop.
            var slots = bar.Slots.Where(slot => slot.SpellState?.Blueprint != null).ToList();
            if (slots.Count == 0) return;

            b.BeginStop("worldmap.spells").PushContext(Loc.T("worldmap.spell_bar"), "list");
            foreach (var slot in slots)
            {
                var state = slot.SpellState;
                var node = GraphNodes.Button(() => Name(state), () => Cast(state), sound: null);
                node.OnTooltip = () => TooltipScreen.Open(slot.MechanicActionBarSlot.GetTooltipTemplate());
                var description = TextUtil.StripRichText(state.Blueprint.SpellDescription);
                node.Announcements = new[]
                {
                    GraphNodes.LabelPart(node.SearchText),
                    new NodeAnnouncement(() => Status(state), kind: AnnouncementKinds.Value),
                    GraphNodes.TooltipPart(() => description),
                };

                b.AddItem(ControlId.Structural("worldmap:spell:" + state.BlueprintGuid), node);
            }
            b.PopContext();
        }

        private static string Name(SpellState state)
        {
            var spell = state.Blueprint;
            var name = TextUtil.StripRichText(spell.SpellName);
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return Loc.T(spell.name == "GlobalSpellAzataSummonIsland"
                ? "worldmap.spell_move_azata_island" : "worldmap.spell_unnamed");
        }

        private static string Status(SpellState state)
            => BlockReason(state) ?? Loc.T("worldmap.spell_ready");

        private static string BlockReason(SpellState state)
        {
            var spell = state.Blueprint;
            if (state.Cooldown > 0f)
            {
                var time = TimeSpan.FromHours(state.Cooldown);
                return Loc.T("crusade.cooldown", new { time = UIUtility.TimeSpanToGameTime(time) });
            }
            if (!spell.CanBeUsed())
            {
                if (spell.Executor is PlayerSelectedNonTarget) return Loc.T("worldmap.spell_select_party");
                if (spell.Executor is SelectedArmyAndPointTarget) return Loc.T("worldmap.spell_select_army");
                return Loc.T("worldmap.spell_unavailable");
            }
            if (OpensTeleport(state) && GlobalMapView.Instance?.Teleport?.GetLocations().Count is null or 0)
                return Loc.T("worldmap.spell_no_destinations");
            return null;
        }

        private static bool OpensTeleport(SpellState state)
            => state.Blueprint.SpellActions.Actions.Any(action => action is OpenTeleportationInterface);

        private static void Cast(SpellState state)
        {
            var slot = ActiveBar?.Slots.FirstOrDefault(s => s.SpellState == state);
            if (slot == null) return;
            var reason = BlockReason(state);
            if (reason != null)
            {
                Tts.Speak(reason);
                return;
            }
            UiSound.Play(UISoundType.ButtonClick);
            slot.OnMainClick();
            if (!state.IsTarget && !OpensTeleport(state))
                Tts.Speak(Loc.T("worldmap.spell_cast", new { spell = Name(state) }));
        }
    }
}
