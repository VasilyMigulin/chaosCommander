namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Контекст резолва ТЕКУЩЕЙ способности (скрэтч, как ChainContext/SummonScratch). RunResolveAbilityQueue
    /// System кладёт сюда инициатора (AbilityOriginComponent ability-сущности) ПЕРЕД применением её эффектов,
    /// а эффекты генерации (GenerateCardEffect.Spawn*) читают его для АТРИБУЦИИ «кем замешано» — эффект не
    /// знает ability-сущность, поэтому передаём через статик. -1 = нет гранта (нативная способность → инициатор
    /// = владелец карты). ECS однопоточный, одна способность за тик → реентерабельности нет.
    /// </summary>
    public static class AbilityResolveContext
    {
        public static int OriginOwnerId = -1;

        /// <summary>Порядковый номер ТЕКУЩЕГО применения этой способности (1 = первое срабатывание).
        /// Источник — AbilityResolveCounterComponent на ability-сущности, инкремент в начале резолва.
        /// Читает RepeatEffect{Source=SelfResolves} («Нечищенный источник»: 1,2,3… маны). 0 = вне резолва.</summary>
        public static int ResolveCount = 0;

        /// <summary>Строковый ключ триггера, ЗАПУСТИВШЕГО текущий резолв (см. TriggerKeys/AbilityFire.Mark) —
        /// null, если триггер не передал ключ (напр. OnKill/OnAttack). Источник — AbilityTriggerKeyComponent
        /// ability-сущности. Читают PlayTargetCardEffect/PlaySameNameFromHandEffect: цель выбирает ИГРОК
        /// только если разыгрывание идёт от OnCast (игрок сам сейчас играет карту-источник) — любой другой
        /// триггер (OnDie/OnTurnEnd/…) может сработать НЕ в ход владельца/без интерактивного контекста →
        /// таргетинг форсится случайным (как ForceRandomTargetingComponent у Фокус-покус).</summary>
        public static string TriggerKey = null;

        public static void Clear() { OriginOwnerId = -1; ResolveCount = 0; TriggerKey = null; }

        // ── ПРИЧИНА для реакций (хрип и т.п.) ────────────────────────────────────────────────────────
        // Живёт ДОЛЬШЕ одного резолва и НЕ чистится в Clear() — намеренно. Смерть от урона обрабатывается
        // не внутри резолва убийцы, а в следующем кадре (TakeDamage/DieSystem идут в _generalSystems, до
        // _abilitySystems), и к моменту, когда хрип зовёт AbilityFire.Mark, резолв убийцы давно закрыт.
        // Поэтому ключ последней отрезолвленной способности переживает её резолв и заменяется только
        // началом следующего. Пауза ActionPacing между резолвами гарантирует, что смерть успевает
        // обработаться, пока «последней» числится именно та способность, что добила.

        /// <summary>Ключ последней НАЧАТОЙ активации — причина для реакций, возникших после неё.</summary>
        public static ActivationKey LastResolvedKey;
        public static bool HasLastResolved;

        static int _childCounter;   // сколько реакций уже породила эта причина

        /// <summary>Резолв начался: эта активация становится причиной для последующих реакций.</summary>
        public static void BeginResolve(in ActivationKey key)
        {
            LastResolvedKey = key;
            HasLastResolved = true;
            _childCounter = 0;
        }

        /// <summary>Ключ для очередного СЛЕДСТВИЯ последней активации. false — причины нет (каскад закрыт),
        /// тогда вызывающий делает активацию корневой. Глубина не ограничена — см. ActivationKey.</summary>
        public static bool TryNextReactionKey(out ActivationKey key)
        {
            key = default;
            if (!HasLastResolved) return false;
            key = LastResolvedKey.Child(_childCounter);
            _childCounter++;
            return true;
        }

        /// <summary>Матч кончился/мир пересоздан — иначе ключ из прошлого боя пережил бы новый.</summary>
        public static void ClearCause() { HasLastResolved = false; LastResolvedKey = default; _childCounter = 0; }
    }

    // === helper (static) ===
    /// <summary>Записка о причине на карте: выделяет ей узел-потомок текущей активации и вешает
    /// CausedByActivationComponent. Зовут все места, где карта появляется/играется КАК СЛЕДСТВИЕ
    /// (PlayCardUtil.Play — free-каст эффектом; CreateCardSystem — порождение с авто-кастом).</summary>
    public static class CauseStamp
    {
        public static void Mark(Leopotam.EcsLite.EcsWorld world, int card)
        {
            if (card < 0) return;
            if (!AbilityResolveContext.TryNextReactionKey(out var node)) return;   // причины нет — карта корневая

            var pool = world.GetPool<CausedByActivationComponent>();
            if (!pool.Has(card)) pool.Add(card);
            pool.Get(card).Node = node;
        }

        /// <summary>Снять записки со всех карт. Зовётся на границе ХОДА: каскад её не пересекает, а карта,
        /// пролежавшая в руке до следующего хода, иначе утащила бы за собой древнюю волну и встала бы
        /// в очередь ПЕРЕД свежими активациями.</summary>
        public static void ClearAll(Leopotam.EcsLite.EcsWorld world)
        {
            var pool = world.GetPool<CausedByActivationComponent>();
            foreach (var e in world.Filter<CausedByActivationComponent>().End()) pool.Del(e);
        }
    }

    // === struct (Component) ===
    /// <summary>Строковый ключ триггера, ПОСЛЕДНИМ вызвавшего AbilityFire.Mark для этой ability-сущности
    /// (см. TriggerKeys). Перезаписывается КАЖДЫМ новым срабатыванием (дедуп-гард в Mark не даёт перезаписать
    /// значение триггера, который уже ждёт резолва). RunResolveAbilityQueueSystem копирует его в
    /// AbilityResolveContext.TriggerKey перед применением эффектов.</summary>
    public struct AbilityTriggerKeyComponent
    {
        public string Key;

        /// <summary>Активация — РЕАКЦИЯ на чужой резолв (хрип, «когда получает урон», «когда сбрасывают»),
        /// а не собственное действие. Такая встаёт в очередь СРАЗУ ЗА своей причиной, а не в конец
        /// (см. ActivationKey). Источник — ITrigger.IsReaction, передаётся через AbilityFire.Mark.</summary>
        public bool IsReaction;
    }

    // === struct (Component) ===
    /// <summary>Сколько раз способность (ability-сущность) уже РЕЗОЛВИЛАСЬ за матч. Инкремент в
    /// RunResolveAbilityQueueSystem ДО применения эффектов (текущее применение учтено). Пассив резолвит
    /// впрыснутую очередь тем же путём → счётчик зеркален. Повторы множителя (PendingCasts) тоже считаются
    /// (Петля: 1,2 в первый ход, 3,4 во второй). КАВЕАТ: стадии цепочек на пассиве (ApplyChainStage) идут
    /// мимо → SelfResolves внутри AbilityChain не использовать.</summary>
    public struct AbilityResolveCounterComponent
    {
        public int Count;
    }

    // === struct (Component) ===
    /// <summary>ВИНОВНИК срабатывания триггера (на ability-сущности): сущность, вызвавшая событие
    /// (OnCreatureInvoked → вышедшее существо). Ставит AbilityFire.Mark (subjectEntity), читает
    /// RunAbilityTargetingSystem при TargetSelection.TriggerSubject («Неудачная молитва»: урон ИМЕННО
    /// вышедшему). Перезаписывается каждым Mark; без виновника — компонент снимается (не протухает).</summary>
    public struct TriggerSubjectComponent
    {
        public int Entity;
    }
}
