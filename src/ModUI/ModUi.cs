using System;
using System.Collections.Generic;
using HarmonyLib;
using WrathAccess.ModUI.Imgui;

namespace WrathAccess.ModUI
{
    /// <summary>
    /// Third-party mod UI support — the coordinator.
    ///
    /// <para>Other mods have no shared UI framework; what nearly all of them share is Unity's
    /// immediate-mode GUI, drawn inside Unity Mod Manager's window (or the game's own native-mod
    /// window, or a window of their own). So the foundation is generic: <see cref="ImguiCapture"/>
    /// records whatever is drawn and can press it; <see cref="ImguiAdapter"/> turns a recorded tree
    /// into our navigation graph with rules that work for any mod.</para>
    ///
    /// <para>Three layers of special-casing sit on top, each optional, each a subclass:</para>
    /// <list type="bullet">
    /// <item><see cref="ModUiHost"/> — a WINDOW we attach to (the mod manager). Knows how to tell it is
    /// open, open/close it, and lay out its shell (tabs, mod list) around the mods' content.</item>
    /// <item><see cref="ImguiAdapter"/> subclasses — one MOD's content. Override a hook to rename or
    /// hide a control, or take over a region's layout entirely; register by mod id.</item>
    /// <item>Probes — Harmony hooks an adapter puts on a mod's OWN widget functions, for controls the
    /// generic recorder can't see or can't name (see the probe API on <see cref="ImguiCapture"/>).</item>
    /// </list>
    ///
    /// Nothing is patched until a host is first seen open: a player who never opens a mod window
    /// never has Unity's GUI hooked.
    /// </summary>
    internal static class ModUi
    {
        private static Harmony _harmony;
        private static bool _installed;
        private static int _pollFrame = -100;

        public static readonly List<ModUiHost> Hosts = new List<ModUiHost>();

        /// <summary>The host whose window is open right now (null = none; the screen is inactive).</summary>
        public static ModUiHost ActiveHost { get; private set; }

        /// <summary>A host by id ("umm", "owlcat"), or null.</summary>
        public static ModUiHost Host(string id)
        {
            for (int i = 0; i < Hosts.Count; i++)
                if (Hosts[i].Id == id) return Hosts[i];
            return null;
        }

        /// <summary>DEBUG: keep recording with no host open (the /imgui dev route).</summary>
        public static bool DevHold;

        /// <summary>The Harmony instance adapter probes patch under (unpatched as one on shutdown).</summary>
        public static Harmony Harmony => _harmony;

        public static void Initialize()
        {
            _harmony = new Harmony("WrathAccess.modui.gen" + WrathAccess.Modularity.ModuleLoader.Generation);
            _installed = false;
            ActiveHost = null;
            DevHold = false;
            ImguiCapture.Reset();
            Hosts.Clear();
            Hosts.Add(new ModUI.Hosts.UmmHost());
            Hosts.Add(new ModUI.Hosts.OwlcatModsHost());
            ModAdapters.Initialize();
        }

        public static void Shutdown()
        {
            try { if (ActiveHost != null) ActiveHost.OnDetach(); } catch (Exception e) { Main.Log?.Error("[modui] detach: " + e); }
            ActiveHost = null;
            try { _harmony?.UnpatchAll(_harmony.Id); } catch (Exception e) { Main.Log?.Error("[modui] unpatch: " + e); }
            _installed = false;
            ImguiCapture.Reset();
            Hosts.Clear();
            ModAdapters.Clear();
        }

        /// <summary>Hook Unity's IMGUI (once). Called the first time a host is open.</summary>
        public static void EnsureInstalled()
        {
            if (_installed || _harmony == null) return;
            _installed = true;
            int n = ImguiPatches.Install(_harmony);
            Main.Log?.Log("[modui] IMGUI capture installed: " + n + " hooks");
        }

        /// <summary>Per frame, BEFORE the screen manager resolves its stack.</summary>
        public static void Tick()
        {
            int f = UnityEngine.Time.frameCount;
            // Hosts are found by reflection; poll at ~10 Hz, not every frame.
            if (f - _pollFrame >= 6 || ActiveHost != null)
            {
                _pollFrame = f;
                ModUiHost open = null;
                for (int i = 0; i < Hosts.Count; i++)
                {
                    bool isOpen;
                    try { isOpen = Hosts[i].IsOpen; }
                    catch (Exception e) { Main.Log?.Error("[modui] host " + Hosts[i].Id + ": " + e.Message); isOpen = false; }
                    if (isOpen) { open = Hosts[i]; break; }
                }
                if (!ReferenceEquals(open, ActiveHost))
                {
                    try { if (ActiveHost != null) ActiveHost.OnDetach(); } catch (Exception e) { Main.Log?.Error("[modui] detach: " + e); }
                    ActiveHost = open;
                    if (open != null)
                    {
                        EnsureInstalled();
                        ModAdapters.InstallProbes();
                        try { open.OnAttach(); } catch (Exception e) { Main.Log?.Error("[modui] attach: " + e); }
                        ImguiCapture.RequestCapture(3);
                    }
                }
            }
            bool want = ActiveHost != null || DevHold;
            if (want && !_installed) EnsureInstalled();
            ImguiCapture.Active = want;
            if (want) ImguiCapture.Tick();
        }
    }
}
