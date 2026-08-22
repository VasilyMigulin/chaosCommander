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
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public int OfferCount = 3;

        // Терминал: куда уходит выбранная карта (для FromPool всегда Hand владельца — создаём новую).
        public DiscoverDest Dest = DiscoverDest.Hand;
        public bool TakeOwnership = false;   // true → карта становится КАСТЕРА (воровство из чужой зоны)

        // Модификаторы ВЫБРАННОЙ карты (аналог SummonModifiers): любой target-эффект — ModifyCostEffect{-2}
        // («стоит на 2 меньше»), BuffStatsEffect и т.п. Зона → RunDiscoverSystem.PlacePicked применяет к
        // существующей сущности (резолв идёт на обоих клиентах → зеркально); пул → едут в GeneratedModScratch,
        // применяет CreateCardSystem при материализации (ключ общий → зеркально). Синка не нужно.
        [Tooltip("Эффекты к выбранной карте («стоит на 2 меньше», бафф/дебафф). Пусто → просто раскопка.")]
        [SerializeReference] public List<IEffect> Modifiers = new();

        // подтип описывает источник кандидатов (зона/пул)
        protected abstract void Configure(EcsWorld world, int cardEntity, ref DiscoverRequestComponent req);

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            if (Modifiers != null)
                foreach (var mod in Modifiers)
                    mod?.Init(world, cardEntity, playerEntity);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (Modifiers != null)
                foreach (var mod in Modifiers)
                    mod?.Dispose();
        }

        // Порядок раскопок в РАМКАХ одного резолва («Приглашение»: рука своя → рука оппонента): растёт
        // на каждый Apply. Оба клиента ре-ранят эффекты в одном порядке → относительный Seq зеркален
        // (абсолютное значение не важно, сравнивается только «кто раньше»).
        static int _seqCounter;

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
            req.Seq               = ++_seqCounter;
            req.Dest              = Dest;
            req.TakeOwnership     = TakeOwnership;
            req.Modifiers         = (Modifiers != null && Modifiers.Count > 0) ? Modifiers.ToArray() : null;
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
        [Tooltip("Ассет CardPool (по критериям, напр. «все зелёные»). Если задан — берём из него, иначе из ручного Pool ниже.")]
        public ScriptableObject PoolAsset;
        [Tooltip("Ручной пул ассетов CardInstanceData (если PoolAsset не задан).")]
        public List<ScriptableObject> Pool = new();

        [Tooltip("Не предлагать карты, уже выбранные ПРЕДЫДУЩИМИ раскопками этого же источника за текущий " +
                 "каст (RecordDiscoverPickEffect должен стоять Modifier'ом у тех раскопок) — «Проклятье для " +
                 "принцессы»: второй проход по тому же пулу эффектов не должен дать дубль первого.")]
        public bool ExcludeAlreadyPicked = false;

        protected override void Configure(EcsWorld world, int cardEntity, ref DiscoverRequestComponent req)
        {
            req.FromPool = true;
            var exp = new List<string>();
            var ids = new List<int>();

            // Пул кладём ПОЛНЫМ, без фильтра: несколько таких эффектов на одной способности резолвятся
            // синхронно один за другим (Apply идёт по списку Effects в кадре резолва), а сам выбор игрока —
            // асинхронный (окно/пик приходит кадрами позже). На момент ЭТОГО Configure отбор предыдущего
            // прохода ещё не записан. Фильтр — в RunDiscoverSystem.BuildPoolOffer, который строит показ
            // ПОСЛЕ того, как HasEarlierPending гарантирует резолв более раннего запроса того же источника.
            if (PoolAsset is ICardPool cp && cp.Cards != null && cp.Cards.Count > 0)
            {
                foreach (var c in cp.Cards)
                    if (c != null) { exp.Add(c.ExpansionId); ids.Add(c.CardId); }
            }
            else if (Pool != null)
            {
                foreach (var so in Pool)
                    if (so is ICreatable c) { exp.Add(c.ExpansionId); ids.Add(c.CardId); }
            }
            req.PoolExp              = exp.ToArray();
            req.PoolCardId           = ids.ToArray();
            req.ExcludeAlreadyPicked = ExcludeAlreadyPicked;
        }
    }

    // === class (OOP) === Раскопка из ИСТОРИИ последних разыгранных заклинаний владельца
    // (RecentSpellsHistoryComponent — трекает LastPlayedSpellTrackerSystem), а не из статичного пула
    // ассетов. «Посмотрите 3 последних разыгранных заклинания, выберите 1» (Батюшка-барыга). Терминал —
    // FromPool: выбранное создаётся ЗАНОВО (своя копия сыгранного заклинания), а не воровство чужой
    // сущности — как обычный GainRandomCardEffect/DiscoverFromPool.
    [Serializable]
    public sealed class DiscoverFromRecentSpellsEffect : DiscoverEffect
    {
        protected override void Configure(EcsWorld world, int cardEntity, ref DiscoverRequestComponent req)
        {
            req.FromPool = true;
            var exp = new List<string>();
            var ids = new List<int>();

            var ownerPool = world.GetPool<OwnerComponent>();
            int playerEntity = ownerPool.Has(cardEntity) ? ZoneListUtil.FindPlayerEntity(world, ownerPool.Get(cardEntity).OwnerId) : -1;
            if (playerEntity >= 0)
            {
                var histPool = world.GetPool<RecentSpellsHistoryComponent>();
                if (histPool.Has(playerEntity))
                {
                    ref var h = ref histPool.Get(playerEntity);
                    if (h.ExpansionIds != null)
                        for (int i = 0; i < h.ExpansionIds.Count; i++)
                        {
                            exp.Add(h.ExpansionIds[i]);
                            ids.Add(h.ModelIds[i]);
                        }
                }
            }

            req.PoolExp    = exp.ToArray();
            req.PoolCardId = ids.ToArray();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCRY («предсказание») — NonTarget-эффект: посмотреть LookCount ВЕРХНИХ карт колоды, одну оставить
    // наверх, остальные — в конец колоды (в исходном относительном порядке). Родственно DISCOVER (то же
    // окно выбора — RunScrySystem), но НЕ перекладывает карту в другую зону и НЕ создаёт новую — переставляет
    // то, что уже лежит в колоде. См. RunScrySystem.
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ScryEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public int LookCount = 3;

        static int _seqCounter;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;
            int ownerPlayer = ZoneListUtil.FindPlayerEntity(world, ownerId);
            if (ownerPlayer < 0) return;

            int e = world.NewEntity();
            ref var req = ref world.GetPool<ScryRequestComponent>().Add(e);
            req.SourceCardEntity  = cardEntity;
            req.OwnerPlayerEntity = ownerPlayer;
            req.OwnerId           = ownerId;
            req.LookCount         = LookCount;
            req.Seq               = ++_seqCounter;
        }
    }
}
