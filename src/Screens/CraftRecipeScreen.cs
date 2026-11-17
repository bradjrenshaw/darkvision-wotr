using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Items.Equipment; // UsableItemType
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Controllers.Rest.State;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.Common; // UIUtility.AddSign
using Kingmaker.UI.MVVM._PCView.Rest;
using Kingmaker.UI.MVVM._VM.Rest;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The camp's craft RECIPE window (<see cref="RestCraftRecipeVM"/>: potions and meals from the
    /// Alchemist panel, scrolls from the Scroll Scribe panel), laid out as the PC view is
    /// (RestCraftRecipePCView): the recipe list on the left — a radio list where EVERY recipe can be
    /// selected, craftable or not (a forbidden one names what is missing; only Ok is blocked), and
    /// Enter on the selected one deselects it — then the selected recipe's block (name, description,
    /// the check's DC / skill bonus / success chance, caster level, one row per ingredient as
    /// needed / owned), the craft kit and its requirements, and the Ok and Back buttons.
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
            var role = kind == CraftKind.Scroll ? CampingRoleType.ScrollScribe : CampingRoleType.Alchemist;
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
            var rest = UIStrings.Instance.Rest;
            var items = vm.Items;
            var selected = vm.SelectedItem.Value;

            // ---- the recipe list (the left column) ----
            b.BeginStop(_key + "recipes").PushContext(TitleKey());
            if (items == null || items.Count == 0)
            {
                b.AddItem(ControlId.Structural(_key + "empty"),
                    GraphNodes.Text(() => EmptyListText()));
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                    b.AddItem(ControlId.Structural(_key + "recipe:" + i), RecipeOption(items[i], vm, i, items.Count));
                int selectedIndex = items.IndexOf(selected);
                if (selectedIndex >= 0)
                    b.SetStart(ControlId.Structural(_key + "recipe:" + selectedIndex));
            }
            b.PopContext();

            // ---- the selected recipe (the right column): name, description, the check, caster level,
            //      ingredients. With nothing selected the game shows its "choose" prompt and the role's
            //      explanation instead (RestCraftRecipeBaseView.SelectItem). ----
            b.BeginStop(_key + "details").PushContext(Loc.T("craft.details"));
            b.AddItem(ControlId.Structural(_key + "selected"), GraphNodes.Text(
                () => SelectedName(vm), () => vm.SelectedItem.Value?.Tooltip));
            b.AddItem(ControlId.Structural(_key + "effect"), GraphNodes.Text(
                () => SelectedDescription(vm), () => vm.SelectedItem.Value?.Tooltip));
            b.AddItem(ControlId.Structural(_key + "dc"), GraphNodes.Text(
                () => Loc.T("craft.dc", new { dc = vm.DCValue.Value }), () => vm.DCTooltipTemplate));
            b.AddItem(ControlId.Structural(_key + "bonus"), GraphNodes.Text(
                () => Loc.T("craft.bonus", new { bonus = UIUtility.AddSign(vm.RollValue.Value) }), () => vm.D20TooltipTemplate));
            b.AddItem(ControlId.Structural(_key + "chance"), GraphNodes.Text(
                () => Loc.T("craft.chance", new { chance = vm.SuccessChanceValue.Value })));
            if (_kind != CraftKind.Cooking)
                EmitCasterLevel(b, vm);
            // One row per ingredient, as the game's slots: needed / owned (Space = the item's tooltip).
            // The list is the bound view's (UpdateIngredients on each pick); it is only current while a
            // recipe is selected.
            if (selected != null && vm.IngredientItems != null)
                for (int i = 0; i < vm.IngredientItems.Count; i++)
                {
                    var ing = vm.IngredientItems[i];
                    b.AddItem(ControlId.Structural(_key + "ingredient:" + i), GraphNodes.Text(
                        () => Loc.T("craft.ingredient", new
                        {
                            name = TextUtil.StripRichText(ing.Ingredient.Name),
                            required = ing.RequiredCount,
                            owned = ing.InventoryCount,
                        }),
                        () => ing.ItemTooltip));
                }
            b.PopContext();

            // ---- the craft kit and what the crafter needs (RestCraftSetPCView; potions and scrolls) ----
            var set = vm.CraftSetVM;
            if (set != null)
            {
                b.BeginStop(_key + "requirements").PushContext(Loc.T("craft.requirements"));
                b.AddItem(ControlId.Structural(_key + "kit"), GraphNodes.Text(
                    () => set.KitIcon != null ? Loc.T("craft.kit", new { name = set.KitName, level = set.KitLevel }) : set.KitName));
                var restrictions = set.Restrictions;
                if (restrictions != null)
                    for (int i = 0; i < restrictions.Count; i++)
                    {
                        var r = restrictions[i];
                        b.AddItem(ControlId.Structural(_key + "restriction:" + i), GraphNodes.Text(
                            () => Loc.T(r.IsAllowed ? "craft.requirement_met" : "craft.requirement_unmet", new { name = r.DisplayName })));
                    }
                b.PopContext();
            }

            // ---- the buttons: Ok (the game's Apply; greyed on a recipe that can't be crafted) and Back ----
            b.BeginStop(_key + "actions");
            b.AddItem(ControlId.Structural(_key + "apply"), GraphNodes.Button(
                () => rest.Apply,
                () => Apply(vm), () => vm.SelectedItem.Value == null || vm.SelectedItem.Value.CanCraft));
            b.AddItem(ControlId.Structural(_key + "back"), GraphNodes.Button(() => rest.Back, ReturnToRole));
        }

        // A recipe row. Enter is the row's click (SelectionGroupEntityView.OnClick): select it, or
        // deselect it when it already is (the group allows switching off). Forbidden recipes select
        // too — the game only greys Ok for them — so their details and ingredients can be read.
        private NodeVtable RecipeOption(RestBaseRecipeItemVM item, RestCraftRecipeVM vm, int index, int count)
        {
            var vt = GraphNodes.ChoiceOption(() => OptionLabel(item),
                () => ReferenceEquals(vm.SelectedItem.Value, item),
                () =>
                {
                    bool select = !item.IsSelected.Value;
                    item.SetSelectedFromView(select);
                    if (!select) Tts.Speak(Loc.T("craft.deselected"), interrupt: true); // "selected" is the live part
                }, GraphNodes.Position(index + 1, count));
            vt.OnTooltip = () =>
            {
                if (item.Tooltip != null) TooltipScreen.Open(item.Tooltip);
            };
            return vt;
        }

        // The row as the game draws it: name, spell level (potions / scrolls), DC, and — forbidden — why.
        private string OptionLabel(RestBaseRecipeItemVM item)
        {
            string label = _kind == CraftKind.Cooking
                ? Loc.T("craft.meal_row", new { name = item.CraftItemName, dc = item.DC })
                : Loc.T("craft.recipe_row", new { name = item.CraftItemName, level = item.CraftItemLevel, dc = item.DC });
            if (item.IsAvail) return label;
            string reasons = ForbiddenText(item);
            return label + ", " + (reasons.Length > 0
                ? Loc.T("craft.forbidden", new { reasons })
                : Loc.T("craft.forbidden_plain"));
        }

        // The game's forbidden label (RestCraftRecipeItemPCView.SetForbiddenText), in its own words:
        // the craft feat, the kit, the reagents — whichever are missing.
        private static string ForbiddenText(RestBaseRecipeItemVM item)
        {
            var rest = UIStrings.Instance.Rest;
            var parts = new List<string>();
            bool crafted = item.ItemType != UsableItemType.Other; // meals need only their ingredients
            if (!item.IsHaveFeature && crafted) parts.Add(rest.CraftFeature);
            if (!item.IsHaveItem && crafted)
                parts.Add(item.ItemType == UsableItemType.Potion ? rest.AlchemyCraftKit : rest.ScrollCraftKit);
            if (!item.IsHaveResources) parts.Add(rest.CraftResources);
            return string.Join(", ", parts);
        }

        private string SelectedName(RestCraftRecipeVM vm)
        {
            var item = vm.SelectedItem.Value;
            if (item != null) return item.CraftItemName;
            var rest = UIStrings.Instance.Rest;
            return _kind == CraftKind.Scroll ? rest.ChooseScrollText : rest.ChooseRecipeText;
        }

        private string SelectedDescription(RestCraftRecipeVM vm)
        {
            var item = vm.SelectedItem.Value;
            if (item == null)
            {
                var rest = UIStrings.Instance.Rest;
                return TextUtil.StripRichText(_kind == CraftKind.Scroll ? rest.ScrollScribingOpeningText : rest.AlchemyOpeningText);
            }
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

        private string EmptyListText()
        {
            if (_kind == CraftKind.Cooking) return Loc.T("craft.no_recipes");
            if (_kind == CraftKind.Scroll)
            {
                var scribe = Game.Instance?.Player?.Camping?.GetCharacterByRoleType(CampingRoleType.ScrollScribe, true);
                return Loc.T(scribe == null ? "craft.no_scroll_scribe" : "craft.no_scroll_spells");
            }
            var primary = Game.Instance?.Player?.Camping?.GetCharacterByRoleType(CampingRoleType.Alchemist, true);
            return Loc.T(primary == null ? "craft.no_potion_primary" : "craft.no_potion_spells");
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

        private CampingRoleType RoleType()
            => _kind == CraftKind.Scroll ? CampingRoleType.ScrollScribe : CampingRoleType.Alchemist;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => ReturnToRole());
        }
    }
}
