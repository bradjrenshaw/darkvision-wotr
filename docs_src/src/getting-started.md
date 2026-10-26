# Getting Started

Launch Pathfinder: Wrath of the Righteous. Only PC is supported; console mode (controller support)
is not implemented yet. If the game has been running for a while and nothing is happening, press
Enter on the keyboard and it should load you into the main menu. The first time you launch, a setup
wizard lets you choose your speech engine and voice, and how combat and world events are spoken.
If speech ever goes silent or ends up on the wrong voice, **F9** resets it to your screen reader.
**F5** quick saves and **F8** quick loads, as in the game.

This page explains how the mod works. For a hands-on guided tour of the opening dungeon, see
[The Caves Beneath Kenabres](walkthrough.md). For the full key list, see [Controls](controls.md).

## Menus and the UI

Menu navigation works as you would expect. Use the **arrow keys** to navigate the current panel and
**Tab** to move between panels. **Enter** is your primary action (usually a left-click in game);
**Backspace** is your secondary / right-click action. **Space** on an element reads its tooltips or
any hyperlinks in its text.

Typeahead is supported — just start typing and the current panel filters to the search text.
**Escape** clears any typeahead filter.

## The in-game UI

In an area, the keyboard starts on the world itself: the movement keys move the cursor and the
letter keys run the scanner and party commands described below. Press **Tab** to bring up the
in-game HUD, a set of panels you Tab between:

- **Action bar** — use and target your character's abilities, spells, and items.
- **Turn** — in turn-based combat only: the initiative order and turn actions.
- **Menu** — the game's menu bar: alter AI behaviour and access functions such as rest and
  formation.
- **Windows** — your character sheet, spellbook, and the rest.

Tabbing past the last panel returns you to the world. While the HUD has focus, the arrows navigate
it, but the other exploration keys keep working.

## Cursors and the scanner

Pathfinder is a CRPG; every action is performed with the mouse. Rather than emulate a 2-D mouse
pointer, the mod gives you a cursor you move around the world. There are two cursors: the **movement
cursor** and the **review cursor**.

### The movement cursor

The cursor has four sets of movement keys. By default, **W, A, S, D** glide it continuously at 15
feet per second while held, **Shift+W, A, S, D** glide twice as fast, and the **arrow keys** step it
one 5-foot tile at a time, as if reading a tactical grid. **Shift+arrows** are free for you to
assign. Each set's mode (continuous, tiled, or none), speed, and tile size can be changed under
**Settings → All settings → Cursor**. Every sound from the game and mod is positioned relative to
this cursor — treat it like an audio camera.

Directions are relative to the way you're facing, which starts at north. **Q** and **E** turn
you while held, and **Shift+Q / Shift+E** turn to the next 45-degree step; **Alt+R** turns you back
to north.

When the cursor is over an object, release the direction and you'll hear its name and information.
**Enter** left-clicks it (attack, loot, open, …). **Backspace** moves your selected party members to
the cursor. **C** snaps the cursor to the party leader. The cursor follows slopes and stairs
automatically, so you never adjust its height by hand.

Two optional movement aids live under **Settings → All settings → Cursor**: **Slide along walls**
makes a blocked glide slip along the wall's surface (funnelling through doorways), and **First-held
direction has priority** makes a held diagonal turn into a gap the moment it opens — hold the
direction you actually want first, add the second to follow the wall.

This game has fog of war: if none of your characters can see a spot, you won't know what's there. A
sound marks the cursor entering or leaving fog of war.

### The review cursor

The **review cursor** is a second cursor for looking at things without moving: it points at one thing
in the area, and you act on it from wherever you are. The quickest way to find something is to cycle
the review cursor by kind. Each press jumps to the next nearest thing of that kind:

- **Period**: enemies
- **Comma**: your party and allies
- **N**: neutral NPCs (people worth talking to)
- **B**: bystanders (background people)
- **M**: interactables and objects (doors, chests, levers, and so on)
- **V**: exits from the room you're in
- **L**: unexplored space

Hold **Shift** with any of them to go backwards. Once you've found something, press **I** to interact
with it (a left-click), or **Slash** to move the movement cursor onto it. **I** is also handy for
targeting abilities and spells without moving the movement cursor.

