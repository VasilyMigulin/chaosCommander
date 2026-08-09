using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Исполняет маршрут PathMoveComponent («идти на N клеток [и ударить]» одним кликом) ПО ОДНОМУ шагу:
    /// ждёт, пока осядет анимация предыдущего шага (MovingTag/AttackAnimPendingTag/PendingOnCast — те же
    /// гейты, что у пассивного реплея), затем эмитит следующий обычный MoveRequestEvent; когда шаги
    /// кончились — финальный AttackRequestEvent (если была цель и она всё ещё валидна).
    ///
    /// Обычно компонент ставит ТОЛЬКО локальный ввод активного клиента (RunSelectCellSystem): каждый
    /// шаг/атака публикует штатные CreatureMovedEvent/CreatureAttackedEvent → CollectActionSystem шлёт
    /// ActionMoveData×N (+ActionAttackData) → пассив реплеит их по одному, синк без новых каналов.
    ///
    /// Исключение — Free-маршрут (ForceAttackEffect, «Позвать стражу»): его ставит ЭФФЕКТ способности как
    /// SummonModifier, который ре-ранится ДЕТЕРМИНИРОВАННО на ОБОИХ клиентах (как обычные SummonModifiers).
    /// Здесь система тоже отрабатывает на обоих — но CollectActionSystem шлёт CreatureMovedEvent/
    /// CreatureAttackedEvent в сеть только по гейту IsOwnCard, так что реально уходит по сети только с
    /// клиента-владельца существа; на зеркале то же самое просчитывается локально и просто не досылается
    /// повторно (тот же принцип, что у generate-эффектов). Free пропускается через MoveRequestEvent.Free/
    /// AttackRequestEvent.Free — MoveSystem/AttackSystem не проверяют и не тратят SpeedComponent.Remaining.
    ///
    /// Прерывание маршрута (компонент снимается, остаток пути отменяется):
    ///   • существо умерло (DeadTag) или ушло с борда;
    ///   • ход владельца закончился (у владельца нет ActiveState);
    ///   • следующая клетка стала ЗАНЯТОЙ (на пути что-то появилось) или (не-Free) скорость кончилась;
    ///   • цель атаки умерла/не смежна к моменту удара — движение уже состоялось, атака просто не бьётся.
    /// AttacksUsed тратится ТОЛЬКО в момент фактической эмиссии обычной (не-Free) атаки — прерванный
    /// маршрут не съедает лимит атак; Free-атака (бонусная) лимит вообще не трогает.
    /// Регистрация: _generalSystems, после RunSelectCellSystem, до MoveSystem (EcsRunHandler).
    /// </summary>
    public sealed class RunPathMoveSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<PathMoveComponent>> _pathFilter = default;
        readonly EcsPoolInject<PathMoveComponent> _pathPool = default;

        // Гейты «пайплайн занят» — как у ReplayActionSystem/RunSelectCellSystem.
        readonly EcsFilterInject<Inc<MovingTag>>             _movingFilter      = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>  _animPendingFilter = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCastFilter = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent>, Exc<DeadTag>> _boardCreatures = default;
        readonly EcsFilterInject<Inc<PlayerComponent, ActiveState>> _activePlayers = default;

        readonly EcsPoolInject<BoardPositionComponent> _posPool    = default;
        readonly EcsPoolInject<SpeedComponent>         _speedPool  = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool  = default;
        readonly EcsPoolInject<PlayerComponent>        _playerPool = default;
        readonly EcsPoolInject<PlayerSideComponent>    _sidePool   = default;
        readonly EcsPoolInject<DeadTag>                _deadPool   = default;
        readonly EcsPoolInject<BoardTag>               _boardPool  = default;
        readonly EcsPoolInject<CreatureTag>            _creaturePool = default;
        readonly EcsPoolInject<AttacksUsedComponent>   _attacksUsedPool = default;
        readonly EcsPoolInject<DoubleAttackTag>        _doubleAttackPool = default;
        readonly EcsPoolInject<MoveRequestEvent>       _movePool   = default;
        readonly EcsPoolInject<AttackRequestEvent>     _attackPool = default;

        // Лимит атак за ход — та же база, что в RunSelectCellSystem.MaxAttacksPerTurn.
        const int MaxAttacksPerTurn = 1;

        public void Run(IEcsSystems systems)
        {
            if (_pathFilter.Value.GetEntitiesCount() == 0) return;

            // Пайплайн занят (идёт шаг/атака/призыв) — ждём оседания, как пассивный реплей.
            if (_movingFilter.Value.GetEntitiesCount() > 0) return;
            if (_animPendingFilter.Value.GetEntitiesCount() > 0) return;
            if (_pendingOnCastFilter.Value.GetEntitiesCount() > 0) return;

            foreach (var e in _pathFilter.Value)
            {
                ref var path = ref _pathPool.Value.Get(e);

                // Существо умерло/ушло с борда или ход владельца закончился → отменить остаток маршрута.
                if (_deadPool.Value.Has(e) || !_boardPool.Value.Has(e) || !OwnerIsActive(e))
                {
                    _pathPool.Value.Del(e);
                    continue;
                }

                if (path.Steps != null && path.Steps.Count > 0)
                {
                    var (row, col, owner) = path.Steps[0];

                    // Скорость кончилась (потрачена чем-то по пути) или клетка стала занятой → прервать.
                    // Free-маршрут (форс-атака) скорость не расходует и её нехватку не проверяет.
                    if ((!path.Free && _speedPool.Value.Get(e).Remaining <= 0)
                        || BoardNav.IsOccupied(_boardCreatures.Value, _posPool.Value, row, col, owner))
                    {
                        _pathPool.Value.Del(e);
                        continue;
                    }

                    path.Steps.RemoveAt(0);
                    ref var move = ref _movePool.Value.Add(e);
                    move.ToRow = row;
                    move.ToCol = col;
                    move.ToOwnerId = owner;
                    move.Free = path.Free;
                    // Один шаг за «оседание» пайплайна: MoveSystem повесит MovingTag, следующий шаг —
                    // после его снятия (гейт в начале Run). return, а не continue — за одно оседание
                    // эмитим ровно одно действие (второй маршрут, если он вдруг есть, подождёт).
                    return;
                }

                // Шаги кончились — финальная атака, если была заказана и всё ещё валидна.
                int target = path.AttackTargetEntity;
                bool free = path.Free;
                _pathPool.Value.Del(e);
                if (target < 0) continue;
                // Free-атака (форс-атака) не тратит скорость, игнорирует лимит атак за ход (бонусная,
                // не вместо обычной) и не помечает AttacksUsedComponent — обычная атака этим же существом
                // в этот ход остаётся доступна.
                if (!free && _speedPool.Value.Get(e).Remaining <= 0) continue;
                if (!free && !CanAttack(e)) continue;
                if (!TargetAttackable(e, target)) continue;

                if (!free) MarkAttacked(e);
                ref var attack = ref _attackPool.Value.Add(e);
                attack.TargetEntity = target;
                attack.Free = free;
                return;   // одно действие за оседание пайплайна
            }
        }

        bool OwnerIsActive(int creature)
        {
            if (!_ownerPool.Value.Has(creature)) return false;
            int ownerId = _ownerPool.Value.Get(creature).OwnerId;
            foreach (var pe in _activePlayers.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return true;
            return false;
        }

        // Цель всё ещё бьётся с текущей позиции: существо — живо, на борде, смежно; аватар (сущность
        // игрока) — стоим на row 0 его стороны (правило «бить аватар только с задней линии врага»).
        bool TargetAttackable(int attacker, int target)
        {
            ref var pos = ref _posPool.Value.Get(attacker);

            if (_playerPool.Value.Has(target))
            {
                int side = _sidePool.Value.Has(target) ? _sidePool.Value.Get(target).Side : -1;
                return pos.Row == 0 && pos.OwnerId == side;
            }

            if (!_creaturePool.Value.Has(target) || !_boardPool.Value.Has(target) || _deadPool.Value.Has(target))
                return false;
            ref var tp = ref _posPool.Value.Get(target);
            return BoardNav.IsNeighbour(pos.Row, pos.Col, pos.OwnerId, tp.Row, tp.Col, tp.OwnerId);
        }

        bool CanAttack(int e)
        {
            int used = _attacksUsedPool.Value.Has(e) ? _attacksUsedPool.Value.Get(e).Value : 0;
            int cap = MaxAttacksPerTurn + (_doubleAttackPool.Value.Has(e) ? 1 : 0);
            return used < cap;
        }

        void MarkAttacked(int e)
        {
            if (!_attacksUsedPool.Value.Has(e)) _attacksUsedPool.Value.Add(e);
            _attacksUsedPool.Value.Get(e).Value++;
        }
    }
}
