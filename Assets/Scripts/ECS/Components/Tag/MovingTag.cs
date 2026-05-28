namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на существо пока оно визуально перемещается к новой клетке.
    /// Блокирует ввод (RunSelectCellSystem, MoveSystem, AttackSystem) до завершения анимации.
    /// </summary>
    public struct MovingTag { }
}
