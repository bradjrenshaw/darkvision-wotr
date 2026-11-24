using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Kingmaker;
using Kingmaker.Blueprints.Items;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.View.MapObjects;
using Newtonsoft.Json;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The Areshkagal slab ("domino") puzzles — Conundrum Unsolved and the later sites built on the
    /// same machinery. What a sighted player reads off the floor, we read off the game's own state:
    ///
    /// <para>Every visible SLAB SPACE covers two grid cells and owns two HIDDEN one-slot containers
    /// (<c>Slot_r_c</c>, parked out of sight). Which of the two holds the slab IS its orientation: a
    /// slab <c>Domino&lt;Set&gt;AxB</c> lying in <c>Slot_X</c> shows its first symbol A on cell X and B
    /// on the partner cell ("Turn slab around" just moves the item across). The outer SOCKETS are
    /// ordinary one-slot containers taking one single-symbol slab each; a few tiles are carved in
    /// from the start. The puzzle layout comes from <c>assets/descriptions/_puzzles.json</c> — cells
    /// are the designers' own (row, column) numbers, so the hidden containers are found by name.</para>
    ///
    /// <para>Also names the slabs themselves: every two-symbol slab shares ONE display name in the
    /// game ("Slab with Two White Symbols" — only the icon differs), so <see cref="ItemSuffix"/>
    /// supplies the symbols for item labels everywhere, puzzle room or not.</para>
    /// </summary>
    internal static class SlabPuzzle
    {
        // ---- the definition file ----

        internal sealed class SetDef
        {
            public string id { get; set; }
            public string pair { get; set; }     // two-symbol slab blueprint names; groups a, b
            public string single { get; set; }   // single-symbol slab blueprint names; group a
            public Dictionary<string, string> symbols { get; set; } // symbol id -> locale key
            [JsonIgnore] public Regex PairRx, SingleRx;
        }
        internal sealed class SpaceDef
        {
            public string @object { get; set; }  // the visible map object (the combine interaction)
            public int[][] cells { get; set; }   // its two [row, column] cells
        }
        internal sealed class SocketDef
        {
            public string @object { get; set; }  // the visible one-slot container
            public int[] cell { get; set; }
        }
        internal sealed class FixedDef
        {
            public int[] cell { get; set; }
            public string symbol { get; set; }   // null = a tile with no symbol (the frame's centre)
        }
        internal sealed class PuzzleDef
        {
            public string area { get; set; }
            public string set { get; set; }
            public string slot { get; set; }        // hidden container name pattern, "Slot_{r}_{c}"
            public bool rows_north { get; set; }    // rising row numbers run map-north
            public bool cols_east { get; set; }     // rising column numbers run map-east
            public List<SpaceDef> spaces { get; set; }
            public List<SocketDef> sockets { get; set; }
            public List<FixedDef> @fixed { get; set; }
            [JsonIgnore] public SetDef Set;
            [JsonIgnore] public List<Cell> Cells;   // reading order: north row first, west to east
        }
        private sealed class FileDef
        {
            public List<SetDef> sets { get; set; }
            public List<PuzzleDef> puzzles { get; set; }
        }

        /// <summary>One tile of the board, placed for a north-up view (Row 0 = northmost, Col 0 = westmost).</summary>
        internal sealed class Cell
        {
            public int R, C;        // the designers' grid numbers
            public int Row, Col;    // view position
            public SpaceDef Space;  // a half of this slab space, or
            public int Half;        //   which of its two cells
            public SocketDef Socket;
            public FixedDef Fixed;
            public Cell Up, Down, Left, Right; // the nearest tile each way along its row / column
            public string Key => R + "_" + C;
        }

        private static readonly List<SetDef> _sets = new List<SetDef>();
        private static readonly List<PuzzleDef> _puzzles = new List<PuzzleDef>();
        private static bool _loaded;
        private static string _area;
        private static PuzzleDef _current;

        /// <summary>Drop everything so the next use re-reads the file (the dev /assets route).</summary>
        public static void Reload()
        {
            _loaded = false;
            _sets.Clear();
            _puzzles.Clear();
            _parsed.Clear();
            _area = null;
            _current = null;
            ResetLive();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var path = Path.Combine(Main.ModDir ?? "", "assets", "descriptions", "_puzzles.json");
                if (!File.Exists(path)) return;
                var file = JsonConvert.DeserializeObject<FileDef>(File.ReadAllText(path));
                if (file?.sets != null)
                    foreach (var s in file.sets)
                    {
                        if (s == null || string.IsNullOrEmpty(s.id)) continue;
                        try
                        {
                            if (!string.IsNullOrEmpty(s.pair)) s.PairRx = new Regex(s.pair, RegexOptions.CultureInvariant);
                            if (!string.IsNullOrEmpty(s.single)) s.SingleRx = new Regex(s.single, RegexOptions.CultureInvariant);
                        }
                        catch (Exception e) { Main.Log?.Warning("[puzzle] bad pattern in set " + s.id + ": " + e.Message); continue; }
                        _sets.Add(s);
                    }
                if (file?.puzzles != null)
                    foreach (var p in file.puzzles)
                    {
                        if (p == null || string.IsNullOrEmpty(p.area)) continue;
                        p.Set = _sets.Find(s => s.id == p.set);
                        if (p.Set == null) { Main.Log?.Warning("[puzzle] " + p.area + ": unknown set " + p.set); continue; }
                        if (string.IsNullOrEmpty(p.slot)) p.slot = "Slot_{r}_{c}";
                        BuildCells(p);
                        _puzzles.Add(p);
                    }
                Main.Log?.Log("[puzzle] " + _sets.Count + " slab sets, " + _puzzles.Count + " puzzles");
            }
            catch (Exception e) { Main.Log?.Warning("[puzzle] load failed: " + e.Message); }
        }

        private static void BuildCells(PuzzleDef p)
        {
            var cells = new List<Cell>();
            if (p.spaces != null)
                foreach (var s in p.spaces)
                {
                    if (s?.cells == null || s.cells.Length != 2) continue;
                    for (int h = 0; h < 2; h++)
                        cells.Add(new Cell { R = s.cells[h][0], C = s.cells[h][1], Space = s, Half = h });
                }
            if (p.sockets != null)
                foreach (var s in p.sockets)
                    if (s?.cell != null) cells.Add(new Cell { R = s.cell[0], C = s.cell[1], Socket = s });
            if (p.@fixed != null)
                foreach (var f in p.@fixed)
                    if (f?.cell != null) cells.Add(new Cell { R = f.cell[0], C = f.cell[1], Fixed = f });

            // View position: the distinct row / column numbers in use, ordered so north is up and
            // west is left (the designers skip numbers — there is no row 0 — so rank, don't subtract).
            var rows = new SortedSet<int>();
            var cols = new SortedSet<int>();
            foreach (var c in cells) { rows.Add(c.R); cols.Add(c.C); }
            var rowList = new List<int>(rows);
            var colList = new List<int>(cols);
            if (p.rows_north) rowList.Reverse();   // highest row number = northmost = view row 0
            if (!p.cols_east) colList.Reverse();   // highest column number = westmost = view column 0
            foreach (var c in cells) { c.Row = rowList.IndexOf(c.R); c.Col = colList.IndexOf(c.C); }
            cells.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));
            // Neighbours for arrow navigation: the NEAREST tile along the same row / column, so the
            // sockets hanging off the frame are one step from its edge whatever the gap.
            foreach (var a in cells)
                foreach (var o in cells)
                {
                    if (o == a) continue;
                    if (o.Col == a.Col)
                    {
                        if (o.Row < a.Row && (a.Up == null || o.Row > a.Up.Row)) a.Up = o;
                        if (o.Row > a.Row && (a.Down == null || o.Row < a.Down.Row)) a.Down = o;
                    }
                    if (o.Row == a.Row)
                    {
                        if (o.Col < a.Col && (a.Left == null || o.Col > a.Left.Col)) a.Left = o;
                        if (o.Col > a.Col && (a.Right == null || o.Col < a.Right.Col)) a.Right = o;
                    }
                }
            p.Cells = cells;
        }

        // ---- the current area's puzzle ----

        /// <summary>Called beside the other per-area refreshes (WorldModel.Tick).</summary>
        public static void Refresh(string areaName)
        {
            if (areaName == _area) return;
            _area = areaName;
            _current = null;
            ResetLive();
            if (string.IsNullOrEmpty(areaName)) return;
            EnsureLoaded();
            _current = _puzzles.Find(p => p.area == areaName);
        }

        /// <summary>This area's puzzle, or null.</summary>
        public static PuzzleDef Current => _current;

        /// <summary>Whether the board is in front of the player: its pieces exist in the loaded scene,
        /// the party has actually seen one of the slab spaces, and stands within reach of the room
        /// (the outdoor part of the same area keeps the puzzle's objects loaded).</summary>
        public static bool Present
        {
            get
            {
                var p = _current;
                if (p?.spaces == null || p.spaces.Count == 0) return false;
                bool seen = false;
                foreach (var s in p.spaces)
                {
                    var e = Find(s.@object);
                    if (e == null) return false;
                    if (e.IsRevealed) seen = true;
                }
                if (!seen) return false;
                var first = Find(p.spaces[0].@object);
                var view = first?.View;
                return view != null
                    && Geo.Distance(Overlays.Cursor.PlayerPosition, view.transform.position) < PresentRadius;
            }
        }
        private const float PresentRadius = 60f;

        // ---- live state ----

        // Scene entities by view name — HIDDEN ones included (a filled socket leaves the entity
        // pools; its container still holds the slab), so look in the area's scene states, not
        // State.MapObjects. Rebuilt when the area state object changes or an entry is disposed.
        private static readonly Dictionary<string, MapObjectEntityData> _byName
            = new Dictionary<string, MapObjectEntityData>(StringComparer.Ordinal);
        private static object _byNameState;

        private static void ResetLive()
        {
            _byName.Clear();
            _byNameState = null;
            _lastState.Clear();
            _baselined = false;
        }

        internal static MapObjectEntityData Find(string viewName)
        {
            if (string.IsNullOrEmpty(viewName)) return null;
            var state = Game.Instance?.State?.LoadedAreaState;
            if (state == null) return null;
            if (!ReferenceEquals(state, _byNameState)) { _byName.Clear(); _byNameState = state; }
            if (_byName.TryGetValue(viewName, out var hit) && hit != null && !hit.IsDisposed) return hit;
            _byName.Remove(viewName);
            try
            {
                foreach (var scene in state.GetAllSceneStates())
                    foreach (var e in scene.AllEntityData)
                    {
                        var mo = e as MapObjectEntityData;
                        if (mo == null || mo.IsDisposed) continue;
                        var v = mo.View;
                        if (v != null && v.name == viewName) { _byName[viewName] = mo; return mo; }
                    }
            }
            catch (Exception ex) { Main.Log?.Warning("[puzzle] find " + viewName + ": " + ex.Message); }
            return null;
        }

        private static string SlotName(PuzzleDef p, int r, int c)
            => p.slot.Replace("{r}", r.ToString()).Replace("{c}", c.ToString());

        private static ItemEntity ItemIn(MapObjectEntityData container)
        {
            var loot = container?.Get<InteractionLootPart>()?.Loot;
            if (loot == null) return null;
            foreach (var it in loot)
                if (it != null) return it;
            return null;
        }

        /// <summary>The symbols a slab space shows on its two cells (in the definition's cell order),
        /// or false when it is empty.</summary>
        internal static bool SpaceSymbols(PuzzleDef p, SpaceDef s, out string sym0, out string sym1)
        {
            sym0 = sym1 = null;
            for (int h = 0; h < 2; h++)
            {
                var item = ItemIn(Find(SlotName(p, s.cells[h][0], s.cells[h][1])));
                if (item == null) continue;
                var parsed = Parse(item.Blueprint);
                if (parsed == null || parsed.B == null) continue;
                // The slab's first symbol sits on the cell whose container holds it.
                if (h == 0) { sym0 = parsed.A; sym1 = parsed.B; }
                else { sym1 = parsed.A; sym0 = parsed.B; }
                return true;
            }
            return false;
        }

        /// <summary>The symbol id showing on a cell right now, or null (empty space / empty socket /
        /// the blank centre tile).</summary>
        internal static string SymbolAt(PuzzleDef p, Cell cell)
        {
            if (cell.Fixed != null) return cell.Fixed.symbol;
            if (cell.Socket != null)
            {
                var item = ItemIn(Find(cell.Socket.@object));
                return item == null ? null : Parse(item.Blueprint)?.A;
            }
            if (cell.Space != null && SpaceSymbols(p, cell.Space, out var s0, out var s1))
                return cell.Half == 0 ? s0 : s1;
            return null;
        }

        /// <summary>The spoken name of a symbol of this puzzle's set.</summary>
        internal static string SymbolName(SetDef set, string symbol)
        {
            if (symbol == null) return null;
            return set?.symbols != null && set.symbols.TryGetValue(symbol, out var key)
                ? Loc.T(key) : symbol;
        }

        /// <summary>The curated name of a puzzle object (ObjectNames — "North slab space").</summary>
        internal static string ObjectName(string viewName)
        {
            var naming = ObjectNames.Resolve(viewName);
            return naming != null ? naming.Name : viewName;
        }

        /// <summary>Which end of its space a half-cell is, as a compass word ("west" / "north" …).</summary>
        internal static string EndWord(PuzzleDef p, Cell cell)
        {
            Cell other = null;
            foreach (var c in p.Cells)
                if (c.Space == cell.Space && c != cell) { other = c; break; }
            if (other == null) return "";
            if (cell.Row == other.Row) return Loc.T(cell.Col < other.Col ? "geo.west" : "geo.east");
            return Loc.T(cell.Row < other.Row ? "geo.north" : "geo.south");
        }

        /// <summary>One slab space as a line: "North slab space: lattice west, star east" / "…: empty".</summary>
        internal static string SpaceLine(PuzzleDef p, SpaceDef s)
        {
            string name = ObjectName(s.@object);
            if (!SpaceSymbols(p, s, out var s0, out var s1))
                return Loc.T("puzzle.space_empty", new { name });
            Cell c0 = null, c1 = null;
            foreach (var c in p.Cells)
                if (c.Space == s) { if (c.Half == 0) c0 = c; else c1 = c; }
            if (c0 == null || c1 == null) return name;
            // Read west-to-east / north-to-south, whichever way the slab lies.
            bool firstIs0 = c0.Row != c1.Row ? c0.Row < c1.Row : c0.Col < c1.Col;
            var first = firstIs0 ? c0 : c1;
            var second = firstIs0 ? c1 : c0;
            return Loc.T("puzzle.space_now", new
            {
                name,
                a = SymbolName(p.Set, firstIs0 ? s0 : s1),
                adir = EndWord(p, first),
                b = SymbolName(p.Set, firstIs0 ? s1 : s0),
                bdir = EndWord(p, second),
            });
        }

        /// <summary>A cell's readout for the puzzle view.</summary>
        internal static string CellLine(PuzzleDef p, Cell cell)
        {
            string sym = SymbolName(p.Set, SymbolAt(p, cell));
            if (cell.Fixed != null)
                return sym == null ? Loc.T("puzzle.centre_tile") : Loc.T("puzzle.carved_tile", new { symbol = sym });
            if (cell.Socket != null)
            {
                string name = ObjectName(cell.Socket.@object);
                return sym == null ? Loc.T("puzzle.socket_empty", new { name })
                    : Loc.T("puzzle.socket_filled", new { name, symbol = sym });
            }
            return Loc.T("puzzle.cell", new
            {
                symbol = sym ?? Loc.T("puzzle.empty"),
                name = ObjectName(cell.Space.@object),
                end = EndWord(p, cell),
            });
        }

        /// <summary>Enter on a cell: start the real interaction with its object (the selected
        /// character walks up; the game then raises its slab menu / one-slot window, which the
        /// combine menu and loot screen make accessible).</summary>
        internal static void Activate(PuzzleDef p, Cell cell)
        {
            string objectName = cell.Space != null ? cell.Space.@object : cell.Socket?.@object;
            if (objectName == null) { Tts.Speak(CellLine(p, cell), interrupt: true); return; }
            var entity = Find(objectName);
            // A filled socket is taken out of play by the game (hidden for good) — nothing to act on.
            if (entity == null || !entity.IsInGame) { Tts.Speak(CellLine(p, cell), interrupt: true); return; }
            var item = WorldModel.Find(entity);
            if (item == null) { Tts.Speak(Loc.T("scan.cant_interact", new { name = ObjectName(objectName) }), interrupt: true); return; }
            Scanner.InteractWith(item);
        }

        // ---- the one-slot windows ----

        // Which piece of the board a container belongs to: a slab space (either of its two hidden
        // containers) or a socket.
        private static bool Owner(PuzzleDef p, ItemsCollection container, out string objectName, out bool socket)
        {
            objectName = null; socket = false;
            if (p == null || container == null) return false;
            if (p.spaces != null)
                foreach (var s in p.spaces)
                    for (int h = 0; h < 2; h++)
                    {
                        var e = Find(SlotName(p, s.cells[h][0], s.cells[h][1]));
                        if (e?.Get<InteractionLootPart>()?.Loot == container) { objectName = s.@object; return true; }
                    }
            if (p.sockets != null)
                foreach (var s in p.sockets)
                    if (Find(s.@object)?.Get<InteractionLootPart>()?.Loot == container) { objectName = s.@object; socket = true; return true; }
            return false;
        }

        /// <summary>The board piece a one-slot window belongs to, by its curated name ("East slab
        /// space") — the game titles these windows with a generic "Loot". Null for any other container.</summary>
        internal static string ContainerName(ItemsCollection container)
            => Owner(_current, container, out var name, out _) ? ObjectName(name) : null;

        /// <summary>The carried slabs of the KIND a puzzle container takes (two-symbol slabs for a
        /// slab space, single-symbol ones for a socket), so the window can say which of them the
        /// game is not offering — it lists only the slabs that currently fit.</summary>
        internal static void CarriedSlabs(ItemsCollection container, List<ItemEntity> into)
        {
            into.Clear();
            var p = _current;
            if (!Owner(p, container, out _, out bool socket)) return;
            var inventory = Game.Instance?.Player?.Inventory;
            if (inventory == null) return;
            foreach (var item in inventory)
            {
                var parsed = Parse(item?.Blueprint);
                if (parsed == null || parsed.Set != p.Set) continue;
                if ((parsed.B == null) == socket) into.Add(item);
            }
        }

        // ---- change announcements ----

        // What each space / socket showed last time we looked; a change speaks the new line — the
        // by-ear twin of watching the slab appear, flip or vanish on the floor.
        private static readonly Dictionary<string, string> _lastState = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _baselined;
        private static float _nextCheck;

        public static void Tick()
        {
            var p = _current;
            if (p == null) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.2f;
            try
            {
                if (!Present) return; // not in the room: keep whatever baseline we had
                bool speak = _baselined;
                if (p.spaces != null)
                    foreach (var s in p.spaces)
                    {
                        string state = SpaceSymbols(p, s, out var s0, out var s1) ? s0 + "|" + s1 : "";
                        if (Changed("space:" + s.@object, state) && speak)
                            Tts.Speak(SpaceLine(p, s));
                    }
                if (p.sockets != null)
                    foreach (var s in p.sockets)
                    {
                        var item = ItemIn(Find(s.@object));
                        string state = item == null ? "" : (Parse(item.Blueprint)?.A ?? "?");
                        if (Changed("socket:" + s.@object, state) && speak)
                        {
                            string name = ObjectName(s.@object);
                            Tts.Speak(state.Length == 0
                                ? Loc.T("puzzle.socket_empty", new { name })
                                : Loc.T("puzzle.socket_filled", new { name, symbol = SymbolName(p.Set, state) }));
                        }
                    }
                _baselined = true;
            }
            catch (Exception e) { Main.Log?.Warning("[puzzle] tick: " + e.Message); }
        }

        private static bool Changed(string key, string state)
        {
            if (_lastState.TryGetValue(key, out var old) && old == state) return false;
            _lastState[key] = state;
            return true;
        }

        // ---- slab item names ----

        internal sealed class Parsed
        {
            public SetDef Set;
            public string A, B; // B null = a single-symbol slab
        }
        private static readonly Dictionary<BlueprintItem, Parsed> _parsed = new Dictionary<BlueprintItem, Parsed>();

        /// <summary>The slab a blueprint is (set + symbols), or null for every other item. Cached per
        /// blueprint — item labels ask on every render.</summary>
        internal static Parsed Parse(BlueprintItem bp)
        {
            if (bp == null) return null;
            if (_parsed.TryGetValue(bp, out var hit)) return hit;
            EnsureLoaded();
            Parsed result = null;
            string name = bp.name;
            if (!string.IsNullOrEmpty(name))
                foreach (var s in _sets)
                {
                    var m = s.PairRx?.Match(name);
                    if (m != null && m.Success) { result = new Parsed { Set = s, A = m.Groups["a"].Value, B = m.Groups["b"].Value }; break; }
                    m = s.SingleRx?.Match(name);
                    if (m != null && m.Success) { result = new Parsed { Set = s, A = m.Groups["a"].Value }; break; }
                }
            _parsed[bp] = result;
            return result;
        }

        /// <summary>What to add to a slab's item name so it can be told apart by ear ("ship and
        /// sickle" / "star"), or null for any other item.</summary>
        internal static string ItemSuffix(BlueprintItem bp)
        {
            var p = Parse(bp);
            if (p == null) return null;
            return p.B == null ? SymbolName(p.Set, p.A)
                : Loc.T("puzzle.slab_pair", new { a = SymbolName(p.Set, p.A), b = SymbolName(p.Set, p.B) });
        }
    }
}
