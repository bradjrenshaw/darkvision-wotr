using System;
using WrathAccess.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Speech
{
    /// <summary>
    /// The voice picker: one collapsible "Voice: &lt;current&gt;" group whose children are the options —
    /// Inherit (additional configs), Automatic, then one sub-group per category from Loadstar's tree
    /// (its default first, then every voice; screen readers that are not running are labelled so and
    /// still selectable, so a saved pick survives a session without the reader). Activating an option
    /// writes the config's <c>voice</c> setting; the manager announces the change through the new voice.
    /// Shared by the full settings tab, the additional-config groups, the basic screen and the wizard.
    /// </summary>
    internal static class SpeechVoicePicker
    {
        /// <summary>Emit the picker if <paramref name="s"/> is a config's <c>voice</c> setting.</summary>
        public static bool TryEmit(GraphBuilder b, Setting s, SpeechConfig config, string prefix)
        {
            if (!(s is StringSetting vs) || vs.Key != "voice" || config == null) return false;
            Emit(b, prefix, config, vs, config.InheritsFromDefault, null, false);
            return true;
        }

        /// <summary>Emit the picker for <paramref name="config"/>. <paramref name="expandAll"/> opens every
        /// group (the wizard's flat list); <paramref name="afterSelect"/> runs after a pick is written.</summary>
        public static void Emit(GraphBuilder b, string prefix, SpeechConfig config, StringSetting setting,
            bool allowInherit, Action<string> afterSelect, bool expandAll)
        {
            bool? expanded = expandAll ? true : (bool?)null;
            b.BeginGroup(ControlId.Structural(prefix + "voice"),
                GraphNodes.Group(() => L("speech.voice", "Voice") + ": " + config.VoiceLabel), expanded);
            if (allowInherit) Option(b, prefix, setting, "", allowInherit, afterSelect);
            Option(b, prefix, setting, SpeechVoices.Auto, allowInherit, afterSelect);
            foreach (var cat in SpeechManager.Tree())
            {
                var c = cat;
                b.BeginGroup(ControlId.Structural(prefix + "voice.cat:" + c.Key),
                    GraphNodes.Group(() => SpeechVoices.CategoryLabel(c.Key, c.Name)), expanded);
                Option(b, prefix, setting, SpeechVoices.CategoryAuto(c.Key), allowInherit, afterSelect);
                foreach (var v in c.Voices)
                    Option(b, prefix, setting, SpeechVoices.Leaf(v.Id), allowInherit, afterSelect);
                b.EndGroup();
            }
            b.EndGroup();
        }

        private static void Option(GraphBuilder b, string prefix, StringSetting setting, string id, bool allowInherit, Action<string> afterSelect)
        {
            b.AddItem(ControlId.Structural(prefix + "voice:" + id),
                GraphNodes.ChoiceOption(
                    () => SpeechVoices.Label(id, allowInherit),
                    () => Current(setting, allowInherit) == id,
                    () =>
                    {
                        setting.Set(id);
                        afterSelect?.Invoke(id);
                    }));
        }

        // The default config has no "inherit": its empty value reads as Automatic.
        private static string Current(StringSetting setting, bool allowInherit)
        {
            var v = setting.Get() ?? "";
            return !allowInherit && v.Length == 0 ? SpeechVoices.Auto : v;
        }

        private static string L(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);
    }
}
