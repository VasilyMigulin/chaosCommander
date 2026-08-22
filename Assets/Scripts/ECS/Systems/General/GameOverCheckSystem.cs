using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Определяет конец матча: у одного/нескольких игроков HP ≤ 0.
    ///
    /// ВАЖНО: проверка выполняется ТОЛЬКО когда осел каскад действий/способностей (нет урона/боя/очереди
    /// способностей в обработке) — чтобы лечащая способность внутри каскада успела вернуть HP и отменить
    /// поражение. Поэтому проверяем не в момент урона, а отдельной системой после оседания пайплайна.
    ///
    /// Все игроки с HP ≤ 0 → проигравшие. Если проиграли ВСЕ → ничья; иначе остальные побеждают
    /// (одновременная смерть обоих естественно даёт ничью).
    /// </summary>
    public sealed class GameOverCheckSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<PlayerComponent, HealthComponent>> _players = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool     = default;
        readonly EcsPoolInject<ActiveState>     _activePool = default;
        readonly EcsPoolInject<AvatarViewComponent> _avatarViewPool = default;   // визуальная реакция проигравшего (PlayDeath)

        // «Каскад в работе»: способности резолвятся/целятся, идёт бой или ждёт необработанный урон.
        readonly EcsFilterInject<Inc<AbilityCastEvent>>      _abilityCast   = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>> _abilityTarget = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>    _abilityQueued = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>  _attackAnim    = default;
        readonly EcsFilterInject<Inc<MovingTag>>             _moving        = default;
        readonly EcsFilterInject<Inc<TakeDamageEvent>>       _takeDamage    = default;
        readonly EcsFilterInject<Inc<AttackHitEvent>>        _attackHit     = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCast = default;   // #2: призыв → OnCast (деатрэттл может добить)

        public void Run(IEcsSystems systems)
        {
            if (MatchState.IsOver) return;
            if (CascadeBusy()) return;   // ждём оседания каскада (лечение ещё может вернуть HP)

            var losers  = new List<int>();
            var winners = new List<int>();
            int localPlayerId = -1;
            bool localIsLoser = false;

            foreach (var pe in _players.Value)
            {
                ref var p  = ref _playerPool.Value.Get(pe);
                bool dead = _hpPool.Value.Get(pe).Current <= 0;

                if (p.IsLocalPlayer) localPlayerId = p.PlayerId;

                if (dead)
                {
                    losers.Add(p.PlayerId);
                    if (p.IsLocalPlayer) localIsLoser = true;

                    // Визуальная реакция проигравшего аватара — fire-and-forget, ничего дальше её не ждёт
                    // (см. AvatarPlayerView.PlayDeath: матч и так закрывается попапом результата ниже).
                    if (_avatarViewPool.Value.Has(pe))
                    {
                        var avatarGo = _avatarViewPool.Value.Get(pe).View;
                        if (avatarGo != null) avatarGo.GetComponent<AvatarPlayerView>()?.PlayDeath();
                    }
                }
                else
                {
                    winners.Add(p.PlayerId);
                }
            }

            if (losers.Count == 0) return;

            MatchState.IsOver = true;

            // Снять ход/ввод у всех — матч окончен.
            foreach (var pe in _players.Value)
                if (_activePool.Value.Has(pe)) _activePool.Value.Del(pe);
            GameEventBus.Publish(new InputBlockedEvent());

            bool isDraw = winners.Count == 0;
            MatchResult localResult = isDraw
                ? MatchResult.Draw
                : (localIsLoser ? MatchResult.Lose : MatchResult.Win);

            GameEventBus.Publish(new MatchEndedEvent
            {
                LocalResult     = localResult,
                IsDraw          = isDraw,
                WinnerPlayerIds = winners.ToArray(),
                LoserPlayerIds  = losers.ToArray(),
            });

            UnityEngine.Debug.Log($"[GameOver] localPlayer={localPlayerId} result={localResult} " +
                $"draw={isDraw} winners=[{string.Join(",", winners)}] losers=[{string.Join(",", losers)}]");
        }

        bool CascadeBusy()
            => _abilityCast.Value.GetEntitiesCount()   > 0
            || _abilityTarget.Value.GetEntitiesCount() > 0
            || _abilityQueued.Value.GetEntitiesCount() > 0
            || _attackAnim.Value.GetEntitiesCount()    > 0
            || _moving.Value.GetEntitiesCount()        > 0
            || _takeDamage.Value.GetEntitiesCount()    > 0
            || _attackHit.Value.GetEntitiesCount()     > 0
            || _pendingOnCast.Value.GetEntitiesCount() > 0;   // #2: отложенный OnCast может изменить исход
    }
}
