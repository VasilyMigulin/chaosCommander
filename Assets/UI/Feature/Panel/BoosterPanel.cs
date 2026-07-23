using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using Game.Core.Backend;
using Game.Core.Configs;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Бустеры игрока: список имеющихся бустеров + открытие. Ролл карт — на сервере (OpenBooster),
    /// клиент показывает выпавшее через BoosterRevealView (веер-ревил). Наследует ComingSoonPanel (кнопка «Назад»).
    ///
    /// Визуал бустера (иконка/имя/префаб-вьюшка пачки) берётся из BoosterConfigAsset по itemId; _boosterIcon —
    /// фолбэк, если конфиг/иконка не заданы.
    ///
    /// Префаб: _boosterConfig (BoosterConfigAsset), _boostersRoot (контейнер), _boosterSlotPrefab (BoosterSlot),
    /// _boosterReveal (BoosterRevealView), _boosterIcon (фолбэк-иконка), _loadingOverlay, _emptyHint, _feedbackText.
    /// </summary>
    public class BoosterPanel : ComingSoonPanel
    {
        [Header("Config")]
        [SerializeField] private BoosterConfigAsset _boosterConfig;   // иконки/имена/префабы-вьюшки по itemId

        [Header("Boosters")]
        [SerializeField] private Transform _boostersRoot;
        [SerializeField] private BoosterSlot _boosterSlotPrefab;
        [SerializeField] private BoosterRevealView _boosterReveal;   // анимированный веер-ревил
        [SerializeField] private Sprite _boosterIcon;                // фолбэк-иконка

        [Header("State")]
        [SerializeField] private GameObject _loadingOverlay;
        [SerializeField] private GameObject _emptyHint;
        [SerializeField] private TMPro.TextMeshProUGUI _feedbackText;

        readonly List<BoosterSlot> _slots = new();
        bool _busy;

        public override void OnInject()
        {
            base.OnInject();
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            Refresh();   // грузим при ОТКРЫТИИ (после логина), а не на init
        }

        void Refresh()
        {
            ClearList();
            SetLoading(true);
            BoosterService.GetOwned(
                onSuccess: owned =>
                {
                    SetLoading(false);
                    Populate(owned);
                },
                onError: err =>
                {
                    SetLoading(false);
                    ShowFeedback(err);
                });
        }

        void Populate(Dictionary<string, int> owned)
        {
            if (_emptyHint != null) _emptyHint.SetActive(owned == null || owned.Count == 0);
            if (owned == null || _boostersRoot == null || _boosterSlotPrefab == null) return;

            foreach (var kv in owned)
            {
                if (kv.Value <= 0) continue;
                var def = _boosterConfig != null ? _boosterConfig.Get(kv.Key) : null;
                Sprite icon = def != null && def.Icon != null ? def.Icon : _boosterIcon;
                string name = def != null && !string.IsNullOrEmpty(def.DisplayName)
                    ? Game.Core.Shared.CardTextLocalization.GetText(def.DisplayName, def.DisplayName)
                    : PrettyName(kv.Key);

                var slot = Instantiate(_boosterSlotPrefab, _boostersRoot);
                slot.gameObject.SetActive(true);
                slot.Init();
                slot.SetData(kv.Key, name, kv.Value, icon, OnOpen, OnHold);   // тап = 1, удержание = окно выбора
                _slots.Add(slot);
            }
        }

        // "booster_standard" → "Standard"
        static string PrettyName(string itemId)
        {
            string s = itemId != null && itemId.StartsWith(BackendConfig.BoosterIdPrefix)
                ? itemId.Substring(BackendConfig.BoosterIdPrefix.Length) : itemId;
            if (string.IsNullOrEmpty(s)) return itemId;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        void OnOpen(string boosterItemId)
        {
            if (_busy) return;
            _busy = true;
            ShowFeedback("");
            var def = _boosterConfig != null ? _boosterConfig.Get(boosterItemId) : null;   // префаб-вьюшка пачки
            BoosterService.Open(boosterItemId,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        // Анимированный веер-ревил (пачка = префаб-вьюшка из конфига); список обновляем после «Забрать».
                        if (_boosterReveal != null) _boosterReveal.Show(resp.Reward, def != null ? def.PackViewPrefab : null, Refresh);
                        else Refresh();
                    }
                    else ShowFeedback(resp != null ? resp.Reason : "open failed");
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        // ── Мульти-открытие: удержание слота → сразу открываем максимум (без окна выбора) ────────
        // Сколько: min(5, есть в наличии). Экран открытия тот же, что у одиночного — пачка с подписью «×N».

        void OnHold(string itemId, int owned)
        {
            int count = Mathf.Min(BoosterService.MaxOpenAtOnce, owned);
            Debug.Log($"[BoosterPanel] Мульти-открытие удержанием: '{itemId}' ×{count} (в наличии {owned})");
            OpenMany(itemId, count);
        }

        void OpenMany(string itemId, int count)
        {
            if (_busy || string.IsNullOrEmpty(itemId) || count < 1) return;
            _busy = true;
            ShowFeedback("");
            var def = _boosterConfig != null ? _boosterConfig.Get(itemId) : null;
            BoosterService.Open(itemId, count,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        // Пачка с подписью «×N» (сколько реально открыто); раскладку ревил выберет сам:
                        // веер (≤ порога карт) или сетка (много карт — мульти).
                        if (_boosterReveal != null)
                            _boosterReveal.Show(resp.Reward, def != null ? def.PackViewPrefab : null, resp.Opened, Refresh);
                        else Refresh();
                    }
                    else ShowFeedback(resp != null ? resp.Reason : "open failed");
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        void SetLoading(bool on) { if (_loadingOverlay != null) _loadingOverlay.SetActive(on); }
        void ShowFeedback(string msg) { if (_feedbackText != null) _feedbackText.text = msg; }

        void ClearList()
        {
            foreach (var s in _slots) { if (s == null) continue; s.Dispose(); Destroy(s.gameObject); }
            _slots.Clear();
        }

        public override void Unject()
        {
            base.Unject();
            ClearList();
        }

        public override void OnDipose()
        {
            ClearList();
            base.OnDipose();
        }
    }
}
