namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на сущность способности пока она ожидает выбора цели игроком.
    /// Снимается когда цель выбрана.
    /// </summary>
    public struct WaitingForTargetState
    {
        public int AbilityIndex;
        public int SourceCardEntity;
    }
}
