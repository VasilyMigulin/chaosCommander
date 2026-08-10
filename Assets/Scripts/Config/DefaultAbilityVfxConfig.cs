using UnityEngine;

namespace Game.Core.Configs
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Универсальные VFX «на кастере» для триггеров OnCast/OnDie — как в HS: «черепок» на деатрэттле,
    /// «белая аура» на баттлкрае. Играют ВСЕГДА при этих двух триггерах, ДОПОЛНИТЕЛЬНО к любому
    /// кастомному VFX способности (не фолбэк на случай отсутствия своего VFX, а универсальный индикатор
    /// «это баттлкрай/деатрэттл сработал», поверх чего угодно ещё). Читает RunResolveAbilityQueueSystem
    /// (EmitDefaultTriggerVfx) по triggerKey текущего резолва. Любой другой триггер (OnAttack/OnTurnStart/…)
    /// этот индикатор не получает — только явно эти два.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Default Ability Vfx Config", fileName = "DefaultAbilityVfxConfig")]
    public sealed class DefaultAbilityVfxConfig : ScriptableObject
    {
        [Tooltip("Вспышка на кастере при разыгрывании (OnCast), если у способности нет собственного VFX.")]
        public GameObject OnCastVfxPrefab;

        [Tooltip("Вспышка на кастере при срабатывании предсмертного эффекта (OnDie), если у способности нет " +
                 "собственного VFX — напр. «черепок», обозначающий, что сработал деатрэттл.")]
        public GameObject OnDieVfxPrefab;

        [Tooltip("ДЕФОЛТНЫЙ эффект появления существа на клетке (пыль «приземлился») — играет как Pre-фаза " +
                 "для ЛЮБОГО существа БЕЗ собственной SummonVfx-спеки (у лег/экзотов своя спека на " +
                 "CardCreatureModel перекрывает этот дефолт целиком). Null — обычное появление без эффекта.")]
        public GameObject DefaultSummonVfxPrefab;

        [Tooltip("Вспышка на существе, когда свойство «Укреплённый» (Shielded) поглощает удар — играет на " +
                 "КАЖДОМ поглощённом ударе (не только на последнем заряде). У свойства нет собственной " +
                 "Vfx-спеки (в отличие от способностей), поэтому это единственный источник VFX для блока.")]
        public GameObject ShieldedBlockVfxPrefab;

        [Tooltip("ПОСТОЯННЫЙ визуал на модели существа, пока активно свойство «Укреплённый» (заряды щита " +
                 "> 0) — сам щит-бабл/аура, а не вспышка блока (та выше, ShieldedBlockVfxPrefab). Крепится " +
                 "к точке атача Body, гаснет вместе со свойством (спали заряды/смерть). См. PropertyAuraVisualSystem.")]
        public GameObject ShieldedAuraVfxPrefab;

        [Tooltip("ПОСТОЯННЫЙ визуал на модели существа, пока активно свойство «Защитник» (Taunt) — крутящаяся " +
                 "аура на точке атача Body. Гаснет вместе со свойством (аура-ревёрт/смерть). См. PropertyAuraVisualSystem.")]
        public GameObject TauntAuraVfxPrefab;

        [Tooltip("Разовая вспышка в момент, когда цель получает стаки статуса «Отравлен» (PoisonComponent) — " +
                 "от урона носителя свойства «Ядовитый» (Venomous). Играет на цели, каждое наложение.")]
        public GameObject PoisonedHitVfxPrefab;

        [Tooltip("ПОСТОЯННЫЙ визуал НАД ГОЛОВОЙ существа, пока на нём висит статус «Отравлен» (PoisonComponent, " +
                 "Stacks > 0) — точка атача Head. Не привязан к свойству-источнику (Venomous), это статус ЦЕЛИ; " +
                 "снимается только смертью носителя (яд не лечится). См. PropertyAuraVisualSystem.")]
        public GameObject PoisonedStatusVfxPrefab;

        [Tooltip("Разовая вспышка на ЦЕЛИ в момент, когда урон носителя свойства «Вампиризм» лечит владельца — " +
                 "визуал самого «укуса»/оттягивания жизни. Играет вместе с VampirismHealVfxPrefab.")]
        public GameObject VampirismHitVfxPrefab;

        [Tooltip("Разовая вспышка НА АВАТАРЕ ВЛАДЕЛЬЦА носителя «Вампиризма» в момент лечения — визуал " +
                 "«пришедшего» здоровья. Играет вместе с VampirismHitVfxPrefab (тот — на цели урона).")]
        public GameObject VampirismHealVfxPrefab;
    }
}
