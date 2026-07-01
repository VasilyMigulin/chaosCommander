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

                // Гейт хода: карты разыгрывает ТОЛЬКО игрок, чей сейчас ход. «Чей ход» = ActiveState ИЛИ
                // каскад начала/конца (StartTurnState/EndTurnState) — иначе ФОРС-ПЛЕЙ с добора на старте хода
                // (OnDrawForcePlay: Подстава/Вонючее облако) отклонялся, т.к. ActiveState вешается лишь ПОСЛЕ
                // оседания каскада. А PlayCardUtil уже снял карту из руки → токен зависал в лимбе (без каста,
                // без синка) → фантом в зеркале руки у оппонента. Пассив ни одного из стейтов не имеет → заблокирован.
                if (!activeStatePool.Has(player) && !startStatePool.Has(player) && !endStatePool.Has(player))
                {
                    Decline(declinePool, card, DeclineReason.Unknown);
                    continue;
                }

                int ownerId = ownerPool.Get(card).OwnerId;

                // pre-cost: лимит чар (5) — чтобы не списывать стоимость зря
                if (charmTag.Has(card) && CharmCount(world, ownerPool, ownerId) >= CharmLimit)
                {
                    Decline(declinePool, card, DeclineReason.CharmLimitReached);
                    continue;
                }

                if (!free && !TryPayCost(world, card, player, out var reason))
                {
                    Decline(declinePool, card, reason);
                    continue;
                }

                // ── делегирование по типу ──
                if (creatureTag.Has(card))
                {
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
        static void Decline(EcsPool<DeclineCardCastEvent> declinePool, int card, DeclineReason reason)
        {
            if (!declinePool.Has(card)) declinePool.Add(card).Reason = reason;
            GameEventBus.Publish(new TargetSelectionCancelledEvent { CardEntity = card });
        }

        static int CharmCount(EcsWorld world, EcsPool<OwnerComponent> ownerPool, int ownerId)
        {
            var filter = world.Filter<CharmTag>().Inc<BoardTag>().End();
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

        static bool TryPayCost(EcsWorld world, int card, int player, out DeclineReason reason)
        {
            reason = DeclineReason.Unknown;

            var goldCost = world.GetPool<GoldCostComponent>();
            if (goldCost.Has(card))
            {
                int cost = CostModifierUtil.Effective(world, player, goldCost.Get(card).Cost);
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
                int cost = CostModifierUtil.Effective(world, player, manaCost.Get(card).Cost);
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
                int cost = CostModifierUtil.Effective(world, player, healthCost.Get(card).Cost);
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
