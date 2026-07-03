using UnityEngine;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Просьба заспавнить вью существа НЕ сразу в клетке, а в мировой точке From, откуда она плавно
    /// «въедет» в клетку (драг-розыгрыш: существо материализовалось под пальцем и переезжает на клетку).
    /// Ставит UI (драг-контроллер) на сущность карты перед размещением; SpawnCreatureViewSystem читает и
    /// снимает. Локально/косметично (только у активного клиента), синка не требует.
    /// </summary>
    public struct ViewSpawnFromComponent
    {
        public Vector3 From;
    }
}
