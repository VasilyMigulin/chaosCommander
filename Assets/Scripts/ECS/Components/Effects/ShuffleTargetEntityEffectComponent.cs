namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Замешать в колоду конкретную сущность (TargetEntity) — обычно выбранную раскопкой.
    /// В отличие от ShuffleCardEffect (создаёт новую сущность из данных), берёт
    /// существующую: снимает HandTag/GraveTag, добавляет в DeckComponent владельца.
    /// </summary>
    public struct ShuffleTargetEntityEffectComponent
    {
        /// <summary>true — мешать в колоду оппонента (по сторонам), иначе — в свою.</summary>
        public bool IntoOpponentDeck;
    }
}
