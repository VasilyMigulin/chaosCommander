using System;
using System.Threading.Tasks;
using Fusion;
using Game.Core.Network;
using UnityEngine;

namespace Game.Core.Photon
{
    // === helper (flow) ===
    /// <summary>
    /// ВОЗВРАТ В НЕЗАВЕРШЁННЫЙ МАТЧ после перезапуска приложения (фаза 2). Вызывается из меню на старте.
    ///
    /// Порядок: запись о матче (MatchSessionStore) → вход в ТУ ЖЕ комнату по имени, в обход матчмейкинга
    /// (тот ищет по MMR и отсеивает полные комнаты) → запрос разрешения у оставшегося пира → загрузка боевой
    /// сцены. Дальше эстафету принимает боевой слой: InitPlayerSystem поднимает игроков из сохранённой
    /// идентичности, WorldResyncSystem просит и применяет снэпшот мира под затемнением.
    ///
    /// ЖИВЁТ В СБОРКЕ Game.Core.Photon, а не рядом с MatchSessionStore: Game.Core.Photon уже ссылается на
    /// Game.Core.Network, и обратная ссылка (ради PhotonInitializer) замкнула бы сборки в цикл.
    ///
    /// ГРАНИЦА MVP: возврат возможен, пока ЖИВА сессия Photon, то есть если ушёл НЕ хост. Уход хоста роняет
    /// сессию для обоих (host migration не реализован) — там по-прежнему технический результат по watchdog.
    /// Поэтому вход строго GameMode.Client: если комнаты уже нет, Fusion честно вернёт ошибку, а не создаст
    /// пустой «фантом» с тем же именем, в котором мы ждали бы несуществующего соперника.
    /// </summary>
    public static class ReconnectService
    {
        const int   LobbySceneIndex    = 2;      // как MatchmakingConfig.SceneIndex
        const float HandlerWaitSeconds = 10f;    // ожидание репликации PhotonRunHandler после джойна
        const float GrantWaitSeconds   = 10f;    // ожидание ответа пира
        const int   RequestRetryMs     = 1000;   // период повторной отправки запроса

        // ЖДЁМ ОСВОБОЖДЕНИЯ СЛОТА. Убитый процесс не разрывает соединение штатно: хост дропает его только
        // по Fusion ConnectionTimeout (NetworkProjectConfig, сейчас 60с), а до тех пор комната числится
        // ПОЛНОЙ и джойн отбивается GameIsFull. Боевой тест 2026-07-30: первая же попытка получила
        // «Session ... is full», и реконнект сдавался — теперь стучимся до победы или до конца окна.
        const float JoinRetryWindowSeconds = 90f;
        const int   JoinRetryDelayMs       = 3000;

        /// <summary>
        /// true — восстановление пошло (грузится боевая сцена); false — остаёмся в меню.
        /// onStillWaiting — периодический сигнал «ещё ждём» для UI (слот освобождается до минуты): текст и
        /// показ остаются на стороне меню, сюда UI-слой не тянем.
        /// </summary>
        public static async Task<bool> TryResumeAsync(Action onStillWaiting = null)
        {
            var photon = PhotonInitializer.Instance;
            if (photon == null) return false;
            if (!MatchSessionStore.TryLoad(out var rec)) return false;

            Debug.Log($"[Reconnect] найден незавершённый матч '{rec.SessionName}' — пробую вернуться");
            ReconnectFlow.Begin(rec);

            if (!await JoinWithRetries(photon, rec.SessionName, onStillWaiting))
                return false;   // Abort уже отработал внутри

            var handler = await WaitFor(UnityEngine.Object.FindFirstObjectByType<PhotonRunHandler>, HandlerWaitSeconds);
            if (handler == null)
                return await Abort("PhotonRunHandler не реплицировался — в комнате никого нет");

            // Запрос шлём ПОВТОРНО: пир мог ещё не обработать наш джойн, а RPC не буферизуются.
            bool accepted = false, answered = false;
            float deadline = Time.realtimeSinceStartup + GrantWaitSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                handler.RPC_RequestReconnect(rec.MyPlayerId);
                await Task.Delay(RequestRetryMs);
                if (ReconnectFlow.TryTakeGrant(out accepted)) { answered = true; break; }
            }

