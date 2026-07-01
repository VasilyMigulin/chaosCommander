using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // DISCOVER («найдите карту») — NonTarget-эффект: предложить N карт, выбранную ПОЛОЖИТЬ В РУКУ.
    // Отличие от таргетинга Selected: Selected ВОЗДЕЙСТВУЕТ на выбранную СУЩЕСТВУЮЩУЮ сущность
    // (урон существу в колоде), а discover ПОЛУЧАЕТ карту. Поэтому цель не нужна (NonTarget):
    // источник — зона (существующие сущности) ИЛИ пул (card-id, карты ещё нет → создаём новую,
    // её нельзя «таргетить» — оттого discover это эффект, а не таргетинг).
    //
    // Async: Apply лишь СОЗДАЁТ запрос (DiscoverRequestComponent); offer→pick→в руку ведёт
    // RunDiscoverSystem. Резолв способности реплеится → Apply исполняется на ОБОИХ клиентах:
    // у активного (своя карта) показывается окно, у пассивного запрос ждёт присланный выбор
    // (CardPickReplayStore через готовый ActionCardPicked-канал, как CardPickSelectionSystem).
    // ─────────────────────────────────────────────────────────────────────────
    public abstract class DiscoverEffect : EffectBase
    {
        public int OfferCount = 3;

        // Терминал: куда уходит выбранная карта (для FromPool всегда Hand владельца — создаём новую).
        public DiscoverDest Dest = DiscoverDest.Hand;
        public bool TakeOwnership = false;   // true → карта становится КАСТЕРА (воровство из чужой зоны)

        // подтип описывает источник кандидатов (зона/пул)
        protected abstract void Configure(EcsWorld world, int cardEntity, ref DiscoverRequestComponent req);

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;
            if (!TryFindOwnerPlayer(world, ownerId, out int ownerPlayer)) return;

            int e = world.NewEntity();
            ref var req = ref world.GetPool<DiscoverRequestComponent>().Add(e);
            req.SourceCardEntity  = cardEntity;
            req.OwnerPlayerEntity = ownerPlayer;
            req.OwnerId           = ownerId;
            req.OfferCount        = OfferCount;
            req.Dest              = Dest;
            req.TakeOwnership     = TakeOwnership;
            Configure(world, cardEntity, ref req);
        }

        static bool TryFindOwnerPlayer(EcsWorld world, int ownerId, out int playerEntity)
        {
            var pp = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
            {
                if (pp.Get(pe).PlayerId != ownerId) continue;
                playerEntity = pe; return true;
            }
            playerEntity = -1; return false;
        }
    }

    // === class (OOP) === Раскопка из ЗОНЫ владельца (колода/рука/кладбище): выбранную СУЩЕСТВУЮЩУЮ
    // карту перевешиваем в руку. Фильтры (цвет/архетип/…) сужают кандидатов. «Очистить от скверны».
    [Serializable]
    public sealed class DiscoverFromZoneEffect : DiscoverEffect
    {
        public TargetZone Zone = TargetZone.Deck;   // Deck / Hand / Grave
        [SerializeReference] public List<ITargetFilter> Filters = new();

        protected override void Configure(EcsWorld world, int cardEntity, ref DiscoverRequestComponent req)
        {
            req.FromPool = false;
            req.Zone     = Zone;
            req.Filters  = Filters != null ? Filters.ToArray() : Array.Empty<ITargetFilter>();
        }
    }

    // === class (OOP) === Раскопка из ПУЛА ассетов (ICreatable): выбранную карту СОЗДАЁМ в руке.
    // Пул сам играет роль фильтра (напр. «все жёлтые спеллы» / «карты с цветом X»). Так как карт
    // ещё нет как сущностей, в окне показываем синтетические токены + визуал из CardConfig.
    [Serializable]
    public sealed class DiscoverFromPoolEffect : DiscoverEffect
    {
        [Tooltip("Пул ассетов CardInstanceData, из которых раскапываем (перетащить).")]
        public List<ScriptableObject> Pool = new();

        protected override void Configure(EcsWorld world, int cardEntity, ref DiscoverRequestComponent req)
        {
            req.FromPool = true;
            var exp = new List<string>();
            var ids = new List<int>();
            if (Pool != null)
                foreach (var so in Pool)
                    if (so is ICreatable c) { exp.Add(c.ExpansionId); ids.Add(c.CardId); }
            req.PoolExp    = exp.ToArray();
            req.PoolCardId = ids.ToArray();
        }
    }
}
