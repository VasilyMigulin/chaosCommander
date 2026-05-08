namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер: ресурсы хода уже начислены.
    /// Удаляется в TurnEndReadySystem при передаче хода.
    /// </summary>
    public struct TurnResourcesGrantedTag { }
}
