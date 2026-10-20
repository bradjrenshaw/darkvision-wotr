# Settings

Press **Ctrl+M** anywhere to open the **mod menu**. It holds **Settings**, lets you re-run the
**setup wizard**, opens **Help** — with **Read documentation** (this book, in your browser) and the
**Audio glossary**, which plays every mod sound on demand with an explanation of what it means —
and links to the Discord and Patreon.

## The setup wizard

The first launch runs a setup wizard automatically, and you can re-run it from the mod menu at any
time. It walks you through the most important choices as a series of steps with a roadmap header you
can jump around in:

- **Speech** — the voice that reads the mod (your screen reader, or a Windows voice), with that
  voice's rate, pitch, and volume right below it when it has them.
- **Event feedback** — how you hear what happens in the world (attacks, damage, healing, spells):
  **positional spoken events** (the default) says each event where it happens, in distinct voices
  for enemies, allies, and sourceless events, alongside the game's own combat and magic log
  messages; **game log only** reads just those log messages. With positional events on, the next
  steps let you choose and tune each of the three voices.

The wizard's presets configure many settings at once; anything it sets can still be fine-tuned
afterward. Cursor movement, wall tones, and the sonar are set up in the settings below.

## The simple settings screen

The mod menu's **Settings** entry opens a small, curated screen with the controls most people
actually want, grouped as:

- **Speech** — the **voice**: Automatic (your screen reader, or the default Windows voice), or a
  specific choice from a tree of what's installed — screen readers, SAPI 5, OneCore, and SAPI 4
  voices, or the clipboard. Only voices actually available on your PC are listed. Below it, the
  settings that voice takes: rate, pitch, and volume (a screen reader has none — your screen
  reader's own settings apply). **F9** anywhere puts it back on
  Automatic.
- **Audio cues** — the master volume, then one node per sound system (sonar, slope, wall tones, fog,
  objects): when it's active (off / when moving / continuous) and its volume. The sonar node also
  carries per-type **Play sound** switches — which kinds of things ping.
- **Announcements** — "how much does it talk" presets: menu verbosity, scanner detail, the
  **direction type** (compass in 8 or 16 points, short spoken-letter forms, relative to your facing,
  or clock face), log detail, and event speech level.
- **Cursor** — the main movement speed, room-change announcements, and the path/AoE speech
  toggles.
- **Input** — every mod action as a rebindable binding, with a clash warning on capture.

Everything here is a view over the same settings the full screen edits — nothing is duplicated, and
hand-tuning in the full screen is never lost (a preset that no longer matches reads as "Custom").

## All settings

The **All settings** button at the bottom opens the full tabbed browser:

- **Audio** — master volume and every system volume; the audio backend (Loadstar, the low-latency
  default, or NAudio if you have trouble with it) and its latency; where the game's own sounds are
  heard from (the cursor, the party leader, or the camera); and stereo / headphone tuning.
- **Cursor** — the cursor in an area, and on the world map. For each, the four
  sets of movement keys (W A S D, Shift+W A S D, the arrows, and Shift+arrows), each with its movement
  mode (continuous, tiled, or none; the world map adds *connected*, which follows the roads) and that
  mode's speed or tile size. The area context also holds the cursor's behaviours: room-change
  announcements, **Slide along walls**, **First-held direction has priority**, **Cursor movement
  resets review cycles**, and **Play terrain sound under cursor** (the game's own footstep sound for
  the ground under the moving cursor — grass, stone, wood, water).
- **Enhancements** — deliberate, opt-outable departures from what a sighted player gets, where that
  materially helps a blind player:
  - **Neutral NPCs ignore fog of war** and **Companions ignore fog of war** — people you have met
    stay locatable in the scanner and on the map even while fog currently hides them.
  - **Strategic hints** — the J cycle: enemy spawn points and enemy objectives.
  - **Achievements stay enabled with mods active** — the game normally switches Steam achievements
    off whenever any mod is loaded. **Claim achievements already earned** awards the story
    achievements you reached before turning this on.

  All are on by default; turn any off for strict sighted parity.
- **Events** — one entry per event the mod can announce (damage, healing, buffs, deaths, …), with
  per-source settings (party / enemy / neutral / sourceless) and the speech configuration each uses;
  plus **additional speech configurations** so events can speak in different voices, positioned in
  the world where the engine supports it.
- **Exploration** — each sensing system's defaults (tile readout, spatial, slope, wall tones,
  object and fog cues, path info). The sonar has its own **Sonar** settings under Scanner, including
  its mode: a *sweep*, or *pulse*, where each thing repeats its own tone faster as it gets closer.
- **Input** — the same rebindable bindings as the simple screen.
- **Log** — which game-log message types are spoken, with presets (Default RTWP / Default Turn
  Based / Nothing).
- **Overlays** — the spatial-audio overlay roster: each overlay's composition and per-system
  overrides, plus add / rename / remove.
- **Scanner** — what the scanner lists (per-type Listed switches), the sonar sound assigned to each
  entity type, **scan announcements** (which parts of an item's line are spoken — name, type, health,
  location and its distance / direction / height sub-toggles) with per-type overrides, and the
  **review cursor sounds**: which sound each of the four sight-and-route outcomes plays (any can be
  Silent).
- **Speech** — the default speech output and its tuning, plus the additional speech configurations
  used by events.
- **UI** — announcement verbosity for menus and controls, with per-element-type overrides.
- **Vision** — for players with some sight: **Camera follows the cursor**, with its sideways and
  forward offsets, angle, preferred zoom, and smoothing.

Every tab has a **Reset to defaults** for just that tab, and a reset-everything below it.

## Notes

- Your Darkvision settings are stored separately from the game's, so an ordinary update keeps
  them, and you can reset any single category back to its defaults without touching the rest.
- While the mod is in alpha, an update that reorganizes the settings starts everyone from the
  defaults instead of converting old values (the setup wizard runs again too). Your old settings
  file is kept as `settings.old.json` beside the new one.
- The first-launch flag is independent of the settings, so a full "reset all" won't re-trigger the
  setup wizard.
