using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Controllers.Clicks;
using Kingmaker.Globalmap.State.InputManager;
using Kingmaker.Globalmap.View;
using Kingmaker.UI.MVVM._VM.Teleport;
using UnityEngine;
using WrathAccess.Exploration;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    internal sealed class GlobalMapSpellTargetScreen : Screen
    {
        private static readonly PropertyInfo InputStateProperty =
            AccessTools.Property(typeof(GlobalMapInputManager), "State");
        private static readonly FieldInfo ValidationField =
            AccessTools.Field(typeof(SelectPawnState), "m_ValidationFunc");

        private static SelectPawnState MapSelection
            => Game.Instance?.SelectedClicksObjectHandler?.GetMode() == PointerMode.GlobalMagicSpell
                ? InputStateProperty?.GetValue(null) as SelectPawnState : null;
        private static TeleportVM Teleport => GlobalMapModel.CurrentVM?.TeleportVM.Value;

        private SelectPawnState _selection;
        private TeleportVM _teleport;
        private List<IGMPoint> _targets;

        public override string Key => "overlay.worldmap_spell_target";
        public override int Layer => 16;
        public override bool Exclusive => true;
        public override string ScreenName => Loc.T(Teleport != null
            ? "worldmap.spell_choose_destination" : "worldmap.spell_choose_target");

        public override bool IsActive() => GlobalMapSpellNodes.ActiveBar != null
            && (Teleport != null || MapSelection != null);

        public override void OnPop()
        {
            if (_teleport != null && ReferenceEquals(_teleport, Teleport)) _teleport.OnClose();
            if (_selection != null && ReferenceEquals(_selection, MapSelection)) CancelAim();
            _teleport = null;
            _selection = null;
            _targets = null;
        }

        public override void Build(GraphBuilder b)
        {
            b.BeginStop("worldmap.spell_targets");
            _teleport = Teleport;
            if (_teleport != null)
            {
                var teleport = _teleport;
                foreach (var entry in teleport.EntityVms)
                    b.AddItem(ControlId.ForObject(entry), GraphNodes.Button(
                        () => TextUtil.StripRichText(entry.LocationName.Value),
                        () => { if (ReferenceEquals(teleport, Teleport)) entry.OnTeleport(); }));
            }
            else if (MapSelection is SelectPawnState selection)
            {
                // The native selection carries the executor's exact validator, including army
                // faction, general requirements, garrison checks and army pathfinding.
                if (!ReferenceEquals(_selection, selection))
                {
                    _selection = selection;
                    _targets = FindTargets(selection);
                }
                foreach (var target in _targets)
                    b.AddItem(ControlId.ForObject(target), GraphNodes.Button(
                        () => Label(target), () => SelectTarget(selection, target)));
                if (_targets.Count == 0)
                    b.AddItem(ControlId.Structural("worldmap:spell:no-targets"), GraphNodes.Text(
                        () => Loc.T("worldmap.spell_no_targets")));
            }
            b.AddItem(ControlId.Structural("worldmap:spell:close-targets"), GraphNodes.Button(
                () => Loc.T("action.close"), Close));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private static List<IGMPoint> FindTargets(SelectPawnState selection)
        {
            var validate = Validator(selection);
            if (validate == null) return new List<IGMPoint>();

            IEnumerable<IGMPoint> candidates = GlobalMapModel.Armies.Select(army => army.View)
                .OfType<GlobalMapArmyPawn>().Where(pawn => pawn != null && pawn.gameObject.activeInHierarchy);
            candidates = candidates.Concat(GlobalMapModel.Locations.Concat(GlobalMapModel.Junctions)
                .Distinct().Where(point => point.gameObject.activeInHierarchy));
            var from = GlobalMapModel.TravelerPos;
            return candidates.Where(validate).OrderBy(target => target is Component view
                ? Geo.Distance(from, view.transform.position) : float.MaxValue).ToList();
        }

        private static Func<IGMPoint, bool> Validator(SelectPawnState selection)
            => ValidationField?.GetValue(selection) as Func<IGMPoint, bool>;

        private static string Label(IGMPoint target) => target is GlobalMapArmyPawn pawn
            ? GlobalMapActions.ArmyLabel(pawn.State, GlobalMapModel.TravelerPos)
            : GlobalMapActions.Label((GlobalMapPointView)target, GlobalMapModel.TravelerPos);

        private void SelectTarget(SelectPawnState selection, IGMPoint target)
        {
            if (!ReferenceEquals(selection, MapSelection)) return;
            if (!(target is Component view) || view == null || !view.gameObject.activeInHierarchy
                || Validator(selection)?.Invoke(target) != true)
            {
                _targets.Remove(target);
                Tts.Speak(Loc.T("worldmap.spell_invalid_target"));
                return;
            }
            var label = Label(target);
            GlobalMapInputManager.OnPointerClickPoint(target);
            if (!ReferenceEquals(selection, MapSelection))
                Tts.Speak(Loc.T("worldmap.spell_cast_on", new { target = label }));
        }

        private static void Close()
        {
            if (Teleport is TeleportVM teleport) teleport.OnClose();
            else CancelAim();
        }

        private static void CancelAim() => Game.Instance?.SelectedClicksObjectHandler?.DropGlobalMagicSpell();
    }
}
