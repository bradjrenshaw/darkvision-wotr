using Kingmaker.Items;
using Kingmaker.UI.MVVM._VM.Slots;
using WrathAccess.Exploration;

namespace WrathAccess.UI
{
    /// <summary>
    /// The spoken name of an item in a slot: the game's own display name, plus — for the few items
    /// the game tells apart ONLY by their icon — what the icon shows. The puzzle slabs are the case
    /// in point: four different slabs all named "Slab with Two White Symbols"
    /// (<see cref="SlabPuzzle.ItemSuffix"/>). One chokepoint so every item list reads them alike.
    /// </summary>
    internal static class ItemNames
    {
        public static string Of(ItemSlotVM slot)
        {
            if (slot == null) return "";
            var name = slot.DisplayName.Value;
            if (string.IsNullOrEmpty(name)) name = slot.Item.Value?.Name ?? "item";
            return Decorate(name, slot.Item.Value);
        }

        public static string Decorate(string name, ItemEntity item)
        {
            var note = SlabPuzzle.ItemSuffix(item?.Blueprint);
            return note == null ? name : Loc.T("item.with_note", new { name, note });
        }
    }
}
