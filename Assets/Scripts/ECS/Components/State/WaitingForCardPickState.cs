namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на сущность способности пока она ожидает выбора карты (раскопка).
    /// Снимается когда карта выбрана.
    /// </summary>
    public struct WaitingForCardPickState
    {
        public int AbilityIndex;
        public int SourceCardEntity;
    }
}
