using System;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит флаги типов целей способности. Вешается на entity способности при инициализации.
    /// Используется RunResolveAbilityEffectSystem для детерминированного выбора цели.
    /// Флаги комбинируются: EnemyCreature | EnemyPlayer = бьёт и существ и игрока врага.
    ///
    /// Модификаторы выборки:
    ///   Random     — цель выбирается случайно из подходящих (seed детерминирован)
    ///   ExcludeSelf — исключает entity кастера из пула целей (актуально для AllyCreature / All)
    /// </summary>
    public struct AbilityTargetFlagsComponent
    {
        public AbilityTargetFlags Flags;
    }

    [Flags]
    public enum AbilityTargetFlags
    {
        None           = 0,

        /// <summary>Существа противника на поле.</summary>
        EnemyCreature  = 1 << 0,

        /// <summary>Союзные существа на поле (не сам источник).</summary>
        AllyCreature   = 1 << 1,

        /// <summary>Сам кастующий игрок.</summary>
        AllyPlayer     = 1 << 2,

        /// <summary>Игрок противника (hero).</summary>
        EnemyPlayer    = 1 << 3,

        /// <summary>Карта-источник (self / кастер).</summary>
        Self           = 1 << 4,

        // ── модификаторы выборки ──────────────────────────────────────────
        /// <summary>
        /// Цель выбирается случайно из всех подходящих.
        /// Seed передаётся детерминировано (через CastEvent или сетевой пакет),
        /// поэтому оба клиента выбирают одинаковую цель.
        /// </summary>
        Random         = 1 << 5,

        /// <summary>
        /// Исключает entity самого кастера из пула целей.
        /// Полезно для эффектов типа «союзное существо кроме себя».
        /// </summary>
        ExcludeSelf    = 1 << 6,

        // ── удобные комбо ──────────────────────────────────────────────────
        /// <summary>Все существа противника + игрок противника.</summary>
        AllEnemies     = EnemyCreature | EnemyPlayer,

        /// <summary>Все союзные существа + свой игрок.</summary>
        AllAllies      = AllyCreature | AllyPlayer,

        /// <summary>Все существа на поле (союзники + враги).</summary>
        AllCreatures   = EnemyCreature | AllyCreature,

        /// <summary>Абсолютно все (оба игрока + все существа).</summary>
        All            = EnemyCreature | AllyCreature | AllyPlayer | EnemyPlayer,

        /// <summary>Случайный враг (существо или игрок).</summary>
        RandomEnemy    = AllEnemies | Random,

        /// <summary>Случайное союзное существо, кроме кастера.</summary>
        RandomAlly     = AllyCreature | Random | ExcludeSelf,
    }
}
