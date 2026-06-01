using Game.Core.Service;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Реакция ауры — повисает на чаре через AuraReactionContainerComponent.
    /// AuraReactionDispatcherSystem подписан на игровые события и, когда событие
    /// типа ListenTo происходит, вызывает OnEvent у всех реакций такого типа,
    /// принадлежащих чарам на доске под контролем нужного игрока.
    /// </summary>
    public interface IAuraReaction
    {
        AuraEventType ListenTo { get; }

        void OnEvent(AuraReactionContext ctx);
    }
}
