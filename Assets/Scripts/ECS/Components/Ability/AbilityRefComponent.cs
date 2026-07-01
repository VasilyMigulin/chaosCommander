using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (ECS) ===
    /// <summary>
    /// МОСТ ECS(struct) -> поведение(class). Висит на ability-сущности и держит ссылку на
    /// живую способность через интерфейс IAbility (Shared.Interface), чтобы Components НЕ
    /// зависел от сборки Game.Core.Ability. Системы находят способность по entity-id и зовут её методы.
    /// </summary>
    public struct AbilityRefComponent
    {
        public IAbility Ability;
    }
}
