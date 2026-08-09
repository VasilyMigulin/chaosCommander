namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Свойство «Ядовитый»/Poisoned (PoisonedProperty). Не путать с PoisonComponent — тем статусом «Отравлен»,
    /// который это свойство навешивает на ЦЕЛЬ. Любой урон, который наносит носитель — бой (AttackHitEvent)
    /// ИЛИ способность (TakeDamageEvent), включая способность, полученную позже через бафф — добавляет Stacks
    /// стаков PoisonComponent цели. Проверяется в TakeDamageSystem.ApplyDamage — единая точка урона, поэтому
    /// источник урона не важен, важен только сам факт «этот урон нанёс Я».
    /// </summary>
    public struct VenomousComponent
    {
        public int Stacks;
    }
}
