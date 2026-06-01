using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Service;

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
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, AttackComponent, HealthComponent, OwnerComponent>, Exc<DeadTag>> _creatures = default;
        readonly EcsFilterInject< Inc<BoardTag, AbilityContainerComponent, OwnerComponent>, Exc<DeadTag>> _sources = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<AbilityContainerComponent> _containerPool = default;
        readonly EcsPoolInject<AuraSourceComponent> _auraPool = default;
        readonly EcsPoolInject<TargetMaskComponent> _flagsPool = default;

        public void Run(IEcsSystems systems)
        {
            // 1. Сброс к базовым статам
            foreach (var c in _creatures.Value)
            {
                _attackPool.Value.Get(c).Value = _attackPool.Value.Get(c).Base;
                _hpPool.Value.Get(c).Max       = _hpPool.Value.Get(c).BaseMax;
                if (_speedPool.Value.Has(c))
                    _speedPool.Value.Get(c).Max = _speedPool.Value.Get(c).BaseMax;
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
                    if (aura.AttackBonus == 0 && aura.HealthBonus == 0 && aura.SpeedBonus == 0) continue;

                    var flags = _flagsPool.Value.Has(abilityEntity)
                        ? _flagsPool.Value.Get(abilityEntity).Mask
                        : TargetMask.AllyCreature;

                    foreach (var c in _creatures.Value)
                    {
                        bool isAlly = _ownerPool.Value.Get(c).OwnerId == srcOwnerId;
                        if (!flags.MatchesCreature(isAlly, isSource: src == c))
                            continue;

                        if (aura.AttackBonus != 0)
                            _attackPool.Value.Get(c).Value += aura.AttackBonus;
                        if (aura.HealthBonus != 0)
                            _hpPool.Value.Get(c).Max += aura.HealthBonus;
                        if (aura.SpeedBonus != 0 && _speedPool.Value.Has(c))
                            _speedPool.Value.Get(c).Max += aura.SpeedBonus;
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
    }
}
