using Loadstar;
using Loadstar.Speech;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// A named, reusable way to speak — a voice pick (<see cref="SpeechVoices"/>) plus the synthesizer
    /// params (rate, volume, boost), held in a settings subtree and turned into a Loadstar
    /// <see cref="Profile"/> on every call (profiles are flat values; the library resolves them against
    /// its voice tree). The DEFAULT config is the root Speech settings (drives all UI/announcement/
    /// dialogue speech); the user can add more (advanced) for the events system to speak specific things
    /// through — e.g. enemy damage in a different voice. An inheriting config's settings follow the
    /// default's until overridden (an empty voice pick = inherit).
    /// </summary>
    public sealed class SpeechConfig
    {
        /// <summary>The config's settings subtree: a hidden "voice" pick + the "sapi" params subnode.</summary>
        public CategorySetting Tree { get; }

        // The config this one inherits from (the default config) — null for the default itself.
        private readonly SpeechConfig _base;

        public SpeechConfig(CategorySetting tree, SpeechConfig baseConfig = null) { Tree = tree; _base = baseConfig; }

        /// <summary>An additional config (follows the default until overridden).</summary>
        public bool InheritsFromDefault => _base != null;

        /// <summary>This config's own pick as saved ("" = inherit / automatic).</summary>
        public string RawVoiceId => Tree?.Get<StringSetting>("voice")?.Get() ?? "";

        /// <summary>The effective pick: the default config's when this one inherits; "auto" when unset.</summary>
        public string VoiceId
        {
            get
            {
                var mine = RawVoiceId;
                if (_base != null && SpeechVoices.IsInherit(mine)) return _base.VoiceId;
                return SpeechVoices.IsInherit(mine) ? SpeechVoices.Auto : mine;
            }
        }

        /// <summary>What the pick reads as in the UI ("Same as the default configuration" when inheriting).</summary>
        public string VoiceLabel => SpeechVoices.Label(RawVoiceId, InheritsFromDefault);

        // ---- the synthesizer params, inherit-aware ----

        private int Int(string key, int fallback)
        {
            var mine = Tree?.Get<CategorySetting>("sapi");
            if (_base == null) return mine?.Get<IntSetting>(key)?.Get() ?? fallback;
            var ni = mine?.Get<NullableIntSetting>(key);
            if (ni != null && ni.IsOverridden && ni.LocalValue.HasValue) return ni.LocalValue.Value;
            return _base.Int(key, fallback);
        }

        /// <summary>Playback gain multiplier for POSITIONAL speech mixed through the mod's audio (SAPI's
        /// own volume tops out quiet after distance attenuation). 1 = as rendered.</summary>
        public float Boost => Int("boost", 100) / 100f;

        /// <summary>The flat Loadstar profile this config speaks through right now. Rate/volume only
        /// matter on synthesizer voices (the library ignores them on a screen reader, whose user owns
        /// its voice); the SAPI −10..10 rate maps onto the library's 0..1.</summary>
        public Profile Profile
        {
            get
            {
                var p = Profile.Default;
                p.Voice = SpeechVoices.Resolve(VoiceId);
                p.Rate = (Int("rate", 2) + 10) / 20f;
                p.Volume = Int("volume", 100) / 100f;
                // Rendered (positional) speech: SAPI voices lead with ~100 ms of silence and trail
                // with more; the library trims both so the first word lands with the event.
                p.Flags = ProfileFlags.TrimSilence;
                return p;
            }
        }

        /// <summary>The voice id the profile resolves to right now (after the library's fallback), 0 if none.</summary>
        public ulong ResolvedVoice
        {
            get
            {
                var ctx = SpeechManager.Context;
                if (ctx == null) return 0;
                ctx.Resolve(Profile, SpeechFeature.Speak, out var voice, out _, out _);
                return voice;
            }
        }

        /// <summary>The resolved voice's name, for announcements ("Voice changed to NVDA").</summary>
        public string ResolvedVoiceName => SpeechManager.Context?.GetVoice(ResolvedVoice)?.Name ?? "";

        /// <summary>The settings-UI kind the profile resolves to right now ("sapi" = takes rate/volume,
        /// "prism" = a screen reader, "clipboard") — shows only the params that apply (Automatic
        /// resolving to SAPI on a screen-reader-less machine correctly reveals the synthesizer knobs).</summary>
        public string ResolvedHandlerKey => SpeechVoices.KindOfVoice(ResolvedVoice);

        /// <summary>Can speech through this config be positioned in the world (the voice renders to
        /// PCM)? Synthesizers yes; screen readers no. The "use positional" CHOICE is a separate, event-side option.</summary>
        public bool SupportsPositional
        {
            get
            {
                var ctx = SpeechManager.Context;
                return ctx != null && ctx.Resolve(Profile, SpeechFeature.Render, out _, out _, out _) == Status.Ok;
            }
        }

        public bool Speak(string text, bool interrupt = false)
        {
            var ctx = SpeechManager.Context;
            return ctx != null && ctx.Speak(SpeechManager.UiQueue, Profile, text, interrupt ? SpeakFlags.Interrupt : SpeakFlags.None, out _) == Status.Ok;
        }

        public bool Output(string text, bool interrupt = false)
        {
            var ctx = SpeechManager.Context;
            return ctx != null && ctx.Output(SpeechManager.UiQueue, Profile, text, interrupt ? SpeakFlags.Interrupt : SpeakFlags.None, out _) == Status.Ok;
        }

        /// <summary>Cut the UI queue: what is speaking and what is pending.</summary>
        public bool Silence()
        {
            var ctx = SpeechManager.Context;
            if (ctx == null) return false;
            ctx.Stop(SpeechManager.UiQueue);
            ctx.Flush(SpeechManager.UiQueue);
            return true;
        }
    }
}
