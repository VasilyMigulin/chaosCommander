using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
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
    }
}
