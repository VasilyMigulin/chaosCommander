using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Маршрут существа из нескольких клеток, исполняемый ПО ОДНОМУ шагу (RunPathMoveSystem превращает
    /// очередной шаг в обычный MoveRequestEvent, дожидаясь конца анимации предыдущего), с опциональной
    /// атакой в конце пути («дойти и ударить» одним кликом).
    ///
    /// Ставится ТОЛЬКО локальным вводом активного клиента (RunSelectCellSystem по клику на дальнюю
    /// клетку/цель) — пассив НЕ получает этот компонент: ему приезжает готовая последовательность
    /// ActionMoveData×N (+ActionAttackData) обычным реплеем, по одному действию за кадр.
    /// </summary>
    public struct PathMoveComponent
    {
        /// <summary>Оставшиеся шаги маршрута, [0] — следующий. Каждый шаг — соседняя клетка.</summary>
        public List<(int Row, int Col, int Owner)> Steps;

        /// <summary>Цель атаки в конце пути: сущность существа ИЛИ игрока (аватар); -1 — только движение.</summary>
        public int AttackTargetEntity;

        /// <summary>
        /// Бесплатный маршрут (Позвать стражу и т.п.): ход/атака не тратят SpeedComponent.Remaining и не
        /// ограничены AttacksUsedComponent (бонусная атака сверх лимита хода, а не вместо него).
        /// </summary>
        public bool Free;
    }
}
