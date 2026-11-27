using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Modding;
using WrathAccess.ModUI.Imgui;

namespace WrathAccess.ModUI.Hosts
{
    /// <summary>
    /// The game's own native-mod window (<see cref="OwlcatModificationsWindow"/>): the list of Owlcat
    /// "modifications" with an enable toggle each and, for those that draw one, a Settings page. It is
    /// plain IMGUI, so the generic reader handles all of it.
    /// </summary>
    internal sealed class OwlcatModsHost : ModUiHost
    {
        private static readonly MethodInfo ShowMethod = AccessTools.Method(typeof(OwlcatModificationsWindow), "Show");
        private static readonly MethodInfo HideMethod = AccessTools.Method(typeof(OwlcatModificationsWindow), "Hide");

        public override string Id => "owlcat";
        public override string Title => Loc.T("modui.owlcat_title");
        public override bool Present => true;
        public override bool IsOpen => OwlcatModificationsWindow.IsActive;

        public override void Open()
        {
            try { ShowMethod?.Invoke(null, null); }
            catch (Exception e) { Main.Log?.Error("[modui] native mod window open: " + e.Message); }
        }

        public override void Close()
        {
            try { HideMethod?.Invoke(null, null); }
            catch (Exception e) { Main.Log?.Error("[modui] native mod window close: " + e.Message); }
        }

        public override bool Owns(ImguiSurface surface)
            => surface.WindowId >= 0 && surface.Owner == typeof(OwlcatModificationsWindow).FullName;
    }
}
