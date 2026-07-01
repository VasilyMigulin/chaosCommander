using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // === class (OOP) ===
    /// <summary>
    /// Контекст способности. Держится КАЖДЫМ поведенческим объектом (триггер/правило/эффект/условие).
    /// Хранит ТОЛЬКО entity-id (int) + мир. Любое состояние ("где карта", "сколько урона")
    /// спрашиваем у world по id, а не кешируем ссылками на объекты состояния.
    /// </summary>
    public sealed class AbilityContext
    {
        public EcsWorld World;
        public int AbilityEntity;   // сущность способности (на ней висит AbilityRefComponent)
        public int CardEntity;      // карта-владелец
        public int PlayerEntity;    // игрок-владелец
        public int AbilityIndex;    // индекс способности в карте — нужен для снапшота действия (синк)
    }
}
