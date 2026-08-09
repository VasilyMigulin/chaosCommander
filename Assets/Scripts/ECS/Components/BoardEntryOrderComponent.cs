using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Порядок ВЫХОДА НА СТОЛ (монотонный штамп): «кто раньше вышел — тот раньше активирует свои
    /// эффекты» (классика ККИ, требование юзера 2026-07-29). Ставится при КАЖДОМ выходе на стол
    /// (RunMoveCardToBoardSystem — касты/призывы/чары/реплей; CreateCardSystem — генерация прямо на борд):
    /// повторный выход (воскрешение/баунс) даёт НОВЫЙ штамп — «заново вышел — снова младший».
    /// Потребляет RunResolveAbilityQueueSystem: способности одной волны каскада (начало/конец хода)
    /// резолвятся по возрастанию Seq. СИНК: сортировка нужна только АКТИВУ — пассив реплеит порядок
    /// актива из снапшотов (ActionAbilityData) как есть.
    /// </summary>
    public struct BoardEntryOrderComponent
    {
        public int Seq;
    }

    public static class BoardEntryOrder
    {
        static int _next;

        /// <summary>Новый матч (зовёт CreateCardSystem.Init — как GeneratedModScratch).</summary>
        public static void Clear() => _next = 0;

        public static void Stamp(EcsWorld world, int entity)
        {
            var pool = world.GetPool<BoardEntryOrderComponent>();
            if (!pool.Has(entity)) pool.Add(entity);
            pool.Get(entity).Seq = ++_next;
        }
    }
}
