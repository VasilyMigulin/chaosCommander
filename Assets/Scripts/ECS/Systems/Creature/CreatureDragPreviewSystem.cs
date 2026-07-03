using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Драг-розыгрыш существа «под пальцем» (косметика, только локальный ввод). UI (PlayCardView) шлёт
    /// сырой ввод (CreatureDragMoved/Released, экранная позиция) и НИЧЕГО не знает о Mono; вся
    /// Mono-осознанная работа здесь:
    ///   • рейкаст в плоскость доски (BoardView.TryBoardPoint) + границы (IsWithinBoard);
    ///   • превью-модель существа под пальцем (префаб из ViewRefComponent, MaterializeEffect dissolve-in,
    ///     покачивание наклоном по скорости движения — дёшево, без физики);
    ///   • подсветка ближайшей СВОБОДНОЙ клетки фронта;
    ///   • ответ UI: CreatureDragOverFieldChangedEvent (карта растворяется/проявляется);
    ///   • на отпускании: валидная клетка → ШТАТНЫЙ коммит (RequestCardCastEvent + CellClickEvent — тот же
    ///     путь, что клик; оплата/валидация/рефанд в существующих системах) + ViewSpawnFromComponent (вью
    ///     «въедет» из-под пальца, PlaySlideInvoke); иначе → TargetSelectionCancelledEvent (UI вернёт карту).
    ///
    /// Регистрация: КОНЕЦ _generalSystems — CellClickEvent, созданный здесь, не виден RunSelectCellSystem
    /// (он уже отработал в этом кадре), но виден роутеру (_abilitySystems) и RunSelectCellBoardSystem
    /// (_creatureSystems) в этом же кадре; DelHere чистит его в конце кадра. Синка не требует.
    /// </summary>
    public sealed class CreatureDragPreviewSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsCustomInject<BoardView> _board = default;
        readonly EcsWorldInject _world = default;

        readonly EcsPoolInject<CreatureTag>            _creaturePool = default;
        readonly EcsPoolInject<ViewRefComponent>       _viewPool     = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool      = default;
        readonly EcsPoolInject<PlayerComponent>        _playerPool   = default;
        readonly EcsPoolInject<ViewSpawnFromComponent> _spawnFromPool = default;
        readonly EcsPoolInject<RequestCardCastEvent>   _castReqPool  = default;
        readonly EcsPoolInject<CellClickEvent>         _clickPool    = default;

        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _localPlayer = default;
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent>, Exc<DeadTag>> _boardCreatures = default;

        const float PreviewLift    = 0.8f;   // высота превью над доской («держим в руке»)
        const float StaleSeconds   = 1.0f;   // нет Moved дольше → драг умер, прибрать превью

        // Состояние текущего драга (один за раз — ввод одним пальцем/мышью).
        bool _subscribed;
        int _card = -1;
        Vector2 _screen;
        bool _movedThisFrame;
        bool _releasePending;
        float _lastMoveTime;

        GameObject _preview;
        bool _overField;
        bool _startedSent;   // «драг существа начался» отправлен (один раз на драг)
        int _validCol = -1;

        public void Init(IEcsSystems systems)
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CreatureDragMovedEvent>(OnMoved);
            GameEventBus.Subscribe<CreatureDragReleasedEvent>(OnReleased);
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CreatureDragMovedEvent>(OnMoved);
            GameEventBus.Unsubscribe<CreatureDragReleasedEvent>(OnReleased);
            _subscribed = false;
            KillPreview();
        }

        void OnMoved(CreatureDragMovedEvent e)
        {
            if (_card >= 0 && e.CardEntity != _card) return;   // один драг за раз
            _card = e.CardEntity;
            _screen = e.ScreenPosition;
            _movedThisFrame = true;
            _lastMoveTime = Time.time;
        }

        void OnReleased(CreatureDragReleasedEvent e)
        {
            if (e.CardEntity != _card) return;
            _screen = e.ScreenPosition;
            _releasePending = true;
        }

        public void Run(IEcsSystems systems)
        {
            if (_card < 0) return;

            if (_releasePending) { HandleRelease(); return; }

            if (_movedThisFrame)
            {
                _movedThisFrame = false;
                HandleMove();
            }
            else if (Time.time - _lastMoveTime > StaleSeconds)
            {
                // Драг оборвался без Released (релэйаут руки/деактивация вью) — прибираем БЕЗУСЛОВНО
                // (не только при живом превью: раньше _card без превью залипал навсегда и гейт
                // «один драг за раз» глушил превью для всех остальных карт). SetOverField(false)
                // вернёт карте видимость, если она осталась растворённой.
                SetOverField(false);
                ResetDrag();
            }
        }

        // ── движение пальца ───────────────────────────────────────────────────

        void HandleMove()
        {
            if (_board.Value == null || !_creaturePool.Value.Has(_card)) { ResetDrag(); return; }

            var cam = BattleCameraSelector.ActiveCamera;
            if (cam == null) return;

            // Один раз на драг: UI узнаёт, что тащит СУЩЕСТВО (размещение только дропом на поле) —
            // отпускание вне поля вернёт карту в руку, а не уйдёт в старый клик-путь (OnUse).
            if (!_startedSent)
            {
                _startedSent = true;
                GameEventBus.Publish(new CreatureDragStartedEvent { CardEntity = _card });
            }

            bool over = _board.Value.TryBoardPoint(cam, _screen, out Vector3 point)
                     && _board.Value.IsWithinBoard(point);

            if (!over)
            {
                SetOverField(false);
                KillPreview();
                _validCol = -1;
                _board.Value.ClearAllHighlights();
                return;
            }

            int side = LocalSide();
            if (side < 0) return;

            if (_preview == null) SpawnPreview(point);
            FollowPreview(point);
            SetOverField(true);

            // ближайшая свободная клетка фронта → подсветка
            int col = _board.Value.NearestFrontCol(side, point);
            bool free = col >= 0 && !IsOccupied(0, col, side);
            _validCol = free ? col : -1;

            _board.Value.ClearAllHighlights();
            if (free)
                _board.Value.GetCell(0, col, side)?.SetHighlight(CellHighlight.Target);
        }

        // ── отпускание ────────────────────────────────────────────────────────

        void HandleRelease()
        {
            _releasePending = false;
            var world = _world.Value;
            int side = LocalSide();

            if (_overField && _preview != null && _validCol >= 0 && side >= 0 && !IsOccupied(0, _validCol, side))
            {
                // Вью существа «въедет» из точки превью (SpawnCreatureViewSystem → PlaySlideInvoke).
                if (!_spawnFromPool.Value.Has(_card)) _spawnFromPool.Value.Add(_card);
                _spawnFromPool.Value.Get(_card).From = _preview.transform.position;

                // ШТАТНЫЙ коммит: каст (оплата в роутере) + клик по клетке (RunSelectCellBoardSystem).
                _castReqPool.Value.Add(world.NewEntity()) = new RequestCardCastEvent { CardEntity = _card };
                _clickPool.Value.Add(world.NewEntity()) = new CellClickEvent { Row = 0, Col = _validCol, OwnerId = side };
            }
            else if (_overField)
            {
                // Держал НАД полем, но клетка невалидна → UI спрятал карту и ждёт ответа — вернуть штатной отменой.
                GameEventBus.Publish(new TargetSelectionCancelledEvent { CardEntity = _card });
            }
            // НЕ над полем — ресет МОЛЧА: UI сам ведёт возврат в руку / старый путь (OnUse), Cancel здесь
            // конфликтовал бы с ними (Released теперь шлётся на КАЖДОЕ отпускание, не только над полем).

            _board.Value?.ClearAllHighlights();
            ResetDrag();
        }

        // ── превью ────────────────────────────────────────────────────────────

        void SpawnPreview(Vector3 point)
        {
            if (!_viewPool.Value.Has(_card)) return;
            var prefab = _viewPool.Value.Get(_card).Prefab;
            if (prefab == null) return;

            _preview = Object.Instantiate(prefab, point + Vector3.up * PreviewLift, Quaternion.identity);
            _preview.name = "CreatureDragPreview";

            // Превью — чистый визуал: без коллайдеров/логики/физики.
            foreach (var c in _preview.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var rb in _preview.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            var cv = _preview.GetComponent<CreatureView>();
            if (cv != null) cv.enabled = false;

            var mat = _preview.GetComponent<MaterializeEffect>();
            if (mat == null) mat = _preview.AddComponent<MaterializeEffect>();
            mat.PlayIn(0.35f);
        }

        void FollowPreview(Vector3 point)
        {
            if (_preview == null) return;
            var t = _preview.transform;
            Vector3 target = point + Vector3.up * PreviewLift;
            Vector3 vel = target - t.position;
            t.position = Vector3.Lerp(t.position, target, 0.5f);

            // Покачивание: наклон в сторону движения (дёшево, без рэгдолла).
            float tiltX = Mathf.Clamp(vel.z * 40f, -22f, 22f);
            float tiltZ = Mathf.Clamp(-vel.x * 40f, -22f, 22f);
            var sway = Quaternion.Euler(tiltX, t.eulerAngles.y, tiltZ);
            t.rotation = Quaternion.Slerp(t.rotation, sway, 0.25f);
        }

        void KillPreview()
        {
            if (_preview != null) { Object.Destroy(_preview); _preview = null; }
        }

        void ResetDrag()
        {
            KillPreview();
            _card = -1;
            _validCol = -1;
            _overField = false;
            _startedSent = false;
            _movedThisFrame = false;
            _releasePending = false;
        }

        // ── хелперы ───────────────────────────────────────────────────────────

        void SetOverField(bool over)
        {
            if (_overField == over) return;
            _overField = over;
            GameEventBus.Publish(new CreatureDragOverFieldChangedEvent { CardEntity = _card, OverField = over });
        }

        int LocalSide()
        {
            foreach (var pe in _localPlayer.Value)
                return _playerPool.Value.Get(pe).PlayerId;
            return -1;
        }

        bool IsOccupied(int row, int col, int ownerSide)
        {
            foreach (var e in _boardCreatures.Value)
            {
                ref var p = ref _posPool.Value.Get(e);
                if (p.Row == row && p.Col == col && p.OwnerId == ownerSide) return true;
            }
            return false;
        }
    }
}
