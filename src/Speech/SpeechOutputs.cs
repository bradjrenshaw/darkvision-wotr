using System;
using System.Collections.Generic;
using Loadstar.Speech;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// The user-facing OUTPUT METHODS a speech config can pick — "who speaks": Auto (your screen
    /// reader), each screen reader Loadstar's voice tree lists on this machine (NVDA, JAWS, …), SAPI,
    /// Clipboard. The ids are the saved-settings vocabulary (a reader's output id is its NAME, as it
    /// has been since 2026-07-22, so files carry over); <see cref="VoiceFor"/> maps an output plus a
    /// SAPI voice name onto a Loadstar voice id, which is what actually speaks. The engine behind a
    /// screen reader (Prism) and the fast native SAPI path are Loadstar's business now, not ours.
    /// </summary>
    internal static class SpeechOutputs
    {
        public const string Auto = "auto";
        public const string Sapi = "sapi";
        public const string Clipboard = "clipboard";

        private const string ReadersKey = "screen_readers";
        private const string SapiKey = "sapi5";
        private const string ClipboardKey = "clipboard";

        private static List<Choice> _choices;
        private static List<Choice> _sapiVoices;

        /// <summary>Forget cached lists (hot reload; the tree changed shape).</summary>
        public static void Reset()
        {
            _choices = null;
            _sapiVoices = null;
        }

        /// <summary>The pickable outputs for this machine: Auto, then every screen reader in the tree
        /// (available or not — a saved pick for a reader that is not running must stay selectable), SAPI,
        /// Clipboard. Built from the first tree snapshot; the choice ids are stable across refreshes.</summary>
        public static List<Choice> Choices()
        {
            if (_choices != null) return _choices;
            var choices = new List<Choice> { new Choice(Auto, "Auto (your screen reader)", "speech.output.auto") };
            var readers = Category(ReadersKey);
            if (readers != null)
                foreach (var v in readers.Voices)
                    choices.Add(new Choice(v.Name, v.Name)); // product names — not translated
            choices.Add(new Choice(Sapi, "SAPI", "speech.sapi"));
            choices.Add(new Choice(Clipboard, "Clipboard", "speech.clipboard"));
            return _choices = choices;
        }

        /// <summary>The SAPI voices as settings choices (id = display name, as the saved files have it).</summary>
        public static List<Choice> SapiVoiceChoices()
        {
            if (_sapiVoices != null) return _sapiVoices;
            var voices = new List<Choice>();
            var sapi = Category(SapiKey);
            if (sapi != null)
                foreach (var v in sapi.Voices)
                    voices.Add(new Choice(v.Name, v.Name));
            if (voices.Count == 0) voices.Add(new Choice("default", "Default", "speech.voice.default"));
            return _sapiVoices = voices;
        }

        /// <summary>Is this output id a screen reader (as opposed to auto/sapi/clipboard)?</summary>
        public static bool IsScreenReader(string outputId)
            => !string.IsNullOrEmpty(outputId) && outputId != Auto && outputId != Sapi && outputId != Clipboard;

        /// <summary>The settings-UI "kind" an output routes through: "sapi" / "clipboard" / "prism"
        /// (any screen reader) / "auto". Drives which params subtree renders beside the dropdown.</summary>
        public static string HandlerKeyFor(string outputId)
        {
            if (outputId == Sapi) return "sapi";
            if (outputId == Clipboard) return "clipboard";
            if (IsScreenReader(outputId)) return "prism";
            return Auto;
        }

        /// <summary>The Loadstar voice id for an output pick. Auto = the root auto (0); a reader = its
        /// leaf (its category's auto if the name is gone); SAPI = the named SAPI voice (the category's
        /// auto if unset/gone); clipboard = the clipboard leaf.</summary>
        public static ulong VoiceFor(string outputId, string sapiVoiceName)
        {
            if (string.IsNullOrEmpty(outputId) || outputId == Auto) return 0;
            if (outputId == Clipboard) return CategoryAutoOrLeaf(ClipboardKey, null);
            if (outputId == Sapi) return CategoryAutoOrLeaf(SapiKey, sapiVoiceName);
            return CategoryAutoOrLeaf(ReadersKey, outputId);
        }

        /// <summary>The settings-UI kind of a RESOLVED voice id ("prism" for a screen reader, "sapi",
        /// "clipboard"), or null when nothing resolved — what tells the UI which params apply.</summary>
        public static string KindOfVoice(ulong voice)
        {
            if (voice == 0) return null;
            var ctx = SpeechManager.Context;
            var v = ctx?.GetVoice(voice);
            if (v == null) return null;
            foreach (var c in SpeechManager.Tree())
            {
                if (c.Id != v.Category) continue;
                switch (c.Key)
                {
                    case ReadersKey: return "prism";
                    case SapiKey: return "sapi";
                    case ClipboardKey: return "clipboard";
                    default: return c.Key;
                }
            }
            return null;
        }

        /// <summary>Map a legacy handler/backend pair (pre-refactor saved settings) to an output id.</summary>
        public static string FromLegacy(string handler, string prismBackend)
        {
            switch (handler)
            {
                case "sapi": return Sapi;
                case "clipboard": return Clipboard;
                case "prism":
                    if (!string.IsNullOrEmpty(prismBackend) && prismBackend != "auto")
                        foreach (var c in Choices())
                            if (c.Id == prismBackend) return prismBackend;
                    return Auto;
                default: return Auto;
            }
        }

        // ---- tree lookups ----

        private static SpeechCategory Category(string key)
        {
            foreach (var c in SpeechManager.Tree())
                if (c.Key == key) return c;
            return null;
        }

        private static ulong CategoryAutoOrLeaf(string categoryKey, string leafName)
        {
            var cat = Category(categoryKey);
            if (cat == null) return 0; // not on this machine: the root auto decides
            if (!string.IsNullOrEmpty(leafName))
                foreach (var v in cat.Voices)
                    if (string.Equals(v.Name, leafName, StringComparison.OrdinalIgnoreCase)) return v.Id;
            return cat.Auto;
        }
    }
}
