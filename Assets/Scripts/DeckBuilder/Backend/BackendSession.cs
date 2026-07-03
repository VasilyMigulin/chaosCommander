using System;
using Game.Core.Configs;
using Game.Core.DeckBuilder;
using UnityEngine;

namespace Game.Core.Backend
{
    /// <summary>
    /// Оркестратор инициализации бэкенда после логина. Последовательность:
    ///   1) синк серверного времени (ServerClock),
    ///   2) одноразовая миграция старой библиотеки в инвентарь (идемпотентно),
    ///   3) загрузка инвентаря → PlayerLibrary + PlayerWallet.
    ///
    /// Каждый шаг не роняет вход: при ошибке логируем и идём дальше (в оффлайн-режиме
    /// игрок хотя бы попадёт в меню). onDone вызывается всегда.
    /// </summary>
    public static class BackendSession
    {
        public static bool Ready { get; private set; }

        public static void Initialize(CardConfig config, Action onDone)
        {
            Ready = false;

            if (config == null)
            {
                Debug.LogError("[BackendSession] CardConfig is null — cannot build library from inventory.");
                onDone?.Invoke();
                return;
            }

            ServerClock.Sync(
                onDone: () => Migrate(config, onDone),
                onError: _ => Migrate(config, onDone));   // время не критично для входа
        }

        static void Migrate(CardConfig config, Action onDone)
        {
            EconomyService.MigrateLibraryIfNeeded(
                onDone: result =>
                {
                    if (result != null && result.Migrated && result.CardsGranted > 0)
                        Debug.Log($"[BackendSession] Migrated {result.CardsGranted} cards to inventory.");
                    LoadInventory(config, onDone);
                },
                onError: err =>
                {
                    Debug.LogWarning($"[BackendSession] MigrateLibrary failed: {err}");
                    LoadInventory(config, onDone);   // всё равно пробуем загрузить, что есть
                });
        }

        static void LoadInventory(CardConfig config, Action onDone)
        {
            PlayerLibrary.LoadFromInventory(config,
                onSuccess: () => { Ready = true; onDone?.Invoke(); },
                onError: err =>
                {
                    Debug.LogWarning($"[BackendSession] LoadFromInventory failed: {err}");
                    onDone?.Invoke();
                });
        }
    }
}
