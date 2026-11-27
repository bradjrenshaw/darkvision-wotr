using System.Text;

namespace WrathAccess.ModUI.Imgui
{
    /// <summary>Debug tooling: the captured IMGUI trees as indented text (the dev server's /imgui).
    /// This is how an adapter gets written — open the mod's window, dump, read what it really draws.</summary>
    internal static class ImguiDump
    {
        public static string All(string filter = null, int maxLines = 4000)
        {
            var sb = new StringBuilder();
            sb.Append("active=").Append(ImguiCapture.Active)
              .Append(" version=").Append(ImguiCapture.Version)
              .Append(" pending=").Append(ImguiCapture.PendingRequests)
              .Append(" frame=").Append(UnityEngine.Time.frameCount).Append('\n');
            int lines = 0;
            foreach (var s in ImguiCapture.Surfaces)
            {
                if (!string.IsNullOrEmpty(filter) && s.Key.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                sb.Append("== ").Append(s.Key)
                  .Append(" title=").Append(Quote(s.Title))
                  .Append(s.Alive ? " alive" : " gone")
                  .Append(" captured@").Append(s.CapturedFrame)
                  .Append(" seen@").Append(s.SeenFrame).Append('\n');
                if (s.Root != null && s.Alive) Node(sb, s.Root, 1, ref lines, maxLines);
            }
            return sb.ToString();
        }

        /// <summary>Dev driver: queue a SetText on the first text field of any live surface.</summary>
        public static string TypeIntoFirstField(string text)
        {
            foreach (var s in ImguiCapture.Surfaces)
            {
                if (!s.Alive || s.Root == null) continue;
                var field = First(s.Root, ImguiKind.TextField);
                if (field == null) continue;
                ImguiCapture.Request(s, field, ImguiVerb.SetText, text);
                return "queued on " + s.Key + " " + field.Key + System.Environment.NewLine;
            }
            return "no text field captured" + System.Environment.NewLine;
        }

        private static ImguiNode First(ImguiNode n, ImguiKind kind)
        {
            if (n.Children == null) return n.Kind == kind ? n : null;
            for (int i = 0; i < n.Children.Count; i++)
            {
                var f = First(n.Children[i], kind);
                if (f != null) return f;
            }
            return null;
        }

        private static void Node(StringBuilder sb, ImguiNode n, int depth, ref int lines, int maxLines)
        {
            if (lines++ > maxLines) return;
            sb.Append(' ', depth * 2).Append(n.Kind);
            if (n.Kind == ImguiKind.Scope)
                sb.Append(' ').Append(n.ScopeKind).Append('=').Append(n.ScopeId).Append(' ').Append(Quote(n.ScopeLabel));
            if (!string.IsNullOrEmpty(n.Text) && n.Kind != ImguiKind.Scope) sb.Append(' ').Append(Quote(n.Text));
            if (!string.IsNullOrEmpty(n.Style)) sb.Append(" style=").Append(n.Style);
            if (!string.IsNullOrEmpty(n.Image)) sb.Append(" image=").Append(n.Image);
            if (!string.IsNullOrEmpty(n.Tooltip)) sb.Append(" tip=").Append(Quote(n.Tooltip));
            if (!n.IsContainer)
            {
                switch (n.Kind)
                {
                    case ImguiKind.Toggle:
                        sb.Append(n.On ? " ON" : " off");
                        if (n.Disclosure) sb.Append(" disclosure");
                        break;
                    case ImguiKind.TextField:
                        sb.Append(" content=").Append(Quote(n.Content));
                        if (n.Multiline) sb.Append(" multiline");
                        break;
                    case ImguiKind.Slider:
                        sb.Append(" value=").Append(n.Value).Append(" [").Append(n.Min).Append("..").Append(n.Max).Append(']');
                        break;
                    case ImguiKind.Choice:
                        sb.Append(" selected=").Append(n.Selected).Append(" cols=").Append(n.Columns).Append(" [");
                        for (int i = 0; n.Options != null && i < n.Options.Length; i++)
                        {
                            if (i > 0) sb.Append(" | ");
                            sb.Append(n.Options[i]);
                        }
                        sb.Append(']');
                        break;
                }
                if (!n.Enabled) sb.Append(" DISABLED");
                if (string.IsNullOrEmpty(n.Text) && !string.IsNullOrEmpty(n.Anchor)) sb.Append(" @").Append(Quote(n.Anchor));
                if (n.Occurrence > 0) sb.Append(" #").Append(n.Occurrence);
                sb.Append("  (").Append((int)n.Rect.x).Append(',').Append((int)n.Rect.y).Append(' ')
                  .Append((int)n.Rect.width).Append('x').Append((int)n.Rect.height).Append(')');
            }
            sb.Append('\n');
            if (n.Children == null) return;
            for (int i = 0; i < n.Children.Count; i++) Node(sb, n.Children[i], depth + 1, ref lines, maxLines);
        }

        private static string Quote(string s)
        {
            if (s == null) return "null";
            if (s.Length > 90) s = s.Substring(0, 90) + "…";
            return "\"" + s.Replace("\n", "\\n") + "\"";
        }
    }
}
