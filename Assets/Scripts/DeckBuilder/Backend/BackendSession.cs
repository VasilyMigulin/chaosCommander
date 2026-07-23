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
    ///   3) загрузка инвентаря → PlayerLibrary + PlayerWallet,
    ///   4) загрузка колод (PlayFab UserData «player_decks») — ПОСЛЕ библиотеки: колода ссылается на
    ///      карты, и панелям (DeckViewPanel/DeckBuildPanel) нужен уже наполненный PlayerLibrary.
    ///      Колоды живут в облаке, а не локально, — иначе с нового устройства они бы пропали.
    ///
    /// Каждый шаг не роняет вход: при ошибке логируем и идём дальше (в оффлайн-режиме
    /// игрок хотя бы попадёт в меню). onDone вызывается всегда.
    /// </summary>
    public static class BackendSession
    {
        public static bool Ready { get; private set; }

        /// <summary>CardConfig текущей сессии — нужен, чтобы применять выданные сервером карты к библиотеке
        /// (DevService/BoosterService/ShopService резолвят item id через него). Ставится в Initialize.</summary>
        public static CardConfig Config { get; private set; }

        public static void Initialize(CardConfig config, Action onDone)
        {
            Ready = false;
            Config = config;

            if (config == null)
            {
                Debug.LogError("[BackendSession] CardConfig is null — cannot build library from inventory.");
                onDone?.Invoke();
                return;
            }

            Debug.Log("[BackendSession] Initialize → ServerClock.Sync…");
            ServerClock.Sync(
                onDone: () => { Debug.Log("[BackendSession] ServerClock OK → Migrate"); Migrate(config, onDone); },
                onError: err => { Debug.LogWarning($"[BackendSession] ServerClock FAILED: {err} → Migrate всё равно"); Migrate(config, onDone); });
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
            Debug.Log("[BackendSession] LoadFromInventory…");
            PlayerLibrary.LoadFromInventory(config,
                onSuccess: () => { Debug.Log($"[BackendSession] Inventory OK → карт в библиотеке: {PlayerLibrary.Entries.Count}"); Ready = true; LoadDecks(onDone); },
                onError: err =>
                {
                    Debug.LogWarning($"[BackendSession] LoadFromInventory failed: {err}");
                    LoadDecks(onDone);   // без библиотеки колоды всё равно прогреем — панель покажет, чего не хватает
                });
        }

        /// <summary>
        /// Прогрев кеша колод. Панели грузят их и сами (DeckViewPanel.OnOpen), но так список открывается
        /// сразу, а не с задержкой на сетевой вызов.
        /// </summary>
        static void LoadDecks(Action onDone)
        {
            DeckStorage.LoadAll(
                onSuccess: decks =>
                {
                    Debug.Log($"[BackendSession] Колод загружено: {decks.Count}.");
                    onDone?.Invoke();
                },
                onError: err =>
                {
                    Debug.LogWarning($"[BackendSession] LoadDecks failed: {err}");
                    onDone?.Invoke();
                });
        }
    }
}
