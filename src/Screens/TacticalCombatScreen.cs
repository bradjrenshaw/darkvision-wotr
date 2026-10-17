using System.Collections.Generic;
using WrathAccess.Input; // InputCategory
using WrathAccess.UI;    // Navigation

namespace WrathAccess.Screens
{
    /// <summary>
    /// A crusade (tactical) battle — the game's <c>RootUIContext.IsTacticalCombat</c>. The base context while
    /// the battle runs, like the in-game and world-map screens: it starts UNFOCUSED so the movement keys
    /// drive the battle-grid cursor (<see cref="WrathAccess.Exploration.Overlays.Cursor.Tactical"/>, read out by
    /// the engaged overlay's <see cref="WrathAccess.Exploration.Overlays.TacticalCellSystem"/>), and claims the
    /// shared Exploration actions (cursor keys, recenter, announce; Route()d by screen) plus InGame for the
    /// pause menu. Tab enters the HUD's three stops (<see cref="TacticalCombatNodes"/>: the acting squad's
    /// bar, the general's bar + spellbook, the initiative order); Escape leaves it again.
    /// </summary>
    public sealed class TacticalCombatScreen : Screen
    {
        public const string ScreenKey = "ctx.tacticalcombat";
        public override string Key => ScreenKey;
        public override string ScreenName => Loc.T("screen.tactical_combat");
        public override int Layer => 0;

        public override bool IsActive() => Kingmaker.Game.Instance?.RootUiContext?.IsTacticalCombat ?? false;

        public override bool StartUnfocused => true;
        public override bool AllowsTypeahead => false;

        // Like the in-game screen: focused, UI wins the shared chords (arrows navigate, Escape backs out of
        // the HUD); unfocused, Exploration moves the grid cursor and Tab (UI only) enters the HUD.
        // Windows for the window keys that make sense in battle (Ctrl+L, the log); the service windows
        // themselves don't open during a battle, so their keys just do nothing here.
        private static readonly IReadOnlyList<InputCategory> Focused =
            new[] { InputCategory.UI, InputCategory.InGame, InputCategory.Exploration, InputCategory.Windows };
        private static readonly IReadOnlyList<InputCategory> Unfocused =
            new[] { InputCategory.Exploration, InputCategory.InGame, InputCategory.UI, InputCategory.Windows };
        public override IReadOnlyList<InputCategory> InputCategories => Navigation.HasFocus ? Focused : Unfocused;

        public override void Build(WrathAccess.UI.Graph.GraphBuilder b) => TacticalCombatNodes.Build(b);
    }
}
