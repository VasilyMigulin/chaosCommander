using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Разбирает ForceSeekAttackTag (Позвать стражу и т.п.): ищет БЛИЖАЙШЕЕ вражеское существо (BFS по всей
    /// доске, без учёта Speed.Remaining — маршрут бесплатный) и, если нашёл, вешает PathMoveComponent{Free=true}
    /// — дальше существо доходит и бьёт штатным RunPathMoveSystem/MoveSystem/AttackSystem (реакции на
    /// атаку/движение срабатывают как обычно, это настоящие MoveRequestEvent/AttackRequestEvent). Нет
    /// достижимого врага — тег просто снимается, существо стоит на месте.
    ///
    /// Тег ставит ForceAttackEffect (Ability-сборка НЕ ссылается на Ecs.Systems, где живёт BoardNav) — эффект
    /// лишь помечает намерение на СВЕЖЕСОЗДАННОЙ сущности (SummonModifier), путь считает эта система. Работает
    /// на ОБОИХ клиентах одинаково: тег ставится при материализации существа (ре-ран генерации, детерминизм
    /// как у обычных SummonModifiers), доска на этот момент зеркальна → BFS даёт идентичный результат.
    /// Регистрация: сразу перед RunPathMoveSystem (EcsRunHandler/TutorialEcsHandler).
    /// </summary>
    public sealed class ForceSeekAttackSystem : IEcsRunSystem
    {
        // С запасом больше диаметра доски (2 ряда × 5 колонок на сторону × 2 стороны).
        const int MaxSearchSteps = 12;

        readonly EcsFilterInject<Inc<ForceSeekAttackTag, CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _seekers = default;
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent, OwnerComponent>, Exc<DeadTag>> _boardCreatures = default;

        readonly EcsPoolInject<ForceSeekAttackTag>     _tagPool   = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool   = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool = default;
        readonly EcsPoolInject<PathMoveComponent>      _pathPool  = default;

        readonly List<int> _scratch = new List<int>();

        public void Run(IEcsSystems systems)
        {
            if (_seekers.Value.GetEntitiesCount() == 0) return;

            // Снимок: тег снимаем у ВСЕХ сразу, дальше фильтр не мешает (мутация во время итерации — плохо).
            _scratch.Clear();
            foreach (var e in _seekers.Value) _scratch.Add(e);

            foreach (var e in _scratch)
            {
                _tagPool.Value.Del(e);
                if (_pathPool.Value.Has(e)) continue;   // уже есть маршрут (не должно, но не перебиваем)

                ref var pos = ref _posPool.Value.Get(e);
                int ownerId = _ownerPool.Value.Get(e).OwnerId;
                var reach = BoardNav.ComputeReachable(_boardCreatures.Value, _posPool.Value, pos.Row, pos.Col, pos.OwnerId, MaxSearchSteps);

                int bestTarget = -1;
                (int, int, int) bestCell = default;
                int bestCost = int.MaxValue;

                foreach (var enemy in _boardCreatures.Value)
                {
                    if (_ownerPool.Value.Get(enemy).OwnerId == ownerId) continue;   // не враг
                    ref var ep = ref _posPool.Value.Get(enemy);

                    foreach (var (nr, nc, no) in BoardNav.GetNeighbours(ep.Row, ep.Col, ep.OwnerId))
                    {
                        if (!reach.Cost.TryGetValue((nr, nc, no), out int cost)) continue;   // не достижимо/занято
                        if (cost >= bestCost) continue;
                        bestCost = cost;
                        bestTarget = enemy;
                        bestCell = (nr, nc, no);
                    }
                }

                if (bestTarget < 0) continue;   // врагов нет/не достать — просто стоим

                ref var path = ref _pathPool.Value.Add(e);
                path.Steps = reach.PathTo(bestCell) ?? new List<(int Row, int Col, int Owner)>();
                path.AttackTargetEntity = bestTarget;
                path.Free = true;
            }
        }
    }
}
