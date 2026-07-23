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

        /// <summary>ПЕЧАТНАЯ база стоимости, если CostAmount — уже эффективная цена с модификаторами
        /// (карты руки: HandUISystem кладёт в CostAmount живой кост — Мастер над чарами применяет скидку
        /// ДО попадания карты в руку). Нужна CardBaseView для окраски коста относительно базы
        /// (дешевле → зелёный). HasBaseCost=false (дефолт struct) → база = CostAmount (как раньше) —
        /// прочие сборщики визуала (мулиган/история/попапы) поле не заполняют и не обязаны.</summary>
        public bool HasBaseCost;
        public int  BaseCostAmount;

        // ── Существо (заполняется только для Creature) ────────────────────────
        public bool IsCreature;
        public int  Attack;
        public int  MaxHealth;
        public int  Speed;

        // ── Флаги ─────────────────────────────────────────────────────────────
        public bool IsCommander;
    }
}
