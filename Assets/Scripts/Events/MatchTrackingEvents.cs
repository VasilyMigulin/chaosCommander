using Game.Core.Service; 

namespace Game.Core.Events
{
    // ── Match tracking events ────────────────────────────────────────────────
    // Публикуются из ECS-систем в момент совершения действия.
    // Подписчик: MatchTracker.

    /// <summary>Карта разыграна с руки на поле.</summary>
    public struct CardTrackedEvent : IGameEvent
    {
        public int CardEntity;
        public int ModelId;
        public string CardName;
        public int PlayerId;
        public EnumService.Rarity Rarity;
        public EnumService.Element Element;
        public EnumService.CardType CardType;
    }

    /// <summary>Нанесён урон (от атаки существа или способности).</summary>
    public struct DamageTrackedEvent : IGameEvent
    {
        public int SourceEntity;
        public int TargetEntity;
        public int SourcePlayerId;
        public int TargetPlayerId;
        public int Amount;
    }

    /// <summary>Существо погибло.</summary>
    public struct DeathTrackedEvent : IGameEvent
    {
        public int CreatureEntity;
        public int OwnerId;
        public int KillerPlayerId;  // -1 если неизвестно
        public string CardName;
        public int ModelId;
    }

    // ── Хуки задач (Game.Core.Progression): публикуются из ECS для трекеров summon/fill/charm/deathrattle ──

    /// <summary>Своя сторона поля заполнена (нет свободных клеток призыва). Публикует BoardFillTrackSystem на
    /// РЕБРЕ «было место → мест нет». OwnerId — владелец стороны (у издателя это локальный игрок).</summary>
    public struct OwnSideFilledTrackedEvent : IGameEvent { public int OwnerId; }

    /// <summary>Старт хода игрока: сколько чар он контролирует (каждая = +1 к «чары × ходов»). Публикует
    /// RunTurnStartSystem только для локального игрока (его ход).</summary>
    public struct CharmsControlledTrackedEvent : IGameEvent { public int OwnerId; public int Count; }

    /// <summary>Сработал хрип «При смерти» (OnDie-триггер существа). OwnerId — владелец умершего существа.
    /// Публикует OnDieTrigger на обоих клиентах (в т.ч. в ход противника); трекер считает только своё.</summary>
    public struct DeathrattleTrackedEvent : IGameEvent { public int OwnerId; }
}