The N/B split separates the people worth talking to from the background crowd: **neutrals** (N) are
NPCs a click gets a real conversation or scripted interaction from — quest-givers, vendors — while
**bystanders** (B) only bark a one-liner, or nothing at all. An NPC whose conversation unlocks later
moves from B to N when it does.

To browse everything instead, use the **scanner**: **Page Up / Page Down** move through the items in
the current category and subcategory (by default, *Everything* in the area), **Ctrl+Page Up/Down**
switches category, and **Shift+Page Up/Down** switches subcategory. Whatever the scanner lands on
becomes the review cursor, so **I** and **Slash** work on it the same way.

Every cycle landing plays one of **four sight-and-route sounds**, telling you how the thing relates
to your cursor: a **straight line** is walkable directly to it; a **path around** exists but bends;
**unreachable** means you can see it but no walkable path leads there; **blocked sight** means a
wall is between you and it. **Semicolon** replays the ping for the current review item on demand,
and the audio glossary (Ctrl+M → Help → Audio glossary) plays all four with explanations. If you'd
like the cycles to snap back to the nearest thing whenever you move, enable **Cursor movement resets
review cycles** under Settings → All settings → Cursor.

**Alt+1** to **Alt+6** put the review cursor straight on party member 1–6, and **J** cycles
*strategic hints*: enemy spawn points that haven't fired yet, and the places enemies are heading for
(the buildings raiders want to burn, say). Strategic hints are an Enhancement — something a sighted
player reads off the screen at a glance — and can be turned off in settings.

Unexplored space (**L**) deserves a note: it cycles the openings where walkable ground you haven't
revealed yet borders ground you have — the places where the map can still grow. If you're ever
unsure where to go next, press **L**, jump the cursor there with **Slash**, and send your party with
**Backspace**.

### Bookmarks

**Alt+B** opens your bookmarks for the area. You can bookmark the cursor's position, or pin a
bookmark to the review item (it then follows that thing if it moves), and give each a name. The
area's bookmarks are listed below; Enter on one puts the review cursor on it, and Delete removes it.
Bookmarks also appear as their own scanner category, and they're saved across sessions.

## Buffers

