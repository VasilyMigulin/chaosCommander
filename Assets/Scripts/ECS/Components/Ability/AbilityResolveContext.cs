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
    }

    // === struct (Component) ===
    /// <summary>Строковый ключ триггера, ПОСЛЕДНИМ вызвавшего AbilityFire.Mark для этой ability-сущности
    /// (см. TriggerKeys). Перезаписывается КАЖДЫМ новым срабатыванием (дедуп-гард в Mark не даёт перезаписать
    /// значение триггера, который уже ждёт резолва). RunResolveAbilityQueueSystem копирует его в
    /// AbilityResolveContext.TriggerKey перед применением эффектов.</summary>
    public struct AbilityTriggerKeyComponent
    {
        public string Key;
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
