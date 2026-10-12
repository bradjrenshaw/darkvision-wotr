using System;
using WrathAccess.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Speech
{
    /// <summary>
    /// The voice picker as it sits in a settings list: a dropdown ("Voice, combo box, &lt;current&gt;")
    /// whose activation opens the voice tree in its own window (<see cref="Screens.VoiceTreeScreen"/>).
    /// Returning from the tree with a new pick announces the live value. Shared by the full settings
    /// tab, the additional-config groups, the basic screen and the wizard.
    /// </summary>
    internal static class SpeechVoicePicker
    {
        /// <summary>Emit the dropdown if <paramref name="s"/> is a config's <c>voice</c> setting.</summary>
        public static bool TryEmit(GraphBuilder b, Setting s, SpeechConfig config, string prefix)
        {
            if (!(s is StringSetting vs) || vs.Key != "voice" || config == null) return false;
            Emit(b, prefix, config, vs, config.InheritsFromDefault, null);
            return true;
        }

        /// <summary>Emit the dropdown for <paramref name="config"/>; <paramref name="afterSelect"/> runs
        /// after a pick is written in the tree window (the wizard speaks a sample).</summary>
        public static void Emit(GraphBuilder b, string prefix, SpeechConfig config, StringSetting setting,
            bool allowInherit, Action<string> afterSelect)
        {
            b.AddItem(ControlId.Structural(prefix + "voice"),
                GraphNodes.Dropdown(
                    () => L("speech.voice", "Voice"),
                    () => config.VoiceLabel,
                    () => Screens.VoiceTreeScreen.Open(config, setting, allowInherit, afterSelect)));
        }

        private static string L(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);
    }
}
