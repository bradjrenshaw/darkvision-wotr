using System;
using System.Collections.Generic;
using UnityEngine;

namespace WrathAccess.ModUI.Imgui
{
    /// <summary>What an injected action does to the control it targets.</summary>
    internal enum ImguiVerb
    {
        Click,     // a button reports "clicked"; a toggle flips
        SetText,   // a text field takes new content
        SetValue,  // a slider takes a new value
        Select,    // a toolbar / selection grid picks option N
    }

    /// <summary>One queued action against a control, applied the next time that control is drawn.</summary>
    internal sealed class ImguiRequest
    {
        public string Surface;
        public ImguiTarget Target;
        public ImguiVerb Verb;
        public string Text;
        public float Value;
        public int Index;
        public int IssuedFrame;
        /// <summary>Called from <see cref="ImguiCapture.Tick"/> (never from inside OnGUI): true when
        /// the control took the action, false when it was no longer there to take it.</summary>
        public Action<bool> Done;
    }

    /// <summary>
    /// The IMGUI recorder. Unity's immediate-mode GUI has nothing to read — no widget tree, no view
    /// model — so we listen to it being DRAWN: Harmony hooks on Unity's own GUI primitives
    /// (<see cref="ImguiPatches"/>) call in here during a repaint, and the calls of one repaint become
    /// one <see cref="ImguiNode"/> tree per <see cref="ImguiSurface"/>.
    ///
    /// <para><b>Acting</b> on a control uses the same hooks in reverse. A control "is clicked" when its
    /// draw call RETURNS true, so a queued <see cref="ImguiRequest"/> is applied by making the matching
    /// call return what a click would have (button → true, toggle → flipped, slider → the new value…).
    /// The mod then runs its own handler exactly as for a real click. Like a real click, the event is
    /// marked Used afterwards so the rest of that pass lays out harmlessly; the half-drawn tree of that
    /// pass is discarded and a fresh capture is forced on the next frame.</para>
    ///
    /// <para><b>Probes</b>: a mod that draws a control WITHOUT Unity's primitives (ModKit's check boxes
    /// paint glyphs straight through GUIStyle.Draw and hit-test the mouse themselves) is invisible to
    /// the generic hooks. An adapter fixes that by hooking the mod's own widget function and reporting
    /// it through the same public calls the generic hooks use: <see cref="Enter"/> / <see cref="Exit"/>
    /// bracket the widget (everything it draws inside is swallowed), <see cref="Leaf"/> records it,
    /// <see cref="TryTake"/> + <see cref="Applied"/> inject. <see cref="BeginScope"/> /
    /// <see cref="EndScope"/> bracket a region with a name ("this is tab X") so adapters get structure
    /// they don't have to guess from layout.</para>
    ///
    /// Everything here runs on the main thread, inside OnGUI. Inert (one static check per hook) unless
    /// <see cref="Active"/>.
    /// </summary>
    internal static class ImguiCapture
    {
        /// <summary>Master switch: record at all. Set by <see cref="ModUi"/> while a mod UI is open.</summary>
        public static bool Active;

        /// <summary>Idle capture cadence in frames. Captures allocate (a node per control), so we don't
        /// do it at 60 Hz; a pending request or <see cref="RequestCapture"/> forces the next frame.</summary>
        public const int Interval = 4;

        /// <summary>Bumped whenever any surface publishes a new tree.</summary>
        public static int Version { get; private set; }

        private sealed class Pass
        {
            public ImguiSurface Surface;
            public ImguiNode Root, Cur;
            public string LastText;   // nearest preceding static text with a letter in it (anchors)
            public string Scope = "";
            public readonly Dictionary<ImguiTarget, int> Occ = new Dictionary<ImguiTarget, int>();
            public int Suppress;      // >0 while inside a primitive / probed widget
            public bool Tainted;      // an action was injected mid-pass: the tree is half old, half new
            public int Leaves;
        }

        private static readonly List<Pass> _stack = new List<Pass>();
        private static readonly List<Pass> _pool = new List<Pass>();
        private static Pass _pass;      // top of _stack
        private static bool _armed;     // this OnGUI call is a repaint on a capture frame
        private static int _curOwner;   // instance id of the MonoBehaviour whose OnGUI is running

        private static readonly Dictionary<long, ImguiSurface> _surfaces = new Dictionary<long, ImguiSurface>();
        private static readonly Dictionary<long, string> _titles = new Dictionary<long, string>();
        private static readonly Queue<ImguiRequest> _requests = new Queue<ImguiRequest>();
        private static readonly List<KeyValuePair<ImguiRequest, bool>> _completed = new List<KeyValuePair<ImguiRequest, bool>>();

