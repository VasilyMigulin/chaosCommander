using System;
using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using AwesomeUI.Interface;
using Game.Core.Configs;
using Game.Core.DeckBuilder;
using Game.Core.Instance.Card;
using Game.Core.Model.Card;
using Game.Core.Service;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.DeckBuilder
{
    /// <summary>
    /// Панель сборки колоды.
    ///
    /// Колода = 1 командир + 20 обычных карт (итого 21).
    /// Командир отображается первым в списке колоды (_deckContent).
    /// Клик по легендарному существу в библиотеке → выбрать как командира.
    /// Клик по остальным картам → добавить в колоду (до 20 карт).
    ///
    /// Структура сцены (Inspector):
    ///   DeckBuilderPanel
    ///     ├─ LibraryScrollView/Viewport/Content  ← _libraryContent
    ///     ├─ DeckScrollView/Viewport/Content     ← _deckContent
    ///     │    (первым будет командир, затем обычные карты)
    ///     ├─ NoCommanderHint                     ← _noCommanderHint
    ///     ├─ DeckNameInput                       ← _deckNameInput
    ///     ├─ CardCountText  "0/21"               ← _cardCountText
    ///     ├─ ColorIndicators                     ← _colorIndicators[]
    ///     │    (по одному GameObject на цвет, включается нужный)
    ///     ├─ SaveButton                          ← _saveButton
    ///     ├─ ClearButton                         ← _clearButton
    ///     └─ FeedbackText                        ← _feedbackText
    ///
    /// Префабы (Inspector):
    ///   _libraryCardPrefab  — LibraryCardView
    ///   _deckCardPrefab     — DeckCardView
    /// </summary>
    public class DeckBuilderPanel : SourcePanel, IPanel
    {
        /// <summary>Привязка элемент → готовый GameObject с иконкой цвета в префабе.</summary>
        [Serializable]
        public struct ElementColorEntry
        {
            public EnumService.Element Element;
            public GameObject          Indicator;
        }

        // ── Inspector ────────────────────────────────────────────────────────

        [Header("Service")]
        [SerializeField] CardConfig _cardConfig;

        [Header("Library")]
        [SerializeField] Transform          _libraryContent;
        [SerializeField] LibraryCardView    _libraryCardPrefab;
        [SerializeField] TMP_InputField     _searchInput;

        [Header("Deck")]
        [SerializeField] Transform          _deckContent;
        [SerializeField] DeckCardView       _deckCardPrefab;
        [SerializeField] TextMeshProUGUI    _cardCountText;
        [SerializeField] GameObject         _noCommanderHint;

        [Header("Color Indicators")]
        [SerializeField] ElementColorEntry[] _colorIndicators;

        [Header("Info")]
        [SerializeField] TMP_InputField     _deckNameInput;
        [SerializeField] TextMeshProUGUI    _feedbackText;

        [Header("Buttons")]
        [SerializeField] Button             _saveButton;
        [SerializeField] Button             _clearButton;

        // ── Runtime ──────────────────────────────────────────────────────────

        DeckBuilderService _service;

        DeckCardView _commanderView;

        readonly List<LibraryCardView> _libraryViews = new List<LibraryCardView>();
        readonly List<DeckCardView>    _deckViews    = new List<DeckCardView>();

        /// <summary>Всего карт в полной колоде: 1 командир + 20 обычных.</summary>
        const int MaxDeckSize = 21;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public override void Init(IPanelController panelController) 
        {
            base.Init(panelController);
            _service = new DeckBuilderService();
        }

        public override void OnInject()
        {
            base.OnInject();
            _saveButton?.onClick.AddListener(OnSaveClicked);
            _clearButton?.onClick.AddListener(OnClearClicked);
            _searchInput?.onValueChanged.AddListener(OnSearchChanged);

            BuildLibraryViews();
            RefreshAll();
        }

        public override void Unject()
        {
            _saveButton?.onClick.RemoveListener(OnSaveClicked);
            _clearButton?.onClick.RemoveListener(OnClearClicked);
            _searchInput?.onValueChanged.RemoveListener(OnSearchChanged);

            foreach (var v in _libraryViews) v.OnAddRequested -= OnLibraryCardAdd;
            foreach (var v in _deckViews)    v.OnRemoveRequested -= OnDeckCardRemove;
            if (_commanderView != null)      _commanderView.OnRemoveRequested -= OnCommanderRemove;
        }

        // ── Library build ────────────────────────────────────────────────────

        void BuildLibraryViews()
        {
            foreach (var v in _libraryViews) v.OnAddRequested -= OnLibraryCardAdd;
            _libraryViews.Clear();

            foreach (Transform child in _libraryContent)
                Destroy(child.gameObject);

            foreach (var entry in PlayerLibrary.Entries.Values)
            {
                var view = Instantiate(_libraryCardPrefab, _libraryContent);
                view.Init();
                view.OnAddRequested += OnLibraryCardAdd;
                _libraryViews.Add(view);
            }
        }

        // ── Refresh ──────────────────────────────────────────────────────────

        void RefreshAll()
        {
            RefreshLibrary();
            RefreshDeck();
            RefreshMeta();
        }

        void RefreshLibrary(string filter = "")
        {
            var entries = new List<CardEntry>(PlayerLibrary.Entries.Values);
            bool hasCommander = _service.Commander != null;

            int i = 0;
            foreach (var entry in entries)
            {
                bool visible = true;

                if (!string.IsNullOrEmpty(filter))
                    visible = entry.Model.Name.ToLower().Contains(filter.ToLower());

                // Скрываем карты чужого цвета (если командир выбран)
                if (hasCommander && visible && !_service.IsColorAllowed(entry.Model))
                    visible = false;

                string key = PlayerLibrary.MakeKey(entry.Model.ExpansionId, entry.Model.Id);
                _service.DeckEntries.TryGetValue(key, out var deckEntry);

                var data = new DeckCardViewData
                {
                    Model      = entry.Model,
                    Icon       = entry.Model.Icon,
                    CardName   = entry.Model.Name,
                    OwnedCount = entry.OwnedCount,
                    DeckCount  = deckEntry?.DeckCount ?? 0,
                    MaxCopies  = DeckBuilderService.MaxCopies(entry.Model.Rarity),
                    Visual     = CardVisualDataFactory.From(entry.Model),
                };

                if (i < _libraryViews.Count)
                {
                    _libraryViews[i].SetData(data);
                    _libraryViews[i].gameObject.SetActive(visible);
                }
                i++;
            }
        }

        void RefreshDeck()
        {
            // Очистить старые вьюхи
            foreach (var v in _deckViews) v.OnRemoveRequested -= OnDeckCardRemove;
            _deckViews.Clear();
            if (_commanderView != null)
            {
                _commanderView.OnRemoveRequested -= OnCommanderRemove;
                Destroy(_commanderView.gameObject);
                _commanderView = null;
            }
            foreach (Transform child in _deckContent) Destroy(child.gameObject);

            // Командир — первым в списке
            if (_service.Commander != null)
            {
                var cmd = _service.Commander;
                PlayerLibrary.TryGet(cmd.ExpansionId, cmd.Id, out var ownedCmd);
                _commanderView = Instantiate(_deckCardPrefab, _deckContent);
                _commanderView.Init();
                _commanderView.SetData(new DeckCardViewData
                {
                    Model       = cmd,
                    Icon        = cmd.Icon,
                    CardName    = cmd.Name,
                    OwnedCount  = ownedCmd?.OwnedCount ?? 1,
                    DeckCount   = 1,
                    MaxCopies   = 1,
                    IsCommander = true,
                    Visual      = CardVisualDataFactory.From(cmd, isCommander: true),
                });
                _commanderView.OnRemoveRequested += OnCommanderRemove;
            }

            // Обычные карты
            foreach (var entry in _service.DeckEntries.Values)
            {
                if (entry.DeckCount <= 0) continue;

                var view = Instantiate(_deckCardPrefab, _deckContent);
                view.Init();
                view.OnRemoveRequested += OnDeckCardRemove;
                view.SetData(new DeckCardViewData
                {
                    Model     = entry.Model,
                    Icon      = entry.Model.Icon,
                    CardName  = entry.Model.Name,
                    DeckCount = entry.DeckCount,
                    MaxCopies = DeckBuilderService.MaxCopies(entry.Model.Rarity),
                    Visual    = CardVisualDataFactory.From(entry.Model),
                });
                _deckViews.Add(view);
            }

            // Счётчик: командир (если есть) + обычные карты
            int commanderCount = _service.Commander != null ? 1 : 0;
            int total = commanderCount + _service.TotalCards;
            if (_cardCountText != null)
                _cardCountText.text = $"{total} / {MaxDeckSize}";
        }

        void RefreshMeta()
        {
            bool hasCmd = _service.Commander != null;
            _noCommanderHint?.SetActive(!hasCmd);

            // Включить нужный индикатор цвета, остальные выключить
            if (_colorIndicators != null)
            {
                foreach (var entry in _colorIndicators)
                {
                    if (entry.Indicator == null) continue;
                    entry.Indicator.SetActive(hasCmd && entry.Element == _service.Commander.Element);
                }
            }
        }

        // ── Handlers ─────────────────────────────────────────────────────────

        void OnLibraryCardAdd(LibraryCardView view)
        {
            if (view.Model == null) return;

            // Легендарное существо без командира → выбрать командиром
            if (_service.Commander == null && DeckBuilderService.IsValidCommander(view.Model))
            {
                _service.TrySetCommander(view.Model);
                RefreshAll();
                ShowFeedback($"Командир: {view.Model.Name}");
                return;
            }

            // Уже заменить командира на другого (кликнуть на другое легендарное существо)
            if (DeckBuilderService.IsValidCommander(view.Model) && view.Model != _service.Commander)
            {
                _service.TrySetCommander(view.Model);
                RefreshAll();
                ShowFeedback($"Командир заменён: {view.Model.Name}");
                return;
            }

            // Проверка лимита обычных карт (20 без командира)
            if (_service.TotalCards >= MaxDeckSize - 1)
            {
                ShowFeedback("Колода заполнена (20 карт + командир)");
                return;
            }

            var result = _service.TryAdd(view.Model);
            if (result == DeckBuilderService.AddResult.Ok)
            {
                RefreshDeck();
                RefreshLibrary(_searchInput != null ? _searchInput.text : "");
            }
            else
            {
                ShowFeedback(ResultMessage(result));
            }
        }

        void OnCommanderRemove(DeckCardView view)
        {
            _service.RemoveCommander();
            RefreshAll();
            ShowFeedback("Командир убран из колоды");
        }

        void OnDeckCardRemove(DeckCardView view)
        {
            if (_service.TryRemove(view.Model))
            {
                RefreshDeck();
                RefreshLibrary(_searchInput != null ? _searchInput.text : "");
            }
        }

        void OnSearchChanged(string value)
        {
            RefreshLibrary(value);
        }

        void OnSaveClicked()
        {
            if (_service.Commander == null)
            {
                ShowFeedback("Сначала выберите командира");
                return;
            }

            string name = _deckNameInput != null && !string.IsNullOrEmpty(_deckNameInput.text)
                ? _deckNameInput.text
                : "Колода";

            var data = _service.Export(name);
            DeckStorage.SaveOrReplace(data,
                onSuccess: () => ShowFeedback($"Колода «{name}» сохранена"),
                onError:   err => ShowFeedback($"Ошибка сохранения: {err}"));
        }

        void OnClearClicked()
        {
            _service.ClearAll();
            RefreshAll();
            ShowFeedback("Колода очищена");
        }

        void ShowFeedback(string message)
        {
            if (_feedbackText != null) _feedbackText.text = message;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static string ResultMessage(DeckBuilderService.AddResult result)
        {
            switch (result)
            {
                case DeckBuilderService.AddResult.NoCommander:      return "Сначала выберите командира";
                case DeckBuilderService.AddResult.WrongColor:       return "Карта не совпадает по цвету с командиром";
                case DeckBuilderService.AddResult.IsCommander:      return "Легендарных существ нельзя добавлять в колоду — только как командира";
                case DeckBuilderService.AddResult.ExoticLimitReached: return "В колоде уже есть экзотическая карта";
                case DeckBuilderService.AddResult.CopyLimitReached:  return "Достигнут лимит копий для этой карты";
                case DeckBuilderService.AddResult.NotEnoughCopies:   return "У вас нет достаточно копий этой карты";
                default:                                             return "";
            }
        }
    }
}
