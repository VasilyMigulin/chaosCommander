using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === helper (static) ===
    /// <summary>
    /// Навигация по доске для ввода/маршрутов: граф соседства клеток (тот же, что исторически жил
    /// локальной функцией в RunSelectCellSystem) + BFS-достижимость по ПУСТЫМ клеткам в бюджете скорости.
    /// Общий для RunSelectCellSystem (подсветка/клик) и RunPathMoveSystem (исполнение маршрута).
    /// ИИ (RunAiTurnSystem) держит СВОЮ копию правил соседства — правила не меняются, копию не трогаем.
    ///
    /// Геометрия: 2 ряда × 5 колонок на сторону; row 0 — «тыл» стороны (у её аватара), row 1 — линия
    /// контакта. Вперёд: row0→row1 своей стороны, row1→row1 ЧУЖОЙ стороны (пересечение фронта).
    /// </summary>
    public static class BoardNav
    {
        public const int Cols = 5;

        public static IEnumerable<(int row, int col, int owner)> GetNeighbours(int row, int col, int owner)
        {
            if (col > 0) yield return (row, col - 1, owner);
            if (col < Cols - 1) yield return (row, col + 1, owner);
            // Назад (в тыл своей половины)
            if (row > 0) yield return (row - 1, col, owner);
            // Вперёд: row=0→row=1 (внутри своей стороны), row=1→row=1 другого owner (пересечение фронта)
            if (row < 1)
                yield return (row + 1, col, owner);
            else
                yield return (1, col, owner == 1 ? 2 : 1);
        }

        public static bool IsNeighbour(int r1, int c1, int o1, int r2, int c2, int o2)
        {
            foreach (var (nr, nc, no) in GetNeighbours(r1, c1, o1))
                if (nr == r2 && nc == c2 && no == o2) return true;
            return false;
        }

        /// <summary>Занята ли клетка ЛЮБЫМ живым существом на борде. creaturesFilter — фильтр
        /// Inc&lt;CreatureTag, BoardTag, BoardPositionComponent, ...&gt; Exc&lt;DeadTag&gt; вызывающей системы.</summary>
        public static bool IsOccupied(EcsFilter creaturesFilter, EcsPool<BoardPositionComponent> posPool,
                                      int row, int col, int owner)
        {
            foreach (var e in creaturesFilter)
            {
                ref var p = ref posPool.Get(e);
                if (p.Row == row && p.Col == col && p.OwnerId == owner) return true;
            }
            return false;
        }

        /// <summary>
        /// Результат BFS-волны: стоимость достижения каждой ПУСТОЙ клетки (шагов от старта, старт = 0)
        /// и parent-ссылки для восстановления пути. Ключ — (row, col, owner).
        /// </summary>
        public sealed class Reachability
        {
            public readonly Dictionary<(int, int, int), int> Cost = new();
            public readonly Dictionary<(int, int, int), (int, int, int)> Parent = new();
            public (int, int, int) Start;

            /// <summary>Путь от старта до клетки (без стартовой, с конечной). Null, если недостижима.</summary>
            public List<(int Row, int Col, int Owner)> PathTo((int, int, int) cell)
            {
                if (!Cost.ContainsKey(cell)) return null;
                var path = new List<(int, int, int)>();
                var cur = cell;
                while (cur != Start)
                {
                    path.Add(cur);
                    cur = Parent[cur];
                }
                path.Reverse();
                return path;
            }
        }

        /// <summary>
        /// BFS по ПУСТЫМ клеткам от позиции существа, не дальше maxSteps шагов (бюджет Speed.Remaining).
        /// Стартовая клетка (занятая самим существом) в Cost с 0; занятые клетки непроходимы и в волну
        /// не попадают. Доска крошечная (20 клеток) — аллокации на клик несущественны.
        /// </summary>
        /// <param name="isMeleeLocked">«Защитник»: клетка, СМЕЖНАЯ с вражеским Защитником, — ТУПИК волны:
        /// достижима (можно встать и атаковать), но волна НЕ продолжается ДАЛЬШЕ неё — обойти/пройти мимо
        /// Защитника нельзя. Если стартовая клетка сама такая — существо вообще никуда не уходит (только
        /// атака, как и было задумано изначально: «нет других вариантов действия»). null → без ограничения.</param>
        public static Reachability ComputeReachable(EcsFilter creaturesFilter, EcsPool<BoardPositionComponent> posPool,
                                                    int startRow, int startCol, int startOwner, int maxSteps,
                                                    Func<(int row, int col, int owner), bool> isMeleeLocked = null)
        {
            var r = new Reachability { Start = (startRow, startCol, startOwner) };
            r.Cost[r.Start] = 0;
            if (maxSteps <= 0) return r;

            var queue = new Queue<(int, int, int)>();
            queue.Enqueue(r.Start);

            while (queue.Count > 0)
            {
                var (cr, cc, co) = queue.Dequeue();
                int cost = r.Cost[(cr, cc, co)];
                if (cost >= maxSteps) continue;
                if (isMeleeLocked != null && isMeleeLocked((cr, cc, co))) continue;   // тупик: дальше не идём

                foreach (var next in GetNeighbours(cr, cc, co))
                {
                    if (r.Cost.ContainsKey(next)) continue;                                   // уже достигнута дешевле/так же
                    if (IsOccupied(creaturesFilter, posPool, next.row, next.col, next.owner)) continue;   // занято → непроходимо
                    r.Cost[next] = cost + 1;
                    r.Parent[next] = (cr, cc, co);
                    queue.Enqueue(next);
                }
            }
            return r;
        }
    }
}
