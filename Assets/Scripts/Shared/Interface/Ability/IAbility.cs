namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Минимальная поверхность способности, видимая ECS-системам через AbilityRefComponent
    /// (который лежит в Game.Core.Ecs.Components). Благодаря этому интерфейсу Components
    /// не зависит от сборки поведения Game.Core.Ability — только от Shared.Interface.
    /// Конкретная реализация — Game.Core.Ability.Ability.
    /// </summary>
    public interface IAbility
    {
        void Dispose();

        /// <summary>ХС-семантика смены контроля: пере-привязать способность к НОВОМУ игроку-владельцу на
        /// ТОЙ ЖЕ ability-сущности (Dispose → очистка компонентов → Init с новым playerEntity). Инстанс
        /// НЕ пересоздаётся — внутренний стейт триггеров/условий переживает смену. Зовут смены контроля:
        /// TakeControlEffect/StealToHand (сборка Ability) и TempControlRevertSystem (сборка Systems —
        /// через этот интерфейс, без ссылки на сборку поведения).</summary>
        void Rebind(Leopotam.EcsLite.EcsWorld world, int abilityEntity, int cardEntity, int newPlayerEntity, int abilityIndex);
    }
}
