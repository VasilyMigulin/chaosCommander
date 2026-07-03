using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Роль эффекта для ИИ (utility AI, PvE): по ролям эффектов карты ИИ понимает, ЧТО карта делает
    /// (урон/бафф/призыв/добор…), и оценивает её полезность в текущей ситуации на доске.
    /// Роли АВТО-выводятся из самих эффектов (свойство AiRole у классов эффектов) — карты размечать
    /// вручную не нужно: собрал карту из кирпичей → ИИ уже понимает её назначение.
    /// </summary>
    public enum AiEffectRole
    {
        Generic,          // не классифицировано — ИИ оценивает по стоимости
        Damage,           // урон по цели/области (AoE-ность определяется таргетингом способности)
        Removal,          // убрать существо с поля (уничтожить/баунс/баниш/украсть/трансформ)
        BuffAlly,         // усиление своих
        DebuffEnemy,      // ослабление чужих
        Summon,           // призыв/спаун существ на поле
        Draw,             // добор/генерация карт себе в руку
        Heal,             // лечение
        Resource,         // ресурсы (золото/мана/кост-модификаторы)
        HandDisruption,   // дискард/кража из руки-колоды оппонента
        Curse,            // «порча» оппонента (замешать вред в его колоду, отнять ресурсы)
    }

    /// <summary>
    /// Эффект способности — что делаем с целью. Лежит в AbilityEffectContainerComponent на
    /// ability-сущности; применяет RunResolveAbilityQueueSystem (если IsReady).
    /// Init/Dispose — подписка/отписка условий внутри эффекта. cardEntity = карта-источник (кастер).
    /// </summary>
    public interface IEffect
    {
        void Init(EcsWorld world, int cardEntity, int playerEntity);
        void Dispose();
        bool IsReady { get; }   // условие готово (null-условие → true)
        void Apply(EcsWorld world, int cardEntity, int target);

        /// <summary>Роль для ИИ (см. AiEffectRole). Default — Generic; эффекты переопределяют.
        /// NB: у EffectBase-наследников работает через virtual в EffectBase (интерфейс-мапа фиксируется
        /// на классе, объявившем IEffect); прямые реализации IEffect объявляют свойство сами.</summary>
        AiEffectRole AiRole => AiEffectRole.Generic;
    }
}
