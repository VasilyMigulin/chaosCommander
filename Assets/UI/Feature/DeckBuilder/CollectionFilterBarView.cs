using System;
using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.DeckBuilder;
using Game.Core.Service;
using Game.Core.Shared;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.DeckBuilder
{
    /// <summary>
    /// Панель доп. фильтров библиотеки в DeckBuildPanel (цвет / тип карты / аддон / диапазон стоимости).
    /// Живёт рядом с уже существующими поиском и тоглом «Show commanders», видна только пока не выбран
    /// командир (DeckBuildPanel.RefreshMeta) — отдельного режима/экрана «Коллекция» НЕТ, это те же самые
    /// карты, что видны при сборке колоды, просто до выбора командира их можно дополнительно сузить.
    /// Владеет вайрингом своих контролов и состоянием <see cref="CollectionFilterService"/>; DeckBuildPanel
    /// только слушает <see cref="Changed"/> и перерисовывает библиотеку.
    ///
    /// Тогл «только командиры» сюда намеренно не входит — DeckBuildPanel переиспользует уже существующий
    /// _commanderFilterToggle.
    /// </summary>
    public class CollectionFilterBarView : MonoBehaviour
    {
        [Header("Color")]
        [SerializeField] ElementFilterToggle[] _colorToggles;

        [Header("Type")]
        [SerializeField] TMP_Dropdown _typeDropdown;

        [Header("Expansion")]
        [SerializeField] TMP_Dropdown _expansionDropdown;

        [Header("Cost 0-10")]
        [SerializeField] TMP_Dropdown _costFromDropdown;
        [SerializeField] TMP_Dropdown _costToDropdown;

        const int MaxCost = 10;

        static readonly EnumService.CardType?[] TypeOptions =
        {
            null,   // Все
            EnumService.CardType.Creature,
            EnumService.CardType.Spell,
            EnumService.CardType.Charm,
        };

        readonly List<string> _expansionIds = new List<string>();
        bool _staticOptionsBuilt;
        CardConfig _cardConfig;

        public CollectionFilterService Filter { get; } = new CollectionFilterService();

        public event Action Changed;

        void RaiseChanged() => Changed?.Invoke();

        // ── Init ─────────────────────────────────────────────────────────────

        /// <summary>Построить опции дропдаунов. Тип/стоимость — один раз (не меняются), аддон — при
        /// каждом вызове (список разблокированных аддонов может измениться между заходами в коллекцию).</summary>
        public void Init(CardConfig config)
        {
            _cardConfig = config;
            if (!_staticOptionsBuilt)
            {
                WarnIfUnassigned();
                BuildTypeDropdown();
                BuildCostDropdown(_costFromDropdown, OnCostFromChanged);
                BuildCostDropdown(_costToDropdown, OnCostToChanged);
                WireColorToggles();
                _staticOptionsBuilt = true;
            }
            BuildExpansionDropdown(config);
        }

        /// <summary>Незаполненные ссылки в инспекторе раньше молча проглатывались (null-conditional) —
        /// фильтр выглядел «просто не работает» без единой подсказки почему. Явно светим в консоль,
        /// что именно не назначено.</summary>
        void WarnIfUnassigned()
        {
            if (_colorToggles == null || _colorToggles.Length == 0)
                Debug.LogWarning("[CollectionFilterBarView] _colorToggles не назначены — цветовые кнопки не будут фильтровать.", this);
            if (_typeDropdown == null)
                Debug.LogWarning("[CollectionFilterBarView] _typeDropdown не назначен.", this);
            if (_expansionDropdown == null)
                Debug.LogWarning("[CollectionFilterBarView] _expansionDropdown не назначен.", this);
            if (_costFromDropdown == null || _costToDropdown == null)
                Debug.LogWarning("[CollectionFilterBarView] _costFromDropdown/_costToDropdown не назначены.", this);
            if (_cardConfig == null)
                Debug.LogWarning("[CollectionFilterBarView] CardConfig не передан из DeckBuildPanel (Init(null)) — дропдаун аддона будет пуст.", this);
        }

        void WireColorToggles()
        {
            if (_colorToggles == null) return;
            foreach (var toggle in _colorToggles)
                if (toggle != null) toggle.OnToggled += OnColorToggled;
        }

        void BuildTypeDropdown()
        {
            if (_typeDropdown == null) return;

            var options = new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData(Loc("ui.deck.type_all", "Все")),
                new TMP_Dropdown.OptionData(Loc("ui.deck.type_creature", "Существо")),
                new TMP_Dropdown.OptionData(Loc("ui.deck.type_spell", "Заклинание")),
                new TMP_Dropdown.OptionData(Loc("ui.deck.type_charm", "Чары")),
            };

            _typeDropdown.onValueChanged.RemoveListener(OnTypeChanged);
            _typeDropdown.ClearOptions();
            _typeDropdown.AddOptions(options);
            _typeDropdown.SetValueWithoutNotify(0);
            _typeDropdown.onValueChanged.AddListener(OnTypeChanged);
        }

        static void BuildCostDropdown(TMP_Dropdown dropdown, UnityEngine.Events.UnityAction<int> listener)
        {
            if (dropdown == null) return;

            var options = new List<TMP_Dropdown.OptionData>(MaxCost + 1);
            for (int i = 0; i <= MaxCost; i++)
                options.Add(new TMP_Dropdown.OptionData(i.ToString()));

            dropdown.onValueChanged.RemoveListener(listener);
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.onValueChanged.AddListener(listener);
        }

        void BuildExpansionDropdown(CardConfig config)
        {
            if (_expansionDropdown == null) return;

            _expansionIds.Clear();
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var exp in GetUnlockedExpansions(config))
            {
                _expansionIds.Add(exp.ExpansionId);
                options.Add(new TMP_Dropdown.OptionData(CardTextLocalization.ExpansionLabel(exp.ExpansionId)));
            }

            _expansionDropdown.onValueChanged.RemoveListener(OnExpansionChanged);
            _expansionDropdown.ClearOptions();
            _expansionDropdown.AddOptions(options);
            _expansionDropdown.onValueChanged.AddListener(OnExpansionChanged);
        }

        /// <summary>Аддоны, доступные в дропдауне: не StoryOnly, и либо не гейтятся кампанией, либо
        /// кампания уже пройдена (CampaignProgress — UI-слой, поэтому эта логика здесь, а не в
        /// CollectionFilterService). Порядок — как в CardConfig.Expansions (порядок релиза).</summary>
        static List<ExpansionConfig> GetUnlockedExpansions(CardConfig config)
        {
            var result = new List<ExpansionConfig>();
            if (config?.Expansions == null) return result;

            bool campaignCompleted = CampaignProgress.Load().Completed;
            foreach (var exp in config.Expansions)
            {
                if (exp == null || string.IsNullOrEmpty(exp.ExpansionId)) continue;
                if (exp.StoryOnly) continue;
                if (exp.RequiresCampaignCompletion && !campaignCompleted) continue;
                result.Add(exp);
            }
            return result;
        }

        /// <summary>Последний (самый «свежий» по порядку списка) разблокированный аддон — дефолт дропдауна
        /// при каждом открытии коллекции. Берём из уже построенного _expansionIds (последний элемент
        /// списка опций), чтобы не гонять GetUnlockedExpansions дважды за один Init/ResetToDefaults.</summary>
        string DefaultExpansionId()
            => _expansionIds.Count > 0 ? _expansionIds[_expansionIds.Count - 1] : null;

        /// <summary>Сброс к дефолтам при каждом открытии панели — фильтры не «залипают» между заходами
        /// (тот же принцип, что и DeckBuildPanel.ResetFilters для поиска/командиров). notify:false — тихо,
        /// без RefreshLibrary: панель и так пересоберёт библиотеку сама следом (см. DeckBuildPanel.OnOpen).</summary>
        public void ResetToDefaults(bool notify = true)
        {
            if (_colorToggles != null)
                foreach (var toggle in _colorToggles)
                    toggle?.SetOn(false, notify: false);
            Filter.ColorMask = 0;

            if (_typeDropdown != null) _typeDropdown.SetValueWithoutNotify(0);
            Filter.TypeFilter = null;

            if (_costFromDropdown != null) _costFromDropdown.SetValueWithoutNotify(0);
            if (_costToDropdown != null) _costToDropdown.SetValueWithoutNotify(MaxCost);
            Filter.CostMin = 0;
            Filter.CostMax = MaxCost;

            string defaultExpansion = DefaultExpansionId();
            int expansionIndex = Mathf.Max(0, _expansionIds.IndexOf(defaultExpansion));
            if (_expansionDropdown != null) _expansionDropdown.SetValueWithoutNotify(expansionIndex);
            Filter.ExpansionId = defaultExpansion;

            SetSecondaryFiltersInteractable(true);   // тогл «только командиры» тоже сбрасывается снаружи (DeckBuildPanel.ResetFilters)

            if (notify) RaiseChanged();
        }

        // ── Listeners ────────────────────────────────────────────────────────

        void OnColorToggled(EnumService.Element element, bool isOn)
        {
            Filter.ColorMask = isOn ? Filter.ColorMask | element : Filter.ColorMask & ~element;
            RaiseChanged();
        }

        void OnTypeChanged(int index)
        {
            Filter.TypeFilter = index >= 0 && index < TypeOptions.Length ? TypeOptions[index] : null;
            RaiseChanged();
        }

        void OnExpansionChanged(int index)
        {
            Filter.ExpansionId = index >= 0 && index < _expansionIds.Count ? _expansionIds[index] : null;
            RaiseChanged();
        }

        void OnCostFromChanged(int value)
        {
            if (_costToDropdown != null && value > _costToDropdown.value)
                _costToDropdown.SetValueWithoutNotify(value);
            Filter.CostMin = value;
            Filter.CostMax = _costToDropdown != null ? _costToDropdown.value : MaxCost;
            RaiseChanged();
        }

        void OnCostToChanged(int value)
        {
            if (_costFromDropdown != null && value < _costFromDropdown.value)
                _costFromDropdown.SetValueWithoutNotify(value);
            Filter.CostMax = value;
            Filter.CostMin = _costFromDropdown != null ? _costFromDropdown.value : 0;
            RaiseChanged();
        }

        /// <summary>Цвет/тип/стоимость игнорируются, пока активен тогл «только командиры» —
        /// визуально гасим, чтобы не выглядело так, будто они всё ещё что-то фильтруют.</summary>
        public void SetSecondaryFiltersInteractable(bool interactable)
        {
            if (_colorToggles != null)
                foreach (var toggle in _colorToggles)
                    toggle?.SetInteractable(interactable);
            if (_typeDropdown != null) _typeDropdown.interactable = interactable;
            if (_costFromDropdown != null) _costFromDropdown.interactable = interactable;
            if (_costToDropdown != null) _costToDropdown.interactable = interactable;
        }

        static string Loc(string key, string ru) => CardTextLocalization.GetText(key, ru);
    }
}
