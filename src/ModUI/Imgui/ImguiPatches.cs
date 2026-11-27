using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WrathAccess.ModUI.Imgui
{
    /// <summary>
    /// The Harmony hooks that feed <see cref="ImguiCapture"/> from Unity's own IMGUI primitives.
    ///
    /// <para>Every hook sits on the DEEPEST method the public overloads funnel into (GUI.DoButton, not
    /// GUI.Button; GUILayoutUtility.BeginLayoutGroup, not GUILayout.BeginHorizontal): the public
    /// wrappers are a few bytes of IL, so Mono inlines them into callers that were compiled before we
    /// patched — a hook on the wrapper would silently miss every mod that drew once at startup.</para>
    ///
    /// <para>Patched by hand (no [HarmonyPatch] attributes — the module's PatchAll must not touch these):
    /// they go in lazily, the first time a mod UI is actually opened, under their own Harmony id, so a
    /// player who never opens a mod window never has Unity's GUI patched at all. Each target is
    /// resolved and patched independently; a miss is logged and the rest still work.</para>
    /// </summary>
    internal static class ImguiPatches
    {
        private static Func<object, bool> _isVertical;

        public static int Install(Harmony h)
        {
            var gui = typeof(GUI);
            var util = typeof(GUIUtility);
            var lay = typeof(GUILayoutUtility);
            int n = 0;

            // ---- pass lifecycle ----
            n += Patch(h, AccessTools.Method(util, "BeginGUI"), prefix: nameof(BeginGuiPrefix));
            n += Patch(h, AccessTools.Method(util, "EndGUI"), prefix: nameof(EndGuiPrefix));
            n += Patch(h, AccessTools.Method(gui, "CallWindowDelegate"),
                prefix: nameof(WindowPrefix), postfix: nameof(WindowPostfix));
            n += Patch(h, AccessTools.Method(gui, "DoWindow"), prefix: nameof(DoWindowPrefix));
            n += Patch(h, AccessTools.Method(gui, "DoModalWindow"), prefix: nameof(DoModalWindowPrefix));

            // ---- structure ----
            PrepareGroupAccessor();
            n += Patch(h, AccessTools.Method(lay, "BeginLayoutGroup"), postfix: nameof(GroupPostfix));
            n += Patch(h, AccessTools.Method(lay, "EndLayoutGroup"), prefix: nameof(GroupEndPrefix));

            // ---- leaves ----
            n += Patch(h, AccessTools.Method(gui, "DoLabel"),
                prefix: nameof(EnterPrefix), postfix: nameof(LabelPostfix));
            n += Patch(h, AccessTools.Method(gui, "Box", new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) }),
                prefix: nameof(EnterPrefix), postfix: nameof(BoxPostfix));
            n += Patch(h, AccessTools.Method(gui, "DoButton"),
                prefix: nameof(EnterPrefix), postfix: nameof(ButtonPostfix));
            n += Patch(h, AccessTools.Method(gui, "DoRepeatButton"),
                prefix: nameof(EnterPrefix), postfix: nameof(ButtonPostfix));
            n += Patch(h, AccessTools.Method(gui, "DoToggle"),
                prefix: nameof(EnterPrefix), postfix: nameof(TogglePostfix));
            n += Patch(h, AccessTools.Method(gui, "DoTextField", new[]
                {
                    typeof(Rect), typeof(int), typeof(GUIContent), typeof(bool), typeof(int),
                    typeof(GUIStyle), typeof(string), typeof(char),
                }),
                prefix: nameof(EnterPrefix), postfix: nameof(TextFieldPostfix));
            n += Patch(h, AccessTools.Method(gui, "Slider"),
                prefix: nameof(EnterPrefix), postfix: nameof(SliderPostfix));
            n += Patch(h, AccessTools.Method(gui, "Scroller"),
                prefix: nameof(ScrollerPrefix), postfix: nameof(ScrollerPostfix));
            n += Patch(h, AccessTools.Method(gui, "DoButtonGrid"),
                prefix: nameof(EnterPrefix), postfix: nameof(GridPostfix));

            // ---- fallback: text painted by custom controls ----
            n += Patch(h, AccessTools.Method(typeof(GUIStyle), "Draw", new[]
                {
                    typeof(Rect), typeof(GUIContent), typeof(int), typeof(bool), typeof(bool),
                    typeof(bool), typeof(bool),
                }),
                postfix: nameof(DrawPostfix));
            return n;
        }

        private static int Patch(Harmony h, MethodBase target, string prefix = null, string postfix = null)
        {
            if (target == null)
            {
                Main.Log?.Error("[modui] IMGUI hook target not found (prefix " + prefix + ", postfix " + postfix + ")");
                return 0;
            }
            try
            {
                h.Patch(target,
                    prefix != null ? new HarmonyMethod(typeof(ImguiPatches), prefix) : null,
                    postfix != null ? new HarmonyMethod(typeof(ImguiPatches), postfix) : null);
                return 1;
            }
            catch (Exception e)
            {
                Main.Log?.Error("[modui] IMGUI hook failed on " + target.DeclaringType?.Name + "." + target.Name + ": " + e.Message);
                return 0;
            }
        }

        // GUILayoutGroup is internal; read its public isVertical field through a compiled accessor
        // (plain FieldInfo.GetValue boxes a bool per layout group per captured frame).
        private static void PrepareGroupAccessor()
        {
            if (_isVertical != null) return;
            try
            {
                var groupType = typeof(GUI).Assembly.GetType("UnityEngine.GUILayoutGroup");
                var field = groupType?.GetField("isVertical", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) { _isVertical = _ => true; return; }
                try
                {
                    var p = Expression.Parameter(typeof(object), "g");
                    _isVertical = Expression.Lambda<Func<object, bool>>(
                        Expression.Field(Expression.Convert(p, groupType), field), p).Compile();
                    _isVertical(Activator.CreateInstance(groupType, true)); // prove it runs here
                }
                catch
                {
                    _isVertical = g => (bool)field.GetValue(g);
                }
            }
            catch (Exception e)
            {
                Main.Log?.Error("[modui] layout group accessor: " + e.Message);
                _isVertical = _ => true;
            }
        }

        // =====================================================================================
        // Hooks. Parameter names bind to the originals' by name (Harmony); keep them exact.
        // =====================================================================================

        private static void BeginGuiPrefix(int instanceID) => ImguiCapture.BeginRoot(instanceID);

        private static void EndGuiPrefix() => ImguiCapture.EndRoot();

        private static void WindowPrefix(int id, int instanceID, out bool __state)
            => __state = ImguiCapture.BeginWindow(id, instanceID);

        private static void WindowPostfix(bool __state)
        {
            if (__state) ImguiCapture.EndWindow();
        }

        private static void DoWindowPrefix(int id, GUIContent title) => ImguiCapture.NoteWindow(id, title);

        private static void DoModalWindowPrefix(int id, GUIContent content) => ImguiCapture.NoteWindow(id, content);

        private static void GroupPostfix(GUIStyle style, Type layoutType, object __result)
        {
            if (!ImguiCapture.Recording || __result == null) return;
            bool scroll = layoutType != null && layoutType.Name == "GUIScrollGroup";
            ImguiCapture.PushGroup(_isVertical(__result), scroll, style);
        }

        private static void GroupEndPrefix()
        {
            if (ImguiCapture.Recording) ImguiCapture.PopGroup();
        }

        private static void EnterPrefix()
        {
            if (ImguiCapture.Recording) ImguiCapture.Enter();
        }

        private static void LabelPostfix(Rect position, GUIContent content, GUIStyle style)
        {
            if (ImguiCapture.Recording) ImguiCapture.Static(ImguiKind.Label, position, content, style);
        }

        private static void BoxPostfix(Rect position, GUIContent content, GUIStyle style)
        {
            if (ImguiCapture.Recording) ImguiCapture.Static(ImguiKind.Box, position, content, style);
        }

        // GUI.DoButton and GUI.DoRepeatButton.
        private static void ButtonPostfix(Rect position, GUIContent content, GUIStyle style, ref bool __result)
        {
            if (!ImguiCapture.Recording || !ImguiCapture.Exit()) return;
            var n = ImguiCapture.Leaf(ImguiKind.Button, position, content?.text, content?.tooltip, content?.image, style);
            ImguiRequest r;
            if (ImguiCapture.TryTake(n, ImguiVerb.Click, out r))
            {
                __result = true;
                ImguiCapture.Applied(r);
            }
        }

        private static void TogglePostfix(Rect position, bool value, GUIContent content, GUIStyle style, ref bool __result)
        {
            if (!ImguiCapture.Recording || !ImguiCapture.Exit()) return;
            var n = ImguiCapture.Leaf(ImguiKind.Toggle, position, content?.text, content?.tooltip, content?.image, style);
            if (n == null) return;
            n.On = value;
            ImguiRequest r;
            if (ImguiCapture.TryTake(n, ImguiVerb.Click, out r))
            {
                __result = !value;
                ImguiCapture.Applied(r);
            }
        }

        // The text the field shows lives in content.text; GUI.TextField returns it only when
        // GUI.changed is set, which Applied does.
        private static void TextFieldPostfix(Rect position, GUIContent content, bool multiline, GUIStyle style, string secureText)
        {
            if (!ImguiCapture.Recording || !ImguiCapture.Exit() || content == null) return;
            var n = ImguiCapture.Leaf(ImguiKind.TextField, position, null, null, null, style);
            if (n == null) return;
            n.Multiline = multiline;
            n.Password = secureText != null;
            // A text field's own text is its VALUE, kept out of the identity (it changes as you type).
            n.Content = content.text ?? "";
            ImguiRequest r;
            if (ImguiCapture.TryTake(n, ImguiVerb.SetText, out r))
            {
                content.text = r.Text ?? "";
                ImguiCapture.Applied(r);
            }
        }

        private static void SliderPostfix(Rect position, float value, float start, float end, GUIStyle slider, ref float __result)
        {
            if (!ImguiCapture.Recording || !ImguiCapture.Exit()) return;
            var n = ImguiCapture.Leaf(ImguiKind.Slider, position, null, null, null, slider);
            if (n == null) return;
            n.Value = value;
            n.Min = start;
            n.Max = end;
            ImguiRequest r;
            if (ImguiCapture.TryTake(n, ImguiVerb.SetValue, out r))
            {
                __result = Mathf.Clamp(r.Value, Mathf.Min(start, end), Mathf.Max(start, end));
                ImguiCapture.Applied(r);
            }
        }

        // A scrollbar is built from a Slider and two RepeatButtons: bracket the whole thing as one
        // control and record nothing for it (the thumb is not a setting).
        private static void ScrollerPrefix()
        {
            if (ImguiCapture.Recording) ImguiCapture.Enter();
        }

        private static void ScrollerPostfix()
        {
            if (ImguiCapture.Recording) ImguiCapture.Exit();
        }

        // GUI.Toolbar and GUI.SelectionGrid.
        private static void GridPostfix(Rect position, int selected, GUIContent[] contents, int xCount,
            GUIStyle style, bool[] contentsEnabled, ref int __result)
        {
            if (!ImguiCapture.Recording || !ImguiCapture.Exit()) return;
            if (contents == null || contents.Length == 0) return;
            var n = ImguiCapture.Leaf(ImguiKind.Choice, position, null, null, null, style);
            if (n == null) return;
            var options = new string[contents.Length];
            for (int i = 0; i < contents.Length; i++)
            {
                var c = contents[i];
                string t = c != null ? c.text : null;
                if (string.IsNullOrEmpty(t) && c != null && c.image != null) t = c.image.name;
                options[i] = t ?? "";
            }
            n.Options = options;
            n.OptionEnabled = contentsEnabled;
            n.Selected = selected;
            n.Columns = xCount > 0 ? xCount : options.Length;
            ImguiRequest r;
            if (ImguiCapture.TryTake(n, ImguiVerb.Select, out r))
            {
                if (r.Index >= 0 && r.Index < options.Length) __result = r.Index;
                ImguiCapture.Applied(r);
            }
        }

        private static void DrawPostfix(GUIStyle __instance, Rect position, GUIContent content)
        {
            if (ImguiCapture.Recording) ImguiCapture.Drawn(position, content, __instance);
        }
    }
}
