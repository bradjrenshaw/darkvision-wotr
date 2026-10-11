using System;
using System.Collections.Generic;
using WrathAccess.Speech;

namespace WrathAccess.Events
{
    /// <summary>
    /// Collects <see cref="ModEvent"/>s and flushes them once per frame (so a burst is read in arrival
    /// order, and we never speak mid-game-frame). PROTOTYPE: no consolidation/dedup — every event reads
    /// individually; never interrupts (the mod's speech rule). Each event resolves its (per-source)
    /// settings via <see cref="EventRegistry"/> and reads through the chosen <see cref="SpeechConfig"/>
    /// on the EVENTS queue: positioned at the unit when the event opts in (the library renders off the
    /// game thread and fires into the audio space), otherwise flat but overlapping on synthesizer voices.
    /// </summary>
    internal static class EventDispatcher
    {
        private static readonly List<ModEvent> _pending = new List<ModEvent>();

        public static void Raise(ModEvent e)
        {
            if (e != null) _pending.Add(e);
        }

        public static void Tick()
        {
            if (_pending.Count == 0) return;
            // Snapshot count: an event's GetMessage/dispatch won't re-enter and grow this mid-loop, but
            // be defensive — only flush what's present now, keep anything raised during the flush.
            int n = _pending.Count;
            for (int i = 0; i < n; i++) Dispatch(_pending[i]);
            _pending.RemoveRange(0, n);
        }

        private static void Dispatch(ModEvent e)
        {
            try
            {
                if (!Main.Enabled || !e.Visible || !EventRegistry.Enabled(e) || !InRange(e)) return;
                var msg = e.GetMessage();
                if (msg == null || msg.IsEmpty) return;
                var text = msg.Resolve();
                SpeechManager.Tap(text); // dev speech log: events speak through a config, not Tts.Speak
                var config = SpeechConfigRegistry.Get(EventRegistry.ConfigId(e));

                bool positional = EventRegistry.Positional(e) && e.Source != EventSources.None && e.Position != UnityEngine.Vector3.zero;
                if (positional && SpeechManager.SpeakEventAt(config, text, e.Position)) return;
                if (SpeechManager.SpeakEvent(config, text)) return;
                config.Output(text, interrupt: false); // no context yet: the UI queue is better than silence
            }
            catch (Exception ex) { Main.Log?.Error("[events] dispatch failed: " + ex.Message); }
        }

        // The spoken-event radius (per-event/per-source "Maximum distance", feet, 0 = unlimited),
        // measured from the same "ears" positional playback uses (cursor if placed, else player) — a
        // distant crowd of neutrals firing buff/heal events at once was pure noise. Sourceless and
        // position-less events always pass (they're party-scoped by nature).
        private static bool InRange(ModEvent e)
        {
            if (e.Source == EventSources.None) return true;
            int maxFt = EventRegistry.MaxDistanceFeet(e);
            if (maxFt <= 0) return true;
            var p = e.Position;
            if (p == UnityEngine.Vector3.zero) return true;
            var from = WrathAccess.Exploration.Cursor.Has
                ? WrathAccess.Exploration.Cursor.Position.Value
                : WrathAccess.Exploration.Overlays.Cursor.PlayerPosition;
            float dx = p.x - from.x, dz = p.z - from.z;
            return WrathAccess.Exploration.Geo.Feet(UnityEngine.Mathf.Sqrt(dx * dx + dz * dz)) <= maxFt;
        }
    }
}
