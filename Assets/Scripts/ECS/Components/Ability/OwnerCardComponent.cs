namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity способности. Хранит entity карты-владельца.
    /// Используется CheckConditionSystem чтобы сообщить карте о готовности способности.
    /// </summary>
    public struct OwnerCardComponent
    {
        public int CardEntity;
    }
}
