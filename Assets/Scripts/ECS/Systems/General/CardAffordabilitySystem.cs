using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Следит за доступностью карт в руке локального игрока для розыгрыша:
    /// при изменении ресурсов (ResourceChangedEvent) пересчитывает все карты
    /// в руке и публикует CardAffordableChangedEvent для каждой.
    /// </summary>
    public sealed class CardAffordabilitySystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsPoolInject<HandTag>          _handTagPool   = default;
        readonly EcsPoolInject<OwnCardTag>        _ownCardPool   = default;
        readonly EcsPoolInject<GoldCostComponent> _goldCostPool  = default;
        readonly EcsPoolInject<ManaCostComponent> _manaCostPool  = default;
        readonly EcsPoolInject<HealthCostComponent> _healthCostPool = default;
        readonly EcsPoolInject<OwnerComponent>    _ownerPool     = default;
        readonly EcsPoolInject<GoldComponent>     _goldPool      = default;
        readonly EcsPoolInject<ManaComponent>     _manaPool      = default;
        readonly EcsPoolInject<PlayerComponent>   _playerPool    = default;
        readonly EcsPoolInject<ActiveState>       _activePool    = default;
        readonly EcsPoolInject<CommanderTag>      _commanderTagPool = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _commanderCdPool = default;
        readonly EcsPoolInject<CharmTag>          _charmTagPool  = default;

        readonly EcsFilterInject<Inc<HandTag, OwnCardTag>> _handFilter = default;
        readonly EcsFilterInject<Inc<CharmTag, BoardTag>>  _boardCharms = default;

        // Максимум чар под контролем игрока — то же число, что и pre-cost гейт в RunCastRouterSystem
        // (там — страховка «не списывать стоимость зря»; здесь — чтобы карта ВООБЩЕ не подсвечивалась
        // играбельной и не перетаскивалась, а не отменялась уже после драга).
        const int CharmLimit = 5;

        bool _resourceDirty;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<ResourceChangedEvent>(OnResourceChanged);
            GameEventBus.Subscribe<CardDrawnEvent>(OnCardDrawn);
            GameEventBus.Subscribe<CardPlacedInHandViewEvent>(OnCardPlacedInView);
            GameEventBus.Subscribe<AbilityReadyEvent>(OnAbilityReady);
            GameEventBus.Subscribe<AbilityNotReadyEvent>(OnAbilityNotReady);
            // Доступность завязана на ход (ActiveState) → пересчитываем на границах хода:
            // когда локальный стал активным и когда ход ушёл оппоненту.
            GameEventBus.Subscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Subscribe<OpponentTurnEndedEvent>(OnOpponentTurnEnded);
            GameEventBus.Subscribe<CostModifierChangedEvent>(OnCostModifierChanged);
            // Кулдаун командира влияет на доступность — пересчитываем на его установке/снятии.
            GameEventBus.Subscribe<CommanderOnCooldownUIEvent>(OnCommanderCooldownChanged);
            GameEventBus.Subscribe<CommanderCooldownExpiredUIEvent>(OnCommanderCooldownExpired);
            // Лимит чар (5) меняется на розыгрыше чары (CardCastEvent — любой каст спелла/чары/существа,
            // дешёвый пересчёт всей руки погоды не делает) и на смерти/истечении чары (CreatureDiedEvent —
            // публикует и DieSystem, и CharmDieSystem). Другие чары в руке могли из-за этого стать (не)играбельны.
            GameEventBus.Subscribe<CardCastEvent>(OnCharmCountMayChange);
            GameEventBus.Subscribe<CreatureDiedEvent>(OnCharmCountMayChange);
        }

        private void OnCharmCountMayChange(CardCastEvent _)     => _resourceDirty = true;
        private void OnCharmCountMayChange(CreatureDiedEvent _) => _resourceDirty = true;

        private void OnResourceChanged(ResourceChangedEvent _)     => _resourceDirty = true;
        private void OnCardDrawn(CardDrawnEvent _)                  => _resourceDirty = true;
        private void OnLocalTurnStarted(LocalTurnStartedEvent _)    => _resourceDirty = true;
        private void OnOpponentTurnEnded(OpponentTurnEndedEvent _)  => _resourceDirty = true;
        private void OnCostModifierChanged(CostModifierChangedEvent _) => _resourceDirty = true;
        private void OnCommanderCooldownChanged(CommanderOnCooldownUIEvent _) => _resourceDirty = true;
        private void OnCommanderCooldownExpired(CommanderCooldownExpiredUIEvent _) => _resourceDirty = true;

        private void OnCardPlacedInView(CardPlacedInHandViewEvent evt)
        {
            int cardEntity = evt.CardEntity;
            if (!_handTagPool.Value.Has(cardEntity)) return;
            if (!_ownCardPool.Value.Has(cardEntity)) return;
            GameEventBus.Publish(new CardAffordableChangedEvent
            {
                CardEntity   = cardEntity,
                IsAffordable = IsAffordable(cardEntity),
            });
            // Иконка/число коста для СВЕЖЕсозданной вьюхи: карта могла прийти в руку при висящем маркере
            // альтернативной уплаты (иконка уплаты вместо ресурса) — SetCard ставит обычную по типу.
            if (TryEffectiveCost(cardEntity, out int eff))
                GameEventBus.Publish(new CardCostChangedEvent { CardEntity = cardEntity, EffectiveCost = eff, AltCostKind = AltKindOf(cardEntity) });
        }

        // Вид альтернативной уплаты владельца карты (-1 = обычная оплата) — для иконки коста в руке.
        private int AltKindOf(int cardEntity)
        {
            if (!_ownerPool.Value.Has(cardEntity)) return -1;
            int ownerEntity = FindPlayerEntity(_ownerPool.Value.Get(cardEntity).OwnerId);
            if (ownerEntity < 0) return -1;
            var altPool = _world.Value.GetPool<AltCostComponent>();
            return altPool.Has(ownerEntity) ? (int)altPool.Get(ownerEntity).Kind : -1;
        }

        private void OnAbilityReady(AbilityReadyEvent evt)
        {
            if (evt.CardEntity < 0) return;
            GameEventBus.Publish(new CardAbilityReadyChangedEvent
            {
                CardEntity = evt.CardEntity,
                IsReady    = true,
            });
        }

        private void OnAbilityNotReady(AbilityNotReadyEvent evt)
        {
            if (evt.CardEntity < 0) return;
            GameEventBus.Publish(new CardAbilityReadyChangedEvent
            {
                CardEntity = evt.CardEntity,
                IsReady    = false,
            });
        }

        public void Run(IEcsSystems systems)
        {
            if (!_resourceDirty) return;
            _resourceDirty = false;

            foreach (var cardEntity in _handFilter.Value)
            {
                bool affordable = IsAffordable(cardEntity);
                GameEventBus.Publish(new CardAffordableChangedEvent
                {
                    CardEntity   = cardEntity,
                    IsAffordable = affordable,
                });

                if (TryEffectiveCost(cardEntity, out int eff))
                    GameEventBus.Publish(new CardCostChangedEvent { CardEntity = cardEntity, EffectiveCost = eff, AltCostKind = AltKindOf(cardEntity) });
            }
        }

        // Эффективная стоимость карты (база + модификатор владельца). false — у карты нет стоимости.
        private bool TryEffectiveCost(int cardEntity, out int effective)
        {
            effective = 0;
            if (!_ownerPool.Value.Has(cardEntity)) return false;
            int ownerEntity = FindPlayerEntity(_ownerPool.Value.Get(cardEntity).OwnerId);
            if (ownerEntity < 0) return false;

            if (_goldCostPool.Value.Has(cardEntity))
            { effective = CostModifierUtil.Effective(_world.Value, ownerEntity, _goldCostPool.Value.Get(cardEntity).Cost); return true; }
            if (_manaCostPool.Value.Has(cardEntity))
            { effective = CostModifierUtil.Effective(_world.Value, ownerEntity, _manaCostPool.Value.Get(cardEntity).Cost); return true; }
            if (_healthCostPool.Value.Has(cardEntity))
            { effective = CostModifierUtil.Effective(_world.Value, ownerEntity, _healthCostPool.Value.Get(cardEntity).Cost); return true; }
            return false;
        }

        private bool IsAffordable(int cardEntity)
        {
            if (!_ownerPool.Value.Has(cardEntity)) return false;
            int ownerEntity = FindPlayerEntity(_ownerPool.Value.Get(cardEntity).OwnerId);
            if (ownerEntity < 0) return false;

            // Карта доступна к розыгрышу ТОЛЬКО в свой ход. ActiveState висит на активном игроке;
            // на чужом ходу его нет → карты не подсвечиваются и не перетаскиваются (см. также гейт
            // в RunCastRouterSystem — defense-in-depth).
            if (!_activePool.Value.Has(ownerEntity)) return false;

            // Командир на кулдауне (после гибели) — недоступен к розыгрышу (не перетаскивается, серый).
            // Компонент снимает RunTurnStartSystem на ходу доступности; здесь перечёт дёргают cooldown-события.
            if (_commanderTagPool.Value.Has(cardEntity) && _commanderCdPool.Value.Has(cardEntity)) return false;

            // Лимит чар (5) — та же проверка, что pre-cost гейт в RunCastRouterSystem, но ЗДЕСЬ она не даёт
            // карте вообще подсветиться зелёной/перетащиться, а не отменяет розыгрыш постфактум (юзер 2026-08-06:
            // "лучше, чтобы вообще нельзя было").
            if (_charmTagPool.Value.Has(cardEntity) && CharmCount(_ownerPool.Value.Get(cardEntity).OwnerId) >= CharmLimit)
                return false;

            // Маркер альтернативной уплаты (Букмекер и семейство): карта оплачивается НЕ ресурсом →
            // играбельность = есть ли ЧЕМ платить (жертва для сброса/жертвы/милла; сама карта — не жертва
            // своего сброса). DamageSelf играбельна всегда (суицид разрешён, HP не гейтим).
            var altPool = _world.Value.GetPool<AltCostComponent>();
            if (altPool.Has(ownerEntity))
                return AltCostUtil.CanPay(_world.Value, altPool.Get(ownerEntity).Kind,
                                          _ownerPool.Value.Get(cardEntity).OwnerId, cardEntity);

            if (_goldCostPool.Value.Has(cardEntity) && _goldPool.Value.Has(ownerEntity))
            {
                ref var gold = ref _goldPool.Value.Get(ownerEntity);
                return gold.Current >= CostModifierUtil.Effective(_world.Value, ownerEntity, _goldCostPool.Value.Get(cardEntity).Cost);
            }

            if (_manaCostPool.Value.Has(cardEntity) && _manaPool.Value.Has(ownerEntity))
            {
                ref var mana = ref _manaPool.Value.Get(ownerEntity);
                return mana.Current >= CostModifierUtil.Effective(_world.Value, ownerEntity, _manaCostPool.Value.Get(cardEntity).Cost);
            }

            // Карта без стоимости — всегда доступна
            return true;
        }

        private int CharmCount(int ownerId)
        {
            int n = 0;
            foreach (var e in _boardCharms.Value)
                if (_ownerPool.Value.Has(e) && _ownerPool.Value.Get(e).OwnerId == ownerId) n++;
            return n;
        }

        private int FindPlayerEntity(int playerId)
        {
            var filter = _world.Value.Filter<PlayerComponent>().End();
            foreach (var e in filter)
            {
                if (_playerPool.Value.Get(e).PlayerId == playerId)
                    return e;
            }
            return -1;
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<ResourceChangedEvent>(OnResourceChanged);
            GameEventBus.Unsubscribe<CardDrawnEvent>(OnCardDrawn);
            GameEventBus.Unsubscribe<CardPlacedInHandViewEvent>(OnCardPlacedInView);
            GameEventBus.Unsubscribe<AbilityReadyEvent>(OnAbilityReady);
            GameEventBus.Unsubscribe<AbilityNotReadyEvent>(OnAbilityNotReady);
            GameEventBus.Unsubscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Unsubscribe<OpponentTurnEndedEvent>(OnOpponentTurnEnded);
            GameEventBus.Unsubscribe<CostModifierChangedEvent>(OnCostModifierChanged);
            GameEventBus.Unsubscribe<CommanderOnCooldownUIEvent>(OnCommanderCooldownChanged);
            GameEventBus.Unsubscribe<CommanderCooldownExpiredUIEvent>(OnCommanderCooldownExpired);
            GameEventBus.Unsubscribe<CardCastEvent>(OnCharmCountMayChange);
            GameEventBus.Unsubscribe<CreatureDiedEvent>(OnCharmCountMayChange);
        }
    }
}
