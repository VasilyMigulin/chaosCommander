using System;
using System.Collections.Generic;
using Game.Core.Shared;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Глобальные флаги «в разделе есть новое» по типобезопасным ключам (RegistryId, см. NotifyKeys).
    /// Кнопки-табы (AwesomeButton с _notifyKey) сами показывают/прячут бейдж и гасят его при открытии.
    /// Флаги ставит доменный/меню-код:
    ///   NotifyState.Set(NotifyKeys.BlackMarket, true) — пришёл новый ассортимент;
    ///   NotifyState.Set(NotifyKeys.Journal, true)     — есть невзятая награда/выполненный дейлик.
    /// </summary>
    public static class NotifyState
    {
        static readonly HashSet<RegistryId> _flags = new HashSet<RegistryId>();

        /// <summary>(key, active) — бейдж включился/выключился.</summary>
        public static event Action<RegistryId, bool> Changed;

        public static bool Has(RegistryId key) => !key.IsEmpty && _flags.Contains(key);

        public static void Set(RegistryId key, bool active)
        {
            if (key.IsEmpty) return;
            bool changed = active ? _flags.Add(key) : _flags.Remove(key);
            if (changed) Changed?.Invoke(key, active);
        }

        public static void Clear(RegistryId key) => Set(key, false);
    }
}
