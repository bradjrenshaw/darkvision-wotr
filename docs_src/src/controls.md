# Controls

Every key below is **rebindable**. Open the mod menu (**Ctrl+M**), go to **Settings → Input**, and
drill into the action you want to change — you can capture a new combination (with a warning if it
clashes) or clear one. The mod menu always shows the complete, current list, so it's the source of
truth if a binding here ever drifts.

Press **Ctrl+Shift+A** at any time to toggle accessibility focus mode (the mod takes over the
keyboard from the game).

Many exploration keys are shared between the area, the map screen, the world map, and crusade
battles: the same key does the equivalent thing in each place. The sections for the map screens and
battles list only what differs.

## Global

These work everywhere.

| Key | Action |
| --- | --- |
| Ctrl+Shift+A | Toggle focus mode |
| Ctrl+M | Open the mod menu (settings, setup wizard, help) |
| F5 | Quick save |
| F8 | Quick load (the newest quick save) |
| F9 | Reset speech to the automatic voice (your screen reader) — a way out if speech goes silent |

## Menus and windows

Used while navigating UI panels (menus, the character sheet, dialogue, vendors, and so on).

| Key | Action |
| --- | --- |
| Arrow keys | Navigate the focused panel / adjust the focused control |
| Tab / Shift+Tab | Next / previous region or panel |
| Enter (or Keypad Enter) | Activate the focused control |
| Backspace | Secondary action (right-click: clear a key binding, open an item's menu, open an action-bar slot's menu) |
| Backslash | Pick up / place the focused item (keyboard drag — e.g. move gear between equipment slots, or abilities onto the action bar) |
| Delete | Remove (clear an action-bar slot, delete a bookmark) |
| Escape | Back / close |
| Space (or F1) | Read tooltip / inline links; on an item, open the game's Information window |
| Home / End | Jump to first / last item |
| Ctrl+Up / Ctrl+Down | Previous / next region within a sheet |
| Letters | Typeahead: jump to the matching entry (Escape clears the search) |

## In an area — the HUD

In an area, the keyboard starts on the world: the arrows move the cursor. **Tab** brings up the
in-game HUD (action bar, the turn panel in turn-based combat, the menu bar, and the windows list), and
Tabbing past its last panel returns you to the world. While the HUD has focus, the arrows navigate it
and the rest of the exploration keys below keep working.

### Action-bar hotkeys

These use the action bar's slots directly, as the game's own number keys do. Using a self-cast
ability, item, or toggle asks first: the first press names it (and, for an area effect, who it would
hit), and a second press within a few seconds uses it.

| Key | Action |
| --- | --- |
| 1 … 9, 0, Minus, Equals | Action bar slots 1–12 |
| Shift + 1 … Equals | Slots 1–12 of the third action-bar row |

The second row and slots 13–14 have no default key; bind them under **Settings → Input** if you want
them.

## Exploration — cursor

The cursor has four sets of movement keys, each with its own movement mode, set under
**Settings → All settings → Cursor**. By default:

| Key | Action |
| --- | --- |
| W A S D | Move the cursor, gliding at 15 feet per second (relative to your facing) |
| Shift + W A S D | Move the cursor faster, at 30 feet per second |
| Arrow keys | Step the cursor one 5-foot tile at a time |
| Shift + arrow keys | Unassigned (pick a mode for it in the Cursor settings) |

