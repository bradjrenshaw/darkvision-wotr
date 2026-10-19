using Loadstar;
using Loadstar.Speech;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// A named, reusable way to speak — a voice pick (<see cref="SpeechVoices"/>) plus the voice
    /// settings (rate, pitch, volume; boost on additional configs), held in a settings subtree and turned into a Loadstar
    /// <see cref="Profile"/> on every call (profiles are flat values; the library resolves them against
    /// its voice tree). The DEFAULT config is the root Speech settings (drives all UI/announcement/
    /// dialogue speech); the user can add more (advanced) for the events system to speak specific things
    /// through — e.g. enemy damage in a different voice. An inheriting config's settings follow the
    /// default's until overridden (an empty voice pick = inherit).
    /// </summary>
    public sealed class SpeechConfig
    {
        /// <summary>The config's settings subtree: a hidden "voice" pick + the "params" subnode.</summary>
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

        // ---- the voice settings, inherit-aware ----

        private int Int(string key, int fallback)
        {
            var mine = Tree?.Get<CategorySetting>("params");
            var own = mine?.Get<IntSetting>(key); // the default config's values, and the boost
            if (own != null) return own.Get();
            var ni = mine?.Get<NullableIntSetting>(key);
            if (ni != null && ni.IsOverridden && ni.LocalValue.HasValue) return ni.LocalValue.Value;
            return _base != null ? _base.Int(key, fallback) : fallback;
        }

        /// <summary>Playback gain multiplier for POSITIONAL speech mixed through the mod's audio (a
        /// synthesizer's own volume tops out quiet after distance attenuation). 1 = as rendered.</summary>
        public float Boost => Int("boost", 100) / 100f;

        /// <summary>The flat Loadstar profile this config speaks through right now. Rate/pitch/volume
        /// only matter on voices that take them (the library ignores them on a screen reader, whose user
        /// owns its voice); the settings' 0..100 map onto the library's 0..1.</summary>
        public Profile Profile
        {
            get
            {
                var p = Profile.Default;
                p.Voice = SpeechVoices.Resolve(VoiceId);
                p.Rate = Int("rate", 60) / 100f;
                p.Pitch = Int("pitch", 50) / 100f;
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

        /// <summary>Does the voice this config resolves to right now support <paramref name="feature"/>?</summary>
        public bool VoiceHas(SpeechFeature feature)
        {
            var v = SpeechManager.Context?.GetVoice(ResolvedVoice);
            return v != null && v.Has(feature);
        }

        /// <summary>Whether a voice setting applies to the resolved voice, so the settings UI shows only
        /// what takes effect (Automatic resolving to a synthesizer on a screen-reader-less machine
        /// reveals the knobs; a screen reader shows none).</summary>
        public bool ParamApplies(string key)
        {
            switch (key)
            {
                case "rate": return VoiceHas(SpeechFeature.SetRate);
                case "pitch": return VoiceHas(SpeechFeature.SetPitch);
                case "volume": return VoiceHas(SpeechFeature.SetVolume);
                case "boost": return SupportsPositional;
                default: return true;
            }
        }

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
