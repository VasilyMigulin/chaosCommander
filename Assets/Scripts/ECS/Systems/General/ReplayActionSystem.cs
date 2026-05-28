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
                Replay(action);
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
            // Хук для авторитарного реплея способностей. Сейчас не используется:
            // авто-триггеры (OnCast и т.д.) детерминированно воспроизводятся через реплей каста,
            // а случайные цели способностей детерминированы стабильным seed
            // (см. RunResolveAbilityEffectSystem.ComputeStableSeed).
            // Реализовать, если появится недетерминизм, который нельзя стабилизировать seed'ом.
            Debug.LogWarning($"[ReplayActionSystem] ActionAbilityData received but not needed (source={s.SourceEntityKey}, ability={s.AbilityIndex})");
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
