using Game.Core.Model.Card;
using Game.Core.Shared;
using UnityEngine;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Данные для отображения карты в библиотеке или колоде.
    /// Не зависит от ECS.
    /// </summary>
    public struct DeckCardViewData
    {
        public CardModel    Model;
        public Sprite       Icon;
        public string       CardName;
        public int          OwnedCount;    // сколько копий у игрока
        public int          DeckCount;     // сколько копий уже в колоде
        public int          MaxCopies;     // максимально допустимо по редкости
        public bool         IsCommander;   // особая метка для слота командира

        /// <summary>Полный визуальный DTO — строится фабрикой из Model.</summary>
        public CardVisualData Visual;
    }
}
