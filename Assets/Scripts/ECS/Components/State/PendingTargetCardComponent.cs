namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity ИГРОКА когда он «держит» карту в руке и ждёт выбора цели.
    /// TargetSelectionSystem читает этот компонент, подсвечивает доступные цели
    /// и при клике по валидной цели создаёт CastEvent с заполненным TargetEntity/TargetCell.
    /// </summary>
    public struct PendingTargetCardComponent
    {
        /// <summary>entity карты которую хотим разыграть.</summary>
        public int CardEntity;

        /// <summary>Тип требуемой цели: Enemy, Ally, Cell, Any.</summary>
        public TargetRequirementType RequiredTarget;
    }

    public enum TargetRequirementType
    {
        EnemyCreature,
        AllyCreature,
        AnyCreature,
        AnyCell,
        /// <summary>Пустая клетка фронтального ряда (row=0) активного игрока. Для размещения существ.</summary>
        OwnFrontCell,
        /// <summary>Случайное вражеское существо на доске.</summary>
        RandomEnemy,
        /// <summary>Случайное союзное существо на доске.</summary>
        RandomAlly,
        /// <summary>Случайное любое существо на доске.</summary>
        RandomAnyCreature,
    }
}
