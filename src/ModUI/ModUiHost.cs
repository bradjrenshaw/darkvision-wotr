using System.Collections.Generic;
using WrathAccess.ModUI.Imgui;

namespace WrathAccess.ModUI
{
    /// <summary>
    /// A window mods show their UI in, that our mod-UI screen attaches to while it is open: Unity Mod
    /// Manager's window, the game's native-mod window, or (later) a mod's own standalone window.
    ///
    /// A host answers three things: is the window open (polled — hosts are other people's code, found
    /// by reflection, and may not be installed at all); how to open and close it; and how to lay its
    /// captured surfaces out as Tab stops. The default layout is one stop per surface, read by the
    /// generic adapter; a host that knows its window (see <see cref="Hosts.UmmHost"/>) overrides
    /// <see cref="Build"/> to give the shell real structure and hand each mod's region to that mod's
    /// adapter via <see cref="ModAdapters.ForMod"/>.
    /// </summary>
    internal abstract class ModUiHost
    {
        /// <summary>Stable id ("umm", "owlcat").</summary>
        public abstract string Id { get; }

        /// <summary>The spoken screen name (localized).</summary>
        public abstract string Title { get; }

        /// <summary>The thing this host attaches to exists in this game install.</summary>
        public abstract bool Present { get; }

        public abstract bool IsOpen { get; }
        public abstract void Open();
        public abstract void Close();

        /// <summary>Escape. Default: close the window; a host with nested things to dismiss first
        /// (a drop-down pop-up) overrides.</summary>
        public virtual void Back() => Close();

        /// <summary>Is this surface part of the host's window (its main window, its pop-ups)?</summary>
        public abstract bool Owns(ImguiSurface surface);

        /// <summary>Our screen took over the keyboard for this window.</summary>
        public virtual void OnAttach() { }

        /// <summary>The window closed (or the mod is unloading): undo whatever OnAttach did.</summary>
        public virtual void OnDetach() { }

        /// <summary>The Tab stop focus starts on when the window opens (null = the first one). Lets a
        /// host keep its tab bar first in Tab order yet open on the content.</summary>
        public virtual object InitialStop => null;

        /// <summary>Declare the graph for the host's live surfaces.</summary>
        public virtual void Build(ModUiContext ctx, List<ImguiSurface> surfaces)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                if (s.Root == null) continue;
                ctx.Surface = s;
                ctx.B.BeginStop("surface:" + s.Key);
                ModAdapters.Generic.Build(ctx, s.Root);
            }
        }
    }
}
