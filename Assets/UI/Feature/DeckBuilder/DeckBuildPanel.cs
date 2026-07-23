using System;
using System.Collections.Generic;
using AwesomeUI.Core.Attributes;
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
    /// РЕДАКТОР колоды (просмотр собранных — в DeckViewPanel).
    ///
    /// Колода = 1 командир + 20 обычных карт (итого 21).
    /// Командир отображается первым в списке колоды (_deckContent).
    /// Клик по легендарному существу в библиотеке → выбрать как командира.
    /// Клик по остальным картам → добавить в колоду (до 20 карт).
    ///
    /// ЧТО РЕДАКТИРУЕМ, решает DeckEditContext (его ставит DeckViewPanel перед открытием):
    ///   • Edit   — существующая колода: подтягиваем её в редактор, при сохранении ЗАМЕНЯЕМ (даже если
    ///              переименовали — за это отвечает OriginalName);
    ///   • Import — колода из кода в буфере обмена: карты, которых нет у игрока, отсеиваются, о чём
    ///              панель сообщает в _feedbackText;
    ///   • New    — пустой редактор.
    /// Контекст ЗАБИРАЕТСЯ при открытии, поэтому повторный вход не подтянет прошлую колоду.
    ///
    /// Структура сцены (Inspector):
    ///   DeckBuildPanel
    ///     ├─ LibraryScrollView/Viewport/Content  ← _libraryContent
    ///     ├─ DeckScrollView/Viewport/Content     ← _deckContent
    ///     │    (первым будет командир, затем обычные карты)
    ///     ├─ NoCommanderHint                     ← _noCommanderHint
    ///     ├─ DeckNameInput                       ← _deckNameInput
    ///     ├─ CardCountText  "0/21"               ← _cardCountText
    ///     ├─ ColorIndicators                     ← _colorIndicators[]
    ///     │    (по одному GameObject на цвет, включается нужный)
    ///     ├─ SaveButton / ClearButton / ExitButton
    ///     ├─ CopyCodeButton                      ← _copyCodeButton (код колоды в буфер обмена)
    ///     └─ FeedbackText                        ← _feedbackText
    ///
    /// Префабы (Inspector):
    ///   _libraryCardPrefab  — LibraryCardView
    ///   _deckCardPrefab     — DeckCardView
    /// </summary>
    public class DeckBuildPanel : SourcePanel, IPanel
    {
        DeckBuilderService _service; 
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
        [SerializeField] Toggle             _commanderFilterToggle;

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
        [SerializeField] Button             _exitButton;
        [Tooltip("Копирует код колоды в буфер обмена — им делятся, как кодом из Hearthstone.")]
        [SerializeField] Button             _copyCodeButton;

        // ── Runtime ──────────────────────────────────────────────────────────

        DeckCardView _commanderView;

        readonly List<LibraryCardView> _libraryViews = new List<LibraryCardView>();
        readonly List<DeckCardView>    _deckViews    = new List<DeckCardView>();

        /// <summary>Имя колоды на момент открытия (пусто → новая). Нужно, чтобы переименование
        /// ЗАМЕНЯЛО колоду, а не создавало вторую.</summary>
        string _originalName;

        /// <summary>Всего карт в полной колоде: 1 командир + 20 обычных.</summary>
        const int MaxDeckSize = 21;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public override void Init(IPanelController panelController) 
        {
            _service = new DeckBuilderService();
            base.Init(panelController);
        }

        public override void OnInject()
        {
            base.OnInject();
            _saveButton?.onClick.AddListener(OnSaveClicked);
            _clearButton?.onClick.AddListener(OnClearClicked);
            _searchInput?.onValueChanged.AddListener(OnSearchChanged);
            _exitButton?.onClick.AddListener(OnExitClicked);
            _copyCodeButton?.onClick.AddListener(OnCopyCodeClicked);
            _commanderFilterToggle?.onValueChanged.AddListener(OnCommanderFilterChanged);

            PlayerLibrary.Changed += OnLibraryChanged;   // выдали карты (грант/бустер) → библиотека обновится в рантайме

            BuildLibraryViews();
            RefreshAll();
        }

        public override void OnOpen(params Action[] onComplete)
        {
            base.OnOpen(onComplete);
            BuildLibraryViews();   // библиотека могла измениться, пока панель была закрыта — пересобираем
            LoadFromContext();     // что редактируем — решил DeckViewPanel
        }

        // Карты появились в рантайме → пересобрать библиотеку, НЕ трогая колоду-в-работе (_service).
        void OnLibraryChanged()
        {
            BuildLibraryViews();
            RefreshLibrary(_searchInput != null ? _searchInput.text : "");
        }

        public override void Unject()
        {
            PlayerLibrary.Changed -= OnLibraryChanged;

            _saveButton?.onClick.RemoveListener(OnSaveClicked);
            _clearButton?.onClick.RemoveListener(OnClearClicked);
            _searchInput?.onValueChanged.RemoveListener(OnSearchChanged);
            _exitButton?.onClick.RemoveListener(OnExitClicked);
            _copyCodeButton?.onClick.RemoveListener(OnCopyCodeClicked);
            _commanderFilterToggle?.onValueChanged.RemoveListener(OnCommanderFilterChanged);

            foreach (var v in _libraryViews) v.OnAddRequested -= OnLibraryCardAdd;
            foreach (var v in _deckViews)    v.OnRemoveRequested -= OnDeckCardRemove;
            if (_commanderView != null)      _commanderView.OnRemoveRequested -= OnCommanderRemove;
        }

        // ── Загрузка колоды на редактирование ────────────────────────────────

        void LoadFromContext()
        {
            var deck = DeckEditContext.Consume(out _originalName, out bool fromCode);

            _service.ClearAll();

            if (deck == null)   // новая колода
            {
                if (_deckNameInput != null) _deckNameInput.text = string.Empty;
                RefreshAll();
                ShowFeedback(string.Empty);
                return;
            }

            int skipped = LoadDeck(deck);

            if (_deckNameInput != null) _deckNameInput.text = deck.Name ?? string.Empty;
            RefreshAll();

            if (fromCode)
                ShowFeedback(skipped > 0
                    ? string.Format(Loc("ui.deck.imported_partial", "Колода из кода собрана частично: не хватает карт — {0}"), skipped)
                    : Loc("ui.deck.imported", "Колода загружена из кода"));
            else if (skipped > 0)
                ShowFeedback(string.Format(Loc("ui.deck.missing_cards", "Часть карт не загрузилась: {0}"), skipped));
            else
                ShowFeedback(string.Empty);
        }

        /// <summary>
        /// Кладём колоду в сервис ПО ОДНОЙ КАРТЕ через TryAdd — то есть через те же правила, что и ручная
        /// сборка (владение, лимиты копий, цвет, экзотика). Для кода из буфера это единственный честный
        /// путь: чужой код может содержать карты, которых у игрока нет — они просто отсеются.
        /// Возвращает, сколько копий не удалось положить.
        /// </summary>
        int LoadDeck(SavedDeckData deck)
        {
            int totalWanted = 0;
            if (deck.Cards != null)
                foreach (var card in deck.Cards) totalWanted += card.Count;

            var commander = _cardConfig != null
                ? _cardConfig.Get(deck.Commander.ExpansionId, deck.Commander.CardId)?.CardData
                : null;

            if (commander == null || !_service.TrySetCommander(commander))
                return totalWanted;   // без командира колода не собирается вообще

            int skipped = 0;
            if (deck.Cards == null) return 0;

            foreach (var saved in deck.Cards)
            {
                var model = _cardConfig.Get(saved.ExpansionId, saved.CardId)?.CardData;
                if (model == null) { skipped += saved.Count; continue; }   // карта из чужого/старого аддона

                for (int i = 0; i < saved.Count; i++)
                {
                    if (_service.TotalCards >= MaxDeckSize - 1) { skipped++; continue; }
                    if (_service.TryAdd(model) != DeckBuilderService.AddResult.Ok) skipped++;
                }
            }
            return skipped;
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

                if (!string.IsNullOrEmpty(filter) && !MatchesSearch(entry.Model, filter))
                    visible = false;

                // Скрываем командира из библиотеки — по ИДЕНТИЧНОСТИ (expansion+id), НЕ по ссылке: после
                // реимпорта колоды командир — другой экземпляр CardModel, сравнение ссылок его пропускало
                // (баг: командира можно было добавить в колоду второй раз как легендарку).
                if (hasCommander && visible && _service.IsCommanderCard(entry.Model))
                    visible = false;

                // Скрываем карты чужого цвета (если командир выбран)
                if (hasCommander && visible && !_service.IsColorAllowed(entry.Model))
                    visible = false;

                // Фильтр «только командиры» (легендарные существа)
                if (visible && _commanderFilterToggle != null && _commanderFilterToggle.isOn
                    && !DeckBuilderService.IsValidCommander(entry.Model))
                    visible = false;

                string key = PlayerLibrary.MakeKey(entry.Model.ExpansionId, entry.Model.Id);
                _service.DeckEntries.TryGetValue(key, out var deckEntry);

                var data = new DeckCardViewData
                {
                    Model      = entry.Model,
                    Icon       = entry.Model.ArtImage,
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
                    Icon        = cmd.ArtImage,
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
                    Icon      = entry.Model.ArtImage,
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
                    entry.Indicator.SetActive(hasCmd && (_service.Commander.Element & entry.Element) != 0);
                }
            }
        }

        // ── Handlers ─────────────────────────────────────────────────────────

        void OnLibraryCardAdd(LibraryCardView view)
        {
            if (view.Model == null) return;

            // Первое легендарное существо → выбрать командиром
            if (_service.Commander == null && DeckBuilderService.IsValidCommander(view.Model))
            {
                _service.TrySetCommander(view.Model);
                if (_commanderFilterToggle != null) _commanderFilterToggle.isOn = false;
                RefreshAll();
                ShowFeedback(string.Format(Loc("ui.deck.commander_set", "Командир: {0}"), view.Model.Name));
                return;
            }

            // Проверка лимита обычных карт (20 без командира)
            if (_service.TotalCards >= MaxDeckSize - 1)
            {
                ShowFeedback(Loc("ui.deck.full", "Колода заполнена (20 карт + командир)"));
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
            _service.ClearAll();
            RefreshAll();
            ShowFeedback(Loc("ui.deck.commander_removed", "Командир убран из колоды"));
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

        void OnCommanderFilterChanged(bool value)
        {
            RefreshLibrary(_searchInput != null ? _searchInput.text : "");
        }

        void OnSaveClicked()
        {
            if (_service.Commander == null)
            {
                ShowFeedback(Loc("ui.deck.no_commander", "Сначала выберите командира"));
                return;
            }

            string name = CurrentName();
            bool isNew = string.IsNullOrEmpty(_originalName);

            // Новая колода под уже занятым именем молча затёрла бы чужую (SaveOrReplace ищет по имени).
            if (isNew && DeckStorage.Exists(name))
            {
                ShowFeedback(string.Format(Loc("ui.deck.name_taken", "Колода «{0}» уже есть — задайте другое имя"), name));
                return;
            }
            if (isNew && DeckStorage.IsFull)
            {
                ShowFeedback(string.Format(Loc("ui.deck.limit", "Больше {0} колод держать нельзя — удалите одну"), DeckStorage.MaxDecks));
                return;
            }

            var data = _service.Export(name);
            DeckStorage.SaveOrReplace(data, _originalName,
                onSuccess: () =>
                {
                    _originalName = name;   // сохранились → дальнейшие сохранения заменяют ЭТУ колоду
                    ShowFeedback(string.Format(Loc("ui.deck.saved", "Колода «{0}» сохранена"), name));
                    NavigateToMainMenu();
                },
                onError: err => ShowFeedback(string.Format(Loc("ui.deck.save_error", "Ошибка сохранения: {0}"), err)));
        }

        /// <summary>Код текущей колоды в буфер обмена — поделиться, как кодом из Hearthstone.</summary>
        void OnCopyCodeClicked()
        {
            if (_service.Commander == null)
            {
                ShowFeedback(Loc("ui.deck.no_commander", "Сначала выберите командира"));
                return;
            }

            string code = DeckCode.Encode(_service.Export(CurrentName()));
            if (string.IsNullOrEmpty(code)) return;

            GUIUtility.systemCopyBuffer = code;
            ShowFeedback(Loc("ui.deck.code_copied", "Код колоды скопирован"));
        }

        string CurrentName()
            => _deckNameInput != null && !string.IsNullOrWhiteSpace(_deckNameInput.text)
                ? _deckNameInput.text.Trim()
                : Loc("ui.deck.default_name", "Колода");

        void OnExitClicked()
        {
            NavigateToMainMenu();
        }

        void NavigateToMainMenu()
        {
            _panelController?.Back();
        }

        void OnClearClicked()
        {
            _service.ClearAll();
            RefreshAll();
            ShowFeedback(Loc("ui.deck.cleared", "Колода очищена"));
        }

        void ShowFeedback(string message)
        {
            if (_feedbackText != null) _feedbackText.text = message;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        // Локализация UI-строк: ключ + русский фоллбэк (русский показывается, пока нет перевода).
        static string Loc(string key, string ru) => Game.Core.Shared.CardTextLocalization.GetText(key, ru);

        /// <summary>
        /// Поиск по названию: матчим и авторское RU-имя (Model.Name), и локализованное имя текущего языка
        /// (в англ. UI — английское). Так поиск работает и по русским, и по английским названиям.
        /// (Кросс-язык — искать EN, пока UI на RU — здесь НЕ покрыт: шлюз локализации отдаёт только
        /// текущий язык. Если нужно — заведём отдельный доступ к таблице по коду локали.)
        /// </summary>
        static bool MatchesSearch(CardModel model, string filter)
        {
            filter = filter.Trim().ToLowerInvariant();
            if (filter.Length == 0) return true;
            if (model == null) return false;

            if (ContainsCI(model.Name, filter)) return true;

            string nameKey   = CardTextLocalization.NameKey(model.ExpansionId, model.Id);
            string localized = CardTextLocalization.GetText(nameKey, model.Name);
            return ContainsCI(localized, filter);
        }

        static bool ContainsCI(string source, string filterLower)
            => !string.IsNullOrEmpty(source) && source.ToLowerInvariant().Contains(filterLower);

        static string ResultMessage(DeckBuilderService.AddResult result)
        {
            switch (result)
            {
                case DeckBuilderService.AddResult.NoCommander:        return Loc("ui.deck.no_commander", "Сначала выберите командира");
                case DeckBuilderService.AddResult.WrongColor:         return Loc("ui.deck.wrong_color", "Карта не совпадает по цвету с командиром");
                case DeckBuilderService.AddResult.ExoticLimitReached: return Loc("ui.deck.exotic_limit", "В колоде уже есть экзотическая карта");
                case DeckBuilderService.AddResult.CopyLimitReached:   return Loc("ui.deck.copy_limit", "Достигнут лимит копий для этой карты");
                case DeckBuilderService.AddResult.NotEnoughCopies:    return Loc("ui.deck.not_enough_copies", "У вас нет достаточно копий этой карты");
                case DeckBuilderService.AddResult.IsCommanderCard:    return Loc("ui.deck.is_commander", "Это карта командира — уже в колоде");
                default:                                               return "";
            }
        }
    }
}
