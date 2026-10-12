using System;
using System.Collections.Generic;
using System.Globalization;
using Loadstar.Speech;

namespace WrathAccess.Speech
{
    /// <summary>
    /// The voice pick vocabulary (2026-10-11, replaces the output-method model): ONE setting per
    /// speech config, a string id over Loadstar's voice tree — <c>auto</c> (the root auto: your running
    /// screen reader, else the default synthesizer voice), <c>auto:&lt;category&gt;</c> (that category's
    /// best), <c>v:&lt;hex&gt;</c> (one voice — a screen reader or an installed synthesizer voice), or
    /// empty (an additional config inherits the default's). Categories and voices come from the
    /// library; the mod localizes the built-in category keys and passes voice names through.
    /// </summary>
    internal static class SpeechVoices
    {
        public const string Auto = "auto";
        private const string CategoryPrefix = "auto:";
        private const string LeafPrefix = "v:";

        public const string ReadersKey = "screen_readers";
        public const string Sapi5Key = "sapi5";
        public const string ClipboardKey = "clipboard";

        public static string CategoryAuto(string categoryKey) => CategoryPrefix + categoryKey;
        public static string Leaf(ulong voiceId) => LeafPrefix + voiceId.ToString("x");
        public static bool IsInherit(string id) => string.IsNullOrEmpty(id);

        /// <summary>The Loadstar voice id a pick resolves to right now (0 = root auto). An unknown or
        /// absent voice keeps its id — the library's own fallback walk decides what speaks.</summary>
        public static ulong Resolve(string id)
        {
            if (IsInherit(id) || id == Auto) return 0;
            if (id.StartsWith(CategoryPrefix, StringComparison.Ordinal))
            {
                var cat = Category(id.Substring(CategoryPrefix.Length));
                return cat?.Auto ?? 0;
            }
            if (id.StartsWith(LeafPrefix, StringComparison.Ordinal)
                && ulong.TryParse(id.Substring(LeafPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return v;
            return 0;
        }

        /// <summary>What a pick reads as in the UI (localized built-ins; voice names pass through).</summary>
        public static string Label(string id, bool inheritAllowed)
        {
            if (IsInherit(id)) return inheritAllowed ? L("speech.voice.inherit", "Same as the default configuration") : L("speech.voice.auto", "Automatic");
            if (id == Auto) return L("speech.voice.auto", "Automatic");
            if (id.StartsWith(CategoryPrefix, StringComparison.Ordinal))
            {
                var key = id.Substring(CategoryPrefix.Length);
                var cat = Category(key);
                return CategoryLabel(key, cat?.Name ?? key) + ": " + L("speech.voice.category_default", "Default");
            }
            var voice = SpeechManager.Context?.GetVoice(Resolve(id));
            if (voice == null) return L("speech.voice.unknown", "Unknown voice");
            return VoiceLabel(voice);
        }

        /// <summary>A leaf's display label: its name, plus "(not running)" for an absent screen reader.</summary>
        public static string VoiceLabel(SpeechVoice voice)
            => voice.Available ? voice.Name : voice.Name + " (" + L("speech.voice.not_running", "not running") + ")";

        public static string CategoryLabel(string key, string libraryName)
            => L("speech.category." + key, libraryName);

        /// <summary>The settings-UI kind of a RESOLVED voice id: "sapi" when the voice takes rate/volume
        /// (any synthesizer category), "prism" for a screen reader, "clipboard"; null when unresolved.
        /// Decides which params render beside the picker.</summary>
        public static string KindOfVoice(ulong voice)
        {
            if (voice == 0) return null;
            var v = SpeechManager.Context?.GetVoice(voice);
            if (v == null) return null;
            if (v.Has(SpeechFeature.SetRate) || v.Has(SpeechFeature.SetVolume)) return "sapi";
            foreach (var c in SpeechManager.Tree())
            {
                if (c.Id != v.Category) continue;
                return c.Key == ReadersKey ? "prism" : c.Key == ClipboardKey ? "clipboard" : c.Key;
            }
            return null;
        }

        /// <summary>Map a pre-2026-10 saved pick (an OUTPUT id — auto / a reader's name / "sapi" /
        /// "clipboard" — plus the SAPI voice name) onto a voice id.</summary>
        public static string FromLegacyOutput(string output, string sapiVoiceName)
        {
            if (string.IsNullOrEmpty(output) || output == Auto) return Auto;
            if (output == "clipboard") return CategoryAuto(ClipboardKey);
            if (output == "sapi") return LeafByName(Sapi5Key, sapiVoiceName) ?? CategoryAuto(Sapi5Key);
            return LeafByName(ReadersKey, output) ?? CategoryAuto(ReadersKey);
        }

        /// <summary>Map the even older handler/backend pair.</summary>
        public static string FromLegacyHandler(string handler, string prismBackend, string sapiVoiceName)
        {
            switch (handler)
            {
                case "sapi": return FromLegacyOutput("sapi", sapiVoiceName);
                case "clipboard": return FromLegacyOutput("clipboard", null);
                case "prism":
                    return !string.IsNullOrEmpty(prismBackend) && prismBackend != Auto ? FromLegacyOutput(prismBackend, null) : Auto;
                default: return Auto;
            }
        }

        // ---- tree lookups ----

        public static SpeechCategory Category(string key)
        {
            foreach (var c in SpeechManager.Tree())
                if (c.Key == key) return c;
            return null;
        }

        private static string LeafByName(string categoryKey, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var cat = Category(categoryKey);
            if (cat == null) return null;
            foreach (var v in cat.Voices)
                if (string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) return Leaf(v.Id);
            return null;
        }

        private static string L(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);
    }
}
