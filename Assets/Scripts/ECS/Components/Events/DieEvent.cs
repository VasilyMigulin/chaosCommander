namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Существо погибло. Вешается на сущность существа перед её удалением.
    /// </summary>
    public struct DieEvent
    {
        public int KillerEntity;   // -1 если нет конкретного убийцы
    }
}
