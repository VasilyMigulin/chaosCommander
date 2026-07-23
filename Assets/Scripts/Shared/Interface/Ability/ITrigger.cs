using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Триггер — как способность узнаёт, что пора активироваться. Лежит в
    /// AbilityTriggerContainerComponent на ability-сущности. В Init подписывается на шину и
    /// запоминает id; при срабатывании вешает AbilityCastEvent на abilityEntity; Dispose отписывается.
    /// </summary>
    public interface ITrigger
    {
        void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity);
        void Dispose();

        /// <summary>Для ИИ (utility AI, PvE): триггер срабатывает КАЖДЫЙ ход, пока источник на поле
        /// (начало/конец хода владельца). Существо с таким триггером генерит ценность просто оставаясь
        /// живым → ИИ прячет его в тыл, а не шлёт в размен. Паттерн — как IEffect.AiRole: default здесь,
        /// турновые триггеры переопределяют.</summary>
        bool AiTurnCycle => false;

        /// <summary>Для ИИ: триггер срабатывает при СМЕРТИ источника (предсмертный хрип). Существо, чья
        /// смерть даёт ПОЛЕЗНЫЙ эффект (роль эффекта не Generic/Curse — служебный реверт аур и навешенные
        /// врагом проклятия не в счёт), наоборот НАРЫВАЕТСЯ: агрессивно лезет под удары, чтобы хрип
        /// сдетонировал поскорее.</summary>
        bool AiDeathTrigger => false;
    }
}