Buffers let you read a character's details line by line without leaving what you're doing. There are
two: the **selected unit** buffer (the character you currently have selected) and the **review
unit** buffer (whatever the review cursor is on, when it's a unit). Each reads the unit's name, hit
points, armor class, and then every buff, debuff, and condition affecting it. Use **Alt+Left /
Alt+Right** to switch buffers (it reads the buffer name the first time), then **Alt+Up / Alt+Down**
to move through its lines.

## Spatial audio

Think of the movement cursor as an audio camera; **overlays** are swappable sets of spatial sounds
layered around it, and you cycle the active one with **Ctrl+O**. The **sonar** periodically pings
nearby things, each with a sound for its type (enemies, allies, interactables, …) placed by distance
and direction, so you can build a picture of your surroundings. You choose which types are included
in the setup wizard or settings; in busy areas (50+ units) enabling everything can get noisy. **Wall
tones** play a tone for each nearby wall in the four cardinal directions, louder as a wall gets
closer. In multi-level areas, **Ctrl+Comma** and **Ctrl+Period** follow the surface up and down, and
**Keypad 5** re-announces the overlay cursor.

## Where you are

Press **Z** for "where am I": your area, whether you're indoors, the room you're standing in
(noting if that spot is still unexplored), and roughly where you are on the map. Press **X** to hear
the mod's own hand-written visual description of the scanner object you're focused on, and
**Shift+X** for the description of the room your cursor is standing in, where one has been authored.
**K** reads the movement cursor's exact position, and **Shift+K** reads your party.

Spoken directions default to the 8-point compass, but the **Direction type** dropdown (in the simple
settings under Announcements) offers more: a 16-point compass for finer bearings, short spoken-letter
forms ("ne", "n ne"), directions **relative to your facing** (ahead / behind / left / right, in 4 or
8 ways), or **clock face** (12 o'clock is dead ahead). The relative and clock styles follow your Q/E
turning; the compass styles stay world-locked.

## The map

**Ctrl+V** opens the accessible area map: a faster cursor you can sweep across the whole area, the
map's curated markers (quest points, exits, loot, people) on **B**, and the same review keys as the
area. The map reviews what you've **discovered** rather than what's currently in sight — and by
default, people you have met stay locatable on it even while fog currently hides them (the "Neutral
NPCs ignore fog of war" entry under **Settings → Enhancements**; turn it off for strict sighted
parity). That's the tool for re-finding a vendor in a big settlement: open the map, press **N**,
jump the cursor there with **Slash**, close the map and send the party.

## Party control and movement

Press **Ctrl+A** to select the whole party, or **Ctrl+1**–**Ctrl+6** to select a single member
(press the same number again to cycle to that member's pet or mount). With the movement cursor over
a spot, **Backspace** sends your selection there: a single character walks over, while the whole
party moves into formation. **H** toggles hold position, **G** stops the selection, **Ctrl+S**
toggles stealth, and **Ctrl+D** toggles AI control for the selection.

## Combat

Turn-based combat is supported. When it begins you'll hear whose turn it is, and you act with the
current character. **R** gives a status readout: in turn-based, the acting character's remaining
actions and movement; in real-time-with-pause, what your selected character(s) are currently doing
(casting, attacking, moving, or idle). **Ctrl+T** switches the whole game between turn-based and
real-time-with-pause, and **Space** toggles pause while exploring.

You can also check any unit without selecting it: when you land on a unit in the scanner, its line
includes what it's doing right now — e.g. "Bob, casting Fireball, HP 23 of 23." That action readout
is its own announcement type, so you can toggle it (and tune it per faction) under **Scan
announcements → Action** in settings.

## Targeting abilities and spells

To use an ability or spell, Tab to the action bar and choose it; the game then enters targeting
mode. Aim with either cursor: move the movement cursor onto a target and press **Enter**, or put the
review cursor on a target and press **I**. **Escape** cancels targeting (when you're not targeting,
Escape opens the game menu instead).

The number keys use the action bar's slots directly, as in the game: **1** to **9**, **0**,
**Minus** and **Equals** for the first twelve slots, and with **Shift** for the third row. A spell or
ability that needs a target enters targeting as above. One that takes effect immediately (a
self-buff, a potion, a toggle) asks first: the first press names it, and for an area effect who it
would hit, and pressing it again within a few seconds uses it.

**Backspace** on an action-bar slot opens its menu: the spell's variants and conversions where it
has them (a cleric's spontaneous Cure, for example), and **Set default attack** for an eligible
ability (an at-will ability or cantrip, like Acid Splash). With a default attack set, attacking an
enemy with Enter or I casts it instead of swinging your weapon — the same auto-use feature the
sighted game offers on right-click. The slot announces "default attack" while one is set, and the
same menu clears it. **Backslash** picks a slot up and puts it down elsewhere on the bar (or drags a
spell or ability onto it from the lists below the bar), and **Delete** clears a slot.

## Your character windows

Open the main character screens directly, both in an area and on the world map: **Ctrl+C** for the
character sheet, **Ctrl+I** for inventory, **Ctrl+B** for the spellbook, **Ctrl+J** for the journal,
**Ctrl+V** for the map, and **Ctrl+L** for the log. Each toggles, so pressing the key again closes
it, and you move around inside with the usual arrows, Tab, and Enter. The encyclopedia doesn't have
a shortcut yet; open it from the Windows panel of the in-game UI.

### Managing items

In the inventory, **Enter** on an item equips or uses it the default way, and **Backspace** opens
its menu — equip to a specific slot (including **Equip in off hand**), drop, split, and so on.
**Backslash** is a keyboard drag: press it on an item to pick it up, then press it again on a slot
to place it there — the way to move gear directly between equipment slots. **Space** on an item
opens the game's Information window, with the item first and, where it applies, what you have
equipped in its place as further panels to compare against. Splitting a stack (or
dropping part of one) opens an **amount window**: focus lands on the slider, arrows adjust the
count, and Enter on the action button confirms.

## Dialogue

Conversations are a transcript you read through: what has already been said, the current line, and
your answer choices, including skill-check options. Choose an answer to continue; storybook passages
(book events) work the same way. Speech never interrupts itself, so lines won't cut each other off.

## Vendors and trade

A vendor window is a series of labelled panels you Tab between: your inventory, the store, your
buying cart, your selling cart, the bulk-sell options, the running deal total, and close. **Enter**
on an item moves it the sensible way for its panel (buying from the store, selling from your
inventory, returning from a cart), **Backspace** offers moving the whole stack, and the deal button
confirms the trade. Moving part of a stack opens the same amount window as splitting does. Both your
inventory and the store start with the game's filters: a search field, a sort order, and item-type
filters.

## Looting

Containers and corpses open a loot window where you can take items one at a time or all at once. When
you leave an area, the game may ask whether to collect any loot you left behind.

## Resting

Press **Ctrl+R** (or choose Rest from the in-game menu bar) — it behaves like a targeted action and
asks you to place your camp — or open the rest screen to manage the camp itself: who cooks, who
keeps watch, and so on.

## The world map

Travelling between locations happens on the world map, which has its own cursor, scanner, and sonar.
Use **Page Up / Page Down** to move through points and **Ctrl+Page Up/Down** to switch categories,
then press **I** to travel to or enter the selected one. You can also review points quickly: **B**
cycles every point nearest first, **M** the points connected to where you are, and **N** the
locations you can actually reach (hold **Shift** to go backwards). Locations you can't enter, or that
travel is restricted to, are announced as such, and any book events or encounters along the way are
accessible.

The **arrow keys** walk the roads. From the place or junction under the cursor, an arrow names the
road leaving in that direction: where it leads, how many miles, and whether it's closed or not yet
explored. Press the same arrow again to move the cursor there, or two arrows together for a
diagonal. Junctions are stops too, read as a *fork* or a *bend* with their roads. Roads you haven't
explored can't be picked from the location list, so the arrows are how you travel them: from where
your party is standing, the second press asks whether to travel down that road (quoting the journey
time), and Cancel just moves the cursor. **W, A, S, D** glide the cursor freely instead.

**Tab** reaches the map's panels: the toolbar (the date, crusade morale, Skip Day, switching between
party and army, and the crusade's stats) and, once you have armies, the army list and the selected
army's details. Your armies are on **Comma** and enemy armies on **Period**; **I** on one of your
armies selects it, and **Y** reads an army's information. The crusade's windows (recruiting,
buying resources, generals) are accessible too.

The selected army's details end with an **Army** button, which opens the army window: rename or
dismiss the army, assign its general, **Split** off a new (empty) army, and manage each squad (Enter
on a squad: split it, merge it, dismiss it). When another of your armies stands on the same spot, a
**Merge** button appears as well; the merge view shows both armies, with buttons to move all squads
either way, and each squad's menu can move it to the other army. An army left empty when the window
closes is removed.

## Crusade battles

When armies meet, the battle plays out on a 9 by 7 grid. The same cursor keys step one cell at a time,
and each cell reads the squad standing there and its strength, whether it's in reach of your acting
squad, and for an enemy squad the game's prediction of what an attack would do ("108 to 243 damage,
1 to 2 killed"). Cells are named like a chessboard — columns A to I from left to right, rows 1 to 7
from the bottom — as the battlefield appears on screen. **Period** and **Comma** cycle enemy squads
and your own; **Enter** orders your acting squad to the cursor's cell (moving, or attacking what's
there), and **I** against the reviewed squad. **Tab** reaches the acting squad's actions, your
general's actions and spellbook, the turn order, and the battle controls (auto-combat, speed,
retreat). Turns, damage, and deaths are read out as they happen.

## Camera and low vision

The **Alt** keys drive the game's camera for players with some vision: **Alt+W, A, S, D** pan,
**Alt+Q / Alt+E** rotate, and **Alt+Page Up / Page Down** zoom. Under **Settings → All settings →
Vision**, **Camera follows the cursor** keeps the camera on the cursor wherever it goes, with the
offset, angle, zoom, and smoothing you choose (the same Alt keys adjust them while it's on).

## Tutorials

The game's tutorial popups (controls, mechanics, and the like) are read out, with options to dismiss
the current one or to stop showing them.

## The game log and barks

Ambient lines characters say (barks) and narrative log messages are spoken as they happen. You can
also open the mod's log from the in-game Windows list to review past messages, grouped into channels
such as combat and dialogue.

## Settings and the setup wizard

Press **Ctrl+M** anywhere to open the mod menu, which holds the mod's settings and lets you re-run
the setup wizard. See [Settings](settings.md) for the full breakdown.
