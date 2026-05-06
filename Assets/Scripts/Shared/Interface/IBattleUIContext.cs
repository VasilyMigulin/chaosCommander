using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Контекст боевого состояния для инъекции в UI.
    /// UI использует World только для публикации ECS-событий, не читает состояние напрямую.
    /// </summary>
    public interface IBattleUIContext
    {
        EcsWorld World { get; }
        bool TryGetPlayer(out int playerEntity);
    }
}
