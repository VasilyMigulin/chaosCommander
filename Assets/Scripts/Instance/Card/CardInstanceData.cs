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

        // Каталожный id карты: "{expansion}_{cardId}" (нижним регистром — см. инвариант). Общий контракт InstanceData.
        public override string ItemId => CardData != null ? $"{ExpansionId}_{CardId}" : null;

        // Миниатюра: заданная вручную, иначе арт карты (CardModel.ArtImage).
        public override Sprite Miniature => _miniature != null ? _miniature : CardData?.ArtImage;
    }
}
