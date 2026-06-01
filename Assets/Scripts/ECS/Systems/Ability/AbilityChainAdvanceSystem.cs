using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Оркестратор шагов цепочки эффектов. Запускается ПЕРЕД RunResolveAbilityEffectSystem.
    ///
    /// Логика на каждую способность с ResolveAbilityEvent:
    ///   • ConditionNotMet → чистим ResolveAbilityEvent + ChainState. Шаги не запускаются.
    ///   • FieldAbilityTag → пропускаем (RunResolveAbilityFieldSystem сам чистит).
    ///   • Уже ждёт резолв шага (NeedsStepResolveTag) → пропускаем.
    ///   • Нет ChainStateComponent → первая итерация: создаём состояние,
    ///       зеркалим PickedCard (если был раскоп), ставим NeedsStepResolveTag.
    ///   • Есть эффекты текущего шага в полёте → ждём.
    ///   • Иначе шаг завершён:
    ///       — если шагов больше нет — чистим ResolveAbilityEvent + ChainState;
    ///       — иначе CurrentStepIndex++, ставим NeedsStepResolveTag.
    /// </summary>
    public sealed class AbilityChainAdvanceSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<ResolveAbilityEvent>> _resolveFilter = default;
        readonly EcsFilterInject<Inc<EffectAbilityRefComponent>> _effectRefFilter = default;

        readonly EcsPoolInject<ResolveAbilityEvent> _resolvePool = default;
        readonly EcsPoolInject<ChainStateComponent> _chainStatePool = default;
        readonly EcsPoolInject<AbilityChainContainerComponent> _chainContainerPool = default;
        readonly EcsPoolInject<NeedsStepResolveTag> _needsStepResolvePool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _effectRefPool = default;
        readonly EcsPoolInject<ConditionNotMetTag> _conditionNotMetPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<CardPickResultComponent> _pickResultPool = default;

        readonly HashSet<int> _busy = new HashSet<int>();

        public void Run(IEcsSystems systems)
        {
            // 1) множество способностей, у которых ещё живы effect-entity текущего шага
            _busy.Clear();
            foreach (var effEntity in _effectRefFilter.Value)
            {
                ref var r = ref _effectRefPool.Value.Get(effEntity);
                _busy.Add(r.AbilityEntity);
            }

            // 2) проход по способностям в резолве
            foreach (var abilityEntity in _resolveFilter.Value)
            {
                // condition провален: чистим и выходим (никаких эффектов)
                if (_conditionNotMetPool.Value.Has(abilityEntity))
                {
                    Cleanup(abilityEntity);
                    continue;
                }

                // текущий шаг сейчас будет резолвиться эффект-системой
                if (_needsStepResolvePool.Value.Has(abilityEntity)) continue;

                // первая итерация — создаём состояние цепочки
                if (!_chainStatePool.Value.Has(abilityEntity))
                {
                    ref var state = ref _chainStatePool.Value.Add(abilityEntity);
                    state.CurrentStepIndex = 0;
                    state.TotalSteps = ComputeTotalSteps(abilityEntity);
                    state.CurrentTargets = new List<int>();
                    state.ProducedEntity = -1;
                    state.HasCapturedCell = false;
                    state.CapturedRow = 0;
                    state.CapturedCol = 0;
                    state.CapturedCellOwnerId = 0;
                    state.PickedCardEntity = ResolvePickedCard(abilityEntity);

                    _needsStepResolvePool.Value.Add(abilityEntity);
                    continue;
                }

                // ждём эффекты текущего шага
                if (_busy.Contains(abilityEntity)) continue;

                // шаг завершён — продвигаем или чистим
                ref var st = ref _chainStatePool.Value.Get(abilityEntity);
                st.CurrentStepIndex++;
                if (st.CurrentStepIndex >= st.TotalSteps)
                {
                    Cleanup(abilityEntity);
                }
                else
                {
                    _needsStepResolvePool.Value.Add(abilityEntity);
                }
            }
        }

        int ComputeTotalSteps(int abilityEntity)
        {
            int n = 1; // шаг 0 — основные Effects абилки (даже если пуст)
            if (_chainContainerPool.Value.Has(abilityEntity))
            {
                ref var c = ref _chainContainerPool.Value.Get(abilityEntity);
                if (c.Steps != null) n += c.Steps.Count;
            }
            return n;
        }

        int ResolvePickedCard(int abilityEntity)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return -1;
            int cardEntity = _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
            if (cardEntity < 0) return -1;
            if (!_pickResultPool.Value.Has(cardEntity)) return -1;
            return _pickResultPool.Value.Get(cardEntity).ChosenCardEntity;
        }

        void Cleanup(int abilityEntity)
        {
            if (_chainStatePool.Value.Has(abilityEntity))
                _chainStatePool.Value.Del(abilityEntity);
            if (_needsStepResolvePool.Value.Has(abilityEntity))
                _needsStepResolvePool.Value.Del(abilityEntity);
            if (_resolvePool.Value.Has(abilityEntity))
                _resolvePool.Value.Del(abilityEntity);
        }
    }
}