        private static int _decidedFrame = -1, _lastCapture = -1000, _force;
        private static bool _capture;

        // Native-backed names allocate a managed string per read; cache them (bounded — mods create
        // throwaway GUIStyle copies every frame).
        private static readonly Dictionary<GUIStyle, string> _styleNames = new Dictionary<GUIStyle, string>();
        private static readonly Dictionary<int, string> _imageNames = new Dictionary<int, string>();

        public static IEnumerable<ImguiSurface> Surfaces => _surfaces.Values;

        /// <summary>A recording pass is running right now (probes: skip your work when false).</summary>
        public static bool Recording => _pass != null || _armed;

        public static int PendingRequests => _requests.Count;

        /// <summary>Capture on the next frame instead of waiting out the idle interval.</summary>
        public static void RequestCapture(int frames = 2) { if (_force < frames) _force = frames; }

        public static void Reset()
        {
            Active = false;
            _stack.Clear();
            _pass = null;
            _armed = false;
            _surfaces.Clear();
            _titles.Clear();
            _requests.Clear();
            _completed.Clear();
            _styleNames.Clear();
            _imageNames.Clear();
            _plain.Clear();
            _force = 0;
        }

        // =====================================================================================
        // Requests
        // =====================================================================================

        public static void Request(ImguiSurface surface, ImguiNode node, ImguiVerb verb,
            string text = null, float value = 0f, int index = 0, Action<bool> done = null)
        {
            if (surface == null || node == null) return;
            _requests.Enqueue(new ImguiRequest
            {
                Surface = surface.Key,
                Target = node.Target,
                Verb = verb,
                Text = text,
                Value = value,
                Index = index,
                IssuedFrame = Time.frameCount,
                Done = done,
            });
        }

        /// <summary>Per-frame housekeeping from the mod's tick (outside OnGUI): run completion
        /// callbacks and drop a request whose control never showed up again.</summary>
        public static void Tick()
        {
            if (_requests.Count > 0 && Time.frameCount - _requests.Peek().IssuedFrame > 90)
                _completed.Add(new KeyValuePair<ImguiRequest, bool>(_requests.Dequeue(), false));
            if (_completed.Count == 0) return;
            for (int i = 0; i < _completed.Count; i++)
            {
                try { _completed[i].Key.Done?.Invoke(_completed[i].Value); }
                catch (Exception e) { Main.Log?.Error("[modui] request callback: " + e); }
            }
            _completed.Clear();
        }

        /// <summary>Does the head request target this just-recorded control? If so it is dequeued and
        /// the caller must apply it (alter the draw call's return value) and call <see cref="Applied"/>.</summary>
        public static bool TryTake(ImguiNode node, ImguiVerb verb, out ImguiRequest request)
        {
            request = null;
            if (_requests.Count == 0 || node == null) return false;
            var p = _pass;
            if (p == null || p.Tainted || !node.Enabled) return false;
            var r = _requests.Peek();
            if (r.Verb != verb) return false;
            if (!string.Equals(r.Surface, p.Surface.Key, StringComparison.Ordinal)) return false;
            if (!r.Target.Equals(node.Target)) return false;
            request = _requests.Dequeue();
            return true;
        }

        /// <summary>An action was just injected: behave like the real click did. The event becomes
        /// Used (later layout calls in this pass go inert instead of mismatching the cached layout),
        /// and every open pass is tainted so its half-old tree is never published.</summary>
        public static void Applied(ImguiRequest request)
        {
            GUI.changed = true;
            var e = Event.current;
            if (e != null) e.type = EventType.Used;
            for (int i = 0; i < _stack.Count; i++) _stack[i].Tainted = true;
            RequestCapture(3);
            if (request != null) _completed.Add(new KeyValuePair<ImguiRequest, bool>(request, true));
        }

        // =====================================================================================
        // Pass lifecycle (called from the GUI begin/end hooks)
        // =====================================================================================

        private static bool CaptureThisFrame()
        {
            int f = Time.frameCount;
            if (f != _decidedFrame)
            {
                _decidedFrame = f;
                _capture = _requests.Count > 0 || _force > 0 || f - _lastCapture >= Interval;
                if (_capture)
                {
                    _lastCapture = f;
                    if (_force > 0) _force--;
                }
            }
            return _capture;
        }

