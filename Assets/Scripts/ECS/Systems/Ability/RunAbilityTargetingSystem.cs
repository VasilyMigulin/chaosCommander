using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Этап таргетинга. Берёт ability-сущности с AbilityTargetingState и вычисляет цели по типу:
    ///   NonTarget (нет таргет-компонента) → [player];
    ///   Field (AbilityFieldComponent)     → все кандидаты в области по фильтрам;
    ///   Target/Random (AbilityTargetComponent) → Count случайных из валидных;
    ///   Target/Selected                   → переход в AbilityTargetPendingState (ждём игрока).
    /// Результат (кроме Selected) → AbilityQueuedState{Targets}; снимает AbilityTargetingState.
    /// </summary>
    public sealed class RunAbilityTargetingSystem : IEcsRunSystem
    {
        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            var statePool = world.GetPool<AbilityTargetingState>();
            var ownerPool = world.GetPool<AbilityOwnerComponent>();
            var targetPool = world.GetPool<AbilityTargetComponent>();
            var fieldPool = world.GetPool<AbilityFieldComponent>();
            var selfPool = world.GetPool<AbilitySelfComponent>();
            var pendingPool = world.GetPool<AbilityTargetPendingState>();
            var pickPendingPool = world.GetPool<AbilityPickPendingState>();
            var queuedPool = world.GetPool<AbilityQueuedState>();

            var filter = world.Filter<AbilityTargetingState>().Inc<AbilityOwnerComponent>().End();

            var pending = new List<int>();
            foreach (var e in filter) pending.Add(e);   // буфер: меняем компоненты по ходу

            foreach (var entity in pending)
            {
                ref var owner = ref ownerPool.Get(entity);

                if (targetPool.Has(entity))
                {
                    ref var tc = ref targetPool.Get(entity);

                    // Виновник триггера («Неудачная молитва»: урон ИМЕННО вышедшему существу). Кандидаты
                    // собираются штатно (те же фильтры: WithoutColor и пр.) — если виновник их не прошёл
                    // (жёлтый) или уже умер, целей нет → способность фуззлится. Синк — target-ключом.
                    if (tc.Selection == TargetSelection.TriggerSubject)
                    {
                        var subjPool = world.GetPool<TriggerSubjectComponent>();
                        int subject = subjPool.Has(entity) ? subjPool.Get(entity).Entity : -1;
                        if (subjPool.Has(entity)) subjPool.Del(entity);   // одноразовый (не протухает)

                        var valid = TargetGather.Gather(world, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone, tc.IncludeCommanderInZones);
                        Queue(world, queuedPool, entity, subject >= 0 && valid.Contains(subject)
                            ? new[] { subject }
                            : Array.Empty<int>());
                        statePool.Del(entity);
                        continue;
                    }

                    // Как TriggerSubject, но БЕЗ требования «жив и стоит в Board-списке» (тот список — Exc<DeadTag>,
                    // а виновник здесь — существо, которое ТОЛЬКО ЧТО умерло: Собиратель кукол реагирует на
                    // OnAllyDiedTrigger и копирует ИМЕННО погибшего. Обычный TriggerSubject тут всегда фуззлился
                    // бы (см. его комментарий: «уже умер → целей нет» — это осознанное поведение ДЛЯ НЕГО,
                    // не баг; здесь — другая семантика по дизайну). Фильтры всё равно применяются напрямую
                    // к субъекту (FiltersOk), просто без сверки со списком «живых на поле».
                    if (tc.Selection == TargetSelection.TriggerSubjectAllowDead)
                    {
                        var subjPool = world.GetPool<TriggerSubjectComponent>();
                        int subject = subjPool.Has(entity) ? subjPool.Get(entity).Entity : -1;
                        if (subjPool.Has(entity)) subjPool.Del(entity);

                        bool ok = subject >= 0 && TargetGather.FiltersOk(world, subject, owner.CardEntity, owner.PlayerEntity, tc.Filters);
                        Queue(world, queuedPool, entity, ok ? new[] { subject } : Array.Empty<int>());
                        statePool.Del(entity);
                        continue;
                    }

                    // Авто-розыгрыш (Фокус-покус): карта с ForceRandomTargetingComponent НЕ передаёт выбор игроку —
                    // Selected трактуем как Random (игра сама выбирает цели, как Йогг-Сарон).
                    bool forceRandom = world.GetPool<ForceRandomTargetingComponent>().Has(owner.CardEntity);

                    // ЧУЖАЯ карта: этот пайплайн крутится ТОЛЬКО на активном клиенте (пассив реплеит уже
                    // выбранные цели снапшотом) — но триггер способности НЕ ОБЯЗАН принадлежать активному
                    // игроку (напр. OnDie у существа ОППОНЕНТА, которое активный только что убил). Без этой
                    // проверки интерактивный Selected-пикер показался бы активному игроку за ЧУЖУЮ способность
                    // (окно выбора цели «не по адресу»). Своя карта — как раньше, интерактив.
                    if (!forceRandom)
                    {
                        var playerPool = world.GetPool<PlayerComponent>();
                        if (!playerPool.Has(owner.PlayerEntity) || !playerPool.Get(owner.PlayerEntity).IsLocalPlayer)
                            forceRandom = true;
                        // Владелец НЕ в своём ходу (эффект сработал в ЧУЖОЙ ход — deathrattle Королевской пиньяты
                        // в ход оппонента): интерактивный выбор физически невозможен (игрок не может кликать не в
                        // свой ход) → форс random, иначе Selected-пикер повис бы навсегда (софтлок, ИИ не отдаёт ход).
                        else if (!IsOwnersTurn(world, owner.PlayerEntity))
                            forceRandom = true;
                    }

                    if (tc.Selection == TargetSelection.Selected && !forceRandom)
                    {
                        // нет валидных целей → способность фуззлится, не зависаем в ожидании выбора
                        var valid = TargetGather.Gather(world, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone, tc.IncludeCommanderInZones);
                        if (valid.Count > 0)
                        {
                            if (tc.Zone == TargetZone.Board)
                            {
                                ref var p = ref pendingPool.Add(entity);   // выбор по клеткам доски
                                p.PlayerEntity = owner.PlayerEntity;
                                p.Chosen = Array.Empty<int>();
                            }
                            else
                            {
                                ref var pp = ref pickPendingPool.Add(entity);   // выбор через окно (колода/рука/кладбище)
                                pp.PlayerEntity = owner.PlayerEntity;
                                pp.Chosen = Array.Empty<int>();
                            }
                        }
                        statePool.Del(entity);
                        continue;
                    }

                    var candidates = TargetGather.Gather(world, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone, tc.IncludeCommanderInZones);

                    // Умный авто-выбор ИИ (PvE): карта несёт AiTargetPreferenceComponent → вместо случайного
                    // кандидата берём лучших по критерию (фильтры уже отсеяли, КОГО можно).
                    var prefPool = world.GetPool<AiTargetPreferenceComponent>();
                    var pref = prefPool.Has(owner.CardEntity) ? prefPool.Get(owner.CardEntity).Mode : AiTargetPreference.None;

                    int[] picked;
                    if (tc.Selection == TargetSelection.Strongest)           picked = PickStrongest(world, candidates, tc.Count);
                    else if (tc.Selection == TargetSelection.MostExpensive)  picked = PickByCost(world, candidates, tc.Count, mostExpensive: true);
                    else if (tc.Selection == TargetSelection.LeastExpensive) picked = PickByCost(world, candidates, tc.Count, mostExpensive: false);
                    else if (tc.Selection == TargetSelection.MostWounded)    picked = PickMostWounded(world, candidates, tc.Count);
                    else if (pref != AiTargetPreference.None)                picked = PickByPreference(world, candidates, tc.Count, pref);
                    else                                                     picked = PickRandom(candidates, tc.Count);
                    Queue(world, queuedPool, entity, picked);
                }
                else if (fieldPool.Has(entity))
                {
                    ref var fc = ref fieldPool.Get(entity);
                    var candidates = TargetGather.Gather(world, fc.Filters, owner.CardEntity, owner.PlayerEntity, fc.Area, fc.Zone, fc.IncludeCommanderInZones);
                    Queue(world, queuedPool, entity, candidates.ToArray());
                }
                else if (selfPool.Has(entity))
                {
                    Queue(world, queuedPool, entity, new[] { owner.CardEntity });     // AbilityToSelf → сам источник
                }
                else
                {
                    Queue(world, queuedPool, entity, new[] { owner.PlayerEntity });   // NonTarget
                }

                statePool.Del(entity);
            }
        }

        /// <summary>
        /// Пересобрать цели способности на АКТУАЛЬНОМ состоянии доски — для резолва (RunResolveAbilityQueueSystem),
        /// чтобы существо, ПРИЗВАННОЕ другой способностью раньше в этой же пачке (OnTurnStart: чара-призыв →
        /// чара-урон/бафф), попадало под действие (Field / Random / Strongest / дешевейший). Возвращает null, если
        /// пересбор НЕ применим: Selected (выбор игрока уже зафиксирован), TriggerSubject/TriggerSubjectAllowDead
        /// (конкретный виновник), NonTarget/Self (цель стабильна). Звать ТОЛЬКО на активе — синк идёт через ключи целей (пассив реплеит
        /// финальный набор из AbilityResolvedNetEvent, а не пересобирает у себя). Та же диспетчеризация, что в Run.
        /// </summary>
        public static int[] RecomputeNonInteractive(EcsWorld world, int abilityEntity)
        {
            var ownerPool = world.GetPool<AbilityOwnerComponent>();
            if (!ownerPool.Has(abilityEntity)) return null;
            ref var owner = ref ownerPool.Get(abilityEntity);

            // Field — все существа области СЕЙЧАС.
            var fieldPool = world.GetPool<AbilityFieldComponent>();
            if (fieldPool.Has(abilityEntity))
            {
                ref var fc = ref fieldPool.Get(abilityEntity);
                return TargetGather.Gather(world, fc.Filters, owner.CardEntity, owner.PlayerEntity, fc.Area, fc.Zone, fc.IncludeCommanderInZones).ToArray();
            }

            // Target — пересобираем кандидатов и повторяем ВЫБОР по Selection (та же логика, что в Run).
            var targetPool = world.GetPool<AbilityTargetComponent>();
            if (!targetPool.Has(abilityEntity)) return null;   // NonTarget/Self — цель стабильна
            ref var tc = ref targetPool.Get(abilityEntity);
            if (tc.Selection == TargetSelection.Selected || tc.Selection == TargetSelection.TriggerSubject
                || tc.Selection == TargetSelection.TriggerSubjectAllowDead) return null;

            var candidates = TargetGather.Gather(world, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone, tc.IncludeCommanderInZones);
            var prefPool = world.GetPool<AiTargetPreferenceComponent>();
            var pref = prefPool.Has(owner.CardEntity) ? prefPool.Get(owner.CardEntity).Mode : AiTargetPreference.None;

            if (tc.Selection == TargetSelection.Strongest)      return PickStrongest(world, candidates, tc.Count);
            if (tc.Selection == TargetSelection.MostExpensive)  return PickByCost(world, candidates, tc.Count, mostExpensive: true);
            if (tc.Selection == TargetSelection.LeastExpensive) return PickByCost(world, candidates, tc.Count, mostExpensive: false);
            if (tc.Selection == TargetSelection.MostWounded)    return PickMostWounded(world, candidates, tc.Count);
            if (pref != AiTargetPreference.None)                return PickByPreference(world, candidates, tc.Count, pref);
            return PickRandom(candidates, tc.Count);
        }

        // Владелец в СВОЁМ ходу? (ActiveState или каскад начала/конца) — иначе интерактивный выбор невозможен.
        static bool IsOwnersTurn(EcsWorld world, int playerEntity)
        {
            if (playerEntity < 0) return false;
            return world.GetPool<ActiveState>().Has(playerEntity)
                || world.GetPool<StartTurnState>().Has(playerEntity)
                || world.GetPool<EndTurnState>().Has(playerEntity);
        }

        static void Queue(EcsWorld world, EcsPool<AbilityQueuedState> pool, int entity, int[] targets)
        {
            // FIFO-ЗАЩИТА (баг 2026-07-30 «выпрыгивает только один Медлительный дворецкий»): способность
            // могла сработать ПОВТОРНО, пока прошлое срабатывание ещё стоит в очереди нерезолвнутым —
            // и перезапись затирала его цели. Классический сценарий: две копии реагируют на выход
            // существа, первая выходит на стол → её выход = новый CreatureInvokedEvent → вторая копия
            // фейрится ещё раз, уже вхолостую (свой не проходит фильтры), и пустой результат стирал
            // ЕЁ ЖЕ валидную цель. Держим ПЕРВОЕ срабатывание (оно старше), новое отбрасываем.
            if (pool.Has(entity))
            {
                var prev = pool.Get(entity).Targets;
                if (prev != null && prev.Length > 0)
                {
                    if (targets != null && targets.Length > 0)
                        UnityEngine.Debug.LogWarning($"[Targeting] ability={entity} повторное срабатывание, пока прошлое в очереди → пропущено (цели первого сохранены)");
                    return;
                }
            }

            if (!pool.Has(entity)) pool.Add(entity);
            ref var q = ref pool.Get(entity);
            q.Targets = targets;
            q.Key = BuildKey(world, entity);
        }

        /// <summary>Ключ порядка для активации. РЕАКЦИЯ (хрип и т.п.) наследует ключ вызвавшей её
        /// активации и встаёт сразу за ней; всё остальное открывает свою волну. Глубина вложенности
        /// исчерпана или причины нет → корневой ключ (отработает, но в конце очереди).</summary>
        static ActivationKey BuildKey(EcsWorld world, int abilityEntity)
        {
            var ownerPool = world.GetPool<AbilityOwnerComponent>();
            var keyPool = world.GetPool<AbilityTriggerKeyComponent>();

            // 1) У карты есть ЗАПИСКА О ПРИЧИНЕ (порождена/разыграна эффектом) — берём её узел напрямую.
            //    Это НЕ зависит от того, сколько прошло времени и опустела ли очередь: записка едет с картой.
            //    Способности одной карты делятся по AbilityIndex, поэтому сохраняют свой порядок.
            if (ownerPool.Has(abilityEntity))
            {
                int card = ownerPool.Get(abilityEntity).CardEntity;
                var causePool = world.GetPool<CausedByActivationComponent>();
                if (causePool.Has(card))
                    return causePool.Get(card).Node.Child(ownerPool.Get(abilityEntity).AbilityIndex);
            }

            // 2) Реактивный триггер (хрип/урон) — родителя берём из текущей активации.
            bool isReaction = keyPool.Has(abilityEntity) && keyPool.Get(abilityEntity).IsReaction;

            if (isReaction && AbilityResolveContext.TryNextReactionKey(out var child))
            {
                // Видно, ЧТО от ЧЕГО произошло — по этой паре строк каскад проверяется без спец-сценария.
                UnityEngine.Debug.Log($"[Queue] следствие {child} ← причина {AbilityResolveContext.LastResolvedKey} "
                                    + $"(ability={abilityEntity})");

                // Глубину НЕ обрезаем — сложный каскад легитимен. Но очень длинная цепочка почти всегда
                // означает зацикливание («хрип призывает то, что снова умирает»), а это подвесит матч
                // независимо от порядка. Поэтому просто громко сообщаем, поведение не меняем.
                if (child.Depth == ActivationKey.SuspiciousDepth)
                    UnityEngine.Debug.LogWarning($"[Queue] цепочка следствий достигла глубины "
                        + $"{ActivationKey.SuspiciousDepth} (ability={abilityEntity}, ключ {child}) — "
                        + "похоже на зациклившийся каскад, проверь карты в цепочке");
                return child;
            }

            // Корень: порядок ВЫХОДА карты на стол, затем индекс способности внутри карты. Оба берём
            // СЕЙЧАС и запекаем в ключ — карта может уйти с доски между постановкой и резолвом.
            var entryPool = world.GetPool<BoardEntryOrderComponent>();
            int entry = 0, idx = 0;
            if (ownerPool.Has(abilityEntity))
            {
                ref var ow = ref ownerPool.Get(abilityEntity);
                idx = ow.AbilityIndex;
                if (entryPool.Has(ow.CardEntity)) entry = entryPool.Get(ow.CardEntity).Seq;
            }
            return ActivationKey.Root(Time.frameCount, entry, idx);
        }

        // Count целей с наибольшей атакой (тай-брейк не важен для синка — выбор активного едет в ключах целей).
        // public — переиспользует RunChainSystem (стадия цепочки с Selection=Strongest).
        public static int[] PickStrongest(EcsWorld world, List<int> candidates, int count)
        {
            if (count <= 0 || candidates.Count == 0) return Array.Empty<int>();
            var atk = world.GetPool<AttackComponent>();
            candidates.Sort((a, b) =>
            {
                int va = atk.Has(a) ? atk.Get(a).Value : 0;
                int vb = atk.Has(b) ? atk.Get(b).Value : 0;
                return vb.CompareTo(va);   // по убыванию атаки
            });
            int take = Math.Min(count, candidates.Count);
            var res = new int[take];
            candidates.CopyTo(0, res, 0, take);
            return res;
        }

        // Count целей с наибольшим ПОТЕРЯННЫМ здоровьем (Max-Current). Небитые (потеряно 0) отсекаются —
        // «самому раненому» без раненых = нет целей, способность фуззлится (Домашний медик не лечит здоровых).
        // Тай-брейк не важен для синка — выбор активного едет в target-ключах (как Strongest/Random).
        // public — переиспользует RunChainSystem (стадия цепочки с Selection=MostWounded).
        public static int[] PickMostWounded(EcsWorld world, List<int> candidates, int count)
        {
            if (count <= 0 || candidates.Count == 0) return Array.Empty<int>();
            var hp = world.GetPool<HealthComponent>();
            int Lost(int e) => hp.Has(e) ? hp.Get(e).Max - hp.Get(e).Current : 0;
            candidates.RemoveAll(e => Lost(e) <= 0);
            if (candidates.Count == 0) return Array.Empty<int>();
            candidates.Sort((a, b) => Lost(b).CompareTo(Lost(a)));   // по убыванию потерянного HP
            int take = Math.Min(count, candidates.Count);
            var res = new int[take];
            candidates.CopyTo(0, res, 0, take);
            return res;
        }

        // Count целей по ЭФФЕКТИВНОЙ стоимости (Gold/Mana/Health — какой кост у карты). mostExpensive → по
        // убыванию, иначе по возрастанию. «Самое слабое существо» (Канализационный прорыв) = LeastExpensive.
        // Тай-брейк не важен для синка — выбор активного едет в target-ключах (как Strongest/Random).
        public static int[] PickByCost(EcsWorld world, List<int> candidates, int count, bool mostExpensive)
        {
            if (count <= 0 || candidates.Count == 0) return Array.Empty<int>();
            var gold = world.GetPool<GoldCostComponent>();
            var mana = world.GetPool<ManaCostComponent>();
            var hp   = world.GetPool<HealthCostComponent>();
            int CostOf(int e) =>
                gold.Has(e) ? gold.Get(e).Cost :
                mana.Has(e) ? mana.Get(e).Cost :
                hp.Has(e)   ? hp.Get(e).Cost   : 0;
            candidates.Sort((a, b) => mostExpensive ? CostOf(b).CompareTo(CostOf(a)) : CostOf(a).CompareTo(CostOf(b)));
            int take = Math.Min(count, candidates.Count);
            var res = new int[take];
            candidates.CopyTo(0, res, 0, take);
            return res;
        }

        // Сортировка по критерию ИИ (AiTargetPreferenceComponent). Детерминизм синка не нужен:
        // компонент ставит только RunAiTurnSystem (PvE, без пассива).
        static int[] PickByPreference(EcsWorld world, List<int> candidates, int count, AiTargetPreference pref)
        {
            if (count <= 0 || candidates.Count == 0) return Array.Empty<int>();
            var atk = world.GetPool<AttackComponent>();
            var hp = world.GetPool<HealthComponent>();

            int Atk(int e) => atk.Has(e) ? atk.Get(e).Value : 0;
            int Cur(int e) => hp.Has(e) ? hp.Get(e).Current : 0;
            int Lost(int e) => hp.Has(e) ? hp.Get(e).Max - hp.Get(e).Current : 0;

            candidates.Sort((a, b) => pref switch
            {
                AiTargetPreference.LowestHealth => Cur(a) != Cur(b) ? Cur(a).CompareTo(Cur(b)) : Atk(b).CompareTo(Atk(a)),   // добить; при равном HP — опаснее
                AiTargetPreference.MostDamaged  => Lost(b).CompareTo(Lost(a)),                                                // макс. потерянного HP
                _                               => Atk(a) != Atk(b) ? Atk(b).CompareTo(Atk(a)) : Cur(b).CompareTo(Cur(a)),   // HighestAttack; при равной атаке — жирнее
            });
            int take = Math.Min(count, candidates.Count);
            var res = new int[take];
            candidates.CopyTo(0, res, 0, take);
            return res;
        }

        static int[] PickRandom(List<int> candidates, int count)
        {
            if (count <= 0 || candidates.Count == 0) return Array.Empty<int>();
            if (candidates.Count <= count) return candidates.ToArray();

            for (int i = 0; i < count; i++)   // частичный Фишер-Йейтс; TODO: детерминизм для синка
            {
                int j = UnityEngine.Random.Range(i, candidates.Count);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            var res = new int[count];
            candidates.CopyTo(0, res, 0, count);
            return res;
        }
    }
}

