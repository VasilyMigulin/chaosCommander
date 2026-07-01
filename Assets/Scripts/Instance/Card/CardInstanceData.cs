using UnityEngine;
using Game.Core.Model.Card;
using Game.Core.Shared.Interface;

namespace Game.Core.Instance.Card
{
    [CreateAssetMenu(fileName = "NewCard", menuName = "Data/CardInstanceData")]
    public class CardInstanceData : InstanceData, ICreatable
    {
        [SerializeReference] public CardModel CardData;

        // ICreatable — идентичность карты для спавна через CreateCardEvent (sync-safe).
        public string ExpansionId => CardData != null ? CardData.ExpansionId : null;
        public int CardId => CardData != null ? CardData.Id : -1;
    }
}