        /// <summary>A MonoBehaviour's OnGUI begins (any event type).</summary>
        public static void BeginRoot(int instanceId)
        {
            _curOwner = instanceId;
            // Whatever an exception left open in the previous OnGUI is abandoned here.
            if (_stack.Count > 0) Abandon();
            _armed = false;
            if (!Active) return;
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            _armed = CaptureThisFrame(); // the root pass itself is created lazily, on first content
        }

        /// <summary>A MonoBehaviour's OnGUI ends normally.</summary>
        public static void EndRoot()
        {
            _armed = false;
            if (_stack.Count == 0) return;
            // Only the root pass can legitimately still be open here; anything above it was left
            // behind by an aborted window and is dropped unpublished.
            var root = _stack[0];
            if (_stack.Count == 1 && root.Root.Kind == ImguiKind.Root) Publish(root);
            Abandon();
        }

        /// <summary>A window's body is about to run. Returns whether a recording pass was opened
        /// (the caller passes that back to <see cref="EndWindow"/>).</summary>
        public static bool BeginWindow(int windowId, int ownerInstance)
        {
            if (!Active) return false;
            var s = SurfaceFor(ownerInstance, windowId);
            s.SeenFrame = Time.frameCount;
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint || !CaptureThisFrame()) return false;
            Push(s, ImguiKind.Window);
            return true;
        }

        public static void EndWindow()
        {
            if (_pass == null || _pass.Root.Kind != ImguiKind.Window) return;
            Publish(_pass);
            Pop();
        }

        /// <summary>GUI.Window was called: remember the title for the surface.</summary>
        public static void NoteWindow(int windowId, GUIContent title)
        {
            if (!Active) return;
            long k = SurfaceKey(_curOwner, windowId);
            string t = title != null ? title.text : null;
            _titles[k] = t;
            ImguiSurface s;
            if (_surfaces.TryGetValue(k, out s)) s.Title = t;
        }

        private static Pass Ensure()
        {
            if (_pass != null) return _pass;
            if (!_armed) return null;
            var s = SurfaceFor(_curOwner, -1);
            s.SeenFrame = Time.frameCount;
            return Push(s, ImguiKind.Root);
        }

