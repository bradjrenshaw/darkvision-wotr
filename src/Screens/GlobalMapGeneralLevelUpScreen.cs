using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Armies;
using Kingmaker.Armies.Blueprints;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.Crusade.ArmyInfo;
using Kingmaker.UI.MVVM._VM.Crusade.LeaderLevelUp;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Accessible version of the crusade general level-up dialog. Game creates this VM on the global
    /// map after the army card's Level Up action. A level-up is one commit step: review gains and the
    /// guaranteed skill, choose one skill, confirm. If enough XP remains, the game leaves another
    /// level-up available after this VM closes.
    /// </summary>
    public sealed class GlobalMapGeneralLevelUpScreen : Screen
    {
        private static readonly FieldInfo LeaderField = AccessTools.Field(typeof(LeaderLevelUpVM), "m_Leader");
        private static readonly FieldInfo StatsField = AccessTools.Field(typeof(LeaderLevelUpVM), "StatsVM");
        private static readonly FieldInfo SkillsField = AccessTools.Field(typeof(LeaderLevelUpVM), "SkillsToChoose");
        private static readonly FieldInfo SelectedField = AccessTools.Field(typeof(LeaderLevelUpVM), "SelectedSkill");
        private static readonly FieldInfo GuaranteedField = AccessTools.Field(typeof(LeaderLevelUpVM), "GuaranteedSkillVM");

        public override string Key => "overlay.worldmap_general_levelup";
        public override int Layer => 17;
        public override bool Exclusive => true;
        public override string ScreenName => Loc.T("crusade.levelup_title");

        private static LeaderLevelUpVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            var vm = rc?.GlobalMapVM?.LeaderLevelUpVM?.Value;
            return vm != null && !vm.IsDisposed ? vm : null;
        }

        public override bool IsActive() => Vm() != null;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            var leader = LeaderField?.GetValue(vm) as ArmyLeader;
            var stats = StatsField?.GetValue(vm);
            var skills = SkillsField?.GetValue(vm) as IList<ArmyLeaderSkillVM>;
            var guaranteed = GuaranteedField?.GetValue(vm) as ArmyLeaderSkillVM;
            string name = leader?.LocalizedName ?? "";
            var ct = UIStrings.Instance.CrusadeTexts;
            string prefix = "gl:" + vm.GetHashCode() + ":";

            b.BeginStop("summary").PushContext(name, "group");
            b.AddItem(ControlId.Structural(prefix + "current"), GraphNodes.Text(() => Loc.T("crusade.level", new { level = vm.CurrentLevel })));
            b.AddItem(ControlId.Structural(prefix + "next"), GraphNodes.Text(() => Loc.T("crusade.levelup_next", new { level = vm.CurrentLevel + 1 })));
            b.PopContext();

            b.BeginStop("stats").PushContext(Loc.T("crusade.levelup_gains"), "group");
            AddRise(b, prefix + "attack", ct.AttackBonusTooltipHeader, stats, "AttackBonusRise");
            AddRise(b, prefix + "defense", ct.DefenceBonusTooltipHeader, stats, "DefenseBonusRise");
            AddRise(b, prefix + "spell", ct.SpellBonusTooltipHeader, stats, "SpellStrengthRise");
            AddRise(b, prefix + "army", ct.ArmySizeIncreaseTooltipHeader, stats, "ArmySizeRise");
            AddRise(b, prefix + "infirmary", ct.InfirmaryTooltipHeader, stats, "InfirmaryRise");
            AddRise(b, prefix + "mana", ct.MagicReserves, stats, "MaxManaRise");
            b.PopContext();

            if (guaranteed != null)
            {
                b.BeginStop("guaranteed").PushContext((string)ct.GuaranteedAbilityText, "group");
                b.AddItem(ControlId.Referenced(guaranteed, prefix + "guaranteed"), SkillNode(guaranteed, null));
                b.PopContext();
            }

            if (skills != null && skills.Count > 0)
            {
                b.BeginStop("choices").PushContext((string)ct.ChooseAbility, "list");
                for (int i = 0; i < skills.Count; i++)
                {
                    var skill = skills[i];
                    if (skill == null) continue;
                    b.AddItem(ControlId.Referenced(skill, prefix + "choice:" + i),
                        SkillNode(skill, SelectedField));
                }
                b.PopContext();
            }

            b.BeginStop("actions");
            b.AddItem(ControlId.Structural(prefix + "confirm"), GraphNodes.Button(
                () => UIStrings.Instance.CommonTexts.Confirm,
                () => Vm()?.Confirm(),
                () => CanConfirm(Vm())));
            b.AddItem(ControlId.Structural(prefix + "close"), GraphNodes.Button(
                () => Loc.T("action.close"), () => Vm()?.Close()));
        }

        private static NodeVtable SkillNode(ArmyLeaderSkillVM skill, FieldInfo selectedField)
        {
            var vt = selectedField == null
                ? GraphNodes.Text(() => skill.Name.Value, () => skill.GetTooltipTemplate())
                : GraphNodes.ChoiceOption(
                    () => skill.Name.Value,
                    () => SelectedSkill(Vm()) == skill.Skill,
                    () => { if (!skill.IsDisposed) skill.ConfirmAction(); });
            if (selectedField != null)
                vt.OnTooltip = () =>
                {
                    var template = skill.GetTooltipTemplate();
                    if (template != null) TooltipScreen.Open(template);
                };
            return vt;
        }

        private static bool CanConfirm(LeaderLevelUpVM vm)
        {
            if (vm == null) return false;
            var options = SkillsField?.GetValue(vm) as IList<ArmyLeaderSkillVM>;
            return options == null || options.Count == 0 || SelectedSkill(vm) != null;
        }

        private static BlueprintLeaderSkill SelectedSkill(LeaderLevelUpVM vm)
        {
            var reactive = SelectedField?.GetValue(vm);
            return reactive?.GetType().GetProperty("Value")?.GetValue(reactive, null) as BlueprintLeaderSkill;
        }

        private static void AddRise(GraphBuilder b, string key, string name, object stats, string fieldName)
        {
            var field = stats != null ? AccessTools.Field(stats.GetType(), fieldName) : null;
            b.AddItem(ControlId.Structural(key), GraphNodes.Text(() =>
            {
                var reactive = field?.GetValue(stats);
                var value = reactive?.GetType().GetProperty("Value")?.GetValue(reactive, null);
                int rise = value is int i ? i : 0;
                return Loc.T("crusade.levelup_increase", new { name, amount = rise >= 0 ? "+" + rise : rise.ToString() });
            }));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Vm()?.Close());
        }
    }
}
