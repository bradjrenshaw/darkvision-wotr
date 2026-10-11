using System;
using System.Collections.Generic;
using Loadstar;
using Loadstar.Speech;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// Owns the Loadstar speech context (<c>loadstar.dll</c> beside Wrath.exe, with <c>prism.dll</c> for
    /// the screen readers) and the mod's queues: the UI queue (0, sequential — announcements, menus,
    /// dialogue; never interrupts by default) and the EVENTS queue (unlimited overlap — combat bursts).
    /// The DEFAULT config is the root Speech settings and drives all UI speech; the events system speaks
    /// specific things through additional <see cref="SpeechConfig"/>s. Every utterance is a flat profile
    /// handed to the library, which resolves it against its voice tree off the game thread — nothing
    /// here blocks, renders, or touches COM. <see cref="Tts"/> is the call-site facade.
    /// </summary>
    public static class SpeechManager
    {
        private static bool _initialized;
        private static ChoiceSetting _outputSetting; // the DEFAULT config's output choice
        private static List<SpeechCategory> _tree;
        private static ulong _treeGeneration;

        /// <summary>The live library context; null until <see cref="EnsureContext"/> opened it (or if
        /// loadstar.dll is missing — then the mod is mute and says so in the log).</summary>
        public static SpeechContext Context { get; private set; }

        /// <summary>The sequential queue every announcement goes through (queue 0 always exists).</summary>
        public const uint UiQueue = 0;

        /// <summary>The unlimited-overlap queue the events system speaks through.</summary>
        public static uint EventQueue { get; private set; }

        /// <summary>The default speech config — the root Speech settings; everything speaks through it.</summary>
        public static SpeechConfig Default { get; private set; }

        /// <summary>Open the library context once (settings-build time, so the output choices can be
        /// listed from the voice tree). Waits briefly for the first probe — tens of milliseconds; the
        /// library never blocks after that.</summary>
        public static void EnsureContext()
        {
            if (Context != null) return;
            try
            {
                var ctx = SpeechContext.Open(); // prism.dll next to Wrath.exe is found by the loader's default search
                var t0 = DateTime.UtcNow;
                while (ctx.Generation == 0 && (DateTime.UtcNow - t0).TotalMilliseconds < 2000)
                    System.Threading.Thread.Sleep(2);
                EventQueue = ctx.CreateQueue(QueueOptions.Unlimited);
                Context = ctx;
                _tree = null;
                Main.Log?.Log("[speech] loadstar speech open in " + (DateTime.UtcNow - t0).TotalMilliseconds.ToString("F0") + " ms; prism: "
                    + (string.IsNullOrEmpty(ctx.PrismVersion) ? "not loaded" : ctx.PrismVersion));
            }
            catch (DllNotFoundException e) { Main.Log?.Error("[speech] loadstar.dll not found next to Wrath.exe — no speech. " + e.Message); }
            catch (EntryPointNotFoundException e) { Main.Log?.Error("[speech] loadstar.dll is too old (missing lv_* exports) — no speech. " + e.Message); }
            catch (Exception e) { Main.Log?.Error("[speech] speech context failed to open — no speech. " + e); }
        }

        /// <summary>The voice tree snapshot (categories in rank order with their voices), re-read when
        /// the library publishes a new generation.</summary>
        public static IReadOnlyList<SpeechCategory> Tree()
        {
            if (Context == null) return Array.Empty<SpeechCategory>();
            ulong g = Context.Generation;
            if (_tree == null || g != _treeGeneration)
            {
                _tree = Context.GetTree();
                _treeGeneration = g;
            }
            return _tree;
        }

        /// <summary>Build the DEFAULT config into the Speech category (output dropdown + the SAPI
        /// params), and adopt it as <see cref="Default"/>.</summary>
        public static void RegisterSettings(CategorySetting speechCategory)
        {
            EnsureContext();
            BuildConfigSchema(speechCategory);
            _outputSetting = speechCategory.Get<ChoiceSetting>("output");
            if (_outputSetting != null) _outputSetting.Changed += OnDefaultOutputChanged;
            Default = new SpeechConfig(speechCategory);
        }

        /// <summary>The schema every speech config shares: a "Speech output" choice — who speaks: Auto /
        /// each screen reader / SAPI / Clipboard (<see cref="SpeechOutputs"/>) — plus the SAPI params
        /// subtree (screen-reader outputs have no params; the reader owns its voice). The default config
        /// (<paramref name="inheritFrom"/> null) gets plain settings; an additional config gets
        /// INHERIT-aware ones. Hidden legacy carriers capture pre-refactor saved values for
        /// <see cref="MigrateLegacy"/>.</summary>
        public static void BuildConfigSchema(CategorySetting into, CategorySetting inheritFrom = null)
        {
            bool inherit = inheritFrom != null;

            var outputChoices = SpeechOutputs.Choices();
            if (inherit)
            {
                var nc = new NullableChoiceSetting("output", "Speech output", outputChoices, localizationKey: "speech.output");
                nc.ResolveInherited = () => inheritFrom.Get<ChoiceSetting>("output")?.ValueId ?? SpeechOutputs.Auto;
                into.Add(nc);
            }
            else
            {
                into.Add(new ChoiceSetting("output", "Speech output", outputChoices, SpeechOutputs.Auto, "speech.output"));
            }

            // The only output with params is SAPI (subtree key unchanged from the handler era, so
            // existing voice/rate/volume/boost settings carry over untouched).
            var sapi = new CategorySetting("sapi", "SAPI", localizationKey: "speech.sapi");
            if (inherit) BuildInheritParams(sapi, inheritFrom.Get<CategorySetting>("sapi"));
            else BuildSapiParams(sapi);
            into.Add(sapi);

            // Legacy migration carriers: the pre-refactor file stored "handler" (+ "prism.backend").
            into.Add(new StringSetting("handler", "Legacy handler", "") { Hidden = true });
            into.Add(new StringSetting("prism.backend", "Legacy prism backend", "") { Hidden = true });
        }

        private static void BuildSapiParams(CategorySetting into)
        {
            into.Add(new IntSetting("rate", "Rate", 2, -10, 10, 1, "speech.sapi.rate"));
            into.Add(new IntSetting("volume", "Volume", 100, 0, 100, 5, "speech.sapi.volume"));
            // Playback gain above SAPI's 100 ceiling, applied only to POSITIONAL speech mixed through
            // the mod's own audio — which is otherwise quiet. 100 = no boost.
            into.Add(new IntSetting("boost", "Volume boost", 100, 100, 400, 10, "speech.sapi.boost"));
            var voices = SpeechOutputs.SapiVoiceChoices();
            into.Add(new ChoiceSetting("voice", "Voice", voices, voices[0].Id, "speech.sapi.voice"));
        }

        // Inherit-aware mirror of the SAPI params: each int becomes a NullableIntSetting that follows
        // the default config's same int; the voice choice gains an "Inherit" option (default).
        private static void BuildInheritParams(CategorySetting into, CategorySetting defaultSub)
        {
            var scratch = new CategorySetting("sapi", "SAPI");
            BuildSapiParams(scratch);
            foreach (var child in scratch.Children)
            {
                switch (child)
                {
                    case IntSetting i:
                        into.Add(new NullableIntSetting(i.Key, i.Label, defaultSub?.Get<IntSetting>(i.Key), i.Min, i.Max, i.Step, i.LocalizationKey));
                        break;
                    case ChoiceSetting c:
                        var cs = new NullableChoiceSetting(c.Key, c.Label, c.Choices, localizationKey: c.LocalizationKey);
                        var defChoice = defaultSub?.Get<ChoiceSetting>(c.Key);
                        cs.ResolveInherited = () => defChoice?.ValueId;
                        into.Add(cs);
                        break;
                    default:
                        into.Add(child);
                        break;
                }
            }
        }

        /// <summary>Convert a config's pre-refactor saved handler/backend into an output pick (run once
        /// per config after settings load; no-op when no legacy value was in the file).</summary>
        public static void MigrateLegacy(CategorySetting tree)
        {
            var legacyHandler = tree?.Get<StringSetting>("handler");
            var legacyBackend = tree?.Get<StringSetting>("prism.backend");
            string handler = legacyHandler?.Get();
            if (string.IsNullOrEmpty(handler)) return;
            string output = SpeechOutputs.FromLegacy(handler, legacyBackend?.Get());
            var plain = tree.Get<ChoiceSetting>("output");
            if (plain != null) plain.Set(output);
            else tree.Get<NullableChoiceSetting>("output")?.SetExplicit(output);
            legacyHandler.Set("");
            legacyBackend?.Set("");
            Main.Log?.Log("[speech] migrated legacy handler '" + handler + "' -> output '" + output + "'.");
        }

        /// <summary>Arm speech after settings load (the persisted output applies from the first word).</summary>
        public static void Initialize()
        {
            if (Default?.Tree != null) MigrateLegacy(Default.Tree);
            _initialized = true;
            if (Context != null && Default != null)
            {
                var st = Context.Resolve(Default.Profile, SpeechFeature.Speak, out var voice, out var fellBack, out _);
                Main.Log?.Log("[speech] default output = " + Default.OutputId + " -> voice " + voice.ToString("x")
                    + " (" + (Context.GetVoice(voice)?.Name ?? "?") + ")" + (fellBack ? " [fell back]" : "") + " " + st);
            }
        }

        public static bool Ready => _initialized && Default != null && Context != null;

        /// <summary>Close the library context (module hot-reload teardown): joins its worker, releases
        /// every voice. The next generation re-opens from the persisted settings.</summary>
        public static void Shutdown()
        {
            _initialized = false;
            try { Context?.Dispose(); } catch (Exception e) { Main.Log?.Error("[speech] dispose: " + e.Message); }
            Context = null;
            _tree = null;
            SpeechOutputs.Reset();
        }

        // ---- the default-config speak API (UI / announcements / dialogue) ----

        public static void Speak(string text, bool interrupt = false) { Tap(text); if (Ready) Default.Speak(text, interrupt); }
        public static void Output(string text, bool interrupt = false) { Tap(text); if (Ready) Default.Output(text, interrupt); }

#if DEBUG
        /// <summary>Dev-only tap: every string spoken through the default path is mirrored here so the dev
        /// server's /speech log can read back what was said. Null in a normal run; set by the dev server.</summary>
        public static Action<string> Observer;
        internal static void Tap(string text) { if (!string.IsNullOrEmpty(text)) Observer?.Invoke(text); }
#else
        internal static void Tap(string text) { } // no-op; the dev speech tap exists only in Debug builds
#endif
        public static void Silence() { if (Ready) Default.Silence(); }

        // ---- the events path ----

        /// <summary>Speak an event line through <paramref name="config"/> on the EVENTS queue: synthesizer
        /// voices overlap (combat bursts), a screen reader queues as it always does.</summary>
        public static bool SpeakEvent(SpeechConfig config, string text)
        {
            if (!Ready || config == null) return false;
            return Context.Output(EventQueue, config.Profile, text, SpeakFlags.None, out _) == Status.Ok;
        }

        /// <summary>Speak an event line AT a world position through <paramref name="config"/>: rendered
        /// off-thread by the library and fired into the mod's audio space. A voice that cannot render
        /// (a screen reader) speaks the line flat instead — the user's voice never changes behind their
        /// back. False when there is no Loadstar audio device to fire into (caller speaks flat).</summary>
        public static bool SpeakEventAt(SpeechConfig config, string text, UnityEngine.Vector3 worldPos)
        {
            if (!Ready || config == null) return false;
            var engine = WrathAccess.Audio.LoadstarEngine.Current;
            if (engine == null) return false;
            return engine.TrySpeakAt(Context, EventQueue, config.Profile, text, worldPos, config.Boost);
        }

        private static void OnDefaultOutputChanged(string outputId)
        {
            if (!_initialized || !Ready) return; // pre-init writes are just the settings file loading
            var label = _outputSetting?.Current?.Label ?? outputId;
            Default.Output(Message.Localized("ui", "speech.handler_changed", new { handler = label }).Resolve());
        }

        /// <summary>Panic recovery (bound to a global hotkey): force the default speech config back to the
        /// Auto output (your screen reader, best available) and ask the library to re-probe, so a reader
        /// started after boot — or a stuck pick — can't keep us silent. Safe to call any time.</summary>
        public static void ResetToPrism()
        {
            if (!Ready) return;
            Context.Refresh();
            var outputSetting = Default.Tree?.Get<ChoiceSetting>("output");
            bool willAnnounceViaChange = outputSetting != null && outputSetting.ValueId != SpeechOutputs.Auto;
            outputSetting?.Set(SpeechOutputs.Auto); // persist; fires OnDefaultOutputChanged IF it changed
            if (!willAnnounceViaChange)
            {
                Context.Resolve(Default.Profile, SpeechFeature.Speak, out var voice, out _, out _);
                var name = Context.GetVoice(voice)?.Name ?? "none";
                Default.Output(Message.Localized("ui", "speech.reset_prism", new { handler = name }).Resolve(), interrupt: true);
            }
            Main.Log?.Log("[speech] reset-to-auto-output requested.");
        }
    }
}
