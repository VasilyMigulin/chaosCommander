namespace Game.Core.Service
{
    /// <summary>
    /// Единый набор флагов целей для всей игры.
    /// Используется и эффектами способностей (кого бьёт), и аурами (на кого действует),
    /// и требованиями выбора при розыгрыше карты (что должен выбрать игрок).
    ///
    /// Комбинируется: EnemyCreature | AllyCreature = любое существо.
    /// Модификаторы: Random (выбрать случайно), All (затронуть всех подходящих),
    /// ExcludeSelf (исключить кастера).
    /// </summary>
    [System.Flags]
    public enum TargetMask
    {
        None          = 0,

        // ── Сущности-цели ─────────────────────────────────────────────
        Self          = 1 << 0,
        AllyCreature  = 1 << 1,
        EnemyCreature = 1 << 2,
        AllyPlayer    = 1 << 3,
        EnemyPlayer   = 1 << 4,

        // ── Клетки доски (размещение / полевые выборы) ────────────────
        /// <summary>Пустая клетка.</summary>
        EmptyCell     = 1 << 5,
        /// <summary>Ограничение: только своя фронтальная линия (row 0). Комбинируется с EmptyCell.</summary>
        OwnFrontRow   = 1 << 6,

        // ── Модификаторы выбора ───────────────────────────────────────
        /// <summary>Выбрать случайно среди подходящих.</summary>
        Random        = 1 << 7,
        /// <summary>Затронуть ВСЕХ подходящих (field), а не одного/первого.</summary>
        All           = 1 << 8,
        /// <summary>Исключить кастера из пула целей.</summary>
        ExcludeSelf   = 1 << 9,

        // ── Удобные комбо ─────────────────────────────────────────────
        AnyCreature   = AllyCreature | EnemyCreature,
        AllAllies     = AllyCreature | AllyPlayer,
        AllEnemies    = EnemyCreature | EnemyPlayer,
        BothPlayers   = AllyPlayer  | EnemyPlayer,
        Everyone      = AnyCreature | BothPlayers | Self,

        /// <summary>Пустая клетка своей фронтальной линии — для размещения существа.</summary>
        OwnFrontCell  = EmptyCell | OwnFrontRow,

        RandomEnemyCreature = EnemyCreature | Random,
        RandomAllyCreature  = AllyCreature  | Random,
        RandomAnyCreature   = AnyCreature   | Random,
    }

    public static class TargetMaskExtensions
    {
        public static bool Has(this TargetMask mask, TargetMask flag) => (mask & flag) != 0;

        /// <summary>
        /// Подходит ли существо под маску по принадлежности (союзник/враг) с учётом ExcludeSelf.
        /// isSource — является ли проверяемое существо самим кастером.
        /// </summary>
        public static bool MatchesCreature(this TargetMask mask, bool isAlly, bool isSource = false)
        {
            if (isSource && mask.Has(TargetMask.ExcludeSelf)) return false;
            if (isAlly  && mask.Has(TargetMask.AllyCreature))  return true;
            if (!isAlly && mask.Has(TargetMask.EnemyCreature)) return true;
            return false;
        }

        /// <summary>Маска нацелена на клетку доски (размещение/полевой выбор), а не на сущность.</summary>
        public static bool TargetsCell(this TargetMask mask) => mask.Has(TargetMask.EmptyCell);

        /// <summary>Маска нацелена на существ.</summary>
        public static bool TargetsCreature(this TargetMask mask) => mask.Has(TargetMask.AnyCreature);
    }
}