            if (!answered) return await Abort("пир не ответил на запрос — вероятно, матч уже закрыт");
            if (!accepted) return await Abort("пир сообщил, что матч завершён");

            Debug.Log("[Reconnect] разрешение получено — гружу боевую сцену");

            // Идентичность матча для рейтинга: reveal-RPC вернувшемуся заново не придёт — берём из записи.
            if (!string.IsNullOrEmpty(rec.MatchId) && !string.IsNullOrEmpty(rec.OpponentPlayFabId))
                Game.Core.Service.MatchIdentity.Set(rec.MatchId, rec.OpponentPlayFabId);

            handler.LoadGameSceneForReconnect();
            return true;
        }

        /// <summary>
        /// Вход в комнату с повторами. «Комната полна» — это НЕ отказ, а ожидаемое состояние: слот убитого
        /// клиента держится до Fusion ConnectionTimeout. Стучимся, пока не пустят или не выйдет окно.
        /// Любая другая ошибка (комнаты нет — хост тоже ушёл) означает, что возвращаться некуда: выходим сразу.
        /// </summary>
        static async Task<bool> JoinWithRetries(PhotonInitializer photon, string room, Action onStillWaiting)
        {
            const int WaitHintEveryAttempts = 5;   // ~15с между напоминаниями, чтобы не спамить тостами
            float deadline = Time.realtimeSinceStartup + JoinRetryWindowSeconds;
            int attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    await photon.StartSession(new SessionParams
                    {
                        Mode              = GameMode.Client,
                        RoomName          = room,
                        LobbySceneIndex   = LobbySceneIndex,
                        TargetPlayerCount = 2,
                        ProvideInput      = true,
                    });
                    Debug.Log($"[Reconnect] вошёл в комнату с попытки #{attempt}");
                    return true;
                }
                catch (SessionFullException)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                        return await Abort($"комната так и не освободила слот за {JoinRetryWindowSeconds:F0}с (попыток: {attempt})");

                    Debug.Log($"[Reconnect] попытка #{attempt}: слот ещё занят (хост дропнет мёртвого пира по таймауту) — жду");
                    if (attempt % WaitHintEveryAttempts == 0) onStillWaiting?.Invoke();
                    await CleanupRunner();
                    await Task.Delay(JoinRetryDelayMs);
                }
                catch (Exception e)
                {
                    return await Abort($"в комнату не попасть ({e.Message}) — сессия закрыта или хост ушёл");
                }
            }
        }

        /// <summary>Прибрать раннер после неудачного джойна — иначе следующая попытка стартует поверх него.</summary>
        static async Task CleanupRunner()
        {
            try
            {
                if (PhotonInitializer.Instance != null)
                    await PhotonInitializer.Instance.EndSession();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Reconnect] очистка раннера между попытками не удалась: {e.Message}");
            }
        }

        /// <summary>Возврат не состоялся: чистим запись (второй попытки не будет) и выходим из сессии.</summary>
        static async Task<bool> Abort(string reason)
        {
            Debug.LogWarning($"[Reconnect] отмена: {reason}");
            ReconnectFlow.Clear();
            MatchSessionStore.Clear();
            try
            {
                if (PhotonInitializer.Instance != null)
                    await PhotonInitializer.Instance.EndSession();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Reconnect] EndSession после отмены не удался: {e.Message}");
            }
            return false;
        }

        static async Task<T> WaitFor<T>(Func<T> probe, float seconds) where T : class
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                var value = probe();
                if (value != null) return value;
                await Task.Delay(200);
            }
            return null;
        }
    }
}
