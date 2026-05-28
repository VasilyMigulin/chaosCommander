using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает способности с FieldAbilityTag — применяет эффекты ко ВСЕМ подходящим целям.
    /// Порядок итерации: row asc → col asc (детерминировано на обоих клиентах).
    /// </summary>
    public sealed class RunResolveAbilityFieldSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        readonly EcsFilterInject<
            Inc<ResolveAbilityEvent, AbilityEffectContainerComponent, AbilityTargetFlagsComponent, FieldAbilityTag>,
            Exc<ConditionNotMetTag>> _filter = default;

        readonly EcsPoolInject<ResolveAbilityEvent> _resolvePool = default;
        readonly EcsPoolInject<AbilityEffectContainerComponent> _effectContainerPool = default;
        readonly EcsPoolInject<AbilityTargetFlagsComponent> _targetFlagsPool = default;
        readonly EcsPoolInject<CastEvent> _castPool = default;

        readonly EcsFilterInject<Inc<BoardTag, BoardPositionComponent, OwnerComponent>> _boardFilter = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var abilityEntity in _filter.Value)
            {
                ref var effectContainer = ref _effectContainerPool.Value.Get(abilityEntity);
                var flags = _targetFlagsPool.Value.Get(abilityEntity).Flags;
                int ownerPlayerId = GetOwnerPlayerId(abilityEntity);

                List<int> targets = CollectAllTargets(flags, ownerPlayerId, abilityEntity);

                foreach (int targetEntity in targets)
                {
                    foreach (var effect in effectContainer.AbilityEffects)
                    {
                        effect.AddEffect(_world.Value, targetEntity);
                    }
                }
            }
        }

        List<int> CollectAllTargets(AbilityTargetFlags flags, int ownerPlayerId, int abilityEntity)
        {
            var result = new List<int>();

            // Self
            if ((flags & AbilityTargetFlags.Self) != 0)
            {
                if (_castPool.Value.Has(abilityEntity))
                {
                    int selfEntity = _castPool.Value.Get(abilityEntity).OwnerEntity;
                    if (selfEntity != -1) result.Add(selfEntity);
                }
            }

            // Игроки
            if ((flags & AbilityTargetFlags.AllyPlayer) != 0)
            {
                int p = FindPlayer(ownerPlayerId, ally: true);
                if (p != -1) result.Add(p);
            }
            if ((flags & AbilityTargetFlags.EnemyPlayer) != 0)
            {
                int p = FindPlayer(ownerPlayerId, ally: false);
                if (p != -1) result.Add(p);
            }

            // Существа на поле — порядок: row asc, col asc
            bool wantEnemy = (flags & AbilityTargetFlags.EnemyCreature) != 0;
            bool wantAlly  = (flags & AbilityTargetFlags.AllyCreature) != 0;

            if (wantEnemy || wantAlly)
            {
                var creatures = CollectCreaturesSorted(ownerPlayerId, wantEnemy, wantAlly);
                result.AddRange(creatures);
            }

            return result;
        }

        int GetOwnerPlayerId(int abilityEntity)
        {
            if (!_castPool.Value.Has(abilityEntity)) return -1;
            int ownerEntity = _castPool.Value.Get(abilityEntity).OwnerEntity;
            if (ownerEntity == -1) return -1;
            if (_ownerPool.Value.Has(ownerEntity))
                return _ownerPool.Value.Get(ownerEntity).OwnerId;
            if (_playerPool.Value.Has(ownerEntity))
                return _playerPool.Value.Get(ownerEntity).PlayerId;
            return -1;
        }

        int FindPlayer(int ownerPlayerId, bool ally)
        {
            foreach (var playerEntity in _playerFilter.Value)
            {
                int pid = _playerPool.Value.Get(playerEntity).PlayerId;
                bool isAlly = pid == ownerPlayerId;
                if (ally == isAlly) return playerEntity;
            }
            return -1;
        }

        List<int> CollectCreaturesSorted(int ownerPlayerId, bool includeEnemy, bool includeAlly)
        {
            var list = new List<(int row, int col, int entity)>();

            foreach (var creatureEntity in _boardFilter.Value)
            {
                ref var pos   = ref _boardPosPool.Value.Get(creatureEntity);
                ref var owner = ref _ownerPool.Value.Get(creatureEntity);

                bool isAlly = owner.OwnerId == ownerPlayerId;
                if (isAlly && !includeAlly) continue;
                if (!isAlly && !includeEnemy) continue;

                list.Add((pos.Row, pos.Col, creatureEntity));
            }

            list.Sort((a, b) =>
            {
                int rowCmp = a.row.CompareTo(b.row);
                return rowCmp != 0 ? rowCmp : a.col.CompareTo(b.col);
            });

            var result = new List<int>(list.Count);
            foreach (var item in list) result.Add(item.entity);
            return result;
        }
    }
}