        private static Pass Push(ImguiSurface surface, ImguiKind rootKind)
        {
            Pass p;
            if (_pool.Count > 0)
            {
                p = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else p = new Pass();
            p.Surface = surface;
            p.Root = new ImguiNode { Kind = rootKind, Children = new List<ImguiNode>(8) };
            p.Cur = p.Root;
            p.LastText = null;
            p.Scope = "";
            p.Occ.Clear();
            p.Suppress = 0;
            p.Tainted = false;
            p.Leaves = 0;
            _stack.Add(p);
            _pass = p;
            return p;
        }

        private static void Pop()
        {
            if (_stack.Count == 0) return;
            var p = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            p.Surface = null;
            p.Root = p.Cur = null;
            _pool.Add(p);
            _pass = _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        }

        private static void Abandon()
        {
            while (_stack.Count > 0) Pop();
        }

        private static void Publish(Pass p)
        {
            if (p.Tainted || p.Leaves == 0) return;
            var s = p.Surface;
            s.Root = p.Root;
            s.CapturedFrame = Time.frameCount;
            s.Version++;
            Version++;
        }

        // =====================================================================================
        // Structure
        // =====================================================================================

        public static void PushGroup(bool vertical, bool scroll, GUIStyle style)
        {
            var p = Ensure();
            if (p == null) return;
            var n = new ImguiNode
            {
                Kind = scroll ? ImguiKind.Scroll : (vertical ? ImguiKind.Vertical : ImguiKind.Horizontal),
                Style = StyleName(style),
                Children = new List<ImguiNode>(4),
                Parent = p.Cur,
                Scope = p.Scope,
            };
            p.Cur.Children.Add(n);
            p.Cur = n;
        }

        public static void PopGroup()
        {
            var p = _pass;
            if (p == null) return;
            // A scope still open inside this group was left unbalanced by its probe: close it.
            while (p.Cur.Kind == ImguiKind.Scope && p.Cur.Parent != null) CloseCurrent(p);
            if (p.Cur.Parent == null) return; // more Ends than Begins — nothing to pop
            CloseCurrent(p);
        }

        private static void CloseCurrent(Pass p)
        {
            var n = p.Cur;
            p.Cur = n.Parent;
            if (n.Kind == ImguiKind.Scope) p.Scope = n.Scope; // back to the enclosing scope
            // Empty containers are pure layout noise (spacers, zero-content boxes): drop them.
            if (n.Children.Count == 0)
            {
                var sibs = p.Cur.Children;
                if (sibs.Count > 0 && ReferenceEquals(sibs[sibs.Count - 1], n)) sibs.RemoveAt(sibs.Count - 1);
            }
        }

        /// <summary>PROBE API: open a named region. Everything recorded until <see cref="EndScope"/>
        /// becomes its children and is identified relative to it. Returns false when not recording
        /// (then do NOT call EndScope).</summary>
        public static bool BeginScope(string kind, string id, string label, object data = null)
        {
            var p = Ensure();
            if (p == null) return false;
            var n = new ImguiNode
            {
                Kind = ImguiKind.Scope,
                ScopeKind = kind,
                ScopeId = id ?? "",
                ScopeLabel = label,
                Data = data,
                Children = new List<ImguiNode>(8),
                Parent = p.Cur,
                Scope = p.Scope, // the ENCLOSING scope (restored on close)
            };
            // Its own identity (kind + id within the enclosing scope, numbered when repeated), so an
            // adapter can re-find the scope itself next frame, not just the leaves inside it.
            n.Text = kind + ":" + n.ScopeId;
            var self = new ImguiTarget(ImguiKind.Scope, p.Scope, n.Text, null, 0);
            int occ;
            p.Occ.TryGetValue(self, out occ);
            n.Occurrence = occ;
            p.Occ[self] = occ + 1;
            p.Cur.Children.Add(n);
            p.Cur = n;
            p.Scope = string.IsNullOrEmpty(p.Scope) ? n.ScopeId : p.Scope + "/" + n.ScopeId;
            // A scope is a unit of identity: a control inside it must not be identified by
            // whatever text happened to be drawn before the scope (the row above in a list).
            p.LastText = null;
            return true;
        }

        public static void EndScope()
        {
            var p = _pass;
            if (p == null) return;
            // Close any layout groups the region left open, up to and including the scope itself.
            var n = p.Cur;
            while (n != null && n.Kind != ImguiKind.Scope) n = n.Parent;
            if (n == null) return;
            while (!ReferenceEquals(p.Cur, n)) CloseCurrent(p);
            CloseCurrent(p);
        }

        // =====================================================================================
        // Leaves
        // =====================================================================================

        /// <summary>PROBE API: a control is about to draw. Everything it draws through Unity's
        /// primitives until the matching <see cref="Exit"/> is swallowed.</summary>
        public static void Enter()
        {
            var p = Ensure();
            if (p != null) p.Suppress++;
        }

        /// <summary>PROBE API: the control finished drawing. True when it should be recorded now
        /// (a recording pass is open and we are not nested inside another control).</summary>
        public static bool Exit()
        {
            var p = _pass;
            if (p == null) return false;
            if (p.Suppress > 0) p.Suppress--;
            return p.Suppress == 0;
        }

        /// <summary>PROBE API: record a control. Call only after <see cref="Exit"/> returned true.</summary>
        public static ImguiNode Leaf(ImguiKind kind, Rect rect, string text, string tooltip = null,
            Texture image = null, GUIStyle style = null)
        {
            var p = _pass;
            if (p == null) return null;
            var n = new ImguiNode
            {
                Kind = kind,
                RawText = text ?? "",
                Text = Plain(text),
                Tooltip = string.IsNullOrEmpty(tooltip) ? null : tooltip,
                Image = ImageName(image),
                Style = StyleName(style),
                Enabled = GUI.enabled,
                Parent = p.Cur,
                Scope = p.Scope,
            };
            try { n.Rect = GUIUtility.GUIToScreenRect(rect); } catch { n.Rect = rect; }
            bool hasText = n.Text.Length > 0;
            if (!hasText) n.Anchor = p.LastText;
            var probe = new ImguiTarget(kind, p.Scope, n.Text, n.Anchor, 0);
            int occ;
            p.Occ.TryGetValue(probe, out occ);
            n.Occurrence = occ;
            p.Occ[probe] = occ + 1;
            p.Cur.Children.Add(n);
            p.Leaves++;
            // Static text names the unlabelled controls that follow it — but a bare number is a
            // value readout, not a name, and would change the identity every time it ticks.
            if (hasText && kind <= ImguiKind.Drawn && HasLetter(n.Text)) p.LastText = n.Text;
            return n;
        }

        /// <summary>Label / Box: static content.</summary>
        public static void Static(ImguiKind kind, Rect rect, GUIContent content, GUIStyle style)
        {
            if (!Exit() || content == null) return;
            string text = content.text;
            var image = content.image;
            if (string.IsNullOrEmpty(text) && image == null) return; // a bare background box
            Leaf(kind, rect, text, content.tooltip, image, style);
        }

        /// <summary>Text painted straight through GUIStyle.Draw outside every known control: some
        /// custom widget. Recorded as plain text so it is at least READABLE until it gets a probe.</summary>
        public static void Drawn(Rect rect, GUIContent content, GUIStyle style)
        {
            var p = _pass;
            if (p == null)
            {
                if (!_armed) return;
            }
            else if (p.Suppress > 0) return;
            if (content == null) return;
            string text = content.text;
            if (string.IsNullOrEmpty(text)) return;
            if (Ensure() == null) return;
            Leaf(ImguiKind.Drawn, rect, text, content.tooltip, null, style);
        }

        // =====================================================================================
        // Helpers
        // =====================================================================================

        private static long SurfaceKey(int owner, int windowId) => ((long)owner << 32) | (uint)windowId;

        private static ImguiSurface SurfaceFor(int owner, int windowId)
        {
            long k = SurfaceKey(owner, windowId);
            ImguiSurface s;
            if (_surfaces.TryGetValue(k, out s)) return s;
            string type = OwnerType(owner);
            s = new ImguiSurface
            {
                Owner = type,
                OwnerInstance = owner,
                WindowId = windowId,
                Key = windowId >= 0 ? "win:" + type + ":" + windowId : "root:" + type,
            };
            string t;
            if (_titles.TryGetValue(k, out t)) s.Title = t;
            _surfaces[k] = s;
            return s;
        }

        private static Func<int, UnityEngine.Object> _findObject;
        private static bool _findObjectTried;

        private static string OwnerType(int instanceId)
        {
            try
            {
                if (!_findObjectTried)
                {
                    _findObjectTried = true;
                    var m = typeof(UnityEngine.Object).GetMethod("FindObjectFromInstanceID",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public);
                    if (m != null)
                        _findObject = (Func<int, UnityEngine.Object>)Delegate.CreateDelegate(
                            typeof(Func<int, UnityEngine.Object>), m);
                }
                var o = _findObject != null ? _findObject(instanceId) : null;
                if (o != null) return o.GetType().FullName;
            }
            catch (Exception e) { Main.Log?.Error("[modui] owner lookup: " + e.Message); }
            return "unknown#" + instanceId;
        }

        private static string StyleName(GUIStyle style)
        {
            if (style == null) return null;
            string name;
            if (_styleNames.TryGetValue(style, out name)) return name;
            if (_styleNames.Count > 512) _styleNames.Clear();
            try { name = style.name; } catch { name = null; }
            _styleNames[style] = name;
            return name;
        }

        private static string ImageName(Texture image)
        {
            if (image == null) return null;
            int id = image.GetInstanceID();
            string name;
            if (_imageNames.TryGetValue(id, out name)) return name;
            if (_imageNames.Count > 512) _imageNames.Clear();
            try { name = image.name; } catch { name = null; }
            if (string.IsNullOrEmpty(name)) name = "";
            _imageNames[id] = name;
            return name;
        }

        private static bool HasLetter(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsLetter(s[i])) return true;
            return false;
        }

