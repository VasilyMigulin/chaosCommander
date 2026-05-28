namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Помечает существо, для которого уже создан визуальный GameObject на сцене.
    /// Используется SpawnCreatureViewSystem чтобы не спавнить view повторно.
    /// </summary>
    public struct ViewSpawnedTag { }
}
