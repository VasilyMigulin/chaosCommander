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
    }
}
