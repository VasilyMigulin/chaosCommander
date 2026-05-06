using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Shared
{
    /// <summary>
    /// Полный набор визуальных данных карты для любой вьюшки.
    /// Собирается из CardModel один раз и передаётся в CardBaseView.SetCard().
    /// </summary>
    public struct CardVisualData
    {
        // ── Базовые ───────────────────────────────────────────────────────────
        public string                CardName;
        public string                Description;
        public Sprite                Icon;
        public EnumService.Rarity    Rarity;
        public EnumService.Element   Element;
        public EnumService.CardType  CardType;

        // ── Стоимость ─────────────────────────────────────────────────────────
        public EnumService.ResourceType CostType;
        public int                   CostAmount;

        // ── Существо (заполняется только для Creature) ────────────────────────
        public bool IsCreature;
        public int  Attack;
        public int  MaxHealth;
        public int  Speed;

        // ── Флаги ─────────────────────────────────────────────────────────────
        public bool IsCommander;
    }
}
