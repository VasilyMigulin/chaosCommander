namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос атаки. Вешается на сущность атакующего.
    /// </summary>
    public struct AttackRequestEvent
    {
        public int TargetEntity;
    }
}
