using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using Game.Core.Configs;
using Game.Core.DeckBuilder;
using Game.Core.Model.Card;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.DeckBuilder
{
    /// <summary>
    /// Просмотр собранных колод (до DeckStorage.MaxDecks = 6). Слот = название + карта командира +
    /// удалить (+ скопировать код). Клик по слоту → DeckBuildPanel на редактирование.
    ///
    /// Кнопка «Добавить колоду» СНАЧАЛА смотрит в буфер обмена: если там код колоды (DeckCode) —
    /// разбираем его и открываем редактор уже наполненным (сценарий «прислали код в чате»); если кода
    /// нет — открываем пустой редактор. Проверка кода не кидает исключений на обычном тексте, так что
    /// жать кнопку безопасно с любым содержимым буфера.
    ///
    /// Префаб:
    ///   _cardConfig   — тот же CardConfig, что у редактора (по нему резолвится командир);
    ///   _slotsRoot    — контейнер (Grid/VerticalLayout), _slotPrefab — DeckSlotView;
    ///   _deckCountText «3 / 6», _emptyHint — заглушка «пока нет колод»;
    ///   _addDeckBtn   — «Добавить колоду» (внизу), _feedbackText — статус/ошибки;
    ///   _confirmDialog (опц.) — подтверждение удаления;
    ///   _buildPanelId — id панели редактора (дропдаун UI/Panel; пусто → «DeckBuildPanel»).
    /// </summary>
    public class DeckViewPanel : SourcePanel
    {
        const string DefaultBuildPanelId = "DeckBuildPanel";

        /// <summary>1 командир + 20 карт — как в DeckBuildPanel.</summary>
        public const int MaxDeckSize = 21;

        [Header("Config")]
        [SerializeField] private CardConfig _cardConfig;

        [Header("Decks")]
        [SerializeField] private Transform _slotsRoot;
        [SerializeField] private DeckSlotView _slotPrefab;
        [SerializeField] private TextMeshProUGUI _deckCountText;
        [SerializeField] private GameObject _emptyHint;

        [Header("Actions")]
        [SerializeField] private Button _addDeckBtn;
        [SerializeField] private ConfirmDialogView _confirmDialog;
        [SerializeField] private TextMeshProUGUI _feedbackText;
        [SerializeField] private GameObject _loadingOverlay;

        [Header("Navigation")]
        [Tooltip("Панель редактора колоды. Пусто → DeckBuildPanel.")]
        [RegistryCategory(RegistryCategories.Panel)]
        [SerializeField] private RegistryId _buildPanelId;

        readonly List<DeckSlotView> _slots = new List<DeckSlotView>();

        public override void OnInject()
        {
            base.OnInject();
            if (_addDeckBtn != null) _addDeckBtn.onClick.AddListener(OnAddDeckClicked);
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            Refresh();   // грузим при ОТКРЫТИИ: вернулись из редактора → список сразу свежий
        }

        // ── Список колод ─────────────────────────────────────────────────────────

        void Refresh()
        {
            SetLoading(true);
            DeckStorage.LoadAll(
                onSuccess: decks => { SetLoading(false); Rebuild(decks); },
                onError: err =>
                {
                    SetLoading(false);
                    Rebuild(new List<SavedDeckData>(DeckStorage.GetCached()));   // офлайн: показываем кеш
                    ShowFeedback(err);
                });
        }

        void Rebuild(List<SavedDeckData> decks)
        {
            ClearSlots();
            decks ??= new List<SavedDeckData>();

            if (_slotsRoot != null && _slotPrefab != null)
            {
                string active = DeckStorage.ActiveDeckName;
                foreach (var deck in decks)
                {
                    var slot = Instantiate(_slotPrefab, _slotsRoot);
                    slot.gameObject.SetActive(true);
                    slot.Init();
                    slot.SetData(deck, ResolveCommander(deck), TotalCards(deck), MaxDeckSize,
                                 isBroken: IsDeckBroken(deck),
                                 isActive: deck.Name == active,
                                 onOpen: OnDeckClicked, onDelete: OnDeleteClicked, onSelect: OnSelectClicked);
                    _slots.Add(slot);
                }
            }

            if (_deckCountText != null) _deckCountText.text = $"{decks.Count} / {DeckStorage.MaxDecks}";
            if (_emptyHint != null) _emptyHint.SetActive(decks.Count == 0);
            if (_addDeckBtn != null) _addDeckBtn.interactable = decks.Count < DeckStorage.MaxDecks;
        }

        CardModel ResolveCommander(SavedDeckData deck)
        {
            if (deck == null || _cardConfig == null) return null;
            var instance = _cardConfig.Get(deck.Commander.ExpansionId, deck.Commander.CardId);
            return instance?.CardData;
        }

        static int TotalCards(SavedDeckData deck)
        {
            if (deck == null) return 0;
            int total = 1;   // командир
            if (deck.Cards != null)
                foreach (var card in deck.Cards) total += card.Count;
            return total;
        }

        /// <summary>
        /// Колода не готова к бою: неполная (нет командира в конфиге / не 21 карта) ИЛИ у игрока НЕТ
        /// нужных карт. Владение проверяем по PlayerLibrary — CardConfig.Get находит карту всегда, поэтому
        /// без этой проверки колода с чужими/утраченными картами ошибочно горела «готова».
        /// </summary>
        bool IsDeckBroken(SavedDeckData deck)
        {
            if (deck == null) return true;
            if (ResolveCommander(deck) == null || TotalCards(deck) != MaxDeckSize) return true;
            return !OwnsAllCards(deck);
        }

        static bool OwnsAllCards(SavedDeckData deck)
        {
            // Командир
            if (!PlayerLibrary.TryGet(deck.Commander.ExpansionId, deck.Commander.CardId, out _)) return false;
            // Карты — в достаточном количестве копий
            if (deck.Cards != null)
                foreach (var card in deck.Cards)
                    if (!PlayerLibrary.TryGet(card.ExpansionId, card.CardId, out var entry) || entry.OwnedCount < card.Count)
                        return false;
            return true;
        }

        // ── Действия ─────────────────────────────────────────────────────────────

        void OnDeckClicked(SavedDeckData deck)
        {
            if (deck == null) return;
            DeckEditContext.Edit(deck);
            OpenBuildPanel();
        }

        /// <summary>
        /// Выбор колоды для боя. Именно её возьмёт InitDeckSystem (DeckStorage.GetActive) и в PvP, и в PvE
        /// (кроме сюжетных боёв, где колоду навязывает энкаунтер).
        /// </summary>
        void OnSelectClicked(SavedDeckData deck)
        {
            if (deck == null) return;

            // Сломанную колоду в бой не пускаем: неполная ИЛИ есть карты, которых у игрока нет.
            if (ResolveCommander(deck) == null || TotalCards(deck) != MaxDeckSize)
            {
                ShowFeedback(UIStrings.DeckIncomplete);
                return;
            }
            if (!OwnsAllCards(deck))
            {
                ShowFeedback(UIStrings.DeckMissingCards);
                return;
            }

            DeckStorage.SetActive(deck.Name);
            Rebuild(new List<SavedDeckData>(DeckStorage.GetCached()));   // перерисовать метки «активная»
            NotifyService.Success(string.Format(UIStrings.DeckSelected, deck.Name));
        }

        void OnDeleteClicked(SavedDeckData deck)
        {
            if (deck == null) return;

            if (_confirmDialog != null)
            {
                _confirmDialog.Show(string.Format(UIStrings.DeckDeleteConfirm, deck.Name), () => DeleteDeck(deck));
                return;
            }
            DeleteDeck(deck);
        }

        void DeleteDeck(SavedDeckData deck)
        {
            SetLoading(true);
            DeckStorage.Delete(deck.Name,
                onSuccess: () => { SetLoading(false); Refresh(); },
                onError: err => { SetLoading(false); ShowFeedback(err); });
        }

        /// <summary>
        /// «Добавить колоду»: код из буфера обмена → импорт, иначе пустой редактор.
        /// Имя импортированной колоды делаем уникальным — иначе сохранение затрёт одноимённую.
        /// </summary>
        void OnAddDeckClicked()
        {
            if (DeckStorage.IsFull)
            {
                ShowFeedback(string.Format(UIStrings.DeckLimitReached, DeckStorage.MaxDecks));
                return;
            }

            string clipboard = GUIUtility.systemCopyBuffer;
            if (DeckCode.TryDecode(clipboard, out var imported))
            {
                imported.Name = DeckStorage.MakeUniqueName(imported.Name, UIStrings.DeckDefaultName);
                DeckEditContext.Import(imported);
                ShowFeedback(string.Format(UIStrings.DeckCodeImported, imported.Name));
            }
            else
            {
                DeckEditContext.CreateNew();
                ShowFeedback(string.Empty);
            }

            OpenBuildPanel();
        }

        void OpenBuildPanel()
        {
            string id = _buildPanelId.IsEmpty ? DefaultBuildPanelId : _buildPanelId.Value;
            if (_panelController?.OpenPanel(id) == null)
                Debug.LogWarning($"[DeckViewPanel] Панель редактора '{id}' не найдена в канвасе (проверь PanelId).", this);
        }

        // ── Прочее ───────────────────────────────────────────────────────────────

        void SetLoading(bool on) { if (_loadingOverlay != null) _loadingOverlay.SetActive(on); }
        void ShowFeedback(string message) { if (_feedbackText != null) _feedbackText.text = message; }

        void ClearSlots()
        {
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                slot.Dispose();
                Destroy(slot.gameObject);
            }
            _slots.Clear();
        }

        public override void Unject()
        {
            if (_addDeckBtn != null) _addDeckBtn.onClick.RemoveListener(OnAddDeckClicked);
            ClearSlots();
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }
    }
}
