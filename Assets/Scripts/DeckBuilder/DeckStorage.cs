using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Хранилище колод. ИСТОЧНИК ПРАВДЫ — облако (PlayFab UserData «player_decks»): иначе с нового
    /// устройства колоды бы пропали. Если облако вернуло пусто — у аккаунта НЕТ колод (не «поднимаем»
    /// локальные: PlayerPrefs привязан к устройству, и чужая колода протекла бы на свежий аккаунт).
    ///
    /// Локальное зеркало (PlayerPrefs, колоды кодами DeckCode) — ТОЛЬКО фолбэк на два случая:
    ///   • ДЕВ/ТЕСТ без PlayFab (SkipLogin): облака нет, зеркало держит колоды между Play Mode;
    ///   • оффлайн залогиненного игрока: cloud НЕДОСТУПЕН (сеть) → показываем последнее виденное.
    /// Ключи зеркала и активной колоды СКОПИРОВАНЫ ПО АККАУНТУ (PlayFabId) — иначе колоды одного игрока
    /// видны другому на том же устройстве.
    /// </summary>
    public static class DeckStorage
    {
        /// <summary>Сколько колод игрок может держать собранными (DeckViewPanel показывает их слотами).</summary>
        public const int MaxDecks = 6;

        // Скоуп = аккаунт. Не залогинен (дев/SkipLogin) → "guest".
        static string Scope => string.IsNullOrEmpty(PlayFabService.PlayFabId) ? "guest" : PlayFabService.PlayFabId;
        static string LocalKey => "player_decks_local_" + Scope;
        static string ActiveKey => "active_deck_" + Scope;

        // Старые НЕскоупленные ключи (до фикса) — источник утечки колод между аккаунтами. Чистим один раз.
        const string LegacyLocalKey = "player_decks_local";
        const string LegacyActiveKey = "active_deck";
        static bool _legacyPurged;

        static List<SavedDeckData> _cache = new List<SavedDeckData>();
        static bool _localLoaded;   // локальное зеркало уже пробовали поднять (даже если оно пустое)

        static void PurgeLegacyKeys()
        {
            if (_legacyPurged) return;
            _legacyPurged = true;
            if (PlayerPrefs.HasKey(LegacyLocalKey))  PlayerPrefs.DeleteKey(LegacyLocalKey);
            if (PlayerPrefs.HasKey(LegacyActiveKey)) PlayerPrefs.DeleteKey(LegacyActiveKey);
            PlayerPrefs.Save();
        }

        public static int Count { get { EnsureLoaded(); return _cache.Count; } }
        public static bool IsFull => Count >= MaxDecks;
        public static bool Exists(string name) { EnsureLoaded(); return _cache.Exists(d => d.Name == name); }

        /// <summary>
        /// Кеш пуст → поднимаем локальное зеркало СИНХРОННО (PlayerPrefs, без сети).
        ///
        /// Зачем: колоды грузит бутстрап (BackendSession → LoadAll), но НЕ во всех путях входа — напр.
        /// SkipLoginForTesting уходит в меню мимо облака. Без этого бой стартовал без колоды, если игрок
        /// не открывал раздел колод (его OnOpen наполнял кеш как побочный эффект). Теперь любой
        /// потребитель (InitDeckSystem, панели) получает данные независимо от того, куда игрок заходил.
        /// Успешный LoadAll позже всё равно перетрёт кеш облачными данными — они главнее.
        /// </summary>
        static void EnsureLoaded()
        {
            if (_localLoaded || _cache.Count > 0) return;
            PurgeLegacyKeys();
            _localLoaded = true;

            var local = LoadLocal();
            if (local.Count == 0) return;

            _cache = local;
            EnsureActiveValid();
            Debug.Log($"[DeckStorage] Кеш был пуст — поднял колоды из локального зеркала: {_cache.Count}.");
        }

        // ── Активная колода (ею играем) ──────────────────────────────────────────

        /// <summary>
        /// Имя колоды, которой игрок идёт в бой. Выбирается в DeckViewPanel, читается InitDeckSystem.
        /// Хранится ЛОКАЛЬНО (PlayerPrefs): это настройка устройства, а не прогресс — на планшете и
        /// телефоне логично играть разными колодами.
        /// </summary>
        public static string ActiveDeckName
        {
            get => PlayerPrefs.GetString(ActiveKey, string.Empty);
            private set { PlayerPrefs.SetString(ActiveKey, value ?? string.Empty); PlayerPrefs.Save(); }
        }

        /// <summary>Сделать колоду активной. Пустое имя / неизвестная колода → сбрасывает выбор.</summary>
        public static void SetActive(string deckName)
        {
            ActiveDeckName = Exists(deckName) ? deckName : string.Empty;
        }

        /// <summary>
        /// Колода для боя: активная, иначе первая (чтобы игрок с одной колодой ничего не выбирал),
        /// иначе null. Единая точка — раньше InitDeckSystem молча брал decks[0].
        /// </summary>
        public static SavedDeckData GetActive()
        {
            EnsureLoaded();   // бой мог начаться до того, как игрок открывал раздел колод
            if (_cache.Count == 0) return null;

            string active = ActiveDeckName;
            if (!string.IsNullOrEmpty(active))
            {
                var found = _cache.Find(d => d.Name == active);
                if (found != null) return found;
            }
            return _cache[0];
        }

        /// <summary>Активная колода пропала (удалили/переименовали) → перевыбрать первую.</summary>
        static void EnsureActiveValid()
        {
            if (_cache.Count == 0) { ActiveDeckName = string.Empty; return; }
            if (!Exists(ActiveDeckName)) ActiveDeckName = _cache[0].Name;
        }

        /// <summary>«Колода», «Колода 2», … — свободное имя для новой/импортированной колоды.</summary>
        public static string MakeUniqueName(string desired, string fallback = "Колода")
        {
            string baseName = string.IsNullOrWhiteSpace(desired) ? fallback : desired.Trim();
            if (!Exists(baseName)) return baseName;

            for (int i = 2; i <= MaxDecks + 1; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!Exists(candidate)) return candidate;
            }
            return baseName;
        }

        public static void LoadAll(Action<List<SavedDeckData>> onSuccess, Action<string> onError = null)
        {
            PurgeLegacyKeys();
            _localLoaded = true;   // источник данных определяем здесь — ленивый подъём зеркала больше не нужен

            PlayFabService.LoadDecks(decks =>
            {
                // Облако — источник правды. Пусто → у аккаунта НЕТ колод. НЕ «поднимаем» локальные:
                // PlayerPrefs привязан к устройству, и на свежем аккаунте так протекала чужая колода.
                _cache = decks ?? new List<SavedDeckData>();
                SaveLocal();                       // обновляем зеркало ЭТОГО аккаунта (в т.ч. очищаем, если пусто)
                EnsureActiveValid();
                onSuccess?.Invoke(_cache);
            },
            onError: err =>
            {
                // Облако НЕДОСТУПНО (нет сети / нет логина) → фолбэк на зеркало ЭТОГО аккаунта. Не ошибка
                // для UI: список открывается, просто данные локальные. not_logged_in → скоуп "guest" (дев).
                _cache = LoadLocal();
                Debug.LogWarning($"[DeckStorage] Облако недоступно ({err}) — колоды из локального зеркала: {_cache.Count}.");
                EnsureActiveValid();
                onSuccess?.Invoke(_cache);
            });
        }

        public static void SaveAll(IEnumerable<SavedDeckData> decks,
            Action onSuccess = null, Action<string> onError = null)
        {
            _cache = new List<SavedDeckData>(decks);
            Push(onSuccess, onError);
        }

        public static void SaveOrReplace(SavedDeckData deck,
            Action onSuccess = null, Action<string> onError = null)
            => SaveOrReplace(deck, deck?.Name, onSuccess, onError);

        /// <summary>
        /// Сохранить колоду, заменив ту, что называлась originalName. Отдельный originalName нужен для
        /// ПЕРЕИМЕНОВАНИЯ: искать по новому имени — значит не найти старую запись и завести дубль.
        /// Пустой originalName → колода новая.
        /// </summary>
        public static void SaveOrReplace(SavedDeckData deck, string originalName,
            Action onSuccess = null, Action<string> onError = null)
        {
            if (deck == null) { onError?.Invoke("deck is null"); return; }

            string key = string.IsNullOrEmpty(originalName) ? deck.Name : originalName;
            int idx = _cache.FindIndex(d => d.Name == key);

            if (idx >= 0) _cache[idx] = deck;
            else
            {
                if (IsFull) { onError?.Invoke("deck_limit"); return; }
                _cache.Add(deck);
            }

            Push(onSuccess, onError);
        }

        public static void Delete(string deckName,
            Action onSuccess = null, Action<string> onError = null)
        {
            _cache.RemoveAll(d => d.Name == deckName);
            Push(onSuccess, onError);
        }

        // ── Синхронизация с фактическим владением ────────────────────────────────

        /// <summary>
        /// Является ли карта командиром хотя бы одной колоды. Такие карты нельзя распылять/продавать:
        /// формат сохранённой колоды ТРЕБУЕТ командира — без него колода невалидна.
        /// </summary>
        public static bool IsCommanderOfAnyDeck(string expansionId, int cardId)
        {
            EnsureLoaded();
            foreach (var d in _cache)
                if (d != null && d.Commander.CardId == cardId && d.Commander.ExpansionId == expansionId)
                    return true;
            return false;
        }

        /// <summary>
        /// Сколько копий карты держит САМАЯ «жадная» колода. «Порвать все дубли» оставляет минимум столько
        /// (и хотя бы 1), иначе распыление сломало бы валидную колоду. Играют одной колодой за раз, поэтому
        /// берём максимум по колодам, а не сумму.
        /// </summary>
        public static int MaxCopiesInAnyDeck(string expansionId, int cardId)
        {
            EnsureLoaded();
            int max = 0;
            foreach (var d in _cache)
            {
                if (d == null) continue;
                // Сайдборд считаем НАРАВНЕ с колодой: карта, лежащая только в отложенных, — такая же
                // занятая копия. Без этого её можно было бы распылить/продать, и колода уходила бы в бой
                // с пустым слотом Сказочника.
                int inDeck = CountIn(d.Cards, expansionId, cardId) + CountIn(d.Sideboard, expansionId, cardId);
                if (inDeck > max) max = inDeck;
            }
            return max;

            static int CountIn(List<OwnedCardData> list, string expansionId, int cardId)
            {
                if (list == null) return 0;
                int n = 0;
                foreach (var c in list)
                    if (c.CardId == cardId && c.ExpansionId == expansionId) n += c.Count;
                return n;
            }
        }

        /// <summary>
        /// Привести колоды к ФАКТИЧЕСКОМУ владению: копий стало меньше (распыление, продажа на аукционе) →
        /// обрезаем количество в колодах, а записи с нулём убираем. Иначе колода держит «фантомные» карты
        /// и уходит в бой с тем, чего у игрока нет. Командира не трогаем — его распыление запрещено.
        /// Сохраняет (в т.ч. в облако) ТОЛЬКО если что-то реально изменилось.
        /// </summary>
        public static void PruneAgainstLibrary(Action onDone = null)
        {
            EnsureLoaded();
            bool changed = false;

            foreach (var deck in _cache)
            {
                if (deck == null) continue;
                // Сайдборд подрезаем ТАК ЖЕ, как колоду: иначе отложенная карта, которой у игрока уже нет,
                // оставалась бы «фантомом» и уезжала в бой.
                changed |= PruneList(deck.Cards);
                changed |= PruneList(deck.Sideboard);
            }

            static bool PruneList(List<OwnedCardData> list)
            {
                if (list == null) return false;
                bool touched = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var c = list[i];
                    int owned = PlayerLibrary.TryGet(c.ExpansionId, c.CardId, out var entry) ? entry.OwnedCount : 0;
                    if (c.Count <= owned) continue;

                    touched = true;
                    if (owned <= 0) list.RemoveAt(i);
                    else { c.Count = owned; list[i] = c; }   // OwnedCardData — struct, кладём обратно
                }
                return touched;
            }

            if (!changed) { onDone?.Invoke(); return; }

            Debug.Log("[DeckStorage] Колоды приведены к фактическому владению — сохраняю.");
            Push(onDone, err =>
            {
                Debug.LogWarning($"[DeckStorage] Не удалось сохранить подчищенные колоды: {err}");
                onDone?.Invoke();
            });
        }

        // ── Запись: локальное зеркало + облако ───────────────────────────────────

        /// <summary>
        /// Пишем ВСЕГДА локально, затем пробуем в облако. Отсутствие логина (дев без PlayFab / SkipLogin)
        /// — НЕ ошибка сохранения: колода уже сохранена локально, UI должен вести себя как при успехе,
        /// иначе редактор колод нечем тестировать до настройки PlayFab.
        /// </summary>
        static void Push(Action onSuccess, Action<string> onError)
        {
            EnsureActiveValid();   // сохранили/удалили → активная могла «уехать»
            SaveLocal();

            PlayFabService.SaveDecks(_cache,
                onSuccess: () => onSuccess?.Invoke(),
                onError: err =>
                {
                    if (err == "not_logged_in")
                    {
                        Debug.LogWarning("[DeckStorage] PlayFab не настроен/нет логина — колоды сохранены только локально.");
                        onSuccess?.Invoke();
                        return;
                    }
                    onError?.Invoke(err);   // настоящая ошибка сети/сервера — сообщаем игроку
                });
        }

        // ── Локальное зеркало (коды колод в PlayerPrefs) ─────────────────────────

        static void SaveLocal()
        {
            var sb = new StringBuilder();
            int written = 0, skipped = 0;

            foreach (var deck in _cache)
            {
                string code = DeckCode.Encode(deck);
                if (string.IsNullOrEmpty(code))
                {
                    // Колода без командира не кодируется — она и в бой не годится, но молча терять её нельзя:
                    // иначе «сохранил → перезапустил → пропала» выглядит как баг хранилища.
                    skipped++;
                    Debug.LogWarning($"[DeckStorage] Колода '{deck?.Name}' не закодирована (нет командира?) — в зеркало не попадёт.");
                    continue;
                }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(code);
                written++;
            }

            PlayerPrefs.SetString(LocalKey, sb.ToString());
            PlayerPrefs.Save();
            Debug.Log($"[DeckStorage] Локальное зеркало обновлено: записано {written}, пропущено {skipped}.");
        }

        static List<SavedDeckData> LoadLocal()
        {
            var result = new List<SavedDeckData>();

            string raw = PlayerPrefs.GetString(LocalKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
            {
                Debug.Log("[DeckStorage] Локальное зеркало пустое (колоды ещё ни разу не сохранялись на этом устройстве).");
                return result;
            }

            foreach (var code in raw.Split('\n'))
            {
                if (DeckCode.TryDecode(code, out var deck)) result.Add(deck);
                else Debug.LogWarning("[DeckStorage] Битый код колоды в локальном зеркале — пропускаю.");
            }
            return result;
        }

        public static IReadOnlyList<SavedDeckData> GetCached()
        {
            EnsureLoaded();
            return _cache;
        }

        /// <summary>
        /// Записывает колоду прямо в кеш без обращения к облаку.
        /// Используется для тестовых колод, собранных в InitState.
        /// </summary>
        public static void SetTesting(SavedDeckData deck)
        {
            _cache.Clear();
            if (deck != null)
                _cache.Add(deck);
        }
    }
}
