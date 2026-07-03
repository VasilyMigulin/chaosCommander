using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Держит командную подкраску вью существ в синхроне с ВЛАДЕЛЬЦЕМ (свои — натуральные/_ownTint,
    /// чужие — _enemyTint на CreatureView): одинаковые существа разных игроков различимы. Локально-
    /// относительная косметика (у каждого клиента подкрашен ВРАГ), синка не требует.
    ///
    /// Реактивность даром: гоняется каждый кадр по вью на борде, а сам SetTeamTint идемпотентен
    /// (кэш состояния, ранний выход) — реально красит только на смене владельца. Так покрываются
    /// и спавн/респавн (командир, воскрешение), и кража контроля (AbilityControl меняет
    /// OwnerComponent.OwnerId БЕЗ респавна вью), и её откат (TempControlRevertSystem).
    /// Регистрация: _generalSystems после SpawnCreatureViewSystem.
    /// </summary>
    public sealed class TeamTintSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<CreatureTag, ViewSpawnedTag, ViewRefComponent, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool  = default;
        readonly EcsPoolInject<OwnerComponent>   _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent>  _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _localPlayer = default;

        public void Run(IEcsSystems systems)
        {
            int localId = -1;
            foreach (var pe in _localPlayer.Value) { localId = _playerPool.Value.Get(pe).PlayerId; break; }
            if (localId < 0) return;

            foreach (var e in _filter.Value)
            {
                ref var vr = ref _viewPool.Value.Get(e);
                if (vr.View == null) continue;
                var view = vr.View.GetComponent<CreatureView>();
                if (view == null) continue;

                view.SetTeamTint(_ownerPool.Value.Get(e).OwnerId != localId);
            }
        }
    }
}
