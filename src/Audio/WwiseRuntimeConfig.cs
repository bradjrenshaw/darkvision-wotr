using System.Reflection;
using HarmonyLib; // AccessTools (protected settings accessor)
using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Applies the mod's Wwise-level audio options each frame (polled, like all live settings):
    ///
    /// - <c>audio.panning</c>: Wwise's global stereo panning rule. The game never calls
    ///   SetPanningRule, so it runs on the conservative "Speakers" default; "Headphones" is
    ///   constant-power panning across the full stereo width — audibly wider positioning
    ///   (user A/B on live cues, 2026-07-28). Game audio only; the classic NAudio path has its
    ///   own spatializer.
    /// - <c>audio.background_audio</c>: keep the game audible when the window loses focus.
    ///   Two vanilla mechanisms silence it: the Unity player pauses outright (runInBackground
    ///   is false and nothing in the game writes it), and the Wwise integration suspends the
    ///   sound engine on focus loss unless its RenderDuringFocusLoss advanced setting is on
    ///   (AkSoundEngineController.OnApplicationFocus → Suspend). Setting both keeps the world
    ///   running and audible while alt-tabbed — the screen-reader multitasking case.
    ///   While that keeps the game running unfocused, frames are capped at <see cref="BackgroundFps"/>:
    ///   the game has no frame cap of its own (VSync off = as fast as the GPU goes, ~420 fps measured at
    ///   the main menu), and nobody is looking at it — only audio and speech matter, which run on their
    ///   own threads.
    /// </summary>
    internal static class WwiseRuntimeConfig
    {
        private static string _appliedPanning;
        private static bool? _appliedBackground;

        // AkCommonPlatformSettings.GetAdvancedSettings is protected; the object it returns carries
        // the public m_RenderDuringFocusLoss field the focus handler reads live on every focus loss.
        private static readonly MethodInfo GetAdvancedSettings =
            AccessTools.Method(typeof(AkCommonPlatformSettings), "GetAdvancedSettings");

        // The third mute path: SoundState.OnApplicationFocusChanged posts the bank-authored
        // "PauseAudio" event on every focus loss — a global mute that survives the engine-level
        // fixes. Its own kill switch (ApplicationFocusEvents.SoundDisabled, normally only the
        // -focus-disable-sound command line) has a private setter.
        private static readonly MethodInfo SetSoundDisabled =
            AccessTools.PropertySetter(typeof(Kingmaker.Utility.ApplicationFocusEvents), "SoundDisabled");

        public static void Tick()
        {
            TickPanning();
            TickBackgroundAudio();
            TickBackgroundFrameCap();
        }

        // ---- the background frame cap ----

        private const int BackgroundFps = 15;
        private static bool _capped;
        private static int _savedVSync, _savedTargetFps;

        // Capped only while background audio keeps the game running AND the window is unfocused (with
        // the setting off the player pauses unfocused anyway). targetFrameRate is ignored while vsync is
        // on, so vsync goes off for the cap; both are restored exactly on focus.
        private static void TickBackgroundFrameCap()
        {
            bool want = (ModSettings.GetSetting<BoolSetting>("audio.background_audio")?.Get() ?? false)
                && !UnityEngine.Application.isFocused;
            if (want == _capped) return;
            if (!want) { ReleaseFrameCap(); return; }
            _savedVSync = UnityEngine.QualitySettings.vSyncCount;
            _savedTargetFps = UnityEngine.Application.targetFrameRate;
            UnityEngine.QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = BackgroundFps;
            _capped = true;
        }

        /// <summary>Undo the background cap (on focus, and on module dispose / master-switch off so the
        /// game never keeps our cap).</summary>
        public static void ReleaseFrameCap()
        {
            if (!_capped) return;
            _capped = false;
            UnityEngine.QualitySettings.vSyncCount = _savedVSync;
            UnityEngine.Application.targetFrameRate = _savedTargetFps;
        }

        private static void TickPanning()
        {
            if (!AkSoundEngine.IsInitialized()) return;
            var pan = ModSettings.GetSetting<ChoiceSetting>("audio.panning")?.ValueId ?? "speakers";
            if (pan == _appliedPanning) return;
            _appliedPanning = pan;
            AkSoundEngine.SetPanningRule(pan == "headphones"
                ? AkPanningRule.AkPanningRule_Headphones : AkPanningRule.AkPanningRule_Speakers);
        }

        private static void TickBackgroundAudio()
        {
            bool on = ModSettings.GetSetting<BoolSetting>("audio.background_audio")?.Get() ?? false;
            // First evaluation after a (re)load: apply ON in full (a saved "on" must work from launch —
            // it used to set only runInBackground, so the Wwise suspend and the PauseAudio mute stayed
            // armed until the setting was toggled), but never WRITE off. Writing runInBackground=false
            // here froze the whole player loop whenever a module hot-reload happened with the game
            // UNFOCUSED (the DEBUG dev server's re-assert runs from the same loop, so it never got
            // to undo it — every /eval timed out until the window was clicked). The vanilla
            // pause-on-unfocus is the engine default anyway; only a live toggle needs the write.
            if (_appliedBackground == null)
            {
                if (!AkSoundEngine.IsInitialized()) return; // seed once Wwise is up (its settings + mute live there)
                _appliedBackground = on;
                if (on) ApplyBackground(true);
                return;
            }
            if (_appliedBackground != on)
            {
                _appliedBackground = on;
                // Written once per toggle: turning OFF restores the vanilla pause-on-unfocus (the
                // DEBUG dev server re-asserts true on its own tick and wins there — intended).
                ApplyBackground(on);
            }
            else if (on)
                UnityEngine.Application.runInBackground = true; // cheap re-assert, mirrors DevServer
        }

        // All three mute paths, one direction.
        private static void ApplyBackground(bool on)
        {
            UnityEngine.Application.runInBackground = on;
            if (GetAdvancedSettings != null
                && AkWwiseInitializationSettings.ActivePlatformSettings is AkCommonPlatformSettings common
                && GetAdvancedSettings.Invoke(common, null) is AkCommonAdvancedSettings adv)
                adv.m_RenderDuringFocusLoss = on;
            SetSoundDisabled?.Invoke(null, new object[] { on });
            // A "PauseAudio" posted BEFORE the flag flipped never gets its "ResumeAudio" (the
            // flag suppresses both directions) — e.g. enabling the setting while the mute is
            // latched. Post the resume defensively; it's a no-op when audio is already live.
            if (on && AkSoundEngine.IsInitialized())
                Kingmaker.Sound.SoundEventsManager.PostEvent("ResumeAudio", null);
        }
    }
}
