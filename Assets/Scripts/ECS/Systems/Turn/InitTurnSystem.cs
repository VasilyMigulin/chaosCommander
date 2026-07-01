using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Фазовая инициализация (TurnPhaseState/GlobalTurnState) удалена — ход теперь управляется
    /// исключительно ActiveState/StartTurnState (см. TurnFlow, RunTurnStartSystem, RunActivateSystem).
    /// Класс оставлен пустым, чтобы не трогать регистрацию; длительность хода — в TurnConfig.
    /// </summary>
    public sealed class InitTurnSystem : IEcsInitSystem
    {
        public void Init(IEcsSystems systems) { }
    }
}
