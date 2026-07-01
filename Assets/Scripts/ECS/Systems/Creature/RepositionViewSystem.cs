using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Двигает ВЬЮШКУ существа на клетку по ViewRepositionRequest (визуальный «переезд», напр. «занять
    /// место» Бешеной бабки). Логическую позицию уже выставил эффект — здесь только GameObject + клик-
    /// координаты вью. Идёт на обоих клиентах (эффект ставит запрос детерминированно), синка не требует.
    /// </summary>
    public sealed class RepositionViewSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsFilterInject<Inc<ViewRepositionRequest, ViewRefComponent>> _filter = default;
        readonly EcsPoolInject<ViewRepositionRequest> _reqPool  = default;
        readonly EcsPoolInject<ViewRefComponent>      _viewPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var e in _filter.Value)
            {
                ref var req = ref _reqPool.Value.Get(e);
                ref var vr  = ref _viewPool.Value.Get(e);

                if (vr.View != null && _boardView.Value != null)
                {
                    var cell = _boardView.Value.GetCell(req.Row, req.Col, req.OwnerId);
                    if (cell != null)
                    {
                        var view = vr.View.GetComponent<CreatureView>();
                        if (view != null)
                        {
                            view.PlayMove(cell.transform.position, onFinished: null);
                            view.SetCell(req.Row, req.Col, req.OwnerId);
                        }
                        else
                        {
                            vr.View.transform.position = cell.transform.position;
                        }
                    }
                }

                _reqPool.Value.Del(e);
            }
        }
    }
}
