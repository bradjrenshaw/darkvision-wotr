# Change Log

This page tracks notable, player-facing changes. The mod is in **alpha**, so it moves quickly; for
the exact history see the [commit log](https://github.com/bradjrenshaw/wotr-access/commits/main).

## 0.1.0 — first public alpha

- **New installer**: WrathAccessInstaller.exe finds your game, installs, updates and uninstalls the
  mod, and keeps itself up to date.
- **The crusade**: the world map's toolbar and crusade stats, armies (selecting, the army list and
  details), recruiting, buying resources, generals, and **crusade battles** on their grid, with the
  game's attack predictions.
- **World-map roads on the arrow keys**: preview and follow each road from a place or junction,
  including travelling down unexplored roads.
- **Action-bar number keys** (1 to Equals, and Shift for the third row), the action-bar slot menu
  (spell conversions and the default attack), and keyboard drag and delete on the bar.
- **F8 quick load**, alongside F5 quick save.
- **Bookmarks** (Alt+B), **strategic hints** (J), and **Alt+1–6** to review a party member.
- **Speech and audio**: a voice picker listing every installed screen reader and Windows voice; F9 to
  reset speech; a low-latency audio engine (Loadstar), a latency setting, and a sonar *pulse* mode.
- The cursor's four sets of movement keys, each with its own mode (the arrows step tile by tile by
  default), and optional terrain footstep sounds under the cursor.
- A **follow-cursor camera** for low-vision players (the Vision settings), with zoom and reset keys.
- **Settings start fresh** in this version: earlier settings are not carried over (the old file is
  kept as settings.old.json), and the setup wizard runs again. Voices now have a pitch setting, and
  the wizard's enemy, ally and sourceless voice steps include the voice picker.
- **Achievements** stay enabled with the mod (an Enhancement), with a way to claim ones you missed.
- Mythic rank-ups, the character-creation appearance step, item Information windows with equipped-item
  comparisons, vendor search and filters, and the retraining windows.
- Hand-written room descriptions for many more areas.

## Earlier alpha builds

The earlier alpha builds already covered a large slice of the game:

- Speech through your screen reader or a Windows voice, and custom keyboard navigation in mouse
  mode. Speech outputs are configurable per use (rate, volume, voice — including OneCore), and event
  voices can be positioned in the world.
- Main menu, the New Game flow, and character creation / level-up.
- A mod menu (Ctrl+M) with simple and full settings, a first-run setup wizard, and an audio glossary.
- Exploration: the movement cursor (with turnable facing on Q/E), the categorized scanner and review
  cursor with sight-and-route pings, sonar and other spatial-audio overlays, wall tones, room/area
  awareness with authored room descriptions, unexplored-space cycling, and world-map travel.
- The accessible map screen (Ctrl+V): a fast map cursor, the game's markers, and met NPCs locatable
  even under fog (an opt-outable Enhancement).
- Dialogue, book events, tutorial popups, loading-screen tips, and the in-game log / barks (log
  review on Ctrl+L).
- Turn-based combat and targeting (abilities, touch spells, a settable default attack, resting on
  Ctrl+R, AoE cast previews, turn delaying).
- Service windows: inventory (with keyboard drag, off-hand equipping, and stack splitting),
  character sheet, spellbook, journal, encyclopedia.
- Vendors / trade (including partial-stack amounts), looting, resting, party selection, and the
  group manager.
- Review buffers for reading a unit's details line by line.
- Party commands: select all / by member, hold position, stop, stealth, and AI toggles.
- Spoken directions in your choice of style: compass (8/16, full or short), relative to facing, or
  clock face.
