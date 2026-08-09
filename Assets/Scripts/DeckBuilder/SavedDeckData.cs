using System;
using System.Collections.Generic;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Персистентные данные сохранённой колоды.
    /// Commander хранится отдельно — это легендарная карта-существо.
    /// </summary>
    [Serializable]
    public class SavedDeckData
    {
        public string         Name;
        public OwnedCardData  Commander;
        public List<OwnedCardData> Cards = new List<OwnedCardData>();

        /// <summary>«Отложенные» карты (сайдборд) — зона, которую объявляет небоевая способность карты
        /// в колоде (Сказочник: SideboardAbility{3}). В бою они НЕ в колоде и НЕ в руке: лежат в своей
        /// зоне, откуда их достаёт способность носителя.
        ///
        /// РЕШЕНИЕ (юзер, 2026-08-01): в РАЗМЕР колоды не входят (20 карт + 3 отложенные), но в ЛИМИТ
        /// КОПИЙ по редкости входят наравне с колодой — иначе сайдборд давал бы «пятую копию» в обход
        /// редкости, которая в этой игре и есть баланс (см. правило 4/3/2/1/1).
        ///
        /// Пусто у подавляющего большинства колод; список, а не массив — чтобы старый JSON без поля
        /// десериализовался в пустой, а не в null.</summary>
        public List<OwnedCardData> Sideboard = new List<OwnedCardData>();
    }
}
