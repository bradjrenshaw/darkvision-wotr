using System;
using System.Collections.Generic;
using Kingmaker.UI; // UISoundType
using WrathAccess.Settings;
using WrathAccess.Speech;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// First-run setup wizard (and re-runnable later): walks a new player through the few high-impact,
    /// preference-driven choices in plain language, reusing the REAL settings controls so the menu stays
    /// the single source of truth. Graph-native (no longer on the <see cref="WizardScreen"/> shell, which
    /// remains for the game-VM wizards): a roadmap stop (one jump-target per active phase, each with a
    /// LIVE one-line summary of its current choice), the current step's content under the step title as
    /// context — each chunk (help text, option list, settings tree, test button) its OWN Tab-stop, like
    /// the old loose-element layout; content keys carry the step so a page turn re-keys the page — and
    /// Back/Next stops.
    /// Advancing plays the page-turn and lands focus on the new page's content (<c>FocusStop</c>). The
    /// phase set is dynamic — the event voice steps appear only when events are spoken. Mod-pushed
    /// (static open flag); Escape closes.
    /// </summary>
    public sealed class SetupWizardScreen : Screen
    {
        public SetupWizardScreen() { Wrap = true; }

        // Speech only: movement, wall tones and sonar are plain defaults (see ScanEnabled /
        // OverlaySettingsRegistry.DefaultOn), tuned in the settings menu.
        private enum Step { Speech, EventFeedback, EnemyVoice, AllyVoice, UnitlessVoice }

        private static bool s_open;
        private static Step s_step;

        public override string Key => "ctx.setupwizard";
        public override string ScreenName => Loc.T("screen.setup_wizard");
        public override int Layer => 36;       // above the mod menu (35); a modal over the main menu
        public override bool Exclusive => true; // owns the keyboard while open
        public override bool IsActive() => s_open;

        public static void Open()
        {
            s_open = true;
            s_step = Step.Speech;
            // Positional events are the default answer: until the wizard has created its event voices
            // (and unless events were turned off for the game log), apply it so Next-Next-Finish gets it.
            if (string.IsNullOrEmpty(ModSettings.GetSetting<StringSetting>(EnemySlot)?.Get()) && CurrentMode() == "positional")
                Apply(events: true);
        }
        private static void Close()
        {
            s_open = false;
            // First-run: remember the wizard's been shown so it won't auto-launch on future boots. Any
            // dismissal (Finish, backing off the first step, or Escape) counts; re-run from the Ctrl+M menu.
            ModSettings.GetSetting<BoolSetting>("wizard.completed")?.Set(true);
        }

        // Open landing goes to the page content, not the roadmap (the roadmap stays first in Tab
        // order). Declarative: an OnPush FocusStop request would be cleared by the attach that follows.
        public override object InitialFocusStop => "content";

        private static string TitleFor(Step step)
        {
            switch (step)
            {
                case Step.Speech: return Loc.T("wizard.speech.title");
                case Step.EventFeedback: return Loc.T("wizard.events.title");
                case Step.EnemyVoice: return Loc.T("wizard.events.enemy_voice_title");
                case Step.AllyVoice: return Loc.T("wizard.events.ally_voice_title");
                case Step.UnitlessVoice: return Loc.T("wizard.events.unitless_voice_title");
                default: return "";
            }
        }


        public override void Build(GraphBuilder b)
        {
            if (!s_open) return;

            // The roadmap: one jump-target per active phase, with a live summary of its current choice —
            // the "see what to revisit and jump straight back" path. Keys are the step names (stable), so
            // roadmap focus survives page turns; jumping moves focus to the content anyway.
            b.BeginStop("steps").PushContext(Loc.T("wizard.steps"), "list");
            foreach (var step in ActiveSteps())
            {
                var s = step; // capture for the live closures
                b.AddItem(ControlId.Structural("wiz:step:" + s),
                    GraphNodes.Tab(() => RoadmapLabel(s), () => s_step == s, () => JumpTo(s)));
            }
            b.PopContext();

            // The current page, under the step title as context; keys carry the step so a page turn
            // re-keys the whole page (GoTo/JumpTo land focus here via FocusStop).
            string k = "wiz:" + s_step + ":";
            b.BeginStop("content").PushContext(TitleFor(s_step));
            BuildContent(b, k);
            b.PopContext();

            // Footer: Back then Next/Finish (labels live).
            b.BeginStop("back").AddItem(ControlId.Structural("wiz:back"),
                GraphNodes.Button(() => Loc.T("wizard.back"), () => GoTo(-1)));
            b.BeginStop("next").AddItem(ControlId.Structural("wiz:next"),
                GraphNodes.Button(() => IsLastStep() ? Loc.T("wizard.finish") : Loc.T("wizard.next"), () => GoTo(+1)));
        }

        private void BuildContent(GraphBuilder b, string k)
        {
            switch (s_step)
            {
                case Step.Speech: BuildSpeechStep(b, k); break;
                case Step.EventFeedback: BuildEventFeedbackStep(b, k); break;
                case Step.EnemyVoice: BuildVoiceStep(b, k, EnemySlot, "wizard.events.enemy_voice_help"); break;
                case Step.AllyVoice: BuildVoiceStep(b, k, AllySlot, "wizard.events.ally_voice_help"); break;
                case Step.UnitlessVoice: BuildVoiceStep(b, k, UnitlessSlot, "wizard.events.unitless_voice_help"); break;
            }
        }

        private static string RoadmapLabel(Step step)
        {
            var title = TitleFor(step);
            var summary = SummaryFor(step);
            return string.IsNullOrEmpty(summary) ? title : title + ": " + summary;
        }

        private static string SummaryFor(Step step)
        {
            switch (step)
            {
                case Step.Speech: return SelectedVoiceLabel();
                case Step.EventFeedback: return Loc.T("wizard.events." + CurrentMode());
                default: return "";
            }
        }

        // ---- Step 1: the voice, with its settings below it (like the settings screen: picking a different
        //      voice re-renders the settings that voice takes; a screen reader shows none) ----

        private static void BuildSpeechStep(GraphBuilder b, string k)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.speech.help")));
            b.BeginStop(k + "options"); // the picker + settings are their own Tab-stop after the help text
            var config = SpeechManager.Default;
            var voice = VoiceSetting();
            if (voice == null) return;
            WrathAccess.Speech.SpeechVoicePicker.Emit(b, k, config, voice, false, SelectedVoice);
            var prm = config.Tree?.Get<CategorySetting>("params");
            if (prm != null) ModSettingsScreen.EmitParamsInline(b, prm, config, k);
        }

        private static void SelectedVoice(string id)
        {
            // The sample IS the feedback: interrupt purges the generic voice-changed confirm (and any
            // backlog from rapid switching), so you hear THIS voice demonstrate itself right where you
            // chose it. (Clipboard has no audio — its "sample" lands on the clipboard, which is its nature.)
            Tts.Speak(Loc.T("wizard.speech.sample", new { name = SelectedVoiceLabel() }), interrupt: true);
        }

        // ---- Step: event feedback (one mode choice → events + log + event voices). "Events" not "combat":
        //      damage / healing / spellcasts etc. fire in AND out of combat. ----

        private const string EnemySlot = "wizard.enemy_config"; // persisted ids of the wizard's three event configs
        private const string AllySlot = "wizard.ally_config";
        private const string UnitlessSlot = "wizard.unitless_config";
        // Every event source bucket the wizard drives. "unitless" = sourceless events (time passing, party-wide
        // notices); without it those fall through to the default voice. The positional preset gives it its own
        // voice (below); screen-reader / log modes set all four the same way via this list.
        private static readonly string[] SourceBuckets = { "party", "enemy", "neutral", "unitless" };

        private static void BuildEventFeedbackStep(GraphBuilder b, string k)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.events.help")));
            b.BeginStop(k + "options");
            b.AddItem(ControlId.Structural(k + "positional"), GraphNodes.ChoiceOption(
                () => Loc.T("wizard.events.positional"), () => CurrentMode() == "positional", () => Apply(events: true)));
            b.AddItem(ControlId.Structural(k + "log"), GraphNodes.ChoiceOption(
                () => Loc.T("wizard.events.log"), () => CurrentMode() == "log", () => Apply(events: false)));
        }

        // The game's Combat + Magic log groups stay ON either way (verbose on purpose: alpha feedback on
        // how much people keep). Events on: spoken in the world with DISTINCT enemy vs ally voices;
        // neutrals share the ally voice, and sourceless (unitless) events get their own voice — they have
        // no map position, so they read NON-positionally (the dispatcher never speaks a sourceless event
        // spatially regardless). Events off: the log alone.
        private static void Apply(bool events) => ModSettings.Batch(() =>
        {
            if (events)
            {
                var enemy = EnsureConfig(EnemySlot, "wizard.events.enemy_config_name");
                var ally = EnsureConfig(AllySlot, "wizard.events.ally_config_name");
                var unitless = EnsureConfig(UnitlessSlot, "wizard.events.unitless_config_name");
                SetEventSource("enemy", enabled: true, config: enemy, positional: true);
                SetEventSource("party", enabled: true, config: ally, positional: true);
                SetEventSource("neutral", enabled: true, config: ally, positional: true);
                SetEventSource("unitless", enabled: true, config: unitless, positional: false);
            }
            else
                foreach (var b in SourceBuckets) SetEventSource(b, enabled: false, config: "default", positional: false);
            SetLogGroup("combat", true);
            SetLogGroup("magic", true);
        });

        // Which mode the live settings reflect (read off the enemy bucket) — for the radio "selected"
        // marker + roadmap summary; the menu's per-source controls remain the source of truth.
        private static string CurrentMode()
            => (ModSettings.GetCategory("events.settings.enemy")?.Get<BoolSetting>("enabled")?.Get() ?? true)
                ? "positional" : "log";

        // Set one source bucket's global event output (the per-source defaults the events inherit).
        private static void SetEventSource(string bucket, bool enabled, string config, bool positional)
        {
            var b = ModSettings.GetCategory("events.settings." + bucket);
            b?.Get<BoolSetting>("enabled")?.Set(enabled);
            b?.Get<ChoiceSetting>("speech_config")?.Set(config);
            b?.Get<BoolSetting>("positional")?.Set(positional);
        }

        // Turn a whole log message group (combat / magic) on or off — the Default overlay's log defaults.
        private static void SetLogGroup(string group, bool on)
        {
            var cat = ModSettings.GetCategory("defaults.log." + group);
            if (cat == null) return;
            foreach (var child in cat.Children)
                if (child is BoolSetting b) b.Set(on);
        }

        // Reuse the slot's remembered config if it still exists, else create a fresh synthesizer config and
        // remember its id (so re-runs reuse/re-tune the same enemy/ally voices, not pile up duplicates).
        private static string EnsureConfig(string slotPath, string nameKey)
        {
            var slot = ModSettings.GetSetting<StringSetting>(slotPath);
            var id = slot?.Get();
            if (!string.IsNullOrEmpty(id))
                foreach (var existing in SpeechConfigRegistry.Ids())
                    if (existing == id) return id;
            id = SpeechConfigRegistry.Add();
            SpeechConfigRegistry.SetName(id, Loc.T(nameKey));
            // An additional config inherits the default's voice until it picks one. The positional voices
            // need render-to-audio, which only a synthesizer does: pick the SAPI 5 category's default.
            SpeechConfigRegistry.Get(id)?.Tree?.Get<StringSetting>("voice")?.Set(
                WrathAccess.Speech.SpeechVoices.CategoryAuto(WrathAccess.Speech.SpeechVoices.Sapi5Key));
            slot?.Set(id);
            return id;
        }

        // ---- Steps: the enemy / ally / sourceless voices (the voice picker, its settings, a test line) ----

        private static void BuildVoiceStep(GraphBuilder b, string k, string slotPath, string helpKey)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T(helpKey)));
            var config = SlotConfig(slotPath);
            var voice = config?.Tree?.Get<StringSetting>("voice");
            if (voice != null)
            {
                b.BeginStop(k + "settings");
                SpeechVoicePicker.Emit(b, k, config, voice, true, null);
                var prm = config.Tree.Get<CategorySetting>("params");
                if (prm != null) ModSettingsScreen.EmitParamsInline(b, prm, config, k);
            }
            b.BeginStop(k + "test").AddItem(ControlId.Structural(k + "test"),
                GraphNodes.Button(() => Loc.T("wizard.events.test"), () => TestVoice(slotPath)));
        }

        // The wizard-created config behind a slot (null until the positional mode created it).
        private static SpeechConfig SlotConfig(string slotPath)
        {
            var id = ModSettings.GetSetting<StringSetting>(slotPath)?.Get();
            return string.IsNullOrEmpty(id) ? null : SpeechConfigRegistry.Get(id);
        }

        private static void TestVoice(string slotPath)
        {
            var id = ModSettings.GetSetting<StringSetting>(slotPath)?.Get();
            if (!string.IsNullOrEmpty(id)) SpeechConfigRegistry.Get(id)?.Speak(Loc.T("wizard.events.test_line"), interrupt: true);
        }

        // ---- navigation ----

        // The active step sequence (dynamic — the event voice steps appear only when events are spoken,
        // and future phases just append here).
        private static Step[] ActiveSteps()
        {
            var steps = new List<Step> { Step.Speech, Step.EventFeedback };
            // tune the three event voices — enemy, ally, and the sourceless (unitless) voice
            if (CurrentMode() != "log") { steps.Add(Step.EnemyVoice); steps.Add(Step.AllyVoice); steps.Add(Step.UnitlessVoice); }
            return steps.ToArray();
        }

        // Jump straight to a phase from the roadmap — any active phase, forward or back (the wizard's phases
        // are independent settings, no gating); this is the "go back and adjust later" path.
        private static void JumpTo(Step step)
        {
            if (s_step == step) return;
            s_step = step;
            UiSound.Play(UISoundType.BookPageTurn);
            Navigation.FocusStop("content"); // land on the new page (its keys carry the step)
        }

        // Step by ±1 through the active sequence; stepping off either end leaves the wizard.
        private static void GoTo(int delta)
        {
            var steps = ActiveSteps();
            int i = Array.IndexOf(steps, s_step) + delta;
            // Stepping forward off the end = Finish: play the same fanfare the chargen wizard plays on
            // completion. Backing off the front (i < 0) is a cancel, so no sound there.
            if (i >= steps.Length) { UiSound.Play(UISoundType.ChargenCompleteClick); Close(); return; }
            if (i < 0) { Close(); return; }
            s_step = steps[i];
            UiSound.Play(UISoundType.BookPageTurn);
            Navigation.FocusStop("content");
        }

        private static bool IsLastStep()
        {
            var steps = ActiveSteps();
            return Array.IndexOf(steps, s_step) == steps.Length - 1;
        }

        // Escape (ui.back) closes the wizard, like the mod menu.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        // ---- helpers ----

        private static StringSetting VoiceSetting() => SpeechManager.Default?.Tree?.Get<StringSetting>("voice");

        // The chosen voice's display label (what it resolved to, else the pick itself).
        private static string SelectedVoiceLabel()
        {
            var name = SpeechManager.Default?.ResolvedVoiceName;
            return string.IsNullOrEmpty(name) ? (SpeechManager.Default?.VoiceLabel ?? "") : name;
        }
    }
}
