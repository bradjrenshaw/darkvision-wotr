using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Designers.EventConditionActionSystem.ContextData;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.View.MapObjects;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Catches the game's combine interaction (<see cref="IInteractCombineHandler"/> — raised when a
    /// character reaches a combine object and interacts) and offers its entries as an accessible
    /// menu. Mirrors the game's own radial exactly (OvertipsViewCombinePartBase.Show): the same
    /// entries, filtered by the same per-entry condition under the same context (the object + the
    /// interacting unit), labelled with the entry's hint, and a choice runs the entry's action under
    /// that context again. The game's radial opens alongside for sighted eyes; we dismiss it when
    /// our menu closes, as its own click-away would.
    /// </summary>
    internal sealed class CombineMenu : IInteractCombineHandler
    {
        private static CombineMenu _instance;

        public static void Initialize()
        {
            if (_instance != null) return;
            _instance = new CombineMenu();
            EventBus.Subscribe(_instance);
        }

        /// <summary>Unhook from the game (module hot-reload teardown).</summary>
        public static void Shutdown()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
        }

        public void HandleOnInteract(InteractionCombinePart combinePart, UnitEntityData user)
        {
            try
            {
                var owner = combinePart?.Owner as MapObjectEntityData;
                var all = combinePart?.Settings?.Entries;
                if (owner == null || all == null) return;

                var entries = new List<InteractionCombineEntry>();
                var labels = new List<string>();
                foreach (var entry in all)
                {
                    if (entry == null) continue;
                    using (ContextData<MapObjectData>.Request().Setup(owner))
                    using (ContextData<InteractingUnitData>.Request().Setup(user))
                    {
                        var cond = entry.Condition?.Get();
                        if (cond != null && !cond.Check()) continue;
                    }
                    string hint = null;
                    try { hint = entry.Hint != null && entry.Hint.String != null ? (string)entry.Hint.String : null; } catch { }
                    entries.Add(entry);
                    labels.Add(string.IsNullOrWhiteSpace(hint) ? Loc.T("combine.unnamed") : TextUtil.StripRichText(hint));
                }

                string title = new ProxyMapObject(owner).Name;
                if (entries.Count == 0)
                {
                    Tts.Speak(Loc.T("combine.nothing", new { name = title }), interrupt: true);
                    DismissRadial(owner);
                    return;
                }

                WrathAccess.Screens.CombineMenuScreen.Open(title, labels,
                    i =>
                    {
                        using (ContextData<MapObjectData>.Request().Setup(owner))
                        using (ContextData<InteractingUnitData>.Request().Setup(user))
                            entries[i].Action?.Get()?.Run();
                    },
                    () => DismissRadial(owner));
            }
            catch (Exception e) { Main.Log?.Warning("[combine] " + e); }
        }

        // The radial hides when its VM's interacting unit clears (OvertipsViewCombinePartBase.Hide).
        private static void DismissRadial(MapObjectEntityData owner)
        {
            try
            {
                var vms = Game.Instance?.RootUiContext?.InGameVM?.DynamicPartVM?.OvertipsVM?.OvertipVms;
                if (vms != null && vms.TryGetValue(owner, out var vm) && vm != null)
                    vm.CombineInteractUnit.Value = null;
            }
            catch (Exception e) { Main.Log?.Warning("[combine] dismiss: " + e.Message); }
        }
    }
}
