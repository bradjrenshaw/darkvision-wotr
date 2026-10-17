using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Armies.TacticalCombat;               // TacticalCombatHelper
using Kingmaker.Armies.TacticalCombat.Grid;          // TacticalCombatGridHelper
using Kingmaker.Armies.TacticalCombat.Parts;         // UnitPartTacticalCombat
using Kingmaker.EntitySystem.Entities;               // UnitEntityData
using Kingmaker.UI.MVVM._VM.TacticalCombat;          // TacticalCombatAttackTooltipVM
using Kingmaker.UnitLogic.Abilities;                 // AbilityData
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// What the game's attack tooltip would say about a target cell: the acting squad's reach, and the
    /// damage and kills an attack (or the heal and revived troops of an aimed spell) would bring. The
    /// numbers come from the game's own <see cref="TacticalCombatAttackTooltipVM"/> — its private
    /// CalculateNumbers / CalculateAbilityNumbers, fed the same fields its hover handler fills (target,
    /// attacker, flanking; the aimed ability), with the walk to the attack cell scored first exactly
    /// as its attack-cell handler does (movement can change the damage). The hover handler itself
    /// can't be used: it reads the real mouse to place the attack.
    /// </summary>
    internal static class TacticalPrediction
    {
        public struct Result
        {
            public bool Heal;
            public int MinDamage, MaxDamage, MinDead, MaxDead;
        }

        private static readonly System.Reflection.FieldInfo UnitF = AccessTools.Field(typeof(TacticalCombatAttackTooltipVM), "m_Unit");
        private static readonly System.Reflection.FieldInfo AttackerF = AccessTools.Field(typeof(TacticalCombatAttackTooltipVM), "m_Attacker");
        private static readonly System.Reflection.FieldInfo FlankF = AccessTools.Field(typeof(TacticalCombatAttackTooltipVM), "m_IsFlanked");
        private static readonly System.Reflection.FieldInfo AbilityF = AccessTools.Field(typeof(TacticalCombatAttackTooltipVM), "m_AbilityWithDamage");
        private static readonly System.Reflection.MethodInfo CalcAttack = AccessTools.Method(typeof(TacticalCombatAttackTooltipVM), "CalculateNumbers");
        private static readonly System.Reflection.MethodInfo CalcAbility = AccessTools.Method(typeof(TacticalCombatAttackTooltipVM), "CalculateAbilityNumbers");

        private static TacticalCombatAttackTooltipVM Vm => Game.Instance?.RootUiContext?.TacticalCombatVM?.AttackTooltipVM;

        /// <summary>The acting squad, when it is ours and free to act (what the click handler requires).</summary>
        public static UnitEntityData OurActor
        {
            get
            {
                var data = Game.Instance?.TacticalCombat?.Data;
                var actor = data?.Turn?.Unit;
                if (actor == null || !actor.IsDirectlyControllable || data.IsBusy || data.Turn.StandardActionUsed) return null;
                return actor;
            }
        }

        /// <summary>Cells our acting squad can move to this turn (the game's own reach), or null.</summary>
        public static HashSet<Vector2Int> Reachable()
        {
            var actor = OurActor;
            if (actor == null) return null;
            try { return new HashSet<Vector2Int>(TacticalCombatGrid.Instance.Pathfinder.GetReachableCells(actor)); }
            catch { return null; }
        }

        /// <summary>Whether our acting squad can attack this target this turn: ranged squads always can;
        /// melee needs a free cell next to the target that it can reach.</summary>
        public static bool CanAttack(UnitEntityData target, out Vector2Int attackCell, out List<Vector3> path)
        {
            attackCell = default; path = null;
            var actor = OurActor;
            if (actor == null || target == null || TacticalModel.IsOwn(target) || !actor.Descriptor.State.CanAct) return false;
            if (TacticalCombatHelper.IsRanged(actor)) return true;
            var reach = Reachable();
            if (reach == null) return false;
            var at = target.Position;
            var toward = actor.Position - at; toward.y = 0f;
            if (toward.sqrMagnitude > 1e-4f) at += toward.normalized * 0.5f;
            attackCell = TacticalCombatGridHelper.GetCellToAttack(at, actor, out _);
            if (!reach.Contains(attackCell)) return false;
            path = Game.Instance.TacticalCombat.FindPath(actor, attackCell);
            return path != null;
        }

        /// <summary>The prediction for attacking (or, with a spell aimed, casting at) a squad; null when
        /// the game would show no tooltip there.</summary>
        public static Result? For(UnitEntityData target)
        {
            var vm = Vm;
            if (vm == null || target == null || target.State.IsDead) return null;
            try
            {
                var aimed = Game.Instance.SelectedAbilityHandler?.SelectedAbility;
                if (aimed != null) return Ability(vm, aimed, target);
                if (!CanAttack(target, out _, out var path)) return null;
                return Attack(vm, OurActor, target, path);
            }
            catch (System.Exception e)
            {
                Main.Log?.Warning("[tactical] prediction failed: " + e.Message);
                return null;
            }
        }

        private static Result? Attack(TacticalCombatAttackTooltipVM vm, UnitEntityData attacker, UnitEntityData target, List<Vector3> path)
        {
            // Flanking, as the hover handler judges it: already engaged by two, or by someone else while we
            // arrive from another cell.
            int engaged = target.CombatState.EngagedBy.Count;
            bool flanked = !target.Descriptor.State.Features.CannotBeFlanked
                && (engaged > 1 || (engaged > 0 && !target.CombatState.EngagedBy.Contains(attacker) && path != null && path.Count > 1));
            UnitF.SetValue(vm, target);
            AttackerF.SetValue(vm, attacker);
            FlankF.SetValue(vm, flanked);
            AbilityF.SetValue(vm, null);
            var part = attacker.Get<UnitPartTacticalCombat>();
            if (part != null && path != null) part.ScoreMovements(path.Count - 1);
            try { CalcAttack.Invoke(vm, null); }
            finally { part?.ClearMovements(); }
            return Read(vm);
        }

        private static Result? Ability(TacticalCombatAttackTooltipVM vm, AbilityData ability, UnitEntityData target)
        {
            UnitF.SetValue(vm, target);
            AttackerF.SetValue(vm, null);
            AbilityF.SetValue(vm, ability);
            CalcAbility.Invoke(vm, null);
            if (UnitF.GetValue(vm) == null) return null; // the ability neither damages nor heals: no tooltip
            return Read(vm);
        }

        private static Result Read(TacticalCombatAttackTooltipVM vm)
            => new Result { Heal = vm.Heal, MinDamage = vm.MinDamage, MaxDamage = vm.MaxDamage, MinDead = vm.MinDeath, MaxDead = vm.MaxDeath };

        /// <summary>The prediction as speech: "3 to 8 damage, 0 to 1 killed" / "heals 5 to 9, 1 revived".</summary>
        public static string Speak(Result r)
        {
            string Range(int a, int b) => a == b ? a.ToString() : Loc.T("tactical.range", new { min = a, max = b });
            return r.Heal
                ? Loc.T("tactical.predict_heal", new { amount = Range(r.MinDamage, r.MaxDamage), count = Range(r.MinDead, r.MaxDead) })
                : Loc.T("tactical.predict_attack", new { damage = Range(r.MinDamage, r.MaxDamage), count = Range(r.MinDead, r.MaxDead) });
        }
    }
}
