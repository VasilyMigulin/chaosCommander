using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Воспроизводит действия оппонента из ActionQueue.
    ///
    /// Каждый кадр достаёт накопившиеся IActionData и запускает соответствующую
    /// детерминированную логику — ровно так же, как если бы эти действия
    /// выполнял локальный игрок.
    ///
    /// Работает только на стороне НЕ активного игрока (пассивного наблюдателя).
    /// </summary>
    public sealed class ReplayActionSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        readonly EcsPoolInject<CastEvent> _castPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<EnemyCardTag> _enemyPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            while (ActionQueue.TryDequeue(out IActionData action))
            {
                Replay(action);

                // После раскопки с пулом нужно отдать кадр CreateCardSystem,
                // чтобы сущность с ChosenEntityKey успела появиться к моменту,
                // когда последующая ActionAbilityData попытается её резолвить.
                if (action is ActionCardPickedData picked && picked.CreateFromPool)
                    break;
            }
        }

        // ── Dispatch ──────────────────────────────────────────────────────────

        private void Replay(IActionData action)
        {
            switch (action)
            {
                case ActionCastData    cast:    ReplayCardCast(cast);    break;
                case ActionMoveData    move:    ReplayCreatureMove(move); break;
                case ActionAttackData  attack:  ReplayCreatureAttack(attack); break;
                case ActionCardPickedData pick: ReplayCardPicked(pick);  break;
                case ActionAbilityData ability: ReplayAbility(ability);  break;
                default:
                    Debug.LogWarning($"[ReplayActionSystem] Unknown IActionData type: {action?.GetType().Name}");
                    break;
            }
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private void ReplayCardCast(ActionCastData s)
        {
            if (_state.Value.TryGetEntity(s.SourceEntityKey, out int cardEntity))
            {
                if (!_enemyPool.Value.Has(cardEntity)) return;
                if (_castPool.Value.Has(cardEntity)) return;

                ref var cast = ref _castPool.Value.Add(cardEntity);
                cast.OwnerEntity  = FindOwnerOf(cardEntity);
                cast.TargetCell   = s.TargetCell;
                cast.TargetEntity = -1;

                if (!string.IsNullOrEmpty(s.TargetEntityKey) &&
                    _state.Value.TryGetEntity(s.TargetEntityKey, out int targetEntity))
                {
                    cast.TargetEntity = targetEntity;
                }
            }
            else
            {
                // Оппонент имеет синхронизированную копию руки — entity должен существовать.
                // Если не найден — рассинхронизация состояния, логируем ошибку.
                Debug.LogError($"[ReplayActionSystem] CardCast: entity not found for key '{s.SourceEntityKey}'. State desync!");
            }
        }

        private void ReplayCreatureMove(ActionMoveData s)
        {
            if (!_state.Value.TryGetEntity(s.SourceEntityKey, out int entity)) return;

            int toRow = s.TargetCell / 5;
            int toCol = s.TargetCell % 5;

            var movePool = _world.Value.GetPool<MoveRequestEvent>();
            if (!movePool.Has(entity))
            {
                ref var move = ref movePool.Add(entity);
                move.ToRow = toRow;
                move.ToCol = toCol;
            }
        }

        private void ReplayCreatureAttack(ActionAttackData s)
        {
            if (!_state.Value.TryGetEntity(s.AttackerEntityKey, out int attacker)) return;
            if (string.IsNullOrEmpty(s.DefenderEntityKey)) return;
            if (!_state.Value.TryGetEntity(s.DefenderEntityKey, out int defender)) return;

            // Добавляем AttackRequestEvent напрямую (как для движения) — AttackSystem
            // воспроизведёт анимацию и применит урон детерминированно.
            var attackPool = _world.Value.GetPool<AttackRequestEvent>();
            if (!attackPool.Has(attacker))
            {
                ref var req = ref attackPool.Add(attacker);
                req.TargetEntity = defender;
            }
        }

        private void ReplayCardPicked(ActionCardPickedData s)
        {
            // Кладём выбор в стор — CardPickSelectionSystem авто-разрешит его, когда
            // воспроизведёт соответствующий вражеский каст:
            //   • сессионный источник → найдёт сущность по ChosenEntityKey;
            //   • пул → создаст сущность с этим ключом из ChosenExpansionId/ChosenCardId.
            CardPickReplayStore.Set(s.SourceEntityKey, new CardPickReplayStore.PickChoice
            {
                ChosenEntityKey = s.ChosenEntityKey,
                CreateFromPool  = s.CreateFromPool,
                ExpansionId     = s.ChosenExpansionId,
                CardId          = s.ChosenCardId,
            });
        }

        private void ReplayAbility(ActionAbilityData s)
        {
            // Авторитарный реплей: активный игрок прислал список целей для конкретного
            // шага цепочки — мы НЕ ре-резолвим, а создаём effect-entity для этих целей
            // и эффектов из соответствующего шага.
            if (string.IsNullOrEmpty(s.SourceEntityKey))
            {
                Debug.LogWarning("[ReplayActionSystem] Ability: empty SourceEntityKey");
                return;
            }

            if (!_state.Value.TryGetEntity(s.SourceEntityKey, out int cardEntity))
            {
                Debug.LogError($"[ReplayActionSystem] Ability: card not found for key '{s.SourceEntityKey}' — desync");
                return;
            }

            var containerPool = _world.Value.GetPool<AbilityContainerComponent>();
            if (!containerPool.Has(cardEntity))
            {
                Debug.LogWarning($"[ReplayActionSystem] Ability: no AbilityContainer on card {cardEntity}");
                return;
            }

            ref var container = ref containerPool.Get(cardEntity);
            if (container.AbilityEntities == null
                || s.AbilityIndex < 0
                || s.AbilityIndex >= container.AbilityEntities.Length)
            {
                Debug.LogWarning($"[ReplayActionSystem] Ability: bad index {s.AbilityIndex} for card {cardEntity}");
                return;
            }

            int abilityEntity = container.AbilityEntities[s.AbilityIndex];
            var effects = GetStepEffects(abilityEntity, s.StepIndex);
            if (effects == null || effects.Count == 0)
                return;

            int ownerEntity = FindOwnerOf(cardEntity);
            bool hasProjectile = _world.Value.GetPool<ProjectileViewComponent>().Has(abilityEntity);

            var effectPool          = _world.Value.GetPool<EffectComponent>();
            var targetEntityPool    = _world.Value.GetPool<TargetEntityComponent>();
            var hitPool             = _world.Value.GetPool<HitComponent>();
            var effectRefPool       = _world.Value.GetPool<EffectAbilityRefComponent>();
            var moveSourceToCellPool = _world.Value.GetPool<MoveSourceToCellEffectComponent>();

            if (s.TargetEntityKeys == null) return;

            foreach (var key in s.TargetEntityKeys)
            {
                int targetEntity = ResolveTargetKey(key);
                if (targetEntity < 0) continue;

                foreach (var effect in effects)
                {
                    int effectEntity = _world.Value.NewEntity();
                    effectPool.Add(effectEntity);

                    ref var ctx = ref targetEntityPool.Add(effectEntity);
                    ctx.TargetEntity = targetEntity;
                    ctx.OwnerEntity  = ownerEntity;

                    ref var refComp = ref effectRefPool.Add(effectEntity);
                    refComp.AbilityEntity = abilityEntity;
                    refComp.StepIndex = s.StepIndex;

                    effect.AddEffect(_world.Value, effectEntity);

                    // Зеркалим захваченную клетку в self-contained эффект,
                    // чтобы apply-система не зависела от ChainState.
                    if (moveSourceToCellPool.Has(effectEntity) && s.HasCapturedCell)
                    {
                        ref var m = ref moveSourceToCellPool.Get(effectEntity);
                        m.HasCell = true;
                        m.Row = s.CapturedRow;
                        m.Col = s.CapturedCol;
                        m.OwnerId = s.CapturedCellOwnerId;
                    }

                    if (!hasProjectile)
                        hitPool.Add(effectEntity);
                }
            }
        }

        private System.Collections.Generic.List<Game.Core.Shared.Interface.IAbilityEffect>
            GetStepEffects(int abilityEntity, int stepIndex)
        {
            if (stepIndex <= 0)
            {
                var pool = _world.Value.GetPool<AbilityEffectContainerComponent>();
                if (!pool.Has(abilityEntity)) return null;
                return pool.Get(abilityEntity).AbilityEffects;
            }

            var chainPool = _world.Value.GetPool<AbilityChainContainerComponent>();
            if (!chainPool.Has(abilityEntity)) return null;
            ref var chain = ref chainPool.Get(abilityEntity);
            int idx = stepIndex - 1;
            if (chain.Steps == null || idx < 0 || idx >= chain.Steps.Count) return null;
            var step = chain.Steps[idx];
            if (step?.Effects == null || step.Effects.Count == 0) return null;

            var result = new System.Collections.Generic.List<Game.Core.Shared.Interface.IAbilityEffect>(step.Effects.Count);
            foreach (var e in step.Effects) result.Add(e);
            return result;
        }

        private int ResolveTargetKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            // Псевдо-ключ игрока: "PLAYER:{PlayerId}"
            if (key.StartsWith("PLAYER:"))
            {
                if (int.TryParse(key.Substring(7), out int pid))
                {
                    foreach (var pe in _playerFilter.Value)
                        if (_playerPool.Value.Get(pe).PlayerId == pid) return pe;
                }
                return -1;
            }

            return _state.Value.TryGetEntity(key, out int entity) ? entity : -1;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private int FindOwnerOf(int cardEntity)
        {
            if (!_ownerPool.Value.Has(cardEntity)) return -1;
            int ownerId = _ownerPool.Value.Get(cardEntity).OwnerId;

            foreach (var pe in _playerFilter.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId == ownerId)
                    return pe;
            }
            return -1;
        }
    }
}
