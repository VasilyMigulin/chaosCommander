using UnityEngine;
using Game.Core.Model.Card;

namespace Game.Core.Instance.Card
{
    [CreateAssetMenu(fileName = "NewCard", menuName = "Data/CardInstanceData")]
    public class CardInstanceData : InstanceData
    {
        [SerializeReference] public CardModel CardData;
    }
}