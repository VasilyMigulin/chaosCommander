using Game.Core.Service;
using Game.Core.Shared.Interface;

namespace Game.Core.Model.Ability
{
    /// <summary>
    /// Абстрактная модель реакции ауры. Конкретные реализации (DamageReflectAuraReaction,
    /// ForceOpponentDiscardOnDrawAuraReaction, и т.д.) переопределяют ListenTo и OnEvent.
    /// Хранится в Ability.AuraReactions (SerializeReference); при Init копируется в
    /// AuraReactionContainerComponent на entity чары.
    /// </summary>
    [System.Serializable]
    public abstract class AuraReaction : IAuraReaction
    {
        public abstract AuraEventType ListenTo { get; }
        public abstract void OnEvent(AuraReactionContext ctx);
    }
}