| Key | Action |
| --- | --- |
| Q / E | Turn your facing left / right (continuous while held) |
| Shift+Q / Shift+E | Turn your facing to the next 45° step |
| Enter | Interact at the cursor (left-click) |
| Backspace | Move the selected party to the cursor |
| C | Bring the cursor back to the party leader |
| Space | Toggle pause (in turn-based combat, end the turn); during a cutscene, skip it |
| Z | Where am I (area, indoors, current room, whether the spot is unexplored, and rough map position) |
| X | Describe the scanner target (the mod's authored visual description of the review item, where one exists) |
| Shift+X | Describe the current room (the mod's authored room description, where one exists) |
| Escape | Cancel targeting; otherwise open the game menu |

## Exploration — party

| Key | Action |
| --- | --- |
| Ctrl+A | Select the whole party |
| Ctrl+1 … Ctrl+6 | Select party member 1–6 (press again for their pet / mount) |
| H | Hold position (toggle) |
| G | Stop (also cancels a pending multi-turn order) |
| Ctrl+S | Toggle stealth |
| Ctrl+D | Toggle AI control |
| Ctrl+R | Rest — arms camp placement; aim with the cursor and press Enter |

## Exploration — combat

| Key | Action |
| --- | --- |
| Ctrl+T | Toggle turn-based / real-time-with-pause |
| R | Status: in turn-based, the acting unit's actions/movement remaining; in real-time, what your selected character(s) are doing |

## Exploration — scanner & review cursor

| Key | Action |
| --- | --- |
| Page Down / Page Up | Next / previous scanner item |
| Ctrl+Page Down / Ctrl+Page Up | Next / previous category |
| Shift+Page Down / Shift+Page Up | Next / previous subcategory |
| I | Interact with the review item |
| Slash (or Home) | Move the cursor to the review item |
| Semicolon | Ping the review item: one of four sounds for sight and route from the cursor (straight line / path around / unreachable / blocked sight — hear them in the audio glossary) |
| K | Announce cursor position |
| Shift+K | Announce party |
| Y | Inspect the review-cursor unit |
| ' (apostrophe) | Inspect the unit under the cursor |
| Alt+B | Bookmarks: bookmark the cursor's position or the review item, and list this area's bookmarks |

Quick review-cursor cycles, nearest first (hold **Shift** to go backwards):

| Key | Action |
| --- | --- |
| Comma | Next party member |
| Period | Next enemy |
| N | Next neutral NPC (people a click gets a real conversation or interaction from) |
| B | Next bystander (background NPCs with only a one-liner bark, or nothing) |
| M | Next interactable / object |
| V | Next room exit |
| L | Next unexplored space (openings where the map can still be revealed) |
| J | Next strategic hint: enemy spawn points and the places enemies are heading for (an Enhancement; see Settings) |
| Alt+1 … Alt+6 | Review party member 1–6 (press again for their pet / mount) |

Every cycle landing plays the same four-way sight-and-route ping as Semicolon, so you always hear
whether the thing is directly walkable-to. Points of interest (the game's map markers) are reviewed
on the map screen (below), not in the area.

## Exploration — buffers

| Key | Action |
| --- | --- |
| Alt+Left / Alt+Right | Previous / next buffer |
| Alt+Up / Alt+Down | Previous / next line in the buffer |

## Exploration — spatial audio overlays

| Key | Action |
| --- | --- |
| Ctrl+O | Cycle the active overlay |
| Ctrl+F1 | Wall tones: cycle mode (off / when moving / continuous) |
| Ctrl+F2 | Sonar: cycle mode |
| Shift+F1 | Wall tones: play while held |
| Shift+F2 | Sonar: play while held |
| Keypad 5 | Announce the cursor |
| Ctrl+Period / Ctrl+Comma | Follow the surface down / up (multi-level areas) |

## Exploration — camera

These keys drive the game's camera, for players with some vision. With **Camera follows the cursor**
turned on (**Settings → All settings → Vision**), they adjust where the camera sits relative to the
cursor instead.

| Key | Action |
| --- | --- |
| Alt + W A S D | Pan the camera |
| Alt+Q / Alt+E | Rotate the camera left / right |
| Alt+Page Up / Alt+Page Down | Zoom in / out |
| Alt+R | Reset the camera, and turn your facing back to north |
| Alt+C | Read the compass (camera facing) |
| Alt+F | Follow the selected character |

## Character windows

Toggle these directly, in an area or on the world map (press again to close). They also work from
inside any of these windows, to switch straight to another one.

| Key | Action |
| --- | --- |
| Ctrl+C | Character sheet |
| Ctrl+I | Inventory |
| Ctrl+B | Spellbook |
| Ctrl+J | Journal |
| Ctrl+V | Map |
| Ctrl+L | Log (past messages, grouped into channels) |

The encyclopedia doesn't have a shortcut yet — open it from the Windows panel of the in-game HUD.

## The map screen

**Ctrl+V** opens the accessible area map: a map cursor that moves three times as fast as the area
cursor, plus the map's curated markers. Movement and most review keys work as in the area; the
differences:

| Key | Action |
| --- | --- |
| Comma / Period | Next party member / enemy |
| N | Next person — neutral NPCs and bystanders together, including people you've met who are currently hidden by fog (see the Enhancements setting) |
| B | Next map marker (quest points, exits, loot, people — the game's points of interest) |
| M | Next exit marker |
| Slash (or Home) | Move the map cursor to the review item |
| Enter | Move the area cursor to the map cursor |
| Backspace | Send the selected party to the map cursor |
| K | Read the map cursor |
| Z | Where am I, at the map cursor |
| X / Shift+X | Describe the room at the map cursor |
| Escape | Close the map |

## World map

The world map has its own cursor, scanner, and sonar. The arrow keys follow the roads: from the place
or junction under the cursor, an arrow first names the road leaving in that direction (where it leads,
how far, and whether it's closed or unexplored), and the same arrow again moves the cursor to the far
end. Press two arrows together for a diagonal. W A S D glide freely across the map.

| Key | Action |
| --- | --- |
| Arrow keys | Follow a road (press once to preview, again to move) |
| W A S D | Move the map cursor freely |
| Shift + W A S D | Move the map cursor freely, faster |
| Page Down / Page Up | Next / previous point |
| Ctrl+Page Down / Ctrl+Page Up | Next / previous category |
| I | Travel to / enter the selected point (on one of your armies: select it) |
| B / Shift+B | Next / previous point, nearest first |
| M / Shift+M | Next / previous connected point |
| N / Shift+N | Next / previous reachable location |
| Period / Shift+Period | Next / previous enemy army |
| Comma / Shift+Comma | Next / previous ally army |
| Y | The reviewed army's information |
| Ctrl+O | Cycle the map overlay |
| Enter | Act on the cursor |
| C | Move the cursor to the party |
| K | Read the cursor |
| Slash (or Home) | Move the cursor to the review point |
| Tab | The map's panels: the toolbar (date, morale, Skip Day, party / army mode, crusade stats) and, with an army selected, the army list and the army's details |
| Escape | Open the menu |

Following a road out from where your party (or army) is standing is how you travel down a road you
haven't explored yet: the second arrow press then asks whether to travel there, quoting the journey
time, and Cancel only moves the cursor.

## Crusade battles

Battles take place on the game's 9 by 7 grid. The cursor steps one cell at a time and reads the cell:
the squad standing there and its strength, whether it's in reach, and for an enemy squad the game's
prediction of what an attack would do. Cells are named like a chessboard, columns A to I left to right
and rows 1 to 7 from the bottom, as you see the battlefield.

| Key | Action |
| --- | --- |
| W A S D (or arrows) | Move one cell |
| Period / Comma | Next enemy squad / next of your own squads (Shift goes backwards) |
| Slash (or Home) | Move the cursor to the reviewed squad |
| Enter | Order the acting squad to the cursor's cell (move, or attack what's there) |
| I | Order the acting squad against the reviewed squad |
| Y | The reviewed squad's information |
| C | Bring the cursor back to the acting squad |
| Escape | Cancel a spell that's waiting for a target; otherwise open the game menu |
| Tab | The battle's panels: the acting squad's actions, your general's actions and spellbook, the turn order, and the battle controls (auto-combat, speed, retreat) |
| Ctrl+L | The battle log |

## Formation editor

Opened from the in-game menu bar. Pick a formation from the list, then Tab to the editing field: a
cursor over the formation where you pick party members up and put them down. Only the custom
formations can be edited; the automatic one arranges itself.

| Key | Action |
| --- | --- |
| W A S D | Move the cursor one step |
| Enter | Pick up the member at the cursor; press again to put them down |
| Comma / Shift+Comma | Hear the next / previous party member's place (the cursor stays put) |
| Slash | Move the cursor to the member you just heard |
| C | Move the cursor to the centre |
| Ctrl+1 … Ctrl+6 | Pick up party member 1–6 (press again for their pet / mount) |
