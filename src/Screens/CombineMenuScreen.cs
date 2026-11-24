using System;
using System.Collections.Generic;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's COMBINE menu, by ear: interacting with a combine object (a puzzle's slab space)
    /// pops a small radial of buttons over it — "Select slab", "Turn slab around" — each with a hint
    /// text and its own availability condition (OvertipsViewCombinePartBase). This is that radial as
    /// a plain button list, pushed as a child screen so it owns the keyboard while open; choosing
    /// runs the entry and closes, Escape closes without acting (the radial's own click-away).
    /// </summary>
    public sealed class CombineMenuScreen : Screen
    {
        private readonly string _title;
        private readonly List<string> _labels;
        private readonly Action<int> _onSelect;
        private readonly Action _onClosed;

        private CombineMenuScreen(string title, List<string> labels, Action<int> onSelect, Action onClosed)
        {
            _title = title;
            _labels = labels;
            _onSelect = onSelect;
            _onClosed = onClosed;
            Wrap = true;
        }

        /// <summary>Open the menu as a child of the current screen. <paramref name="onClosed"/> runs
        /// however it closes (a choice or Escape) — the caller dismisses the game's radial there.</summary>
        public static void Open(string title, List<string> labels, Action<int> onSelect, Action onClosed)
        {
            UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
            ScreenManager.Current?.PushChild(new CombineMenuScreen(title, labels, onSelect, onClosed));
        }

        public override string Key => "overlay.combinemenu";
        public override string ScreenName => _title;
        public override bool IsActive() => false; // never poll-pushed — only ever a child screen

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private void Close()
        {
            ParentScreen?.RemoveChild(this);
            try { _onClosed?.Invoke(); } catch (Exception e) { Main.Log?.Warning("[combine] close: " + e.Message); }
        }

        public override void Build(GraphBuilder b)
        {
            for (int i = 0; i < _labels.Count; i++)
            {
                int idx = i;
                string label = _labels[i];
                b.AddItem(ControlId.Structural("combine:" + i), GraphNodes.Button(
                    () => label,
                    () => { Close(); _onSelect?.Invoke(idx); },
                    position: GraphNodes.Position(i + 1, _labels.Count)));
            }
        }
    }
}
