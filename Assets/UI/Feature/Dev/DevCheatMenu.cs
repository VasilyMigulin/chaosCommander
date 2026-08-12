using System.Collections.Generic;
using Game.Core.Backend;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// DEV-меню для быстрого теста сервиса. Само-инициализируется (вешать не нужно), клавиша F2 или кнопка
    /// «DEV» (её рисует LogCopyOverlay) открывает/закрывает. Активно ТОЛЬКО в редакторе / dev-билде /
    /// под аккаунтом разработчика (серверный флаг isDev). Никаких префабов — всё на OnGUI.
    ///
    /// Пока открыто — ПОЛНОЭКРАННЫЙ НЕПРОЗРАЧНЫЙ оверлей + глушим EventSystem, чтобы игровые uGUI-кнопки
    /// под панелью НЕ нажимались вместе с дев-кнопками (IMGUI и uGUI — разные пути ввода, панель их не
    /// перехватывает сама). Контент скроллится.
    /// </summary>
    public class DevCheatMenu : MonoBehaviour
    {
        [SerializeField] private KeyCode _toggleKey = KeyCode.F2;

        // Создаём ВСЕГДА, видимость решает рантайм-гейт Enabled. Не под #if DEVELOPMENT_BUILD: иначе в обычном
        // билде объекта нет, и дев-аккаунт не откроет панель на устройстве. Безопасность — на isDev (сервер
        // пишет флаг) + сервер НЕЗАВИСИМО проверяет devEnabled() на каждом гранте.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("[DevCheatMenu]");
            DontDestroyOnLoad(go);
            go.AddComponent<DevCheatMenu>();
        }

        Vector2 _scroll;
        string _amount = "1000";
        string _boosterId = "booster_standard";
        string _cardId = "standard_50";   // id ВСЕГДА нижним регистром: standard_<cardId> (как в каталоге/CardConfig)
        string _avatarId = "avatar_prince";
        string _mmr = "";
        string _promoCode = "TEST-PROMO";   // код из Title Data promoConfig (или купон PlayFab)
        string _status = "";

        string[] _expIds;   // список экспеншенов (из CardConfig) для дропдауна «выдать коллекцию»
        int _expIndex;

        // Ссылки на текстуры/EventSystem — держим, чтобы не течь и корректно восстанавливать.
        Texture2D _bgTex, _btnTex, _btnHiTex, _fieldTex;
        EventSystem _blockedEs;

        bool Enabled => Game.Core.Service.DebugFlags.DevUiAllowed;
        static bool Open => Game.Core.Service.DebugFlags.DevOverlayOpen;

        void Update()
        {
            if (Enabled && Input.GetKeyDown(_toggleKey))
                Game.Core.Service.DebugFlags.DevOverlayOpen = !Game.Core.Service.DebugFlags.DevOverlayOpen;

            ApplyInputCapture();
        }

        // Пока панель открыта — глушим EventSystem: uGUI-кнопки игры не должны срабатывать под оверлеем.
        // IMGUI (эта панель, DEV/LOG) работает без EventSystem, так что кнопки панели остаются живыми.
        void ApplyInputCapture()
        {
            var es = EventSystem.current;
            bool block = Enabled && Open;

            if (block && es != null && es.enabled) { _blockedEs = es; es.enabled = false; }
            else if (!block && _blockedEs != null) { _blockedEs.enabled = true; _blockedEs = null; }
        }

        void OnDisable()
        {
            if (_blockedEs != null) { _blockedEs.enabled = true; _blockedEs = null; }   // не оставить игру без ввода
        }

        void OnGUI()
        {
            if (!Enabled || !Open) return;

            float s = Mathf.Max(2.2f, Screen.width / 480f);
            int fs = Mathf.RoundToInt(9 * s);
            int vpad = Mathf.RoundToInt(9 * s);
            EnsureTextures();

            // Крупные КОНТРАСТНЫЕ стили: сплошной цветной фон кнопок + белый жирный текст (дефолтный скин
            // блёклый). LogCopyOverlay использует свои копии стилей — на него не влияет; uGUI на IMGUI не завязан.
            var sk = GUI.skin;
            sk.label.fontSize = sk.button.fontSize = sk.textField.fontSize = fs;
            sk.button.fontStyle = FontStyle.Bold;
            sk.button.normal.background = _btnTex;
            sk.button.hover.background = sk.button.active.background = _btnHiTex;
            sk.button.normal.textColor = sk.button.hover.textColor = sk.button.active.textColor = Color.white;
            sk.button.padding = new RectOffset(Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s), vpad, vpad);
            sk.button.margin = new RectOffset(4, 4, 4, 4);
            sk.textField.normal.background = _fieldTex;
            sk.textField.normal.textColor = Color.white;
            sk.textField.padding = new RectOffset(Mathf.RoundToInt(6 * s), 4, vpad, vpad);
            sk.label.normal.textColor = Color.white;

            // Полноэкранный НЕПРОЗРАЧНЫЙ фон — полностью закрывает игру.
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bgTex);

            float pad = 10f;
            GUILayout.BeginArea(new Rect(pad, pad, Screen.width - pad * 2, Screen.height - pad * 2));

            // Шапка: заголовок + крупная «Закрыть» (на устройстве кнопка DEV может быть под фоном).
            GUILayout.BeginHorizontal();
            GUILayout.Label("DEV · СЕРВИС", Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕ Закрыть", GUILayout.Width(Screen.width * 0.32f)))
                Game.Core.Service.DebugFlags.DevOverlayOpen = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawContent();
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        void DrawContent()
        {
            int amount = int.TryParse(_amount, out var a) ? a : 0;

            GUILayout.Label("ВАЛЮТА", Bold);
            _amount = GUILayout.TextField(_amount);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("GD +N (сервер)")) DevService.GrantCurrency("GD", amount, _ => Ok($"GD +{amount}"), Fail);
            if (GUILayout.Button("GD +N (локально)")) { PlayerWallet.Set("GD", PlayerWallet.Gold + amount); Ok($"локально GD +{amount}"); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("GM +N (сервер)")) DevService.GrantCurrency("GM", amount, _ => Ok($"GM +{amount}"), Fail);
            if (GUILayout.Button("GM +N (локально)")) { PlayerWallet.Set("GM", PlayerWallet.Gems + amount); Ok($"локально GM +{amount}"); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SC +N (сервер)")) DevService.GrantCurrency("SC", amount, _ => Ok($"SC +{amount}"), Fail);   // Обрывки — для рынка
            if (GUILayout.Button("SC +N (локально)")) { PlayerWallet.Set("SC", PlayerWallet.Get("SC") + amount); Ok($"локально SC +{amount}"); }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Баланс: GD {PlayerWallet.Gold} · GM {PlayerWallet.Gems} · SC {PlayerWallet.Get("SC")}");

            GUILayout.Space(8);
            GUILayout.Label("КОЛЛЕКЦИЯ (выдать весь сет)", Bold);
            DrawExpansionPicker();

            GUILayout.Space(8);
            GUILayout.Label("БУСТЕРЫ", Bold);
            _boosterId = GUILayout.TextField(_boosterId);
            if (GUILayout.Button("Выдать бустер (сервер)")) DevService.GrantBooster(_boosterId, 1, _ => Ok("бустер выдан"), Fail);
            if (GUILayout.Button("Выдать + открыть (лог, без визуала)")) GrantAndOpen();
            if (GUILayout.Button("Открыть (без визуала)")) OpenBoosterNoVisual();
            if (GUILayout.Button("Открыть бустер (тест reveal)")) OpenBoosterTest();

            GUILayout.Space(8);
            GUILayout.Label("КАРТЫ", Bold);
            _cardId = GUILayout.TextField(_cardId);
            if (GUILayout.Button("Выдать карту (сервер)")) DevService.GrantCard(_cardId, 1, _ => Ok("карта выдана"), Fail);

            GUILayout.Space(8);
            GUILayout.Label("АВАТАР (косметика)", Bold);
            _avatarId = GUILayout.TextField(_avatarId);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Надеть")) { Game.Core.Service.EquippedAvatar.ItemId = _avatarId; Ok($"надет {_avatarId}"); }
            if (GUILayout.Button("Снять")) { Game.Core.Service.EquippedAvatar.ItemId = ""; Ok("аватар снят (дефолт)"); }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Сейчас надет: {(Game.Core.Service.EquippedAvatar.HasAvatar ? Game.Core.Service.EquippedAvatar.ItemId : "—")}");

            GUILayout.Space(8);
            GUILayout.Label("РЕЙТИНГ (подбор соперника)", Bold);
            if (string.IsNullOrEmpty(_mmr)) _mmr = Game.Core.Service.PlayerRating.Mmr.ToString();
            _mmr = GUILayout.TextField(_mmr);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Задать MMR (локально)") && int.TryParse(_mmr, out int mmr))
            {
                Game.Core.Service.PlayerRating.Mmr = mmr;
                Ok($"MMR = {Game.Core.Service.PlayerRating.Mmr} ({Game.Core.Service.PlayerRating.RankName})");
            }
            if (GUILayout.Button("Задать MMR (сервер)") && int.TryParse(_mmr, out int srvMmr))
                DevService.SetMmr(srvMmr, v => Ok($"серверный MMR = {v}"), Fail);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Синк MMR с сервера")) RatingService.Fetch(() => Ok($"MMR = {Game.Core.Service.PlayerRating.Mmr}"));
            GUILayout.Label($"Сейчас: {Game.Core.Service.PlayerRating.Mmr} · {Game.Core.Service.PlayerRating.RankName}");

            GUILayout.Space(8);
            GUILayout.Label("ЖУРНАЛ (репорт прогресса задач)", Bold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("play_games +1")) DailyService.ReportProgress("play_games", 1, () => { Ok("play_games +1"); RefreshJournal(); }, Fail);
            if (GUILayout.Button("win_games +1"))  DailyService.ReportProgress("win_games", 1, () => { Ok("win_games +1"); RefreshJournal(); }, Fail);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("play_cards +5")) DailyService.ReportProgress("play_cards", 5, () => { Ok("play_cards +5"); RefreshJournal(); }, Fail);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Завершить все")) DevService.CompleteTasks(() => { Ok("все задачи завершены"); RefreshJournal(); }, Fail);
            if (GUILayout.Button("Сбросить журнал")) DevService.ResetJournal(() => { Ok("журнал сброшен"); RefreshJournal(); }, Fail);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Сбросить чёрный рынок")) DevService.ResetBlackMarket(() => Ok("чёрный рынок сброшен"), Fail);

            GUILayout.Space(8);
            GUILayout.Label("ПРОМОКОД", Bold);
            _promoCode = GUILayout.TextField(_promoCode);
            if (GUILayout.Button("Активировать промокод")) RedeemPromo();
            // Сбрасывает только СВОИ кампании (promoConfig). Нативный купон PlayFab сгорает в сервисе —
            // повторно его не активировать даже здесь, для теста генерь новую пачку купонов.
            if (GUILayout.Button("Забыть активированные (свои кампании)"))
                DevService.ResetPromo(() => Ok("промокоды сброшены"), Fail);
            if (GUILayout.Button("Рассчитать аукционы (крон)"))
                AuctionService.ResolveNow(r => Ok($"рассчитано лотов: {(r != null ? r.Resolved : 0)}"), Fail);

            GUILayout.Space(8);
            GUILayout.Label("UI-ТЕСТЫ", Bold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Тост")) NotifyService.Success("Тест уведомления");
            if (GUILayout.Button("Reward-тост")) NotifyService.Reward("+100 GD");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Бейдж «Чёрный рынок» вкл/выкл"))
                NotifyState.Set(NotifyKeys.BlackMarket, !NotifyState.Has(NotifyKeys.BlackMarket));
            if (GUILayout.Button("Попап награды")) ShowRewardPopup();
            if (GUILayout.Button("Входное окно (inbox)")) ShowInbox();
            if (GUILayout.Button("Гейт версии")) ShowVersionGate();

            GUILayout.Space(8);
            if (GUILayout.Button("Обновить кошелёк с сервера"))
                EconomyService.RefreshWallet(() => Ok("кошелёк обновлён"), Fail);

            GUILayout.Space(6);
            GUILayout.Label(_status, Wrap);
            GUILayout.Space(12);
        }

        // Дропдаун сетов = сетка-выбор (IMGUI своего дропдауна не имеет). Список — из CardConfig.
        void DrawExpansionPicker()
        {
            if (_expIds == null || _expIds.Length == 0) _expIds = LoadExpansionIds();
            if (_expIds.Length == 0) { GUILayout.Label("Сеты не загружены (CardConfig пуст?)"); return; }

            _expIndex = Mathf.Clamp(_expIndex, 0, _expIds.Length - 1);
            _expIndex = GUILayout.SelectionGrid(_expIndex, _expIds, Mathf.Min(3, _expIds.Length));

            string exp = _expIds[_expIndex];
            if (GUILayout.Button($"Выдать всю коллекцию «{exp}»"))
                DevService.GrantExpansion(exp, r => Ok($"выдан сет «{exp}»"), Fail);
        }

        static string[] LoadExpansionIds()
        {
            var cfg = BackendSession.Config;
            if (cfg == null || cfg.Expansions == null) return System.Array.Empty<string>();
            var list = new List<string>();
            foreach (var e in cfg.Expansions)
                if (e != null && !string.IsNullOrEmpty(e.ExpansionId)) list.Add(e.ExpansionId);
            return list.ToArray();
        }

        void EnsureTextures()
        {
            if (_bgTex != null) return;
            _bgTex    = Solid(new Color(0.07f, 0.07f, 0.10f, 1f));    // непрозрачный тёмный фон
            _btnTex   = Solid(new Color(0.20f, 0.42f, 0.70f, 1f));    // синие кнопки
            _btnHiTex = Solid(new Color(0.30f, 0.56f, 0.85f, 1f));    // подсветка
            _fieldTex = Solid(new Color(0.16f, 0.16f, 0.20f, 1f));    // поля ввода
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        // ── Действия ──────────────────────────────────────────────────────────
        void OpenBoosterTest()
        {
            BoosterService.Open(_boosterId, r =>
            {
                var reveal = FindObjectOfType<BoosterRevealView>();
                if (reveal != null && r != null && r.Success) reveal.Show(r.Reward);
                else Ok(r != null && r.Success ? "открыт (нет BoosterRevealView)" : $"отказ: {r?.Reason}");
            }, Fail);
        }

        void OpenBoosterNoVisual()
        {
            BoosterService.Open(_boosterId,
                r => { if (r != null && r.Success) Ok($"открыт: {CardsText(r.Reward)}"); else Fail($"отказ: {r?.Reason}"); },
                Fail);
        }

        void GrantAndOpen()
        {
            DevService.GrantBooster(_boosterId, 1,
                _ => BoosterService.Open(_boosterId,
                    r => { if (r != null && r.Success) Ok($"выдан+открыт: {CardsText(r.Reward)}"); else Fail($"отказ: {r?.Reason}"); },
                    Fail),
                Fail);
        }

        static string CardsText(RewardBundle reward)
        {
            if (reward?.Cards == null || reward.Cards.Count == 0) return "нет карт";
            var sb = new System.Text.StringBuilder();
            foreach (var c in reward.Cards) { if (sb.Length > 0) sb.Append(", "); sb.Append(c.ItemId).Append(" x").Append(c.Amount); }
            return sb.ToString();
        }

        // Промокод «как из UI»: сервер решает всё сам, здесь только показываем итог и, если попап
        // награды есть в сцене, гоняем его же — чтобы проверить и выдачу, и визуал разом.
        void RedeemPromo()
        {
            PromoService.Redeem(_promoCode,
                r =>
                {
                    if (r == null || !r.Success) { Fail($"отказ: {r?.Reason}"); return; }
                    Ok($"принят: {UIStrings.RewardSummary(r.Reward)}");
                    var v = FindObjectOfType<RewardPopupView>();
                    if (v != null) v.Show(r.Reward, UIStrings.PromoRewardTitle(r.TitleKey));
                },
                Fail);
        }

        void ShowRewardPopup()
        {
            var v = FindObjectOfType<RewardPopupView>();
            if (v != null) v.Show(FakeReward(), "Тест");
            else Fail("нет RewardPopupView в сцене");
        }

        void ShowInbox()
        {
            var v = FindObjectOfType<WindowNewPopup>();
            if (v != null) v.ShowQueue(new[] { new InboxEntry { Title = "Подарок", Message = "Тестовая награда", Reward = FakeReward() } });
            else Fail("нет WindowNewPopup в сцене");
        }

        void ShowVersionGate()
        {
            var v = FindObjectOfType<VersionGateView>();
            if (v != null) v.Show("https://example.com", "Тест: обновите приложение");
            else Fail("нет VersionGateView в сцене");
        }

        RewardBundle FakeReward()
        {
            var b = new RewardBundle();
            b.Currencies.Add(new CurrencyAmount { Code = "GD", Amount = 100 });
            b.Cards.Add(new GrantedCard { ItemId = _cardId, Amount = 1 });
            return b;
        }

        static void RefreshJournal() { var p = FindObjectOfType<DailyPanel>(); if (p != null) p.Refresh(); }

        void Ok(string msg)   { _status = msg; NotifyService.Info(msg); }
        void Fail(string err) { _status = "Ошибка: " + err; NotifyService.Warning(err); }

        GUIStyle Header => new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = GUI.skin.label.fontSize + 4 };
        GUIStyle Bold   => new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
        GUIStyle Wrap   => new GUIStyle(GUI.skin.label) { wordWrap = true };
    }
}
