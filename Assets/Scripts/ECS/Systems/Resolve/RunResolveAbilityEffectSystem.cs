using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Создаёт effect entity для каждой цели каждого эффекта способности.
    ///
    /// Приоритет выбора цели:
    ///   1. AbilityChosenTargetComponent — игрок явно выбрал цель
    ///   2. FieldAbilityTag → делегируется RunResolveAbilityFieldSystem
    ///   3. AbilityTargetFlagsComponent:
    ///      - Random     → случайная цель (seed = abilityEntity, детерминировано)
    ///      - ExcludeSelf → кастер исключается из пула
    ///      - иначе      → первый по (row asc, col asc)
    /// </summary>
    public sealed class RunResolveAbilityEffectSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        readonly EcsFilterInject<
            Inc<ResolveAbilityEvent, AbilityEffectContainerComponent>,
            Exc<ConditionNotMetTag, FieldAbilityTag>> _filter = default;

        readonly EcsPoolInject<ResolveAbilityEvent> _resolvePool = default;
        readonly EcsPoolInject<AbilityEffectContainerComponent> _effectContainerPool = default;
        readonly EcsPoolInject<AbilityChosenTargetComponent> _chosenTargetPool = default;
        readonly EcsPoolInject<AbilityTargetFlagsComponent> _targetFlagsPool = default;
        readonly EcsPoolInject<CastEvent> _castPool = default;
        readonly EcsPoolInject<ProjectileViewComponent> _projectileViewPool = default;

        readonly EcsPoolInject<EffectComponent> _effectPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetEntityPool = default;
        readonly EcsPoolInject<HitComponent> _hitPool = default;

        readonly EcsFilterInject<Inc<BoardTag, BoardPositionComponent, OwnerComponent>> _boardFilter = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var abilityEntity in _filter.Value)
            {
                ref var effectContainer = ref _effectContainerPool.Value.Get(abilityEntity);

                int ownerPlayerId  = GetOwnerPlayerId(abilityEntity);
                int ownerEntity    = GetOwnerEntity(abilityEntity);
                bool hasProjectile = _projectileViewPool.Value.Has(abilityEntity);

                var targets = ResolveTargets(abilityEntity, ownerPlayerId, ownerEntity);

                foreach (int targetEntity in targets)
                {
                    foreach (var effect in effectContainer.AbilityEffects)
                    {
                        int effectEntity = _world.Value.NewEntity();
                        _effectPool.Value.Add(effectEntity);

                        ref var ctx = ref _targetEntityPool.Value.Add(effectEntity);
                        ctx.TargetEntity = targetEntity;
                        ctx.OwnerEntity  = ownerEntity;

                        effect.AddEffect(_world.Value, effectEntity);

                        if (!hasProjectile)
                            _hitPool.Value.Add(effectEntity);
                    }
                }
            }
        }

        List<int> ResolveTargets(int abilityEntity, int ownerPlayerId, int ownerEntity)
        {
            var result = new List<int>();

            // 1. Явно выбранная цель
            if (_chosenTargetPool.Value.Has(abilityEntity))
            {
                int chosen = _chosenTargetPool.Value.Get(abilityEntity).TargetEntity;
                if (chosen != -1)
                {
                    result.Add(chosen);
                    return result;
                }
            }

            if (!_targetFlagsPool.Value.Has(abilityEntity)) return result;

            var flags       = _targetFlagsPool.Value.Get(abilityEntity).Flags;
            bool isRandom   = (flags & AbilityTargetFlags.Random) != 0;
            bool excludeSelf = (flags & AbilityTargetFlags.ExcludeSelf) != 0;

            // 2. Self (модификаторы не применяются)
            if ((flags & AbilityTargetFlags.Self) != 0)
            {
                if (ownerEntity != -1) { result.Add(ownerEntity); return result; }
            }

            // Собираем кандидатов
            var candidates = new List<int>();

            if ((flags & AbilityTargetFlags.AllyPlayer) != 0)
            {
                int p = FindPlayer(ownerPlayerId, ally: true);
                if (p != -1) candidates.Add(p);
            }
            if ((flags & AbilityTargetFlags.EnemyPlayer) != 0)
            {
                int p = FindPlayer(ownerPlayerId, ally: false);
                if (p != -1) candidates.Add(p);
            }

            bool wantEnemy = (flags & AbilityTargetFlags.EnemyCreature) != 0;
            bool wantAlly  = (flags & AbilityTargetFlags.AllyCreature)  != 0;

            if (wantEnemy || wantAlly)
                CollectCreaturesSorted(ownerPlayerId, wantEnemy, wantAlly, candidates);

            // Исключаем кастера
            if (excludeSelf && ownerEntity != -1)
                candidates.Remove(ownerEntity);

            if (candidates.Count == 0) return result;

            if (isRandom)
            {
                // Seed детерминирован между клиентами: берётся из NetworkEntityKey
                // карты-источника + индекса способности (entity id различается на клиентах).
                var rng = new System.Random(ComputeStableSeed(abilityEntity));
                result.Add(candidates[rng.Next(0, candidates.Count)]);
            }
            else
            {
                result.Add(candidates[0]);
            }

            return result;
        }

        void CollectCreaturesSorted(int ownerPlayerId, bool includeEnemy, bool includeAlly,
                                    List<int> output)
        {
            var list = new List<(int row, int col, int entity)>();

            foreach (var creatureEntity in _boardFilter.Value)
            {
                ref var pos   = ref _boardPosPool.Value.Get(creatureEntity);
                ref var owner = ref _ownerPool.Value.Get(creatureEntity);
                bool isAlly   = owner.OwnerId == ownerPlayerId;

                if (isAlly  && !includeAlly)  continue;
                if (!isAlly && !includeEnemy) continue;

                list.Add((pos.Row, pos.Col, creatureEntity));
            }

            list.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.col.CompareTo(b.col));

            foreach (var item in list)
                output.Add(item.entity);
        }

        int GetOwnerPlayerId(int abilityEntity)
        {
            if (!_castPool.Value.Has(abilityEntity)) return -1;
            int oe = _castPool.Value.Get(abilityEntity).OwnerEntity;
            if (oe == -1) return -1;
            if (_ownerPool.Value.Has(oe))  return _ownerPool.Value.Get(oe).OwnerId;
            if (_playerPool.Value.Has(oe)) return _playerPool.Value.Get(oe).PlayerId;
            return -1;
        }

        int GetOwnerEntity(int abilityEntity)
        {
            if (!_castPool.Value.Has(abilityEntity)) return -1;
            return _castPool.Value.Get(abilityEntity).OwnerEntity;
        }

        int FindPlayer(int ownerPlayerId, bool ally)
        {
            foreach (var playerEntity in _playerFilter.Value)
            {
                int pid   = _playerPool.Value.Get(playerEntity).PlayerId;
                bool isAlly = pid == ownerPlayerId;
                if (ally == isAlly) return playerEntity;
            }
            return -1;
        }

        // Стабильный seed: FNV-1a по NetworkEntityKey карты-источника + индекс способности.
        // Одинаков на обоих клиентах (в отличие от локального entity id).
        int ComputeStableSeed(int abilityEntity)
        {
            string key = null;
            int abilityIndex = 0;

            if (_abilitySourcePool.Value.Has(abilityEntity))
            {
                ref var src = ref _abilitySourcePool.Value.Get(abilityEntity);
                abilityIndex = src.AbilityIndex;
                if (src.CardEntity >= 0 && _netKeyPool.Value.Has(src.CardEntity))
                    key = _netKeyPool.Value.Get(src.CardEntity).NetworkEntityKey;
            }

            if (string.IsNullOrEmpty(key))
                return abilityEntity; // запасной вариант (может рассинхронизироваться)

            unchecked
            {
                const uint prime = 16777619;
                uint hash = 2166136261;
                for (int i = 0; i < key.Length; i++)
                {
                    hash ^= (uint)key[i];
                    hash *= prime;
                }
                hash ^= (uint)abilityIndex;
                hash *= prime;
                return (int)hash;
            }
        }
    }
}
