using System;
using Game.Core.Service;
using UnityEngine;

namespace AwesomeUI.Core.Card
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Единый источник правды для «одинаковых на каждой карте» спрайтов — раньше редактировался
    /// (одинаково!) в 4 массивах на КАЖДОМ из 10+ префабов-вариантов CardBaseView (бадж редкости/фон
    /// редкости/иконки стоимости). CardBaseView читает его через InterfaceConfig.Current вместо своих
    /// собственных сериализованных массивов — один раз проставить, дальше подхватывается всеми вариантами.
    /// </summary>
    [CreateAssetMenu(menuName = "AwesomeUI/Interface Config", fileName = "InterfaceConfig")]
    public sealed class InterfaceConfig : ScriptableObject
    {
        /// <summary>Текущий конфиг — простое settable-свойство, БЕЗ обращения к UIModule/UIHandler отсюда:
        /// AwesomeUI.Core уже ссылается на AwesomeUI.Feature, а тот — на AwesomeUI.Core.Card (тут живёт
        /// CardBaseView) → если Card потянется обратно к Core/Handler, получится цикл сборок (Core→Feature
        /// →Card→Core). Поэтому связь ОДНОСТОРОННЯЯ: UIHandler.Init() (в AwesomeUI.Core.Handler — ссылка
        /// ТУДА безопасна, обратного пути Card→Handler нет) сам пушит сюда значение один раз при старте
        /// через GetConfig&lt;InterfaceConfig&gt;() из своего _globalConfigs.</summary>
        public static InterfaceConfig Current { get; set; }

        [Serializable]
        public struct RarityVisual
        {
            public EnumService.Rarity Rarity;
            public Sprite Badge;
            public Sprite Background;
        }

        [Serializable]
        public struct CostIconVisual
        {
            public EnumService.ResourceType ResourceType;
            public Sprite Icon;
        }

        /// <summary>Иконка валюты по коду ("GD"/"GM"). Единый источник для магазина/рынка/аукциона/наград —
        /// чтобы не назначать спрайт валюты на каждом слоте.</summary>
        [Serializable]
        public struct CurrencyVisual
        {
            public string Code;    // "GD" / "GM"
            public Sprite Icon;
        }

        /// <summary>Иконка АЛЬТЕРНАТИВНОЙ уплаты (семейство «Бесчестный букмекер»): пока висит маркер,
        /// карты руки показывают её вместо иконки ресурса. Kind = AltCostKind (int, без ссылки на
        /// ECS-сборку): 0 = урон себе, 1 = сброс карты, 2 = жертва существа, 3 = карта из колоды.</summary>
        [Serializable]
        public struct AltCostIconVisual
        {
            [Tooltip("AltCostKind: 0 = урон себе (Букмекер), 1 = сброс карты, 2 = жертва существа, 3 = карта из колоды.")]
            public int Kind;
            public Sprite Icon;
        }

        [SerializeField] RarityVisual[] _rarities;
        [SerializeField] CostIconVisual[] _costIcons;
        [SerializeField] CurrencyVisual[] _currencies;
        [SerializeField] AltCostIconVisual[] _altCostIcons;

        public Sprite GetRarityBadge(EnumService.Rarity rarity)
        {
            if (_rarities == null) return null;
            foreach (var r in _rarities)
                if (r.Rarity == rarity) return r.Badge;
            return null;
        }

        public Sprite GetRarityBackground(EnumService.Rarity rarity)
        {
            if (_rarities == null) return null;
            foreach (var r in _rarities)
                if (r.Rarity == rarity) return r.Background;
            return null;
        }

        public Sprite GetCostIcon(EnumService.ResourceType resourceType)
        {
            if (_costIcons == null) return null;
            foreach (var c in _costIcons)
                if (c.ResourceType == resourceType) return c.Icon;
            return null;
        }

        /// <summary>Иконка альтернативной уплаты по AltCostKind (int). null — не назначена (вьюха
        /// оставит иконку ресурса как фоллбэк).</summary>
        public Sprite GetAltCostIcon(int kind)
        {
            if (_altCostIcons == null) return null;
            foreach (var a in _altCostIcons)
                if (a.Kind == kind) return a.Icon;
            return null;
        }

        /// <summary>Иконка валюты по коду ("GD" — Бабосик/банкнота, "GM" — Безделушки/кристалл).</summary>
        public Sprite GetCurrencyIcon(string code)
        {
            if (_currencies == null || string.IsNullOrEmpty(code)) return null;
            foreach (var c in _currencies)
                if (c.Code == code) return c.Icon;
            return null;
        }
    }
}
