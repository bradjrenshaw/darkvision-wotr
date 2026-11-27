using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WrathAccess.ModUI.Imgui;

namespace WrathAccess.ModUI.Mods
{
    /// <summary>
    /// Probes for ModKit, the widget library ToyBox is built on (and other mods bundle their own copy
    /// of — every assembly that contains it gets hooked).
    ///
    /// ModKit's check boxes and disclosure arrows are fully hand-rolled: one function hit-tests the
    /// mouse itself and paints a glyph and a label straight through GUIStyle.Draw. No Unity control is
    /// involved, so the generic recorder sees two loose bits of text and nothing to press. That one
    /// function — <c>ModKit.Private.UI.Toggle(Rect, …)</c>, the funnel under Toggle, CheckBox,
    /// DisclosureToggle and friends — is hooked here and reported as a real toggle. It returns
    /// "was clicked" (the caller flips the value), so injecting a click is returning true.
    /// </summary>
    internal sealed class ModKitProbes : ImguiAdapter
    {
        public override string Id => "lib.modkit";

        private readonly HashSet<Assembly> _scanned = new HashSet<Assembly>();
        private static readonly Dictionary<Type, GUIContent> _disclosureOn = new Dictionary<Type, GUIContent>();

        public override void InstallProbes(Harmony harmony)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!_scanned.Add(asm)) continue;
                Type ui;
                try { ui = asm.GetType("ModKit.Private.UI", false); }
                catch { continue; }
                if (ui == null) continue;
                var toggle = AccessTools.Method(ui, "Toggle", new[]
                {
                    typeof(Rect), typeof(GUIContent), typeof(bool), typeof(bool),
                    typeof(GUIContent), typeof(GUIContent), typeof(GUIStyle), typeof(GUIStyle),
                });
                if (toggle == null)
                {
                    Main.Log?.Log("[modui] ModKit in " + asm.GetName().Name + " has no Toggle(Rect, …) funnel; its check boxes stay plain text");
                    continue;
                }
                harmony.Patch(toggle,
                    new HarmonyMethod(typeof(ModKitProbes), nameof(TogglePrefix)),
                    new HarmonyMethod(typeof(ModKitProbes), nameof(TogglePostfix)));
                Main.Log?.Log("[modui] ModKit toggle probe installed in " + asm.GetName().Name);
            }
        }

        private static void TogglePrefix()
        {
            if (ImguiCapture.Recording) ImguiCapture.Enter();
        }

        private static void TogglePostfix(Rect rect, GUIContent label, bool value, bool isEmpty,
            GUIContent on, MethodBase __originalMethod, ref bool __result)
        {
            if (!ImguiCapture.Recording || !ImguiCapture.Exit()) return;
            var n = ImguiCapture.Leaf(ImguiKind.Toggle, rect, label != null ? label.text : null,
                label != null ? label.tooltip : null);
            if (n == null) return;
            n.On = value;
            n.Disclosure = IsDisclosure(__originalMethod.DeclaringType, on);
            ImguiRequest r;
            if (ImguiCapture.TryTake(n, ImguiVerb.Click, out r))
            {
                __result = true;
                ImguiCapture.Applied(r);
            }
        }

        // A disclosure toggle is the same function called with the arrow glyphs instead of the tick
        // glyphs. Read lazily, from inside the widget's own call — never force another mod's static
        // initialiser to run early.
        private static bool IsDisclosure(Type ui, GUIContent on)
        {
            if (ui == null || on == null) return false;
            GUIContent arrow;
            if (!_disclosureOn.TryGetValue(ui, out arrow))
            {
                try { arrow = ui.GetField("DisclosureOnContent", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as GUIContent; }
                catch { arrow = null; }
                _disclosureOn[ui] = arrow;
            }
            return arrow != null && ReferenceEquals(arrow, on);
        }
    }
}
