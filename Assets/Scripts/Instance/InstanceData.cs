using UnityEngine;

namespace Game.Core.Instance
{
    /// <summary>
    /// База ЛЮБОЙ продаваемой/косметической сущности (карта, аватар, а дальше — рубашки колод, доски,
    /// эмоции, НАБОРЫ). Единый контракт — каталожный <see cref="ItemId"/>: по нему магазин/награды/инвентарь
    /// резолвят предмет одинаково, независимо от типа. Новый вид косметики = новый наследник с своим ItemId.
    /// </summary>
    public abstract class InstanceData : ScriptableObject
    {
        /// <summary>Каталожный id в PlayFab (карта — "{expansion}_{cardId}", аватар — "avatar_{id}",
        /// бустер — "booster_{id}", набор — "bundle_{id}" и т.д.). Уникален в каталоге.</summary>
        public abstract string ItemId { get; }

        [Tooltip("Миниатюра предмета для КОМПАКТНЫХ мест (ценник магазина, иконка награды, ячейка дня, слот " +
                 "инвентаря). Пусто → тип отдаёт своё «родное» арт (карта — арт карты, аватар — иконку).")]
        [SerializeField] protected Sprite _miniature;

        /// <summary>Единая миниатюра предмета — один аксессор для ЛЮБОГО типа (карта/аватар/бустер/набор).
        /// По умолчанию — заданный _miniature; наследник может подставить своё арт, если поле пустое.</summary>
        public virtual Sprite Miniature => _miniature;
    }
}