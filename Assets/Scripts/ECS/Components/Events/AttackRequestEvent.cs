namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос атаки. Вешается на сущность атакующего.
    /// </summary>
    public struct AttackRequestEvent
    {
        public int TargetEntity;

        /// <summary>Бесплатная атака (Позвать стражу и т.п.): AttackSystem не проверяет и не тратит SpeedComponent.Remaining.</summary>
        public bool Free;
    }
}
