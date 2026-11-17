using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Controllers.Rest.State;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.MVVM._PCView.Rest;
using Kingmaker.UI.MVVM._VM.Rest;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Accessible counterpart to the game's Alchemy craft page. The game opens separate potion and meal
    /// selection pages from the Alchemist panel; this child screen keeps that flow, while exposing the
    /// live recipe VM, its eligibility, ingredients, DC, and potion caster-level choice.
    /// </summary>
    public sealed class CraftRecipeScreen : Screen
    {
        private enum CraftKind { Potion, Cooking, Scroll }

        private readonly RestVM _rest;
        private readonly CraftKind _kind;
        private readonly string _key;
        private RestCraftRecipeVM _recipe;
        private bool _itemsLoaded;

        private CraftRecipeScreen(RestVM rest, CraftKind kind)
        {
            _rest = rest;
            _kind = kind;
            _key = "rest.craft:" + rest.GetHashCode() + ":" + kind.ToString().ToLowerInvariant() + ":";
            Wrap = true;
        }

        public static void Open(RestVM rest, bool potion)
            => Open(rest, potion ? CraftKind.Potion : CraftKind.Cooking);

        public static void OpenScroll(RestVM rest)
            => Open(rest, CraftKind.Scroll);

        private static void Open(RestVM rest, CraftKind kind)
        {
            if (rest == null) return;
            var parent = ScreenManager.Current;
            var role = kind == CraftKind.Scroll
                ? Kingmaker.Controllers.Rest.State.CampingRoleType.ScrollScribe
                : Kingmaker.Controllers.Rest.State.CampingRoleType.Alchemist;
            var stage = kind == CraftKind.Cooking ? CraftStage.CraftSecondType : CraftStage.CraftFirstType;
            EventBus.RaiseEvent(delegate(IRestRoleUIStageEvents h)
            {
                h.RestStageOpened(role, stage);
            });
            parent?.PushChild(new CraftRecipeScreen(rest, kind));
        }

        public override string Key => _key;
        public override string ScreenName => TitleKey();
        public override bool IsActive() => false; // opened only as a child of the camp screen
        private RestCraftRecipeVM Recipe
        {
            get
            {
                if (_recipe == null)
                    _recipe = _kind == CraftKind.Potion ? _rest.AlchemyRecipeVM
                        : _kind == CraftKind.Cooking ? _rest.BrothRecipeVM : _rest.ScrollsRecipeVM;
                if (!_itemsLoaded && _recipe.RecipeSelector.Value == null)
                {
                    _recipe.CollectItems();
                    _itemsLoaded = true;
                }
                else if (!_itemsLoaded)
                {
                    _itemsLoaded = true;
                }
                return _recipe;
            }
        }

        public override void Build(GraphBuilder b)
        {
            var vm = Recipe;
            string title = TitleKey();
            var items = vm.Items;
            var selected = vm.SelectedItem.Value;

            b.BeginStop(_key + "recipes").PushContext(title);
            if (items == null || items.Count == 0)
            {
                b.AddItem(ControlId.Structural(_key + "empty"),
                    GraphNodes.Text(() => EmptyListText()));
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    int index = i;
                    var vt = RecipeOption(item, vm, index, items.Count);
                    b.AddItem(ControlId.Structural(_key + "recipe:" + i), vt);
                }
                int selectedIndex = items.IndexOf(selected);
                if (selectedIndex >= 0)
                    b.SetStart(ControlId.Structural(_key + "recipe:" + selectedIndex));
            }
            b.AddItem(ControlId.Structural(_key + "clear"), GraphNodes.Button(
                () => Loc.T("craft.clear_selection"), () => vm.SelectedItem.Value = null));
            b.PopContext();

            b.BeginStop(_key + "details").PushContext(Loc.T("craft.details"));
            b.AddItem(ControlId.Structural(_key + "selected"), GraphNodes.Text(
                () => SelectedSummary(vm), () => vm.SelectedItem.Value?.Tooltip));
            if (_kind != CraftKind.Cooking)
                EmitCasterLevel(b, vm);
            b.AddItem(ControlId.Structural(_key + "ingredients"), GraphNodes.Text(
                () => IngredientSummary(vm)));
            b.AddItem(ControlId.Structural(_key + "effect"), GraphNodes.Text(
                () => SelectedEffect(vm),
                () => vm.SelectedItem.Value?.Tooltip));
            b.PopContext();

            b.BeginStop(_key + "actions");
            b.AddItem(ControlId.Structural(_key + "apply"), GraphNodes.Button(
                () => Loc.T(_kind == CraftKind.Potion ? "craft.potion.apply"
                    : _kind == CraftKind.Cooking ? "craft.cooking.apply" : "craft.scroll.apply"),
                () => Apply(vm), () => vm.SelectedItem.Value == null || vm.SelectedItem.Value.CanCraft));
            b.AddItem(ControlId.Structural(_key + "back"), GraphNodes.Button(
                () => Loc.T(_kind == CraftKind.Scroll ? "craft.back_to_scribing" : "craft.back_to_alchemy"), ReturnToRole));
        }

        private NodeVtable RecipeOption(RestBaseRecipeItemVM item, RestCraftRecipeVM vm, int index, int count)
        {
            Func<bool> selected = () => ReferenceEquals(vm.SelectedItem.Value, item);
            Func<bool> available = () => item.CanCraft;
            var vt = GraphNodes.ChoiceOption(() => OptionLabel(item), selected, () =>
            {
                if (!available()) return;
                vm.SelectedItem.Value = item;
                Tts.Speak(OptionLabel(item), interrupt: true);
            }, GraphNodes.Position(index + 1, count));
            vt.Announcements = vt.Announcements.Concat(new[] { GraphNodes.DisabledPart(available) }).ToList();
            vt.OnTooltip = () =>
            {
                if (item.Tooltip != null) TooltipScreen.Open(item.Tooltip);
            };
            return vt;
        }

        private string OptionLabel(RestBaseRecipeItemVM item)
        {
            string status = Availability(item);
            string details = _kind == CraftKind.Cooking
                ? Loc.T("craft.cooking.option_details", new { dc = item.DC, status })
                : Loc.T(_kind == CraftKind.Scroll ? "craft.scroll.option_details" : "craft.potion.option_details",
                    new { level = item.CraftItemLevel, dc = item.DC, status });
            return Loc.T("craft.recipe_option", new { name = item.CraftItemName, details });
        }

        private static string Availability(RestBaseRecipeItemVM item)
        {
            if (item.CanCraft) return Loc.T("craft.available");
            if (item is RestCraftRecipeItemVM potion)
            {
                var info = potion.Item;
                var missing = new List<string>();
                if (!info.IsKnowAbility) missing.Add(Loc.T("craft.spell_not_known"));
                if (!info.IsHaveFeature) missing.Add(Loc.T("craft.missing_feature"));
                if (!info.IsHaveItem) missing.Add(Loc.T("craft.missing_item"));
                if (!info.IsHaveResources) missing.Add(Loc.T("craft.missing_ingredients"));
                if (missing.Count > 0) return string.Join(", ", missing);
            }
            return Loc.T("craft.missing_ingredients");
        }

        private string SelectedSummary(RestCraftRecipeVM vm)
        {
            var item = vm.SelectedItem.Value;
            if (item == null) return Loc.T("craft.no_selection");
            string result = Loc.T("craft.selected_summary", new
            {
                name = item.CraftItemName,
                dc = vm.DCValue.Value,
                bonus = vm.RollValue.Value,
                chance = vm.SuccessChanceValue.Value,
                status = Availability(item),
            });
            return TextUtil.StripRichText(result);
        }

        private string EmptyListText()
        {
            if (_kind == CraftKind.Cooking) return Loc.T("craft.no_recipes");
            if (_kind == CraftKind.Scroll)
            {
                var scribe = Game.Instance?.Player?.Camping?.GetCharacterByRoleType(
                    Kingmaker.Controllers.Rest.State.CampingRoleType.ScrollScribe, true);
                return Loc.T(scribe == null ? "craft.no_scroll_scribe" : "craft.no_scroll_spells");
            }
            var primary = Game.Instance?.Player?.Camping?.GetCharacterByRoleType(CampingRoleType.Alchemist, true);
            return Loc.T(primary == null ? "craft.no_potion_primary" : "craft.no_potion_spells");
        }

        private static string SelectedEffect(RestCraftRecipeVM vm)
        {
            var item = vm.SelectedItem.Value;
            if (item == null) return "";
            string effect = item.CraftItemDesc ?? "";
            if (item is RestBrothRecipeItemVM meal)
            {
                var favorite = meal.BrothRecipe.UnitBuffs.FirstOrDefault();
                if (favorite?.Unit != null)
                    effect += "\n\n" + string.Format(UIStrings.Instance.Tooltips.FavoriteDish,
                        favorite.Unit.LocalizedName.String);
            }
            return TextUtil.StripRichText(effect);
        }

        private static string IngredientSummary(RestCraftRecipeVM vm)
        {
            var item = vm.SelectedItem.Value;
            if (item == null) return Loc.T("craft.ingredients_none");
            if (item is RestBrothRecipeItemVM meal)
            {
                var entries = meal.BrothRecipe.Ingredients;
                if (entries == null || entries.Length == 0) return Loc.T("craft.ingredients_none");
                return Loc.T("craft.ingredients", new
                {
                    value = string.Join(", ", entries.Select(x => TextUtil.StripRichText(x.Item.Name + " × " + x.Count))),
                });
            }
            if (vm.IngredientItems == null || vm.IngredientItems.Count == 0)
                return Loc.T("craft.ingredients_none");
            return Loc.T("craft.ingredients", new
            {
                value = string.Join(", ", vm.IngredientItems.Select(x => TextUtil.StripRichText(x.Ingredient.Name))),
            });
        }

        private void EmitCasterLevel(GraphBuilder b, RestCraftRecipeVM vm)
        {
            var caster = vm.CasterLevelVM;
            var current = caster?.SelectedItem.Value;
            if (current == null) return;
            b.AddItem(ControlId.Structural(_key + "caster-level"), GraphNodes.Dropdown(
                () => Loc.T("craft.caster_level"),
                () => current.CasterLevel.ToString(),
                () =>
                {
                    var levels = caster.CasterLevelSelector?.EntitiesCollection;
                    if (levels == null || levels.Count == 0) return;
                    var labels = levels.Select(x => Loc.T("craft.caster_level_option", new
                    {
                        level = x.CasterLevel,
                        dc = x.Difficulty,
                        days = x.DaysCraft,
                        available = x.IsAvailable.Value ? Loc.T("craft.available") : Loc.T("state.disabled"),
                    })).ToList();
                    int selected = levels.IndexOf(current);
                    ChoiceSubmenuScreen.Open(Loc.T("craft.caster_level"), labels, selected, index =>
                    {
                        if (index >= 0 && index < levels.Count && levels[index].IsAvailable.Value)
                            caster.SelectedItem.Value = levels[index];
                    });
                }, () => caster.IsAvailable.Value));
        }

        private void Apply(RestCraftRecipeVM vm)
        {
            vm.ApplyItemToCraft(() =>
            {
                CloseGameStage();
                ParentScreen?.RemoveChild(this);
            });
        }

        private void ReturnToRole()
        {
            EventBus.RaiseEvent(delegate(IRestRoleUIStageEvents h)
            {
                h.RestStageOpened(RoleType(), CraftStage.UnitSelection);
            });
            ParentScreen?.RemoveChild(this);
        }

        private void CloseGameStage()
        {
            EventBus.RaiseEvent(delegate(IRestRoleUIStageEvents h)
            {
                h.RestStageClosed(RoleType(), _kind == CraftKind.Cooking ? CraftStage.CraftSecondType : CraftStage.CraftFirstType,
                    fromCloseButton: true);
            });
        }

        private string TitleKey() => Loc.T(_kind == CraftKind.Potion ? "craft.potion.title"
            : _kind == CraftKind.Cooking ? "craft.cooking.title" : "craft.scroll.title");

        private Kingmaker.Controllers.Rest.State.CampingRoleType RoleType()
            => _kind == CraftKind.Scroll ? Kingmaker.Controllers.Rest.State.CampingRoleType.ScrollScribe
                : Kingmaker.Controllers.Rest.State.CampingRoleType.Alchemist;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => ReturnToRole());
        }
    }
}
