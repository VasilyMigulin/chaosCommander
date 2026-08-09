using System.Collections.Generic;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Скрэтч-буфер сущностей, призванных в ТЕКУЩЕМ резолве способности. Заполняет SummonEffect
    /// (актив), читает RunResolveAbilityQueueSystem сразу после применения эффектов, чтобы положить
    /// призванных в AbilityResolvedNetEvent.SummonedEntities → снапшот → пассив применит к ним
    /// модификаторы призыва. Лежит в Components, т.к. и Ability, и Systems ссылаются на Components.
    ///
    /// Безопасность: ECS однопоточный, резолв одной способности за тик, эффекты синхронны →
    /// нет реентерабельности. RunResolveAbilityQueueSystem чистит буфер перед каждым резолвом.
    /// </summary>
    public static class SummonScratch
    {
        public static readonly List<int> Summoned = new List<int>();

        // Занятые клетки фронта в ТЕКУЩЕМ резолве: размещение идёт через MoveCardToBoardEvent/CreateCardEvent
        // (позиция выставится позже в кадре), поэтому несколько спавнов за один резолв (в т.ч. RepeatEffect+
        // одиночный спавн) считали бы клетки свободными повторно → коллизия. Резервируем тут. (ownerId,col) упак.
        static readonly HashSet<long> _claimedCells = new HashSet<long>();

        public static void Clear() { Summoned.Clear(); _claimedCells.Clear(); }
        public static void Add(int entity) => Summoned.Add(entity);

        // (ownerId,row,col): ряд в ключе — «заполнить сторону» (задний ряд) не должно конфликтовать резервами
        // с фронтом той же колонки. Перегрузки без row — легаси-фронт (row 0 = BoardFrontRow.FrontRow).
        static long CellKey(int ownerId, int row, int col) => ((long)ownerId << 16) | (uint)((row & 0xFF) << 8) | (uint)(col & 0xFF);
        public static bool IsCellClaimed(int ownerId, int col) => IsCellClaimed(ownerId, 0, col);
        public static bool IsCellClaimed(int ownerId, int row, int col) => _claimedCells.Contains(CellKey(ownerId, row, col));
        public static void ClaimCell(int ownerId, int col) => ClaimCell(ownerId, 0, col);
        public static void ClaimCell(int ownerId, int row, int col) => _claimedCells.Add(CellKey(ownerId, row, col));
    }

    /// <summary>
    /// Модификаторы для ГЕНЕРИРУЕМЫХ карт (FillRow/SpawnCardOnBoard и пр.): CreateCardEvent обрабатывается
    /// отложенно (буфер CreateCardSystem), сущности в момент Apply эффекта ещё нет — поэтому эффект регистрирует
    /// модификаторы здесь ПО ДЕТЕРМИНИРОВАННОМУ ключу порождаемой карты, а CreateCardSystem применяет их сразу
    /// после материализации (модель уже инициализирована). СИНК ДАРОМ: generate-эффекты ре-ранятся на ОБОИХ
    /// клиентах (пассив реплеит резолв) с теми же ключами → скрэтч заполняется и применяется зеркально
    /// (в отличие от summon-семейства, где размещает только актив и модификаторы едут pending-каналом).
    /// Статик как SummonScratch: резолв однопоточный; чистится в CreateCardSystem.Init (новый матч).
    /// </summary>
    public static class GeneratedModScratch
    {
        struct Entry
        {
            public string Key;
            public int SourceCard;
            public IReadOnlyList<IEffect> Mods;
        }

        static readonly List<Entry> _entries = new List<Entry>();

        public static void Register(string key, int sourceCard, IReadOnlyList<IEffect> mods)
        {
            if (string.IsNullOrEmpty(key) || mods == null || mods.Count == 0) return;
            _entries.Add(new Entry { Key = key, SourceCard = sourceCard, Mods = mods });
        }

        public static bool TryConsume(string key, out int sourceCard, out IReadOnlyList<IEffect> mods)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key != key) continue;
                sourceCard = _entries[i].SourceCard;
                mods = _entries[i].Mods;
                _entries.RemoveAt(i);
                return true;
            }
            sourceCard = -1; mods = null; return false;
        }

        public static void Clear() => _entries.Clear();
    }
}
