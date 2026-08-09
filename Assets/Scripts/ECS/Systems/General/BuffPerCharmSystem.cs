using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// «+X/+Y за каждую чару под вашим контролем» (Обжора, BuffPerCharmComponent) — РЕАКТИВНО (по решению
    /// юзера 2026-07-29, не по-кадрово): пересчёт только по событиям, меняющим состав чар на поле или зону
    /// носителя (розыгрыш/генерация/смерть/призыв/сброс/добор) + страховка на границе хода (TurnStarted,
    /// оба клиента — закрывает редкие пути без доменного события, напр. возврат чар из кладбища).
    /// Пересчёт: дифф желаемого бонуса (Per × число чар владельца на поле) с навешанным (Applied*) через
    /// стек модификаторов статов. Бонус действует В РУКЕ И НА СТОЛЕ (в руке виден через
    /// HandCardStatsViewSystem — та диффит atk/hp карт руки сама); в колоде/кладбище — снимается
    /// (смерть: мягкие модификаторы чистит DieSystem, RemoveModifier тогда no-op, Applied обнуляется).
    /// СИНК: события пересчёта публикуются на ОБОИХ клиентах (каст/реплей/ре-ран резолвов) → бонус
    /// зеркален; страховка TurnStarted гарантирует схождение не позже границы хода.
    /// Регистрация: _generalSystems, после CharmDieSystem (истёкшие чары выходят из счёта тем же пересчётом).
    /// </summary>
    public sealed class BuffPerCharmSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsFilterInject<Inc<BuffPerCharmComponent, CreatureTag>> _buffed = default;
        readonly EcsFilterInject<Inc<CharmTag, BoardTag, OwnerComponent>, Exc<DeadTag>> _charms = default;

        readonly EcsPoolInject<BuffPerCharmComponent> _perCharmPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool  = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool     = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<HandTag>  _handPool  = default;
        readonly EcsPoolInject<DeadTag>  _deadPool  = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _cooldownPool = default;

        // Реактивность: события лишь взводят флаг, один дифф-проход в ближайшем Run (дёшево и без
        // спама при пачке событий в одном кадре — каскад конца хода даёт один пересчёт).
        bool _dirty = true;   // старт матча: применить начальное состояние (карты могли прийти с чарами на столе)
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPlayedEvent>(OnAny);        // чара разыграна (актив и реплей пассива)
            GameEventBus.Subscribe<CardGeneratedEvent>(OnAny);     // чара-токен создана (Вуду/Подписка, ре-ран на обоих)
            GameEventBus.Subscribe<CreatureDiedEvent>(OnAny);      // чара умерла/истёк таймер (CharmDieSystem, оба клиента)
            GameEventBus.Subscribe<CreatureInvokedEvent>(OnAny);   // носитель вышел на стол (бонус активен и в руке, но зона сменилась)
            GameEventBus.Subscribe<CardDiscardedEvent>(OnAny);     // носитель/чара сброшены из руки
            GameEventBus.Subscribe<CardDrawnEvent>(OnAny);         // носитель добран в руку → применить текущий бонус
            GameEventBus.Subscribe<TurnStartedEvent>(OnAny);       // страховка: редкие пути без событий (чары из кладбища)
        }

        public void Destroy(IEcsSystems systems)
        {
            if (!_subscribed) return;
            _subscribed = false;
            GameEventBus.Unsubscribe<CardPlayedEvent>(OnAny);
            GameEventBus.Unsubscribe<CardGeneratedEvent>(OnAny);
            GameEventBus.Unsubscribe<CreatureDiedEvent>(OnAny);
            GameEventBus.Unsubscribe<CreatureInvokedEvent>(OnAny);
            GameEventBus.Unsubscribe<CardDiscardedEvent>(OnAny);
            GameEventBus.Unsubscribe<CardDrawnEvent>(OnAny);
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnAny);
        }

        void OnAny(CardPlayedEvent _)      => _dirty = true;
        void OnAny(CardGeneratedEvent _)   => _dirty = true;
        void OnAny(CreatureDiedEvent _)    => _dirty = true;
        void OnAny(CreatureInvokedEvent _) => _dirty = true;
        void OnAny(CardDiscardedEvent _)   => _dirty = true;
        void OnAny(CardDrawnEvent _)       => _dirty = true;
        void OnAny(TurnStartedEvent _)     => _dirty = true;

        public void Run(IEcsSystems systems)
        {
            if (!_dirty) return;
            _dirty = false;

            foreach (var e in _buffed.Value)
            {
                ref var per = ref _perCharmPool.Value.Get(e);

                // Бонус действует В РУКЕ и НА СТОЛЕ; в колоде/кладбище/лимбе — снят (при смерти мягкие
                // модификаторы уже почистил DieSystem — RemoveModifier тогда no-op, дифф просто обнуляется).
                // Кулдаун (командир после гибели) отключает способности карты — общее правило проекта.
                bool active = (_boardPool.Value.Has(e) || _handPool.Value.Has(e))
                              && !_deadPool.Value.Has(e)
                              && !_cooldownPool.Value.Has(e);
                int count = 0;
                if (active && _ownerPool.Value.Has(e))
                {
                    int ownerId = _ownerPool.Value.Get(e).OwnerId;
                    foreach (var ch in _charms.Value)
                        if (_ownerPool.Value.Get(ch).OwnerId == ownerId) count++;
                }

                int wantAtk = active ? per.AttackPerCharm * count : 0;
                int wantHp  = active ? per.HealthPerCharm * count : 0;

                if (wantAtk != per.AppliedAttack && _attackPool.Value.Has(e))
                {
                    ref var atk = ref _attackPool.Value.Get(e);
                    if (per.AppliedAttack != 0) atk.RemoveModifier(per.AppliedAttack);
                    if (wantAtk != 0) atk.AddModifier(wantAtk);
                    per.AppliedAttack = wantAtk;
                }

                if (wantHp != per.AppliedHealth && _hpPool.Value.Has(e))
                {
                    ref var hp = ref _hpPool.Value.Get(e);
                    int delta = wantHp - per.AppliedHealth;
                    // Current фиксируем ДО «танца» remove→add: промежуточный RecalculateValue (после снятия
                    // старого модификатора Max временно падает до базы) КЛАМПИТ Current — 4/4 при апгрейде
                    // бонуса 2→4 превращалось в 4/6 вместо 6/6 (терялось всё выше промежуточного минимума).
                    int cur = hp.Current;
                    if (per.AppliedHealth != 0) hp.RemoveModifier(per.AppliedHealth);
                    if (wantHp != 0) hp.AddModifier(wantHp);
                    // РОСТ Max лечит на дельту (семантика баффа «+X/+Y», как BuffStats/SetBaseMax): Обжора
                    // 2/2 при двух чарах = 6/6, а НЕ «6 макс / 2 текущих» (AddModifier сам Current не тянет).
                    // Усадка (delta < 0): Current остаётся прежним и режется клампом по новому Max.
                    hp.Current = System.Math.Min(cur + System.Math.Max(delta, 0), hp.Max);
                    per.AppliedHealth = wantHp;
                    GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = e });   // усадка баффа могла добить (комбо с дебаффом)
                }
            }
        }
    }
}
