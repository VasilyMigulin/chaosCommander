using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // === helper (static) ===
    /// <summary>
    /// Постоянный бонус к длительности БУДУЩИХ чар игрока (Зачарованный: «ваши чары в этом матче длятся
    /// на 1 дольше») — та же идея, что CastMultiplierService: match-lifetime числовое состояние на
    /// ownerId, которое сама карта смотрит в момент своей инициализации, без ECS-фильтрации (запрос тут
    /// точечный, «сколько у ownerId» — крутить EcsFilter незачем). CardCharmModel.OnInit читает Get() ОДИН
    /// РАЗ при инициализации КАЖДОЙ следующей чары; уже стоящие на столе чары бонус не переполучают.
    /// СИНК даром: бампает CardCharmModel.OnInit resolve мирроринг — то же свойство, что у CastMultiplierService.
    /// </summary>
    public static class CharmDurationBonusService
    {
        static readonly Dictionary<int, int> _bonusByOwner = new Dictionary<int, int>();

        public static void Add(int ownerId, int amount)
        {
            if (amount == 0) return;
            _bonusByOwner.TryGetValue(ownerId, out int cur);
            _bonusByOwner[ownerId] = cur + amount;
        }

        public static int Get(int ownerId)
            => _bonusByOwner.TryGetValue(ownerId, out int v) ? v : 0;

        public static void Clear() => _bonusByOwner.Clear();
    }
}
