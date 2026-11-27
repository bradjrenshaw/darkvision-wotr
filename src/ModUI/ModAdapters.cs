using System;
using System.Collections.Generic;
using WrathAccess.ModUI.Imgui;

namespace WrathAccess.ModUI
{
    /// <summary>
    /// The registry of mod-specific adapters. Adding support for a mod = one
    /// <see cref="ImguiAdapter"/> subclass under <c>src/ModUI/Mods/</c> and one line in
    /// <see cref="Initialize"/>. A mod with no adapter gets <see cref="Generic"/>.
    ///
    /// LIBRARY adapters aren't about one mod but about a widget library several mods bundle
    /// (ModKit): they install probes on the library's own functions and claim the scopes those probes
    /// record, so the library's widgets read properly inside any mod, adapter or not. A mod built on
    /// the library derives its adapter from the library's.
    /// </summary>
    internal static class ModAdapters
    {
        /// <summary>The fallback: the generic reader, no mod knowledge.</summary>
        public static readonly ImguiAdapter Generic = new ImguiAdapter();

        private static readonly Dictionary<string, ImguiAdapter> _byMod =
            new Dictionary<string, ImguiAdapter>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<ImguiAdapter> _all = new List<ImguiAdapter>();

        public static void Initialize()
        {
            Clear();
            Register(new Mods.ModKitAdapter()); // widget library bundled by ToyBox and others
            Register(new Mods.ToyBoxAdapter());
        }

        public static void Clear()
        {
            _byMod.Clear();
            _all.Clear();
        }

        public static void Register(ImguiAdapter adapter)
        {
            _all.Add(adapter);
            if (!string.IsNullOrEmpty(adapter.Id) && adapter.Id != "generic") _byMod[adapter.Id] = adapter;
        }

        /// <summary>The adapter for a mod by its manager id (Unity Mod Manager's Info.Id, or a native
        /// mod's UniqueName); the generic reader when the mod has none.</summary>
        public static ImguiAdapter ForMod(string modId)
        {
            ImguiAdapter a;
            return modId != null && _byMod.TryGetValue(modId, out a) ? a : Generic;
        }

        /// <summary>The adapter that presents a scope node: the mod's, for a "mod" scope; else
        /// whichever adapter claims it; else null (the current adapter keeps going).</summary>
        public static ImguiAdapter ForScope(ImguiNode scope)
        {
            if (scope == null) return null;
            if (scope.ScopeKind == "mod") return ForMod(scope.ScopeId);
            for (int i = 0; i < _all.Count; i++)
            {
                bool claims;
                try { claims = _all[i].Claims(scope); } catch { claims = false; }
                if (claims) return _all[i];
            }
            return null;
        }

        /// <summary>Give every adapter the chance to hook its mod. Safe to call repeatedly.</summary>
        public static void InstallProbes()
        {
            var h = ModUi.Harmony;
            if (h == null) return;
            for (int i = 0; i < _all.Count; i++)
            {
                try { _all[i].InstallProbes(h); }
                catch (Exception e) { Main.Log?.Error("[modui] probes for " + _all[i].Id + ": " + e); }
            }
        }
    }
}
