using WrathAccess.ModUI.Imgui;

namespace WrathAccess.ModUI.Mods
{
    /// <summary>
    /// ToyBox (Unity Mod Manager id <c>0ToyBox0</c>). Built on ModKit, so its check boxes and
    /// disclosure arrows come through <see cref="ModKitProbes"/>; everything else is Unity's own
    /// controls and reads generically. This class is where ToyBox-specific presentation goes.
    /// </summary>
    internal sealed class ToyBoxAdapter : ImguiAdapter
    {
        public override string Id => "0ToyBox0";

        // ToyBox draws thin horizontal rules as empty boxes and pads with blank labels; the generic
        // rules already skip textless statics. Decorative glyph-only labels go here as they turn up.
        protected override bool Hide(ImguiNode n) => false;
    }
}
