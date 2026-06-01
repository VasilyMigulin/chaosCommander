using Leopotam.EcsLite;
using Game.Core.Service;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Контекст вызова реакции ауры. Заполняется AuraReactionDispatcherSystem
    /// и передаётся реализациям IAuraReaction.OnEvent.
    /// </summary>
    public struct AuraReactionContext
    {
        public EcsWorld World;

        public AuraEventType EventType;

        /// <summary>Entity самой чары (на доске).</summary>
        public int CharmEntity;

        /// <summary>OwnerId владельца чары (= того, на кого подписана реакция).</summary>
        public int OwnerPlayerId;

        /// <summary>Entity игрока-владельца чары.</summary>
        public int OwnerPlayerEntity;

        /// <summary>Entity «второй стороны» события: для урона — кто получил, для розыгрыша — кто разыграл и т.д.</summary>
        public int EventPlayerEntity;
        public int EventPlayerId;

        /// <summary>Числовая величина события (амплитуда урона / количество карт и т.п.).</summary>
        public int EventAmount;

        /// <summary>Дополнительная сущность, связанная с событием (карта / существо).</summary>
        public int EventCardEntity;

        /// <summary>true, если сейчас ХОД владельца чары — реакции вроде «на моём ходу …».</summary>
        public bool OwnerIsActivePlayer;
    }
}
