using Game.Core.Events;
using Game.Core.Service;   // EnumService.ResourceType

namespace Game.Core.Progression
{
    // Трекеры на УЖЕ существующих событиях шины. Четыре типа (summon_creatures / fill_board / charm_turns /
    // deathrattle) требуют новых публикаций из ECS — их трекеры добавятся отдельно.

    // ── Матч ──────────────────────────────────────────────────────────────────

    /// <summary>«Сыграйте X игр»: +1 за каждый завершённый матч (исход неважен).</summary>
    public sealed class PlayGamesTracker : TaskTracker
    {
        public override string Type => TaskTypes.PlayGames;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<MatchEndedEvent>(OnEnd);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<MatchEndedEvent>(OnEnd);
        void OnEnd(MatchEndedEvent e) => Report(1);
    }

    /// <summary>«Одержите X побед»: +1 если локальный игрок победил.</summary>
    public sealed class WinGamesTracker : TaskTracker
    {
        public override string Type => TaskTypes.WinGames;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<MatchEndedEvent>(OnEnd);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<MatchEndedEvent>(OnEnd);
        void OnEnd(MatchEndedEvent e) { if (e.LocalResult == MatchResult.Win) Report(1); }
    }

    // ── Карты / бой ───────────────────────────────────────────────────────────

    /// <summary>«Разыграйте X карт»: только СВОИ касты (CardPlayedEvent.PlayerId == локальный игрок).</summary>
    public sealed class PlayCardsTracker : TaskTracker
    {
        public override string Type => TaskTypes.PlayCards;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<CardPlayedEvent>(OnPlayed);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<CardPlayedEvent>(OnPlayed);
        void OnPlayed(CardPlayedEvent e)
        {
            int local = TaskTrackingService.LocalPlayerId;
            if (local >= 0 && e.PlayerId == local) Report(1);
        }
    }

    /// <summary>«Уничтожьте X существ»: наше убийство ЧУЖОГО существа (убийца — мы, владелец — не мы).</summary>
    public sealed class KillCreaturesTracker : TaskTracker
    {
        public override string Type => TaskTypes.KillCreatures;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<DeathTrackedEvent>(OnDeath);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<DeathTrackedEvent>(OnDeath);
        void OnDeath(DeathTrackedEvent e)
        {
            int local = TaskTrackingService.LocalPlayerId;
            if (local >= 0 && e.KillerPlayerId == local && e.OwnerId != local) Report(1);
        }
    }

    /// <summary>«Нанесите X урона»: суммарный урон, нанесённый НАМИ по ВРАГУ (source — мы, target — не мы).</summary>
    public sealed class DealDamageTracker : TaskTracker
    {
        public override string Type => TaskTypes.DealDamage;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<DamageTrackedEvent>(OnDamage);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<DamageTrackedEvent>(OnDamage);
        void OnDamage(DamageTrackedEvent e)
        {
            int local = TaskTrackingService.LocalPlayerId;
            if (local >= 0 && e.SourcePlayerId == local && e.TargetPlayerId != local && e.Amount > 0)
                Report(e.Amount);
        }
    }

    // ── Мана ──────────────────────────────────────────────────────────────────
    // ResourceChangedEvent даёт только ТЕКУЩЕЕ значение (NewValue), не дельту — считаем diff от прошлого
    // снапшота. Базовое значение сбрасываем в начале каждого матча (PlayerAssignedEvent), чтобы первый
    // снапшот матча не улетел в «восстановление».

    /// <summary>«Потратьте X маны»: сумма УМЕНЬШЕНИЙ маны локального игрока.</summary>
    public sealed class SpendManaTracker : TaskTracker
    {
        int _last = -1;   // -1 = базовое ещё не установлено
        public override string Type => TaskTypes.SpendMana;
        public override void Subscribe()
        {
            GameEventBus.SubscribePersistent<ResourceChangedEvent>(OnRes);
            GameEventBus.SubscribePersistent<PlayerAssignedEvent>(OnMatch);
        }
        public override void Unsubscribe()
        {
            GameEventBus.UnsubscribePersistent<ResourceChangedEvent>(OnRes);
            GameEventBus.UnsubscribePersistent<PlayerAssignedEvent>(OnMatch);
        }
        void OnMatch(PlayerAssignedEvent e) { if (e.IsLocalPlayer) _last = -1; }
        void OnRes(ResourceChangedEvent e)
        {
            if (!e.isLocalPlayer || e.Type != EnumService.ResourceType.Mana) return;
            if (_last >= 0 && e.NewValue < _last) Report(_last - e.NewValue);
            _last = e.NewValue;
        }
    }

    /// <summary>«Восстановите X маны»: сумма УВЕЛИЧЕНИЙ маны локального игрока.</summary>
    public sealed class RestoreManaTracker : TaskTracker
    {
        int _last = -1;
        public override string Type => TaskTypes.RestoreMana;
        public override void Subscribe()
        {
            GameEventBus.SubscribePersistent<ResourceChangedEvent>(OnRes);
            GameEventBus.SubscribePersistent<PlayerAssignedEvent>(OnMatch);
        }
        public override void Unsubscribe()
        {
            GameEventBus.UnsubscribePersistent<ResourceChangedEvent>(OnRes);
            GameEventBus.UnsubscribePersistent<PlayerAssignedEvent>(OnMatch);
        }
        void OnMatch(PlayerAssignedEvent e) { if (e.IsLocalPlayer) _last = -1; }
        void OnRes(ResourceChangedEvent e)
        {
            if (!e.isLocalPlayer || e.Type != EnumService.ResourceType.Mana) return;
            if (_last >= 0 && e.NewValue > _last) Report(e.NewValue - _last);
            _last = e.NewValue;
        }
    }
}
