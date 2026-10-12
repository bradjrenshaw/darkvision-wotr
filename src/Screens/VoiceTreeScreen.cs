using System;
using WrathAccess.Settings;
using WrathAccess.Speech;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The voice tree, pushed as a CHILD SCREEN of whatever screen's Voice dropdown opened it
    /// (<see cref="Screen.PushChild"/>): Inherit (additional configs), Automatic, then one group per
    /// category from Loadstar's tree — its default first, then every AVAILABLE voice (a screen reader
    /// that is not running is not offered; a saved pick for one keeps its name on the dropdown and the
    /// library's fallback decides what speaks). Picking writes the config's <c>voice</c> setting and
    /// closes; Back closes without changing anything. Focus lands on the current pick, with its category
    /// expanded, so opening reads the selected voice first.
    /// </summary>
    public sealed class VoiceTreeScreen : Screen
    {
        private readonly SpeechConfig _config;
        private readonly StringSetting _setting;
        private readonly bool _allowInherit;
        private readonly Action<string> _afterSelect;
        private readonly string _current;

        private VoiceTreeScreen(SpeechConfig config, StringSetting setting, bool allowInherit, Action<string> afterSelect)
        {
            _config = config;
            _setting = setting;
            _allowInherit = allowInherit;
            _afterSelect = afterSelect;
            var v = setting.Get() ?? "";
            _current = !allowInherit && v.Length == 0 ? SpeechVoices.Auto : v;
            Wrap = true;
        }

        /// <summary>Open the tree as a child of the current screen (the dropdown's activation).</summary>
        public static void Open(SpeechConfig config, StringSetting setting, bool allowInherit, Action<string> afterSelect = null)
        {
            if (config == null || setting == null) return;
            UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
            ScreenManager.Current?.PushChild(new VoiceTreeScreen(config, setting, allowInherit, afterSelect));
        }

        public override string Key => "overlay.voicetree";
        public override string ScreenName => L("speech.voice", "Voice");
        public override bool IsActive() => false; // never poll-pushed — only ever a child screen

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private void Close() => ParentScreen?.RemoveChild(this);

        public override void Build(GraphBuilder b)
        {
            if (_allowInherit) Option(b, "");
            Option(b, SpeechVoices.Auto);
            string currentCategory = CategoryOfPick(_current);
            foreach (var cat in SpeechManager.Tree())
            {
                var c = cat;
                // The current pick's category opens; the rest start collapsed (a long voice list stays short).
                b.BeginGroup(ControlId.Structural("cat:" + c.Key),
                    GraphNodes.Group(() => SpeechVoices.CategoryLabel(c.Key, c.Name)),
                    expanded: c.Key == currentCategory ? true : (bool?)null);
                Option(b, SpeechVoices.CategoryAuto(c.Key));
                foreach (var v in c.Voices)
                    if (v.Available) Option(b, SpeechVoices.Leaf(v.Id));
                b.EndGroup();
            }
            b.SetStart(ControlId.Structural("voice:" + _current)); // land on the current pick
        }

        private void Option(GraphBuilder b, string id)
        {
            b.AddItem(ControlId.Structural("voice:" + id),
                GraphNodes.ChoiceOption(
                    () => SpeechVoices.Label(id, _allowInherit),
                    () => id == _current,
                    () =>
                    {
                        _setting.Set(id);
                        _afterSelect?.Invoke(id);
                        Close();
                    }));
        }

        // Which category group holds a pick ("auto:<key>" or a leaf's category); null for Automatic/Inherit.
        private static string CategoryOfPick(string id)
        {
            if (id.StartsWith("auto:", StringComparison.Ordinal)) return id.Substring(5);
            var voice = SpeechManager.Context?.GetVoice(SpeechVoices.Resolve(id));
            if (voice == null || id == SpeechVoices.Auto || id.Length == 0) return null;
            foreach (var c in SpeechManager.Tree())
                if (c.Id == voice.Category) return c.Key;
            return null;
        }

        private static string L(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);
    }
}
