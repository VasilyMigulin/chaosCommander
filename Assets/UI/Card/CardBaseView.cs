using System;
using AwesomeUI.Core.Slot;
using DG.Tweening;
using Game.Core.Service;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AwesomeUI.Core.Card
{
    /// <summary>
    /// Базовый компонент карты. Содержит все общие UI-поля:
    ///   - Бэкграунд редкости
    ///   - Название, тип, описание
    ///   - Стоимость (значение + иконки Gold/Mana/Health)
    ///   - Индикаторы элемента (массив, включается нужный)
    ///   - Стат-блок существа (Attack / Health / Speed) — скрывается для заклинаний и чармов
    ///
    /// Наследники вызывают ApplyVisualData(CardVisualData) чтобы заполнить все поля.
    /// </summary>
    public abstract class CardBaseView : SourceSlot, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        // ── Редкость ──────────────────────────────────────────────────────────
        // Спрайт баджа/фона берётся из InterfaceConfig (единый на все карты — см. InterfaceConfig.Current),
        // а не из локального массива на каждом префабе (было 5 записей, продублированных в 10+ префабах).

        [Header("Rarity")]
        [Tooltip("Назначить вручную — Image, на который ставится спрайт баджа редкости (сам спрайт берётся из InterfaceConfig по Rarity). Автопоиском НЕ обрабатывается.")]
        [SerializeField] Image _rarityBadge;

        // ── Бэкграунд карты ───────────────────────────────────────────────────

        [Header("Background")]
        [Tooltip("Назначить вручную — Image, на который ставится спрайт фона (сам спрайт берётся из InterfaceConfig по Rarity). Автопоиском НЕ обрабатывается.")]
        [SerializeField] Image                _cardBackground;
        [SerializeField] CardHighlightEffect _highlight;

        // ── Арт карты ────────────────────────────────────────────────────────

        [Header("Art")]
        [SerializeField] Image _artImage;

        // ── Основная информация ───────────────────────────────────────────────

        [Header("Card Info")]
        [SerializeField] TextMeshProUGUI _nameText;
        [SerializeField] TextMeshProUGUI _typeText;
        [SerializeField] TextMeshProUGUI _descriptionText;

        // ── Стоимость ─────────────────────────────────────────────────────────

        [Header("Cost")]
        [SerializeField] TextMeshProUGUI _costText;
        [Tooltip("Если не назначено вручную — ищется автоматически по дочернему объекту с именем \"CostIcon\".")]
        [SerializeField] Image           _costIcon;

        // ── Элемент ───────────────────────────────────────────────────────────
        // Индикаторы больше НЕ проставляются руками — авто-поиск по именам enum EnumService.Element
        // (Red/Blue/Green/Yellow/White/Black) среди дочерних объектов, см. AutoResolveReferences.

        ElementIndicatorEntry[] _elementIndicators;

        struct ElementIndicatorEntry
        {
            public EnumService.Element Element;
            public GameObject          Indicator;
        }

        // ── Стат-блок существа ────────────────────────────────────────────────

        [Header("Creature Stats")]
        [SerializeField] GameObject      _creatureStatsRoot;
        [SerializeField] TextMeshProUGUI _attackText;
        [SerializeField] TextMeshProUGUI _healthText;
        [SerializeField] TextMeshProUGUI _speedText;

        // ── Живые статы: цвет относительно БАЗЫ + панч при изменении ─────────
        // База (печатные значения) захватывается в ApplyVisualData из CardVisualData; живые значения
        // прилетают в SetCostAmount (CardCostChangedEvent) и SetLiveStats (HandCardStatsChangedUIEvent).
        [Header("Stat change feedback")]
        [Tooltip("Цвет стата, ставшего ЛУЧШЕ базы (дешевле кост / выше атака / выше макс. HP).")]
        [SerializeField] Color _buffColor   = new Color(0.35f, 1f, 0.35f);
        [Tooltip("Цвет стата, ставшего ХУЖЕ базы (дороже кост / ниже атака) и HP при полученном уроне.")]
        [SerializeField] Color _debuffColor = new Color(1f, 0.35f, 0.35f);
        [SerializeField] float _statPunchScale    = 0.35f;
        [SerializeField] float _statPunchDuration = 0.3f;

        int _baseCost, _baseAttack, _baseMaxHealth;          // печатные значения (из CardVisualData)
        int _shownCost, _shownAttack, _shownHealth;          // что сейчас на тексте (для панча по изменению)
        Color _defaultCostColor, _defaultAttackColor, _defaultHealthColor;
        bool _defaultColorsCaptured;

        void CaptureDefaultColors()
        {
            if (_defaultColorsCaptured) return;
            _defaultColorsCaptured = true;
            if (_costText   != null) _defaultCostColor   = _costText.color;
            if (_attackText != null) _defaultAttackColor = _attackText.color;
            if (_healthText != null) _defaultHealthColor = _healthText.color;
        }

        void PunchStat(TextMeshProUGUI t)
        {
            if (t == null) return;
            t.transform.DOKill(true);
            t.transform.localScale = Vector3.one;
            t.transform.DOPunchScale(Vector3.one * _statPunchScale, _statPunchDuration, vibrato: 1, elasticity: 0.5f);
        }

        /// <summary>Живые статы существа-карты (рука): текст + цвет относительно базы + панч при изменении.
        /// Правила: атака выше базы → зелёная, ниже → красная; HP — при уроне (current &lt; max) показываем
        /// ТЕКУЩЕЕ красным, без урона — max (зелёный, если выше базы). punch=false — первичная отрисовка
        /// (карта только пришла в руку уже с бафами) — цветим, но не дёргаем.</summary>
        public void SetLiveStats(int attack, int maxHealth, int currentHealth, bool punch = true)
        {
            CaptureDefaultColors();

            if (_attackText != null)
            {
                _attackText.text  = attack.ToString();
                _attackText.color = attack > _baseAttack ? _buffColor
                                  : attack < _baseAttack ? _debuffColor : _defaultAttackColor;
                if (punch && attack != _shownAttack) PunchStat(_attackText);
                _shownAttack = attack;
            }

            if (_healthText != null)
            {
                bool damaged = currentHealth < maxHealth;
                int shown = damaged ? currentHealth : maxHealth;
                _healthText.text  = shown.ToString();
                _healthText.color = damaged                  ? _debuffColor
                                  : maxHealth > _baseMaxHealth ? _buffColor : _defaultHealthColor;
                if (punch && shown != _shownHealth) PunchStat(_healthText);
                _shownHealth = shown;
            }
        }

        // ── Авто-поиск ссылок ───────────────────────────────────────────────────
        // ТОЛЬКО для того, что реально удобно/безопасно искать по имени (индикаторы элемента — 6 объектов,
        // муторно проставлять руками; иконка стоимости — один тип на карту). RarityBadge/CardBackground —
        // ВСЕГДА руками (SerializeField) — их не трогаем автопоиском: карта-источник — единственный Image на
        // карте, и он должен указываться явно, не угадываться по имени (Unity вызывает Awake на каждом уровне
        // иерархии независимо — не через virtual-диспетчеризацию, поэтому подклассы со своим Awake() это не ломает).

        protected virtual void Awake() => AutoResolveReferences();

        void AutoResolveReferences()
        {
            if (_costIcon == null) _costIcon = FindChildComponent<Image>("CostIcon");

            if (_elementIndicators == null || _elementIndicators.Length == 0)
                _elementIndicators = AutoFindElementIndicators();
        }

        ElementIndicatorEntry[] AutoFindElementIndicators()
        {
            var values = (EnumService.Element[])Enum.GetValues(typeof(EnumService.Element));
            var list = new System.Collections.Generic.List<ElementIndicatorEntry>(values.Length);
            foreach (var value in values)
            {
                var child = FindChildTransform(value.ToString());
                if (child != null)
                    list.Add(new ElementIndicatorEntry { Element = value, Indicator = child.gameObject });
            }
            return list.ToArray();
        }

        /// <summary>Рекурсивный поиск потомка по точному имени объекта (любая глубина вложенности).</summary>
        Transform FindChildTransform(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == name) return t;
            return null;
        }

        T FindChildComponent<T>(string name) where T : Component
        {
            var t = FindChildTransform(name);
            return t != null ? t.GetComponent<T>() : null;
        }

        // ── API ───────────────────────────────────────────────────────────────

        /// <summary>Публичный рендер полной карты из CardVisualData (для дисплейных вьюшек вне слот-контекста,
        /// напр. выкат «оппонент разыграл карту»). Внутри — тот же ApplyVisualData.</summary>
        public void SetVisual(in CardVisualData data) => ApplyVisualData(data);

        protected void ApplyVisualData(in CardVisualData data)
        {
            // Страховка от гонки с Awake(): некоторые вьюшки (напр. библиотека) заполняют карту данными
            // СРАЗУ после Instantiate, ПОКА объект ещё неактивен — Unity в этом случае откладывает Awake()
            // до реальной активации, и авто-найденные ссылки (costIcon/индикаторы элемента) на момент вызова
            // ApplyVisualData ещё пустые. Вызов идемпотентен (внутри — проверки на null/пустой массив).
            AutoResolveReferences();

            ApplyRarity(data.Rarity);
            ApplyBackground(data.Rarity);
            ApplyArt(data.Icon);
            ApplyInfo(data.CardName, data.CardType, data.Description);
            // База для окраски: CostAmount карт руки — уже ЭФФЕКТИВНАЯ цена (Мастер над чарами дисконтит
            // созданную карту ДО попадания в руку) — печатная база тогда едет отдельно (HasBaseCost).
            ApplyCost(data.CostType, data.CostAmount, data.HasBaseCost ? data.BaseCostAmount : data.CostAmount);
            ApplyElement(data.Element);
            ApplyCreatureStats(data.IsCreature, data.Attack, data.MaxHealth, data.Speed);
        }

        public void SetHighlight(CardHighlightEffect.HighlightType type, bool active)
        {
            if (_highlight != null)
                _highlight.SetState(type, active);
        }

        public void ResetHighlight()
        {
            if (_highlight != null)
                _highlight.ResetAll();
        }

        /// <summary>
        /// Перестроить локализованный текст карты при смене языка (CardLocalizationRefresher зовёт это
        /// на LocaleService.Changed). База — no-op; модель-driven вьюшки переопределяют и пересобирают
        /// CardVisualData из своей Model.
        /// </summary>
        public virtual void RefreshLocalization() { }

        // ── Art ──────────────────────────────────────────────────────────────

        void ApplyArt(Sprite icon)
        {
            if (_artImage == null) return;
            _artImage.sprite  = icon;
            _artImage.enabled = icon != null;
        }

        // ── Rarity ───────────────────────────────────────────────────────────

        void ApplyRarity(EnumService.Rarity rarity)
        {
            if (_rarityBadge == null) return;
            var cfg = InterfaceConfig.Current;
            if (cfg == null) return;
            _rarityBadge.sprite = cfg.GetRarityBadge(rarity);
        }

        // ── Background ───────────────────────────────────────────────────────

        void ApplyBackground(EnumService.Rarity rarity)
        {
            if (_cardBackground == null) return;
            var cfg = InterfaceConfig.Current;
            if (cfg == null) return;
            _cardBackground.sprite = cfg.GetRarityBackground(rarity);
        }

        // ── Info ─────────────────────────────────────────────────────────────

        void ApplyInfo(string cardName, EnumService.CardType cardType, string description)
        {
            if (_nameText        != null) _nameText.text        = cardName    ?? "";
            if (_typeText        != null) _typeText.text        = CardTypeLabel(cardType);
            if (_descriptionText != null) _descriptionText.text = description ?? "";
        }

        // ── Cost ─────────────────────────────────────────────────────────────

        /// <summary>Обновить ТОЛЬКО число стоимости (эффективная цена с модификаторами — Гиперинфляция,
        /// скидки). Тип/иконку не трогаем. Зовёт PlayCardView по CardCostChangedEvent. Цвет относительно
        /// БАЗЫ: дешевле → зелёный, дороже → красный; панч при изменении показанного значения.</summary>
        public void SetCostAmount(int amount)
        {
            if (_costText == null) return;
            CaptureDefaultColors();

            _costText.text  = amount.ToString();
            _costText.color = amount < _baseCost ? _buffColor
                            : amount > _baseCost ? _debuffColor : _defaultCostColor;
            if (amount != _shownCost) PunchStat(_costText);
            _shownCost = amount;
        }

        void ApplyCost(EnumService.ResourceType costType, int amount, int baseAmount)
        {
            // Полная перерисовка = новая карта в слоте: фиксируем ПЕЧАТНУЮ базу (baseAmount) и красим
            // сразу относительно неё — карта могла прийти уже с дисконтом (Мастер над чарами), тогда
            // amount < baseAmount → зелёный с первого кадра, без панча (первичная отрисовка).
            CaptureDefaultColors();
            _baseCost  = baseAmount;
            _shownCost = amount;
            if (_costText != null)
            {
                _costText.text  = amount.ToString();
                _costText.color = amount < _baseCost ? _buffColor
                                : amount > _baseCost ? _debuffColor : _defaultCostColor;
            }

            if (_costIcon == null) return;
            var cfg = InterfaceConfig.Current;
            if (cfg == null) return;
            _costIcon.sprite = cfg.GetCostIcon(costType);
        }

        // ── Element ───────────────────────────────────────────────────────────
        void ApplyElement(EnumService.Element element)
        {
            if (_elementIndicators == null) return;
            foreach (var entry in _elementIndicators)
            {
                if (entry.Indicator != null)
                    entry.Indicator.SetActive((element & entry.Element) != 0);
            }
        }

        // ── Creature Stats ────────────────────────────────────────────────────
        void ApplyCreatureStats(bool isCreature, int attack, int health, int speed)
        {
            if (_creatureStatsRoot != null)
                _creatureStatsRoot.SetActive(isCreature);

            if (!isCreature) return;

            // Полная перерисовка = новая карта: значения из CardVisualData — ПЕЧАТНАЯ база; фиксируем её
            // и сбрасываем фидбэк живых статов (цвет/панч-состояние) к дефолту.
            CaptureDefaultColors();
            _baseAttack    = attack;
            _baseMaxHealth = health;
            _shownAttack   = attack;
            _shownHealth   = health;

            if (_attackText != null) { _attackText.text = attack.ToString(); _attackText.color = _defaultAttackColor; }
            if (_healthText != null) { _healthText.text = health.ToString(); _healthText.color = _defaultHealthColor; }
            if (_speedText  != null) _speedText.text  = speed.ToString();
        }

        // ── Helpers ─────────────────────────────────────────────────────────── 
        static string CardTypeLabel(EnumService.CardType type)
            => Game.Core.Shared.CardTextLocalization.TypeLabel(type);

        // ── Hold-inspect (удержание карты → предпросмотр) ─────────────────────
        // Детект удержания живёт в базе, чтобы был у всех карт (библиотека/колода/попап).
        // Что делать по удержанию — решает наследник в OnHoldTriggered (open предпросмотр своей Model).

        [Header("Hold Inspect")]
        [SerializeField] bool  _holdInspectEnabled = true;
        [SerializeField] float _holdThreshold = 0.45f;

        bool  _pressed;
        bool  _holdFired;
        bool  _holdClickConsumed;
        float _pressTime;

        protected virtual void Update()
        {
            if (!_pressed || _holdFired || !_holdInspectEnabled) return;
            if (Time.unscaledTime - _pressTime < _holdThreshold) return;

            _holdFired = true;
            _holdClickConsumed = true;   // погасить последующий onClick (add/remove)
            OnHoldTriggered();
        }

        /// <summary>Вызывается при удержании ≥ порога. Наследник открывает предпросмотр своей карты.</summary>
        protected virtual void OnHoldTriggered() { }

        /// <summary>Вызывается, когда палец отпущен/увели ПОСЛЕ того как удержание уже сработало (см.
        /// OnHoldTriggered) — наследник закрывает предпросмотр (напр. PlayHistoryDrawerView: скрыть
        /// карточку-инспектор). Не вызывается на обычном коротком тапе (там просто onClick).</summary>
        protected virtual void OnHoldReleased() { }

        /// <summary>Наследник зовёт первым в OnClick: true → это был hold-предпросмотр, обычное действие пропустить.</summary>
        protected bool ConsumeHoldClick()
        {
            if (!_holdClickConsumed) return false;
            _holdClickConsumed = false;
            return true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _holdFired = false;
            _holdClickConsumed = false;
            _pressTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)   => HandleRelease();
        public void OnPointerExit(PointerEventData eventData) => HandleRelease();

        // Общий пресс-релиз для Up/Exit (палец может уйти с объекта, не отпуская — тот же случай, что и
        // отпускание). _holdFired гасим сразу, чтобы Up+Exit подряд на одном жесте не вызвали двойной релиз.
        void HandleRelease()
        {
            _pressed = false;
            if (!_holdFired) return;
            _holdFired = false;
            OnHoldReleased();
        }
    }
}