        private static readonly Dictionary<string, string> _plain = new Dictionary<string, string>();
        private static readonly System.Text.StringBuilder _plainSb = new System.Text.StringBuilder();

        /// <summary>Text with rich-text tags removed and outer whitespace trimmed. The same labels
        /// are drawn every frame, so results are cached (bounded); untagged text costs nothing.</summary>
        public static string Plain(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool tagged = s.IndexOf('<') >= 0;
            if (!tagged)
            {
                int n = s.Length;
                if (!char.IsWhiteSpace(s[0]) && !char.IsWhiteSpace(s[n - 1])) return s;
            }
            string cached;
            if (_plain.TryGetValue(s, out cached)) return cached;
            if (_plain.Count > 4096) _plain.Clear();
            string result;
            if (!tagged) result = s.Trim();
            else
            {
                _plainSb.Length = 0;
                int i = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '<')
                    {
                        int close = s.IndexOf('>', i + 1);
                        // A tag starts with a letter or '/' right after '<' ("a < b" is not one).
                        if (close > i + 1 && (char.IsLetter(s[i + 1]) || s[i + 1] == '/'))
                        {
                            i = close + 1;
                            continue;
                        }
                    }
                    _plainSb.Append(c);
                    i++;
                }
                result = _plainSb.ToString().Trim();
            }
            _plain[s] = result;
            return result;
        }
    }
}
