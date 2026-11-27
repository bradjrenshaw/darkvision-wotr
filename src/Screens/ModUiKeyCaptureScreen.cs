using System;
using UnityEngine;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Captures one key combination for ANOTHER mod's hotkey (a ModKit "Bind" slot): the sibling of
    /// <see cref="ModKeyCaptureScreen"/>, which does the same for our own actions and carries their
    /// conflict rules. Pushed as a child of the mod-window screen. It sets
    /// <see cref="Screen.CapturesRawInput"/> so our navigation stands down, waits for the key that
    /// opened it to be released, then hands the next non-modifier key (with the Ctrl/Shift/Alt held)
    /// to the caller, which stores it in the other mod. Escape cancels.
    /// </summary>
    public sealed class ModUiKeyCaptureScreen : Screen
    {
        private readonly string _what;
        private readonly Func<KeyCode, bool, bool, bool, bool> _store; // (key, ctrl, shift, alt) → stored?
        private bool _armed;
        private bool _awaitRelease;
        private KeyCode _releaseKey;

        private ModUiKeyCaptureScreen(string what, Func<KeyCode, bool, bool, bool, bool> store)
        {
            _what = what ?? "";
            _store = store;
        }

        public static void Open(string what, Func<KeyCode, bool, bool, bool, bool> store)
        {
            if (store != null) ScreenManager.Current?.PushChild(new ModUiKeyCaptureScreen(what, store));
        }

        public override string Key => "overlay.moduikeycapture";
        public override bool CapturesRawInput => true;
        public override bool IsActive() => false; // only ever a child screen

        public override void OnFocus()
            => Tts.Speak(Message.Localized("settings", "rebind.prompt", new { action = _what }).Resolve());

        public override void OnUpdate()
        {
            // After a bind, stay up (input still stood down) until the key is released — otherwise
            // the same press carries on into the window underneath.
            if (_awaitRelease)
            {
                if (!UnityEngine.Input.GetKey(_releaseKey)) Close();
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Tts.Speak(Message.Localized("settings", "rebind.cancelled").Resolve());
                _releaseKey = KeyCode.Escape;
                _awaitRelease = true; // or this Escape also closes the mod window
                return;
            }

            // The Enter that opened the dialog must come up first, or it is the key captured.
            if (!_armed)
            {
                if (!UnityEngine.Input.anyKey) _armed = true;
                return;
            }

            if (!UnityEngine.Input.anyKeyDown) return;

            foreach (var key in ModKeyCaptureScreen.CaptureKeys)
            {
                if (!UnityEngine.Input.GetKeyDown(key)) continue;
                bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
                bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
                bool alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
                bool ok;
                try { ok = _store(key, ctrl, shift, alt); }
                catch (Exception e)
                {
                    ok = false;
                    Main.Log?.Error("[modui] key capture: " + e);
                }
                if (ok)
                    Tts.Speak(Message.Localized("settings", "rebind.bound", new
                    {
                        action = _what,
                        combo = new WrathAccess.Input.KeyboardBinding(key, ctrl, shift, alt).DisplayName,
                    }).Resolve());
                else
                    Tts.Speak(Loc.T("modui.bind_failed"));
                _releaseKey = key;
                _awaitRelease = true;
                return;
            }
        }

        private void Close() => ParentScreen?.RemoveChild(this);
    }
}
