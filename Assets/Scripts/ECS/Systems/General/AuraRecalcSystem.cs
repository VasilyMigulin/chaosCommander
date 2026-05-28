using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Пересчитывает эффективные статы существ от активных аур (постоянные эффекты чар/существ).
    ///
    /// Каждый кадр:
    ///   1. Сбрасывает Value = Base, Max = BaseMax у всех существ на поле.
    ///   2. Прибавляет бонусы аур от источников на поле (BoardTag + AuraSourceComponent)
    ///      по фильтру целей (AllyCreature / EnemyCreature / ExcludeSelf).
    ///   3. Клампит Current к Max (снятие ауры по HP может понизить текущее HP).
    ///
    /// Перманентные баффы пишутся в Base/BaseMax (ApplyBuffSystem) и попадают сюда автоматически.
    /// Аура по HP повышает максимум (Current не растёт сам — лечится до нового максимума).
    /// Снятие ауры (источник ушёл с поля) учитывается на следующем кадре.
    /// </summary>
    public sealed class AuraRecalcSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<
            Inc<CreatureTag, BoardTag, AttackComponent, HealthComponent, OwnerComponent>,
            Exc<DeadTag>> _creatures = default;

        readonly EcsFilterInject<
            Inc<BoardTag, AbilityContainerComponent, OwnerComponent>,
            Exc<DeadTag>> _sources = default;

        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<AbilityContainerComponent> _containerPool = default;
        readonly EcsPoolInject<AuraSourceComponent> _auraPool = default;
        readonly EcsPoolInject<AbilityTargetFlagsComponent> _flagsPool = default;

        public void Run(IEcsSystems systems)
        {
            // 1. Сброс к базовым статам
            foreach (var c in _creatures.Value)
            {
                _attackPool.Value.Get(c).Value = _attackPool.Value.Get(c).Base;
                _hpPool.Value.Get(c).Max       = _hpPool.Value.Get(c).BaseMax;
            }

            // 2. Прибавляем бонусы аур
            foreach (var src in _sources.Value)
            {
                int srcOwnerId = _ownerPool.Value.Get(src).OwnerId;
                ref var container = ref _containerPool.Value.Get(src);
                if (container.AbilityEntities == null) continue;

                foreach (var abilityEntity in container.AbilityEntities)
                {
                    if (!_auraPool.Value.Has(abilityEntity)) continue;

                    ref var aura = ref _auraPool.Value.Get(abilityEntity);
                    if (aura.AttackBonus == 0 && aura.HealthBonus == 0) continue;

                    var flags = _flagsPool.Value.Has(abilityEntity)
                        ? _flagsPool.Value.Get(abilityEntity).Flags
                        : AbilityTargetFlags.AllyCreature;

                    foreach (var c in _creatures.Value)
                    {
                        if (!Matches(flags, srcOwnerId, _ownerPool.Value.Get(c).OwnerId, src == c))
                            continue;

                        if (aura.AttackBonus != 0)
                            _attackPool.Value.Get(c).Value += aura.AttackBonus;
                        if (aura.HealthBonus != 0)
                            _hpPool.Value.Get(c).Max += aura.HealthBonus;
                    }
                }
            }

            // 3. Клампим текущее HP к (возможно изменившемуся) максимуму
            foreach (var c in _creatures.Value)
            {
                ref var hp = ref _hpPool.Value.Get(c);
                if (hp.Current > hp.Max) hp.Current = hp.Max;
                if (hp.Current < 0)      hp.Current = 0;
            }
        }

        private static bool Matches(AbilityTargetFlags flags, int sourceOwnerId, int creatureOwnerId, bool isSource)
        {
            if (isSource && (flags & AbilityTargetFlags.ExcludeSelf) != 0)
                return false;

            bool isAlly = creatureOwnerId == sourceOwnerId;

            if (isAlly  && (flags & AbilityTargetFlags.AllyCreature)  != 0) return true;
            if (!isAlly && (flags & AbilityTargetFlags.EnemyCreature) != 0) return true;

            return false;
        }
    }
}
