using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Configs;
using Game.Core.Model.Card;
using Game.Core.Model.Card.Creature;
using Game.Core.Service;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Карта-с-уровнями (надстройка CardModel.Tiers): каждый кадр вычисляет текущий уровень из источника
    /// (CardModel.TierSource, напр. золото Max владельца по порогам Threshold) и устанавливает статы (Attack/Health/
    /// Speed) и стоимость уровня как НОВУЮ БАЗУ (SetBase/SetBaseMax — НЕ бафф-модификаторы). Это устойчиво к
    /// ClearModifiers (смерть/возврат tier не сбрасывает), баффы складываются поверх, а возврат в руку/из кладбища
    /// (ResetStats: Current=Max) лечит до полного HP уровня. Идемпотентно (трогаем только при смене базы).
    /// Способности уровней НЕ переключает — они инитятся все и гейтятся по CurrentTier в AbilityFire.Mark
    /// (AbilityTierGateComponent).
    ///
    /// При СМЕНЕ уровня (или первом показе) публикует CardTierChangedUIEvent: перерендеренное описание активного
    /// уровня (ключ card.{exp}.{id}.tier.{N}.desc + сноска «Соберите больше золота…», если есть куда расти) +
    /// номер уровня для баннера. UI (PlayCardView) обновляет текст и баннер. Модель берём рантайм из CardConfig.
    /// СИНК ДАРОМ: источник (золото) зеркален у обоих клиентов → уровень/статы/cost/гейт/описание совпадают.
    /// </summary>
    public sealed class CardTierSystem : IEcsInitSystem, IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        // Вьюхи карт, у которых только что вызван SetCard (баннер сброшен) — переиздать им уровень.
        readonly Queue<int> _placedViews = new Queue<int>();

        public void Init(IEcsSystems systems)
            => GameEventBus.Subscribe<CardPlacedInHandViewEvent>(e => _placedViews.Enqueue(e.CardEntity));

        readonly EcsFilterInject<Inc<CardTierComponent, CardModelComponent>> _tiered = default;
        readonly EcsPoolInject<CardTierComponent>  _tierPool  = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool = default;
        readonly EcsPoolInject<OwnerComponent>     _ownerPool = default;

        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool     = default;
        readonly EcsPoolInject<SpeedComponent>  _speedPool  = default;
        readonly EcsPoolInject<GoldCostComponent>   _goldCostPool = default;
        readonly EcsPoolInject<ManaCostComponent>   _manaCostPool = default;
        readonly EcsPoolInject<HealthCostComponent> _hpCostPool   = default;

        readonly EcsFilterInject<Inc<PlayerComponent>> _players = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<GoldComponent>   _goldPool   = default;
        readonly EcsPoolInject<ManaComponent>   _manaPool   = default;

        // «Трупы» (TierSource.Corpses): существа в кладбище владельца СЕЙЧАС + его погибшие токены
        // (счётчик MatchCounter.TokensDied — токены в кладбище не попадают, Limbo).
        readonly EcsFilterInject<Inc<GraveTag, CreatureTag, OwnerComponent>> _graveCreatures = default;
        readonly EcsPoolInject<MatchCounterComponent> _matchCounterPool = default;

        public void Run(IEcsSystems systems)
        {
            // Вьюха карты (пере)создана (SetCard сбросил баннер) → сбросить Announced, чтобы главный цикл
            // переиздал CardTierChangedUIEvent (баннер + описание). Ordering-фикс: событие первого показа могло
            // уйти ДО того, как вьюха вызвала SetCard, — тогда баннер оставался выключен.
            while (_placedViews.Count > 0)
            {
                int card = _placedViews.Dequeue();
                if (_tierPool.Value.Has(card)) _tierPool.Value.Get(card).Announced = false;
            }

            foreach (var e in _tiered.Value)
            {
                var model = ResolveModel(e);
                if (model?.Tiers == null || model.Tiers.Count == 0) continue;

                ref var c = ref _tierPool.Value.Get(e);
                int metric  = SourceValue(e, model.TierSource);
                int newTier = TierForMetric(model.Tiers, metric);
                int prev = c.CurrentTier;
                bool firstShow = !c.Announced;
                c.CurrentTier = newTier;   // ставим ДО применения статов (и для гейта AbilityFire.Mark)

                var td = model.Tiers[newTier];
                if (td != null)
                {
                    // Статы/стоимость уровня — как НОВАЯ БАЗА (SetBase/SetBaseMax, НЕ бафф-модификаторы): устойчиво
                    // к ClearModifiers (смерть/возврат его не сбрасывает), баффы складываются поверх, а возврат в
                    // руку/из кладбища (ResetStats: Current=Max) лечит до полного HP уровня. Идемпотентно —
                    // трогаем только при смене базы. SetBaseMax сам тянет Current/Remaining за выросший Max.
                    if (_attackPool.Value.Has(e))
                    {
                        ref var atk = ref _attackPool.Value.Get(e);
                        if (atk.Base != td.Attack) atk.SetBase(td.Attack);
                    }
                    if (_hpPool.Value.Has(e))
                    {
                        ref var hp = ref _hpPool.Value.Get(e);
                        if (hp.BaseMax != td.Health)
                        {
                            hp.SetBaseMax(td.Health);
                            GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = e });   // усадка Max могла добить
                        }
                    }
                    if (_speedPool.Value.Has(e))
                    {
                        ref var sp = ref _speedPool.Value.Get(e);
                        if (sp.BaseMax != td.Speed) sp.SetBaseMax(td.Speed);
                    }
                    SetTierCost(e, td.PlayCostAmount);
                }

                // Смена уровня (или первый показ) → уведомить UI: баннер + перерендер описания.
                if (newTier != prev || firstShow)
                {
                    c.Announced = true;
                    int pe = OwnerPlayerEntity(e);
                    var cardType = _modelPool.Value.Get(e).CardType;
                    string desc = RenderTierDescription(e, model, cardType, newTier, pe);
                    if (_viewPool.Value.Has(e)) _viewPool.Value.Get(e).Description = desc;   // снэпшот — корректный текст при пересоздании вьюхи
                    GameEventBus.Publish(new CardTierChangedUIEvent { CardEntity = e, Tier = newTier, Description = desc });
                }
            }
        }

        // Описание активного уровня: свой tier-ключ + подстановка *N* (live) + сноска про повышение уровня.
        string RenderTierDescription(int cardEntity, CardModel model, EnumService.CardType cardType, int tier, int playerEntity)
        {
            string key  = CardTextLocalization.TierDescKey(model.ExpansionId, model.Id, tier);
            var    live = CardDynamicValues.Collect(_world.Value, cardEntity, playerEntity);
            string body = CardDescriptionFormatter.Format(key, model.Description, cardType, 0, live);

            // Свойства (Двойной удар/Защитник/...) — ярлык-кейворд ВСЕГДА в начале, вне авторского текста
            // и вне зависимости от уровня карты.
            if (model is CardCreatureModel creature && creature.Properties != null && creature.Properties.Count > 0)
            {
                var keys = new List<string>(creature.Properties.Count);
                foreach (var prop in creature.Properties)
                    if (prop != null) keys.Add(prop.Key);
                body = CardDescriptionFormatter.BuildPropertyPrefix(keys) + body;
            }

            // Сноска (как строка длительности у чар): если ещё есть куда расти — подсказка про источник.
            if (tier < model.Tiers.Count - 1)
            {
                string hint = TierHint(model.TierSource);
                if (!string.IsNullOrEmpty(hint))
                    body = string.IsNullOrEmpty(body) ? hint : body + "\n" + hint;
            }
            return body;
        }

        static string TierHint(TierSource src)
        {
            switch (src)
            {
                case TierSource.GoldMax:
                    return CardTextLocalization.GetText("tier.hint.goldmax", "(Больше золота — выше уровень)");
                case TierSource.ManaMax:
                    return CardTextLocalization.GetText("tier.hint.manamax", "(Больше маны — выше уровень)");
                case TierSource.Corpses:
                    return CardTextLocalization.GetText("tier.hint.corpses", "(Больше трупов — выше уровень)");
                default:
                    return null;
            }
        }

        // Стоимость уровня — как новая база (SetBase) того cost-компонента, что есть у карты. Идемпотентно.
        void SetTierCost(int e, int amount)
        {
            if (_goldCostPool.Value.Has(e))
            {
                ref var cc = ref _goldCostPool.Value.Get(e);
                if (cc.Base != amount) { cc.SetBase(amount); GameEventBus.Publish(new CostModifierChangedEvent()); }
            }
            else if (_manaCostPool.Value.Has(e))
            {
                ref var cc = ref _manaCostPool.Value.Get(e);
                if (cc.Base != amount) { cc.SetBase(amount); GameEventBus.Publish(new CostModifierChangedEvent()); }
            }
            else if (_hpCostPool.Value.Has(e))
            {
                ref var cc = ref _hpCostPool.Value.Get(e);
                if (cc.Base != amount) { cc.SetBase(amount); GameEventBus.Publish(new CostModifierChangedEvent()); }
            }
        }

        CardModel ResolveModel(int e)
        {
            ref var m = ref _modelPool.Value.Get(e);
            var inst = _cardConfig.Value.Get(m.ExpansionId, m.ModelId);
            return inst != null ? inst.CardData : null;
        }

        int OwnerPlayerEntity(int cardEntity)
        {
            if (!_ownerPool.Value.Has(cardEntity)) return -1;
            int ownerId = _ownerPool.Value.Get(cardEntity).OwnerId;
            foreach (var pe in _players.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }

        int SourceValue(int cardEntity, TierSource src)
        {
            switch (src)
            {
                case TierSource.GoldMax:
                {
                    int pe = OwnerPlayerEntity(cardEntity);
                    return pe >= 0 && _goldPool.Value.Has(pe) ? _goldPool.Value.Get(pe).Max : 0;
                }
                case TierSource.ManaMax:
                {
                    int pe = OwnerPlayerEntity(cardEntity);
                    return pe >= 0 && _manaPool.Value.Has(pe) ? _manaPool.Value.Get(pe).Max : 0;
                }
                case TierSource.Corpses:
                {
                    // Существа в кладбище владельца СЕЙЧАС (воскрешение из кладбища понижает счёт — это
                    // «текущие трупы») + его токены, погибшие за матч (в кладбище не попадают — счётчик).
                    if (!_ownerPool.Value.Has(cardEntity)) return 0;
                    int ownerId = _ownerPool.Value.Get(cardEntity).OwnerId;
                    int corpses = 0;
                    foreach (var g in _graveCreatures.Value)
                        if (_ownerPool.Value.Get(g).OwnerId == ownerId) corpses++;
                    int pe = OwnerPlayerEntity(cardEntity);
                    if (pe >= 0 && _matchCounterPool.Value.Has(pe)) corpses += _matchCounterPool.Value.Get(pe).TokensDied;
                    return corpses;
                }
                default:
                    return 0;   // None — всегда уровень 0
            }
        }

        // Наибольший уровень, чей Threshold ≤ metric (Tiers по возрастанию Threshold); минимум — уровень 0.
        static int TierForMetric(System.Collections.Generic.List<CardTier> tiers, int metric)
        {
            int result = 0;
            for (int i = 0; i < tiers.Count; i++)
                if (tiers[i] != null && metric >= tiers[i].Threshold) result = i;
            return result;
        }
    }
}
