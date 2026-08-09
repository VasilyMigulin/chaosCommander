using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Визуальные данные карты, собранные из CardModel при инициализации entity.
    /// Используется UI-системами для отображения карты без обращения к CardConfig.
    /// </summary>
    public struct CardViewDataComponent
    {
        // ── Основная информация ───────────────────────────────────────────────
        public string                    CardName;
        public string                    Description;
        public Sprite                    ArtImage;

        // ── Тип / редкость / элемент ─────────────────────────────────────────
        public EnumService.CardType      CardType;
        public EnumService.Rarity        Rarity;
        public EnumService.Element       Element;

        // ── Стоимость ─────────────────────────────────────────────────────────
        public EnumService.ResourceType  CostType;
        public int                       CostAmount;

        // ── Существо (заполняется в CardCreatureModel.OnInit) ─────────────────
        public bool IsCreature;
        public int  Attack;
        public int  MaxHealth;
        public int  Speed;

        // ── Флаги ─────────────────────────────────────────────────────────────
        public bool IsCommander;

        /// <summary>Снимок для UI-показа карты вне руки (уничтожена/разыграна из колоды и т.п.).
        /// Кост — ПЕЧАТНЫЙ (без живых модификаторов владельца): для витринного показа этого достаточно,
        /// а эффективную цену честно считает только HandUISystem для карт, реально попавших в руку.</summary>
        public Shared.CardVisualData ToVisual() => new Shared.CardVisualData
        {
            CardName    = CardName,
            Description = Description,
            Icon        = ArtImage,
            CardType    = CardType,
            Rarity      = Rarity,
            Element     = Element,
            CostType    = CostType,
            CostAmount  = CostAmount,
            IsCreature  = IsCreature,
            Attack      = Attack,
            MaxHealth   = MaxHealth,
            Speed       = Speed,
            IsCommander = IsCommander,
        };
    }
}
