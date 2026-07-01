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
    }
}
