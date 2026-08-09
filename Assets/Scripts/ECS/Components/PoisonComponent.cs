namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Статус «Отравлен» — НЕ авторится напрямую как свойство карты (нет своего ICreatureProperty), а
    /// навешивается TakeDamageSystem.ApplyDamage на ЦЕЛЬ, когда её ударил носитель свойства Poisoned/«Ядовитый»
    /// (VenomousComponent). Stacks урона тикает В КОНЦЕ ХОДА ВЛАДЕЛЬЦА поражённой сущности (не источника,
    /// наложившего яд) — см. PoisonTickSystem. Копится (каждое наложение += Stacks), не снимается игровыми
    /// средствами (нет эффекта «вылечить яд»), живёт до смерти носителя. Работает на существах И игроках —
    /// PoisonTickSystem фильтра BoardTag не требует. Урон идёт обычным TakeDamageEvent → уважает
    /// Shield/Invulnerable, как любой другой урон.
    /// </summary>
    public struct PoisonComponent
    {
        public int Stacks;
    }
}
