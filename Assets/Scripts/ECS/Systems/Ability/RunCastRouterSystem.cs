using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Каст-роутер — вход нового пайплайна. Ловит RequestCardCastEvent (от UI), оплачивает
    /// стоимость, делегирует по типу карты (существо/заклинание/чары) и на финише публикует
    /// CardCastEvent — на него подписаны OnCast-триггеры способностей.
    /// TODO (per-type): размещение существа (выбор клетки, async), перенос заклинания в кладбище,
    /// лимит чар (макс 5) + перенос на борд. Сейчас делается оплата + finish (триггеры).
    /// </summary>
    public sealed class RunCastRouterSystem : IEcsRunSystem
    {
        const int CharmLimit = 5;   // максимум чар под контролем игрока

        // Геометрия ряда призыва — как в RunSelectCellBoardSystem/BoardFrontRow: row 0 своей стороны,
        // порядок заполнения от центра наружу. Свой экземпляр, потому что BoardFrontRow живёт в сборке
        // Game.Core.Ability, на которую сборка систем не ссылается.
        const int FrontRow  = 0;
        const int BoardCols = 5;
        static readonly int[] ColOrder = { 2, 1, 3, 0, 4 };

        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            var reqPool = world.GetPool<RequestCardCastEvent>();
            var ownerPool = world.GetPool<OwnerComponent>();
            var playerPool = world.GetPool<PlayerComponent>();
            var declinePool = world.GetPool<DeclineCardCastEvent>();

            var activeStatePool = world.GetPool<ActiveState>();
            var startStatePool  = world.GetPool<StartTurnState>();   // каскад НАЧАЛА хода (форс-плей с добора)
            var endStatePool    = world.GetPool<EndTurnState>();     // каскад КОНЦА хода
            var creatureTag = world.GetPool<CreatureTag>();
            var spellTag = world.GetPool<SpellTag>();
            var charmTag = world.GetPool<CharmTag>();
            var commanderTag = world.GetPool<CommanderTag>();
            var commanderCd  = world.GetPool<CommanderCooldownComponent>();
            var pendingCellPool = world.GetPool<PendingSelectCellState>();
            var moveToGravePool = world.GetPool<MoveCardToGraveEvent>();
            var moveToBoardPool = world.GetPool<MoveCardToBoardEvent>();

            var filter = world.Filter<RequestCardCastEvent>().End();

            foreach (var reqEntity in filter)
            {
                int card = reqPool.Get(reqEntity).CardEntity;
                if (card < 0) continue;
                bool free = reqPool.Get(reqEntity).Free;   // форс-розыгрыш без оплаты (Барабук)

                int player = FindPlayer(world, playerPool, ownerPool, card);
                if (player < 0) continue;

                // Гейт хода: РУЧНОЙ розыгрыш — только игрок, чей сейчас ход. «Чей ход» = ActiveState ИЛИ
                // каскад начала/конца (StartTurnState/EndTurnState) — иначе ФОРС-ПЛЕЙ с добора на старте хода
                // (OnDrawForcePlay: Подстава/Вонючее облако) отклонялся, т.к. ActiveState вешается лишь ПОСЛЕ
                // оседания каскада. А PlayCardUtil уже снял карту из руки → токен зависал в лимбе (без каста,
                // без синка) → фантом в зеркале руки у оппонента. Пассив ни одного из стейтов не имеет → заблокирован.
                // FREE каст (форс от эффекта/триггера, не рукой игрока) гейт хода НЕ применяет: deathrattle-
                // заклинания (Королевская пиньята) срабатывают при смерти В ЛЮБОЙ ход, в т.ч. чужой — иначе Decline.
                if (!free && !activeStatePool.Has(player) && !startStatePool.Has(player) && !endStatePool.Has(player))
                {
                    Decline(world, declinePool, card, DeclineReason.Unknown);
                    continue;
                }

                int ownerId = ownerPool.Get(card).OwnerId;

                // pre-cost: командир на кулдауне (после гибели вернулся в руку) — играть нельзя, пока
                // не истечёт. CommanderCooldownComponent висит на ходах смерти и следующем (skip-one),
                // RunTurnStartSystem снимает его на старте хода доступности. Проверяем ДО оплаты.
                if (commanderTag.Has(card) && commanderCd.Has(card))
                {
                    Decline(world, declinePool, card, DeclineReason.CommanderOnCooldown);
                    continue;
                }

                // pre-cost: лимит чар (5) — чтобы не списывать стоимость зря
                if (charmTag.Has(card) && CharmCount(world, ownerPool, ownerId) >= CharmLimit)
                {
                    Decline(world, declinePool, card, DeclineReason.CharmLimitReached);
                    continue;
                }

                // pre-cost: доп. цена ПОВЕРХ обычной (RequiresAdditionalCostComponent) — печатное свойство
                // карты (не AltCost — тот временный маркер игрока для чужого СЛЕДУЮЩЕГО каста). Саму уплату
                // (сброс/жертву/милл/урон) делает СОБСТВЕННЫЙ OnCast-эффект карты на резолве — здесь только
                // гейт: нечем платить → недоступна, как нехватка маны.
                var addCostPool = world.GetPool<RequiresAdditionalCostComponent>();
                if (addCostPool.Has(card)
                    && !AltCostUtil.CanPay(world, addCostPool.Get(card).Kind, ownerId, card))
                {
                    Decline(world, declinePool, card, DeclineReason.NoAdditionalCostPayment);
                    continue;
                }

                if (!free)
                {
                    if (world.GetPool<AltCostComponent>().Has(player))
                    {
                        // АЛЬТЕРНАТИВНАЯ УПЛАТА (семейство «Бесчестный букмекер»): ресурс НЕ списываем,
                        // вместо него уплата по виду маркера. Жертвы роллит АКТИВ (пассиву едут ключами в
                        // ActionCastData.AltPaid*). Это СТОИМОСТЬ: нечем платить (нет карты для сброса/
                        // существа/карт в колоде) → каст отклоняется, как без маны; заряд НЕ тратится.
                        if (!AltCostUtil.CanPay(world, world.GetPool<AltCostComponent>().Get(player).Kind, ownerId, card))
                        {
                            Decline(world, declinePool, card, DeclineReason.NoAltCostPayment);
                            continue;
                        }
                        var kind = AltCostUtil.ConsumeCharge(world, player);
                        int amount = 0;
                        string[] keys = null;
                        switch (kind)
                        {
                            case AltCostKind.DamageSelf:
                                // Урон себе на эффективную стоимость: штатный пайплайн → Вуду-редирект/
                                // пейн-триггеры работают (задумка архетипа); суицид разрешён.
                                amount = EffectiveCost(world, card, player);
                                AltCostUtil.DamageSelf(world, player, card, amount);
                                break;
                            case AltCostKind.DiscardHand:
                            {
                                int victim = AltCostUtil.RollOwnHandCard(world, ownerId, exclude: card);
                                if (victim >= 0) { AltCostUtil.Discard(world, victim); keys = new[] { NetKeyOf(world, victim) }; }
                                break;
                            }
                            case AltCostKind.SacrificeCreature:
                            {
                                int victim = AltCostUtil.RollOwnBoardCreature(world, ownerId);
                                if (victim >= 0) { AltCostUtil.Sacrifice(world, victim); keys = new[] { NetKeyOf(world, victim) }; }
                                break;
                            }
                            case AltCostKind.MillDeck:
                            {
                                int victim = AltCostUtil.RollOwnDeckCard(world, ownerId);
                                if (victim >= 0) { AltCostUtil.Mill(world, victim); keys = new[] { NetKeyOf(world, victim) }; }
                                break;
                            }
                        }

                        var paidPool = world.GetPool<AltPaidComponent>();
                        if (!paidPool.Has(card)) paidPool.Add(card);
                        ref var paid = ref paidPool.Get(card);
                        paid.Kind = kind; paid.Amount = amount; paid.Keys = keys;
                        // Заряд потреблён (возможно, маркер снят) → перекраска руки.
                        GameEventBus.Publish(new CostModifierChangedEvent());
                    }
                    else if (!TryPayCost(world, card, player, out var reason))
                    {
                        Decline(world, declinePool, card, reason);
                        continue;
                    }
                }

                // ── делегирование по типу ──
                if (creatureTag.Has(card))
                {
                    // Клетку выбирает тот, чей сейчас ход: человек кликом (RunSelectCellBoardSystem) или ИИ
                    // (RunAiTurnSystem.TryAct, шаг 1 — он смотрит только СВОИ карты). Спросить владельца
                    // можно, лишь пока его ввод жив: ActiveState (ход идёт) или StartTurnState (ход вот-вот
                    // начнётся, окно откроется после каскада). В EndTurnState ввод уже выключен и обратно
                    // не включится, а в ЧУЖОЙ ход владелец кликать не может вовсе — спрашивать некого.
                    //
                    // Сюда это доезжает через free-каст (гейт хода выше пропускает вне хода только его):
                    // «разыграй N случайных карт» с хрипа Королевской пиньяты в ход оппонента может вытащить
                    // СУЩЕСТВО. Без авто-размещения PendingSelectCellState висел бы до чужого клика, а он в
                    // PipelineGate — то есть встала бы вся очередь авто-кастов (и остаток каскада не сыграл бы).
                    // Ставим сами — ровно как PlayCardUtil при форс-розыгрыше из эффекта.
                    if (!activeStatePool.Has(player) && !startStatePool.Has(player))
                    {
                        int autoCol = FreeFrontCell(world, ownerId);
                        if (autoCol < 0)
                        {
                            // Ряд призыва полон — ставить некуда. Карта остаётся в руке (стоимость вернёт
                            // Decline), а не зависает без клетки и без каста.
                            Decline(world, declinePool, card, DeclineReason.Unknown);
                            continue;
                        }
                        ref var autoMove = ref moveToBoardPool.Add(card);
                        autoMove.Row = FrontRow; autoMove.Col = autoCol; autoMove.OwnerId = ownerId;
                        var invokePool = world.GetPool<InvokeEvent>();
                        if (!invokePool.Has(card)) invokePool.Add(card);   // свой OnCast на размещении
                        continue;
                    }

                    // Существо: ждём выбор клетки (async). RunSelectCellBoardSystem ловит клик →
                    // RunMoveCardToBoardSystem ставит на борд → RunInvokeCreatureSystem публикует CardCastEvent.
                    if (!pendingCellPool.Has(card)) pendingCellPool.Add(card).OwnerPlayerEntity = player;
                    continue;   // CardCastEvent НЕ публикуем здесь — он на размещении существа
                }
                else if (spellTag.Has(card))
                {
                    // эффекты резолвятся через CardCastEvent ниже; карту — в кладбище.
                    // TODO: момент ухода в кладбище — после резолва эффектов (сейчас сразу).
                    if (!moveToGravePool.Has(card)) moveToGravePool.Add(card);
                }
                else if (charmTag.Has(card))
                {
                    // чары на борд без позиции (Row=-1).
                    if (!moveToBoardPool.Has(card))
                    {
                        ref var m = ref moveToBoardPool.Add(card);
                        m.Row = -1; m.Col = -1; m.OwnerId = -1;
                    }
                }

                // финиш каста (заклинание/чары) → триггеры способностей
                GameEventBus.Publish(new CardCastEvent { CardEntity = card });
            }
        }

        // Отказ каста: помечаем карту + просим UI вернуть её в руку (PlayCardView слушает это событие).
        static void Decline(EcsWorld world, EcsPool<DeclineCardCastEvent> declinePool, int card, DeclineReason reason)
        {
            if (!declinePool.Has(card)) declinePool.Add(card).Reason = reason;
            GameEventBus.Publish(new TargetSelectionCancelledEvent { CardEntity = card });

            // Ручной розыгрыш эту точку проходит, ПОКА карта ещё в руке (её зону снимает код НИЖЕ по потоку,
            // уже ПОСЛЕ всех этих гейтов) — для него всё ниже просто не сработает (HandTag уже есть). А вот
            // форс/фри-каст (PlayCardUtil.Play — deathrattle, дискавер, эффект «разыграй карту») снимает
            // исходную зону СРАЗУ, до того как AutoCastSystem вообще превратит маркер в этот запрос (иначе
            // однокадровый RequestCardCastEvent не пережил бы границу кадра — см. докстринг PlayCardUtil.Play).
            // Если такой каст всё же отклонён (лимит чар и т.п.) — карта виснет НИ В ОДНОЙ зоне и пропадает
            // без следа (баг 2026-08-23: «Зачаровать матч» выбрала чару сверх лимита в 5 — чара тихо исчезала).
            if (world.GetPool<HandTag>().Has(card) || world.GetPool<BoardTag>().Has(card)
                || world.GetPool<DeckTag>().Has(card) || world.GetPool<GraveTag>().Has(card))
                return;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(card)) return;
            int ownerId = ownerPool.Get(card).OwnerId;

            int playerEntity = FindPlayer(world, world.GetPool<PlayerComponent>(), ownerPool, card);
            if (playerEntity < 0) return;

            var handTagPool = world.GetPool<HandTag>();
            if (!handTagPool.Has(card)) handTagPool.Add(card);

            var handPool = world.GetPool<HandComponent>();
            if (handPool.Has(playerEntity))
            {
                ref var hand = ref handPool.Get(playerEntity);
                hand.CardEntities ??= new System.Collections.Generic.List<int>();
                if (!hand.CardEntities.Contains(card)) hand.CardEntities.Add(card);
                hand.Count = hand.CardEntities.Count;
            }

            UnityEngine.Debug.LogWarning($"[Decline] card={card} reason={reason}: карта была вне всех зон (форс-каст отклонён) — вернул в руку owner={ownerId}");
            GameEventBus.Publish(new CardDrawnEvent { CardEntity = card, PlayerId = ownerId });
        }

        // Свободная клетка ряда призыва владельца для АВТО-размещения (спросить некого). Занятость
        // считаем как RunSelectCellBoardSystem — по существам на доске, плюс исключаем клетки, уже
        // забронированные необработанными MoveCardToBoardEvent: два авто-каста в одном кадре иначе
        // выбрали бы одну и ту же клетку (борда-то они ещё не достигли).
        static int FreeFrontCell(EcsWorld world, int ownerId)
        {
            var occupied = new bool[BoardCols];

            var posPool = world.GetPool<BoardPositionComponent>();
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<BoardPositionComponent>().Exc<DeadTag>().End())
            {
                ref var p = ref posPool.Get(e);
                if (p.OwnerId != ownerId || p.Row != FrontRow) continue;
                if (p.Col >= 0 && p.Col < BoardCols) occupied[p.Col] = true;
            }

            var movePool = world.GetPool<MoveCardToBoardEvent>();
            foreach (var e in world.Filter<MoveCardToBoardEvent>().End())
            {
                ref var m = ref movePool.Get(e);
                if (m.OwnerId != ownerId || m.Row != FrontRow) continue;
                if (m.Col >= 0 && m.Col < BoardCols) occupied[m.Col] = true;
            }

            foreach (var c in ColOrder)
                if (!occupied[c]) return c;
            return -1;
        }

        // Токены (TokenTag) в лимит НЕ считаются — та же конвенция, что у PlayerStatsViewSystem.GatherAuras
        // (бар аур тоже Exc<TokenTag>): токен — расходник без «владения» в обычном смысле (не уходит на
        // кладбище, не тратит лимит копий), лимит защищает от РУЧНОГО заспама настоящими чарами.
        static int CharmCount(EcsWorld world, EcsPool<OwnerComponent> ownerPool, int ownerId)
        {
            var filter = world.Filter<CharmTag>().Inc<BoardTag>().Exc<TokenTag>().End();
            int n = 0;
            foreach (var e in filter)
                if (ownerPool.Has(e) && ownerPool.Get(e).OwnerId == ownerId) n++;
            return n;
        }

        static int FindPlayer(EcsWorld world, EcsPool<PlayerComponent> playerPool,
                              EcsPool<OwnerComponent> ownerPool, int card)
        {
            if (!ownerPool.Has(card)) return -1;
            int ownerId = ownerPool.Get(card).OwnerId;

            var filter = world.Filter<PlayerComponent>().End();
            foreach (var e in filter)
                if (playerPool.Get(e).PlayerId == ownerId) return e;
            return -1;
        }

        static string NetKeyOf(EcsWorld world, int entity)
        {
            var pool = world.GetPool<NetworkEntityComponent>();
            return pool.Has(entity) ? pool.Get(entity).NetworkEntityKey : null;
        }

        // Эффективная стоимость карты (тот кост-компонент, что есть + модификатор владельца) — для пейн-оплаты.
        static int EffectiveCost(EcsWorld world, int card, int player)
        {
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) return CostModifierUtil.Effective(world, player, card, g.Get(card).Cost);
            var m = world.GetPool<ManaCostComponent>();   if (m.Has(card)) return CostModifierUtil.Effective(world, player, card, m.Get(card).Cost);
            var h = world.GetPool<HealthCostComponent>(); if (h.Has(card)) return CostModifierUtil.Effective(world, player, card, h.Get(card).Cost);
            return 0;
        }

        static bool TryPayCost(EcsWorld world, int card, int player, out DeclineReason reason)
        {
            reason = DeclineReason.Unknown;

            var goldCost = world.GetPool<GoldCostComponent>();
            if (goldCost.Has(card))
            {
                int cost = CostModifierUtil.Effective(world, player, card, goldCost.Get(card).Cost);
                var pool = world.GetPool<GoldComponent>();
                if (!pool.Has(player) || pool.Get(player).Current < cost) { reason = DeclineReason.NotEnoughGold; return false; }
                ref var gold = ref pool.Get(player);
                gold.Current -= cost;
                PublishResource(world, player, EnumService.ResourceType.Gold, gold.Current, gold.Max);
                return true;
            }

            var manaCost = world.GetPool<ManaCostComponent>();
            if (manaCost.Has(card))
            {
                int cost = CostModifierUtil.Effective(world, player, card, manaCost.Get(card).Cost);
                var pool = world.GetPool<ManaComponent>();
                if (!pool.Has(player) || pool.Get(player).Current < cost) { reason = DeclineReason.NotEnoughMana; return false; }
                ref var mana = ref pool.Get(player);
                mana.Current -= cost;
                PublishResource(world, player, EnumService.ResourceType.Mana, mana.Current, mana.Max);
                return true;
            }

            var healthCost = world.GetPool<HealthCostComponent>();
            if (healthCost.Has(card))
            {
                int cost = CostModifierUtil.Effective(world, player, card, healthCost.Get(card).Cost);
                var pool = world.GetPool<HealthComponent>();   // у игрока тоже есть HealthComponent
                if (!pool.Has(player) || pool.Get(player).Current < cost) { reason = DeclineReason.NotEnoughHealth; return false; }
                ref var hp = ref pool.Get(player);
                hp.Current -= cost;
                PublishResource(world, player, EnumService.ResourceType.Health, hp.Current, hp.Max);
                return true;
            }

            // Карта без стоимости — бесплатно.
            return true;
        }

        static void PublishResource(EcsWorld world, int player, EnumService.ResourceType type, int newValue, int maxValue)
        {
            bool isLocal = world.GetPool<PlayerComponent>().Get(player).IsLocalPlayer;
            GameEventBus.Publish(new ResourceChangedEvent
            {
                isLocalPlayer = isLocal,
                Type = type,
                NewValue = newValue,
                MaxValue = maxValue,
            });
        }
    }
}
