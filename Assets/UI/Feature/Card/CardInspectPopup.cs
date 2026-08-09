using System.Collections;
using System.Collections.Generic;
using Game.Core.Instance.Card;
using Game.Core.Model.Card;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Оверлей предпросмотра карты: крупная карта + описание + список СВЯЗАННЫХ карт
    /// (что карта создаёт/замешивает/призывает) через VerticalLayoutGroup.
    /// Открывается удержанием карты (CardInspectBus.Requested), закрывается тапом по фону.
    ///
    /// Размещение в сцене: как последний (верхний) ребёнок панели/канваса колодостроителя, чтобы
    /// рисоваться поверх. Сам GameObject держим активным (видимость — через CanvasGroup), иначе
    /// подписка в OnEnable не сработает.
    ///
    /// Поля инспектора:
    ///   _mainCard          — StaticCardView крупной карты
    ///   _descriptionText   — (опц.) отдельное крупное описание
    ///   _relatedSection    — контейнер «заголовок + список» (прячется, если связанных нет)
    ///   _relatedContent    — Transform с VerticalLayoutGroup (сюда кладутся карточки)
    ///   _relatedCardPrefab — префаб StaticCardView для элемента списка
    ///   _backgroundButton  — полноэкранная кнопка-фон для закрытия
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class CardInspectPopup : MonoBehaviour
    {
        [Header("Main card")]
        [SerializeField] StaticCardView _mainCard;
        [SerializeField] TextMeshProUGUI _descriptionText;

        [Header("Related (VerticalLayoutGroup)")]
        [SerializeField] GameObject _relatedSection;
        [Tooltip("Заголовок секции «Связанные карты» (локализуется из кода).")]
        [SerializeField] TextMeshProUGUI _relatedTitleText;
        [SerializeField] Transform _relatedContent;
        [SerializeField] StaticCardView _relatedCardPrefab;

        [Header("Keyword hints (блоки-подсказки механик рядом с картой, как в ХС)")]
        [Tooltip("Контейнер секции подсказок — прячется целиком, если подсказок для карты нет.")]
        [SerializeField] GameObject _keywordSection;
        [Tooltip("Рут с LayoutGroup, куда инстанцируются блоки подсказок.")]
        [SerializeField] Transform _keywordContent;
        [SerializeField] KeywordHintBlockView _keywordHintPrefab;

        [Header("Close")]
        [SerializeField] Button _backgroundButton;

        [Header("Порвать (распыление — только осмотр из коллекции)")]
        [Tooltip("Контейнер кнопки «Порвать». Прячется, если осмотр не из коллекции или карты нет у игрока.")]
        [SerializeField] GameObject _dustRoot;
        [SerializeField] Button _dustButton;
        [Tooltip("Число Обрывков за распыление (по редкости) — ставит код.")]
        [SerializeField] TextMeshProUGUI _dustAmountText;
        [Tooltip("Иконка валюты Обрывки (SC) — ставит код из InterfaceConfig.")]
        [SerializeField] Image _dustIcon;
        [Tooltip("Подпись «Порвать» (опц.) — код ставит из UIStrings; можно и LocalizedText ui.card.dust.")]
        [SerializeField] TextMeshProUGUI _dustLabel;

        [Header("Порвать все дубли этой карты (опц.)")]
        [Tooltip("Кнопка «Порвать все дубли» — рвёт все копии сверх нужных для колод (мин. 1). Прячется, если дублей нет.")]
        [SerializeField] Button _dustAllButton;
        [Tooltip("Суммарные Обрывки за все дубли — ставит код.")]
        [SerializeField] TextMeshProUGUI _dustAllAmountText;
        [Tooltip("Подпись «Порвать всё лишнее» (опц.) — код ставит из UIStrings.")]
        [SerializeField] TextMeshProUGUI _dustAllLabel;

        [Header("Animation")]
        [SerializeField] float _fadeDuration = 0.15f;

        CardModel _current;   // сейчас показанная карта (для распыления)
        int _dustAllCount;    // сколько копий распылит «все дубли» (owned − нужные колодам, мин. 1)

        /// <summary>Рут лэйаута со связанными картами (токены, что карта порождает) — для внешних
        /// фич/позиционирования, чтобы не лазить в приватные поля попапа.</summary>
        public Transform RelatedContentRoot => _relatedContent;

        CanvasGroup _cg;
        Coroutine _fade;
        readonly List<StaticCardView> _related = new List<StaticCardView>();
        readonly List<KeywordHintBlockView> _keywords = new List<KeywordHintBlockView>();

        void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            SetVisible(false, instant: true);
            if (_backgroundButton != null) _backgroundButton.onClick.AddListener(Hide);
            if (_dustButton != null) _dustButton.onClick.AddListener(OnDustClicked);
            if (_dustAllButton != null) _dustAllButton.onClick.AddListener(OnDustAllClicked);
            if (_relatedTitleText != null) _relatedTitleText.text = UIStrings.RelatedCards;
        }

        void OnEnable() => CardInspectBus.Requested += Show;

        void OnDisable()
        {
            CardInspectBus.Requested -= Show;
            if (_fade != null) { StopCoroutine(_fade); _fade = null; }
        }

        public void Show(CardModel source, bool allowDust)
        {
            if (source == null) return;
            _current = source;

            _mainCard?.SetModel(source);
            if (_descriptionText != null)
                _descriptionText.text = CardVisualDataFactory.From(source).Description;

            BuildRelated(source);
            BuildKeywords(source);
            BuildDust(source, allowDust);
            SetVisible(true);
        }

        // «Порвать»: только осмотр из коллекции (allowDust) И у игрока есть копия. Число — по редкости.
        // Командира колоды рвать НЕЛЬЗЯ: сохранённая колода без командира невалидна — кнопку прячем.
        void BuildDust(CardModel source, bool allowDust)
        {
            int owned = 0;
            bool canDust = allowDust
                && Game.Core.DeckBuilder.PlayerLibrary.TryGet(source.ExpansionId, source.Id, out var entry)
                && (owned = entry.OwnedCount) > 0
                && !Game.Core.DeckBuilder.DeckStorage.IsCommanderOfAnyDeck(source.ExpansionId, source.Id);

            if (_dustRoot != null) _dustRoot.SetActive(canDust);
            if (!canDust) { if (_dustAllButton != null) _dustAllButton.gameObject.SetActive(false); return; }

            int per = Game.Core.Service.DustValues.For(source.Rarity);
            if (_dustAmountText != null) _dustAmountText.text = per.ToString();
            if (_dustIcon != null)
            {
                var icon = MetaIcon.Currency(Game.Core.Backend.BackendConfig.ScrapsCode);
                _dustIcon.sprite = icon; _dustIcon.enabled = icon != null;
            }
            if (_dustLabel != null) _dustLabel.text = UIStrings.Dust;
            if (_dustButton != null) _dustButton.interactable = true;

            // «Все дубли»: оставляем МАКСИМУМ КОПИЙ ДЛЯ КОЛОДЫ по редкости (common 4 / rare 3 / epic 2 /
            // leg 1 / exotic 1 — DeckBuilderService.MaxCopies), но не меньше, чем реально стоит в колодах.
            // Так игрок всегда может собрать карту в полный комплект, а рвётся только сверх лимита. Карты в
            // лотах аукциона в escrow и в owned не попадают — их не заденет.
            int keep = Mathf.Max(
                Game.Core.DeckBuilder.DeckBuilderService.MaxCopies(source.Rarity),
                Game.Core.DeckBuilder.DeckStorage.MaxCopiesInAnyDeck(source.ExpansionId, source.Id));
            _dustAllCount = Mathf.Max(0, owned - keep);
            // Один лишний — это не «дубли»: его рвёт обычная кнопка «Порвать». Оптовую показываем от 2+.
            bool showAll = _dustAllButton != null && _dustAllCount > 1;
            if (_dustAllButton != null) _dustAllButton.gameObject.SetActive(showAll);
            if (showAll)
            {
                if (_dustAllAmountText != null) _dustAllAmountText.text = (per * _dustAllCount).ToString();
                if (_dustAllLabel != null) _dustAllLabel.text = UIStrings.DustAll(_dustAllCount);
                _dustAllButton.interactable = true;
            }
        }

        void OnDustClicked() => DoDust(1);

        // «Порвать все дубли»: рвём owned − нужные колодам (мин. 1), посчитано в BuildDust.
        void OnDustAllClicked() { if (_dustAllCount > 0) DoDust(_dustAllCount); }

        void DoDust(int count)
        {
            if (_current == null || count < 1) return;
            string itemId = Game.Core.Backend.CardItemId.Of(_current.ExpansionId, _current.Id);
            SetDustInteractable(false);   // защита от даблклика на время запроса

            Game.Core.Backend.DustService.Dust(itemId, count,
                resp =>
                {
                    if (resp != null && resp.Success)
                    {
                        NotifyService.Reward($"+{resp.Amount} {UIStrings.CurrencyName(Game.Core.Backend.BackendConfig.ScrapsCode)}");
                        if (resp.RemainingCount <= 0) Hide();          // копий не осталось — закрываем
                        else BuildDust(_current, true);                // пересчитать суммы/видимость «все дубли»
                    }
                    else
                    {
                        SetDustInteractable(true);
                        NotifyService.Warning(resp != null ? UIStrings.BackendReason(resp.Reason) : "dust failed");
                    }
                },
                err => { SetDustInteractable(true); NotifyService.Warning(UIStrings.BackendReason(err)); });
        }

        void SetDustInteractable(bool on)
        {
            if (_dustButton != null) _dustButton.interactable = on;
            if (_dustAllButton != null) _dustAllButton.interactable = on;
        }

        public void Hide() => SetVisible(false);

        // Блоки-подсказки механик (архетипы сейчас; Защитник/Двойной удар и др. — по мере появления,
        // см. KeywordHintsResolver). Секция целиком прячется, если подсказок нет.
        void BuildKeywords(CardModel source)
        {
            foreach (var v in _keywords)
                if (v != null) Destroy(v.gameObject);
            _keywords.Clear();

            var hints = KeywordHintsResolver.Resolve(source);
            if (_keywordSection != null) _keywordSection.SetActive(hints.Count > 0);
            if (_keywordContent == null || _keywordHintPrefab == null) return;

            foreach (var hint in hints)
            {
                var view = Instantiate(_keywordHintPrefab, _keywordContent);
                view.Setup(hint.Title, hint.Description);
                _keywords.Add(view);
            }
        }

        void BuildRelated(CardModel source)
        {
            foreach (var v in _related)
                if (v != null) Destroy(v.gameObject);
            _related.Clear();

            int tierCount = source.Tiers != null ? source.Tiers.Count : 0;
            var cards = RelatedCardsResolver.Resolve(source);
            bool any = cards.Count > 0 || tierCount > 0;
            // Нет связанных и уровней — гасим секцию целиком (заголовок+список); если секция в префабе не
            // провязана, гасим хотя бы сам рут лэйаута, чтобы пустой контейнер не занимал место рядом с картой.
            if (_relatedSection != null) _relatedSection.SetActive(any);
            else if (_relatedContent != null) _relatedContent.gameObject.SetActive(any);
            if (_relatedContent == null || _relatedCardPrefab == null) return;

            // Заголовок: карта только с уровнями → «Уровни карты»; есть и связанные — стандартный.
            if (_relatedTitleText != null)
                _relatedTitleText.text = (tierCount > 0 && cards.Count == 0)
                    ? CardTextLocalization.GetText("ui.card.tiers", "Уровни карты")
                    : UIStrings.RelatedCards;

            BuildTierPreviews(source, tierCount);   // уровни ПЕРВЫМИ (это «про саму карту»), связанные ниже

            foreach (var model in cards)
            {
                var view = Instantiate(_relatedCardPrefab, _relatedContent);
                view.SetModel(model);
                _related.Add(view);
            }
        }

        // Карта-с-уровнями: каждый уровень — ПОЛНОЦЕННАЯ мини-карта в списке «связанных» (решение юзера
        // 2026-07-29: тексты уровней некуда впихнуть в описание → показываем уровнями-картами): свои
        // статы/кост/описание уровня (tier.N.desc), имя «Уровень N — от X маны/золота/трупов». Модель НЕ
        // клонируется — собираем CardVisualData напрямую (SetVisual). Model у вьюхи null → hold-инспект
        // мини-уровня безопасно молчит (OnHoldTriggered гейтится).
        void BuildTierPreviews(CardModel source, int tierCount)
        {
            if (tierCount == 0) return;

            var cardType = source.GetCardType();
            bool isCreature = cardType == Game.Core.Service.EnumService.CardType.Creature;

            for (int i = 0; i < tierCount; i++)
            {
                var t = source.Tiers[i];
                if (t == null) continue;

                string name = string.Format(CardTextLocalization.GetText("tier.preview.name", "Уровень {0}"), i + 1);
                if (i > 0)
                    name += " — " + string.Format(ThresholdFormat(source.TierSource), t.Threshold);

                string descKey = CardTextLocalization.TierDescKey(source.ExpansionId, source.Id, i);
                var visual = new CardVisualData
                {
                    CardName    = name,
                    Description = CardDescriptionFormatter.Format(descKey, source.Description, cardType, 0, null),
                    Icon        = source.ArtImage,
                    Rarity      = source.Rarity,
                    Element     = source.Element,
                    CardType    = cardType,
                    CostType    = t.PlayCost,
                    CostAmount  = t.PlayCostAmount,
                    IsCreature  = isCreature,
                    Attack      = t.Attack,
                    MaxHealth   = t.Health,
                    Speed       = t.Speed,
                };

                var view = Instantiate(_relatedCardPrefab, _relatedContent);
                view.SetVisual(visual);
                _related.Add(view);
            }
        }

        // Подпись порога уровня по источнику прокачки (TierSource).
        static string ThresholdFormat(TierSource src)
        {
            switch (src)
            {
                case TierSource.ManaMax: return CardTextLocalization.GetText("tier.preview.thr.manamax", "от {0} маны");
                case TierSource.Corpses: return CardTextLocalization.GetText("tier.preview.thr.corpses", "от {0} трупов");
                default:                 return CardTextLocalization.GetText("tier.preview.thr.goldmax", "от {0} золота");
            }
        }

        void SetVisible(bool visible, bool instant = false)
        {
            _cg.blocksRaycasts = visible;
            _cg.interactable = visible;

            // Пока попап виден — прячем HUD (ref-counted, идемпотентно). Этот попап живёт на CanvasGroup
            // (GameObject всегда активен), поэтому active-based механизмы (HidesHud/_hideForPanels) тут не
            // подходят — управляем видимостью HUD напрямую отсюда, где и рулим своей видимостью.
            if (visible) HudVisibility.Hide(this);
            else HudVisibility.Show(this);

            if (_fade != null) { StopCoroutine(_fade); _fade = null; }

            if (instant || !gameObject.activeInHierarchy)
            {
                _cg.alpha = visible ? 1f : 0f;
                return;
            }
            _fade = StartCoroutine(Fade(visible ? 1f : 0f));
        }

        IEnumerator Fade(float target)
        {
            float start = _cg.alpha;
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                _cg.alpha = Mathf.Lerp(start, target, t / _fadeDuration);
                yield return null;
            }
            _cg.alpha = target;
            _fade = null;
        }
    }
}
