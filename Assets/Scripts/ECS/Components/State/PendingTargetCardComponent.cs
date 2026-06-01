using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity ИГРОКА когда он «держит» карту в руке и ждёт выбора цели.
    /// TargetSelectionSystem читает этот компонент, подсвечивает подходящие цели
    /// и при клике по валидной цели создаёт CastEvent с заполненным TargetEntity/TargetCell.
    /// </summary>
    public struct PendingTargetCardComponent
    {
        /// <summary>entity карты которую хотим разыграть.</summary>
        public int CardEntity;

        /// <summary>Маска требуемой цели (существо/клетка/сторона).</summary>
        public TargetMask RequiredTarget;
    }
}
