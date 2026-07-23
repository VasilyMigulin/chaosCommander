using System;
using AwesomeUI.Core.Slot;
using Game.Core.DeckBuilder;
using Game.Core.Model.Card;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.DeckBuilder
{
    /// <summary>
    /// Слот собранной колоды в DeckViewPanel: название + карта командира + кнопки «удалить» и
    /// «скопировать код». Клик по самому слоту → открыть колоду в редакторе (DeckBuildPanel).
    ///
    /// Префаб:
    ///   корень      — Button, у SourceSlot выставить IsButton = true, EnableIcon = false;
    ///   _commanderView — префаб InspectCardView (на нём StaticCardView), сюда рендерится командир;
    ///   _nameText   — название колоды, _countText — «21 / 21»;
    ///   _deleteBtn  — крестик; _copyCodeBtn (опц.) — копирует код колоды в буфер обмена;
    ///   _brokenBadge (опц.) — колода неполная или в ней карты, которых нет в конфиге/библиотеке.
    /// </summary>
    public class DeckSlotView : SourceSlot
    {
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _countText;
        [SerializeField] private StaticCardView _commanderView;
        [SerializeField] private Button _deleteBtn;
        [SerializeField] private Button _copyCodeBtn;
        [SerializeField] private GameObject _brokenBadge;

        [Header("Выбор колоды для боя")]
        [Tooltip("«Играть этой» — делает колоду активной. Клик по самому слоту открывает редактор.")]
        [SerializeField] private Button _selectBtn;
        [Tooltip("Метка «активная» — эта колода пойдёт в бой (PvP и PvE).")]
        [SerializeField] private GameObject _activeBadge;

        SavedDeckData _deck;
        Action<SavedDeckData> _onOpen;
        Action<SavedDeckData> _onDelete;
        Action<SavedDeckData> _onSelect;

        public SavedDeckData Deck => _deck;

        public override AwesomeUI.Core.Slot.SourceSlot Init()
        {
            base.Init();
            // Молчаливая пустая ссылка тут особенно коварна: бадж останется включённым таким, каким его
            // нарисовали в префабе, — и будет гореть на ВСЕХ слотах, будто выбраны все колоды сразу.
            if (_selectBtn == null || _activeBadge == null)
                Debug.LogWarning($"[DeckSlotView] '{name}': не назначены _selectBtn / _activeBadge — " +
                                 "выбор колоды не работает, метка «активная» не переключается.", this);
            return this;
        }

        bool _broken;

        /// <param name="commander">Модель командира (резолвит панель через CardConfig). null → карта не найдена.</param>
        /// <param name="totalCards">Карт в колоде вместе с командиром — для «21 / 21».</param>
        /// <param name="maxCards">Полный размер колоды.</param>
        /// <param name="isBroken">Колода не готова к бою: неполная ИЛИ есть карты, которых у игрока нет.
        /// Считает панель (она знает библиотеку) — слот сам владение не проверяет.</param>
        /// <param name="isActive">Этой колодой игрок идёт в бой (DeckStorage.ActiveDeckName).</param>
        public void SetData(SavedDeckData deck, CardModel commander, int totalCards, int maxCards, bool isBroken, bool isActive,
                            Action<SavedDeckData> onOpen, Action<SavedDeckData> onDelete, Action<SavedDeckData> onSelect)
        {
            _broken = isBroken;
            _deck = deck;
            _onOpen = onOpen;
            _onDelete = onDelete;
            _onSelect = onSelect;

            if (_deleteBtn != null)
            {
                _deleteBtn.onClick.RemoveListener(OnDeleteClicked);
                _deleteBtn.onClick.AddListener(OnDeleteClicked);
            }
            if (_copyCodeBtn != null)
            {
                _copyCodeBtn.onClick.RemoveListener(OnCopyCodeClicked);
                _copyCodeBtn.onClick.AddListener(OnCopyCodeClicked);
            }
            if (_selectBtn != null)
            {
                _selectBtn.onClick.RemoveListener(OnSelectClicked);
                _selectBtn.onClick.AddListener(OnSelectClicked);
                _selectBtn.interactable = !isActive;   // активную выбирать незачем
            }
            if (_activeBadge != null)
            {
                _activeBadge.SetActive(isActive);
                // Бейдж — ТОЛЬКО визуал: не должен перехватывать клики, иначе перекрывает кнопку удаления
                // под собой (симптом: у активной колоды «удалить» не нажимается). Выбор активной и так
                // заблокирован через _selectBtn.interactable=false — raycast баджу не нужен.
                var cg = _activeBadge.GetComponent<CanvasGroup>();
                if (cg == null) cg = _activeBadge.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }

            if (_commanderView != null)
            {
                _commanderView.gameObject.SetActive(commander != null);
                if (commander != null) _commanderView.SetModel(commander, isCommander: true);
            }

            if (_countText != null) _countText.text = $"{totalCards} / {maxCards}";
            if (_brokenBadge != null) _brokenBadge.SetActive(isBroken);

            UpdateView();
        }

        public override void UpdateView()
        {
            if (_nameText != null)
                _nameText.text = _deck != null && !string.IsNullOrEmpty(_deck.Name) ? _deck.Name : UIStrings.DeckDefaultName;
        }

        public override void OnClick() => _onOpen?.Invoke(_deck);

        void OnDeleteClicked() => _onDelete?.Invoke(_deck);

        void OnSelectClicked() => _onSelect?.Invoke(_deck);

        // Код колоды кладём в системный буфер — им делятся так же, как кодом из Hearthstone.
        void OnCopyCodeClicked()
        {
            string code = DeckCode.Encode(_deck);
            if (string.IsNullOrEmpty(code)) return;

            GUIUtility.systemCopyBuffer = code;
            NotifyService.Success(UIStrings.DeckCodeCopied);
        }

        public override void OnUse() { }

        public override void Unject()
        {
            _onOpen = null;
            _onDelete = null;
            _onSelect = null;
        }

        public override void Dispose()
        {
            if (_deleteBtn != null) _deleteBtn.onClick.RemoveListener(OnDeleteClicked);
            if (_copyCodeBtn != null) _copyCodeBtn.onClick.RemoveListener(OnCopyCodeClicked);
            if (_selectBtn != null) _selectBtn.onClick.RemoveListener(OnSelectClicked);
            Unject();
            base.Dispose();
        }
    }
}
