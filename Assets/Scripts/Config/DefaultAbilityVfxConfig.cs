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
    }
}
