using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Состояние цепочки на entity способности во время её разрешения.
    /// Создаётся AbilityChainAdvanceSystem при первом резолве, обновляется
    /// между шагами; удаляется когда цепочка пройдена (или абилка не имеет шагов).
    /// </summary>
    public struct ChainStateComponent
    {
        /// <summary>
        /// Текущий шаг. 0 = основные Effects абилки; 1..N = ChainSteps[i-1].
        /// </summary>
        public int CurrentStepIndex;

        /// <summary>Сколько ВСЕГО шагов в цепочке (включая шаг 0). Если ChainSteps пуст — равен 1.</summary>
        public int TotalSteps;

        /// <summary>
        /// Цели текущего шага. Заполняются RunResolveAbilityEffectSystem при создании
        /// effect-entity и читаются AbilityChainAdvanceSystem для пропагирования в
        /// следующий шаг (если TargetSource == PreviousTarget).
        /// </summary>
        public List<int> CurrentTargets;

        /// <summary>
        /// «Продукт» текущего шага — entity, на которую укажет следующий шаг
        /// при TargetSource = PreviousProduced. Для DealDamage/Heal/etc. — первая цель.
        /// Для PickCard — выбранная карта (выставляется ApplyPickCardSystem).
        /// </summary>
        public int ProducedEntity;

        /// <summary>true если CapturedRow/Col/OwnerId валидны (предыдущая цель была на доске).</summary>
        public bool HasCapturedCell;
        public int CapturedRow;
        public int CapturedCol;
        public int CapturedCellOwnerId;

        /// <summary>
        /// Карта, выбранная раскопкой при касте (если у источника был
        /// RequireCardPickPlayRequirement). Зеркалится ChainAdvanceSystem при входе,
        /// сохраняется на всю цепочку. Читается ChainTargetSource.PickedCard.
        /// -1 если раскопки не было.
        /// </summary>
        public int PickedCardEntity;
    }
}
