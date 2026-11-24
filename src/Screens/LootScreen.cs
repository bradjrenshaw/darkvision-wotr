using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized Leave/remove-loot labels)
using Kingmaker.UI.MVVM._VM.Loot;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The loot window (<see cref="LootVM"/>) shown when you loot a container or corpse — driven, like the
    /// dialogue screen, off a reactive on the in-game static HUD (<c>LootContextVM.LootVM</c>), not a
    /// RootUIContext service window. Each loot source (a corpse/chest = <see cref="LootObjectVM"/>) is its
    /// own Tab-stop list of items; arrows move within a source, Tab moves between sources. Enter takes an
    /// item — immediate mode drops the emptied slot from the next render, so focus slides to the nearest
    /// remaining item and announces it. "Take all" / Skin / the zone-exit controls are their own stops;
    /// Escape closes. Chest-type windows (LootVM.ShowStash) add the party-stash side as its own stop:
    /// your inventory (filters + items), Enter deposits an item into the container.
    ///
    /// <para>ONE-SLOT containers (a puzzle socket, a quest pedestal — <c>LootWindowMode.OneSlot</c>)
    /// are a different window in the game and here: the single slot (Enter takes its item back),
    /// then your inventory with the items that FIT listed first (the game dims the rest; Enter places
    /// one), then the game's Apply button. No take-all — there is no collector in this mode.</para>
    /// </summary>
    public sealed class LootScreen : Screen
    {
        public override string Key => "ctx.loot";
        public override string ScreenName => Loc.T("screen.loot");
        public override int Layer => 15; // over the in-game context + service windows, alongside dialogue

        private static LootVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        public override bool IsActive() => Vm() != null;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "loot:" + vm.GetHashCode() + ":"; // a new LootVM = a fresh window = fresh keys

            if (vm.Mode == LootContextVM.LootWindowMode.OneSlot && vm.InteractionSlot != null)
            {
                BuildOneSlot(b, vm, k);
                return;
            }

            // One Tab-stop list per loot source; only slots that still hold an item emit, so taking an
            // item slides focus to the nearest survivor (and an emptied source drops out entirely).
            if (vm.ContextLoot != null)
            {
                int s = 0;
                foreach (var obj in vm.ContextLoot)
                {
                    if (obj?.SlotsGroup == null) { s++; continue; }
                    bool any = false;
                    foreach (var slot in obj.SlotsGroup.VisibleCollection)
                        if (slot != null && slot.HasItem) { any = true; break; }
                    if (!any) { s++; continue; }

                    b.BeginStop(k + "src:" + s);
                    b.PushContext(string.IsNullOrEmpty(obj.DisplayName) ? Loc.T("loot.container") : obj.DisplayName, "list");
                    int i = 0;
                    foreach (var slot in obj.SlotsGroup.VisibleCollection)
                    {
                        if (slot != null && slot.HasItem)
                        {
                            // Rows share a LANDING GROUP per container, so taking one lands focus on the
                            // container's next row (then its previous). Without it, taking the TOP row
                            // fell to the generic backward walk — the previous container's last item
                            // (tester repro: "as if I'd pressed Shift+Tab").
                            var row = GraphNodes.LootItem(vm, slot);
                            row.LandGroup = k + "src:" + s;
                            b.AddItem(ControlId.Referenced(slot, k + "src:" + s + ":item:" + i), row);
                        }
                        i++;
                    }
                    b.PopContext();
                    s++;
                }
            }

            // Take all — gated like the game's Collect All button (CanCollectItems): trophy items needing
            // a skin roll don't count, so a corpse holding only those reads it as unavailable instead of
            // silently doing nothing.
            b.BeginStop(k + "takeall").AddItem(ControlId.Structural(k + "takeall"),
                GraphNodes.Button(() => Loc.T("loot.take_all"), vm.CollectAll,
                    () => vm.LootCollector != null ? vm.LootCollector.CanCollectItems : vm.HasItemsToLoot,
                    sound: null)); // CollectAll plays the game's own loot sounds

            // Skin — present while the window holds trophy items (HasItemsToSkinning), mirroring the
            // game's skin button: one roll over every trophy in the window (the VM plays the LootSkinning
            // sound), then announce the game's own success/half/failure line (the real view raises it as
            // a warning notification). Successfully skinned items become takeable.
            if (vm.LootCollector != null && vm.LootCollector.HasItemsToSkinning)
            {
                var collector = vm.LootCollector;
                b.BeginStop(k + "skin").AddItem(ControlId.Structural(k + "skin"),
                    GraphNodes.Button(
                        () => TextUtil.StripRichText(UIStrings.Instance.LootWindow.Skinning),
                        () =>
                        {
                            collector.UseSkinning(out int max, out int current);
                            var line = current == max ? UIStrings.Instance.CommonTexts.SkinningSuccess
                                : current != 0 ? UIStrings.Instance.CommonTexts.SkinningHalfSuccess
                                : UIStrings.Instance.CommonTexts.SkinningFailed;
                            Tts.Speak(TextUtil.StripRichText(line), interrupt: true);
                        },
                        () => collector.HasItemsToSkinning,
                        sound: null)); // the VM plays LootSkinning itself
            }

            // The party-stash side (chests, the personal chest, zone exits — LootVM.ShowStash): your
            // inventory with its search / sort / type filters, then the items; Enter on an item puts
            // it INTO the container (the sighted double-click / drag onto the chest).
            var stash = vm.ShowStash ? vm.InventoryStash : null;
            if (stash?.ItemSlotsGroup != null)
            {
                b.BeginStop(k + "stash");
                b.PushContext(Loc.T("loot.your_inventory"));
                InventoryScreen.EmitItemFilters(b, stash.ItemsFilter, stash.ItemSlotsGroup, k + "stash:");
                b.PushContext(Loc.T("loot.items"), "list");
                int si = 0; bool anyStash = false;
                foreach (var slot in stash.ItemSlotsGroup.VisibleCollection)
                {
                    if (slot != null && slot.HasItem)
                    {
                        anyStash = true;
                        // Identity = the ITEM ENTITY, not the slot VM: the stash group re-deals its
                        // items into positional slots after every transfer, so a slot-keyed node just
                        // silently changes content under focus; an entity key loses the deposited
                        // item's node instead, and focus slides to the neighbour and announces it.
                        // ControlId equality is the STRUCTURAL key alone, so the key must carry the
                        // entity too: a positional "item:N" key makes the re-dealt neighbour look like
                        // the same node (silent). Reference hash, like the vendor sheet's row keys.
                        object ent = (object)slot.Item.Value ?? slot;
                        var row = GraphNodes.StashItem(vm, slot);
                        row.LandGroup = k + "stash"; // depositing lands on the next stash row, not the filters
                        b.AddItem(ControlId.Referenced(ent, k + "stash:item:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ent)), row);
                    }
                    si++;
                }
                if (!anyStash) b.AddItem(ControlId.Structural(k + "stash:none"), GraphNodes.Text(() => Loc.T("loot.stash_empty")));
                b.PopContext();
                b.PopContext();
            }

            // Zone-exit variant (leaving the area with uncollected loot): mirror the game's extra
            // controls — a Leave button (proceed WITHOUT collecting; in this mode Take all also leaves,
            // both run the deferred transition) and the remove-uncollected-loot toggle. Escape stays the
            // cancel (vm.Close keeps you in the area), exactly like the game's X / EscManager.
            if (vm.Mode == LootContextVM.LootWindowMode.ZoneExit)
            {
                b.BeginStop(k + "leave").AddItem(ControlId.Structural(k + "leave"),
                    GraphNodes.Button(() => TextUtil.StripRichText(UIStrings.Instance.LootWindow.LeaveZone), vm.LeaveZone));
                b.BeginStop(k + "removeloot").AddItem(ControlId.Structural(k + "removeloot"),
                    GraphNodes.Toggle(
                        () => TextUtil.StripRichText(UIStrings.Instance.LootWindow.RemoveUncollectedLootHint),
                        () => vm.RemoveUncollectedLoot.Value,
                        vm.SwitchRemoveUncollectedLoot));
            }
        }

        // ----- the one-slot window -----

        private static readonly List<Kingmaker.Items.ItemEntity> _carried = new List<Kingmaker.Items.ItemEntity>();

        // The container's slots group (one container per one-slot window).
        private static Kingmaker.UI.MVVM._VM.Slots.SlotsGroupVM<Kingmaker.UI.MVVM._VM.Slots.ItemSlotVM> OneSlotGroup(LootVM vm)
        {
            if (vm.ContextLoot == null) return null;
            foreach (var obj in vm.ContextLoot)
                if (obj?.SlotsGroup != null) return obj.SlotsGroup;
            return null;
        }

        // Mirrors InteractionSlotPartVM.WantInsert's first guard: nothing goes in while it holds an item.
        private static bool OneSlotOccupied(LootVM vm)
        {
            var group = OneSlotGroup(vm);
            return group?.MechanicCollection != null && group.MechanicCollection.Items.Count > 0;
        }

        private static void BuildOneSlot(GraphBuilder b, LootVM vm, string k)
        {
            var part = vm.InteractionSlot;
            var group = OneSlotGroup(vm);

            // The slot itself: what it holds (Enter takes it back out), or that it is empty.
            b.BeginStop(k + "slot");
            // A puzzle's container reads by the board piece it belongs to (the game's own title for
            // these is a generic "Loot").
            string title = WrathAccess.Exploration.SlabPuzzle.ContainerName(group?.MechanicCollection)
                ?? (string.IsNullOrWhiteSpace(part.Name) ? Loc.T("loot.container") : TextUtil.StripRichText(part.Name));
            b.PushContext(title, "list");
            if (!string.IsNullOrWhiteSpace(part.Description))
                b.AddItem(ControlId.Structural(k + "slot:desc"), GraphNodes.Text(() => TextUtil.StripRichText(part.Description)));
            Kingmaker.UI.MVVM._VM.Slots.ItemSlotVM held = null;
            if (group != null)
                foreach (var slot in group.VisibleCollection)
                    if (slot != null && slot.HasItem) { held = slot; break; }
            if (held != null)
                b.AddItem(ControlId.Referenced(held, k + "slot:item"), GraphNodes.LootItem(vm, held));
            else
                b.AddItem(ControlId.Structural(k + "slot:empty"), GraphNodes.Text(() => Loc.T("loot.slot_empty")));
            b.PopContext();

            // Your inventory. The game shows every item and dims the ones the slot refuses; by ear
            // the ones that fit come first, the rest follow marked as not fitting.
            var insertable = vm.InventoryStash?.InsertableSlotsGroup;
            if (insertable != null)
            {
                b.BeginStop(k + "stash");
                b.PushContext(Loc.T("loot.your_inventory"), "list");
                bool any = false;
                for (int pass = 0; pass < 2; pass++)
                    foreach (var slot in insertable.VisibleCollection)
                    {
                        if (slot == null || !slot.HasItem) continue;
                        if (slot.CanInsert.Value != (pass == 0)) continue;
                        any = true;
                        // Entity-keyed, like the chest stash rows: the group re-deals its slots after a transfer.
                        object ent = (object)slot.Item.Value ?? slot;
                        var row = InsertableItem(vm, slot);
                        row.LandGroup = k + "stash";
                        b.AddItem(ControlId.Referenced(ent, k + "stash:item:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ent)), row);
                    }
                if (!any) b.AddItem(ControlId.Structural(k + "stash:none"), GraphNodes.Text(() => Loc.T("loot.stash_empty")));
                // The game lists ONLY what fits; a sighted player sees at a glance which of their
                // slabs are missing from the list. Say it: the carried slabs it is not offering.
                if (held == null)
                {
                    WrathAccess.Exploration.SlabPuzzle.CarriedSlabs(group?.MechanicCollection, _carried);
                    foreach (var slot in insertable.VisibleCollection)
                        if (slot != null && slot.HasItem && slot.CanInsert.Value) _carried.Remove(slot.Item.Value);
                    if (_carried.Count > 0)
                    {
                        var notes = new List<string>();
                        foreach (var item in _carried)
                        {
                            var note = WrathAccess.Exploration.SlabPuzzle.ItemSuffix(item.Blueprint);
                            if (note != null && !notes.Contains(note)) notes.Add(note);
                        }
                        string line = Loc.T("loot.not_fitting", new { items = string.Join(", ", notes) });
                        b.AddItem(ControlId.Structural(k + "stash:unfit"), GraphNodes.Text(() => line));
                    }
                }
                b.PopContext();
            }

            // The game's own button for this window: Apply = close.
            b.BeginStop(k + "apply").AddItem(ControlId.Structural(k + "apply"),
                GraphNodes.Button(() => TextUtil.StripRichText(UIStrings.Instance.SettingsUI.Apply), vm.Close));
        }

        // An inventory item in the one-slot window: Enter places it (the sighted double-click —
        // InsertableLootSlotView.Insert → LootVM.HandleTryInsertSlot), refused aloud when the slot
        // won't take it (the game's "can not insert" dimming) or already holds something.
        private static NodeVtable InsertableItem(LootVM vm, InsertableLootSlotVM slot)
        {
            System.Func<string> label = () =>
            {
                var name = ItemNames.Of(slot);
                int count = slot.Count.Value;
                return count > 1 ? name + ", " + count : name;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => slot.CanInsert.Value ? null : Loc.T("loot.does_not_fit"),
                        live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                OnActivate = () =>
                {
                    if (!slot.CanInsert.Value) { Tts.Speak(Loc.T("loot.does_not_fit"), interrupt: true); return; }
                    if (OneSlotOccupied(vm)) { Tts.Speak(Loc.T("loot.slot_occupied"), interrupt: true); return; }
                    vm.HandleTryInsertSlot(slot);
                    if (OneSlotOccupied(vm)) UiSound.Play(Kingmaker.UI.UISoundType.LootCollectOne);
                    else Tts.Speak(Loc.T("loot.does_not_fit"), interrupt: true);
                },
                OnTooltip = () => ItemNodes.OpenItemInfo(slot),
            };
        }

        // Escape closes the window (the game's own EscManager is muted while focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.Close());
        }
    }
}
