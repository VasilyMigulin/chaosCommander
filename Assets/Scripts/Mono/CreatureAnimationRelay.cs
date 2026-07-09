using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Реле Animation Events. Animator висит на ДОЧЕРНЕМ объекте, и Unity вызывает анимационные ивенты
    /// именно на объекте с аниматором — родительский CreatureView их не получает. Поэтому это реле
    /// добавляется на объект аниматора (в CreatureView.Awake), имеет методы с теми же именами, что и в
    /// клипе, и пробрасывает вызовы обратно в CreatureView через делегаты.
    ///
    /// КОНВЕНЦИЯ ИМЁН (авторит аниматор/гейм-дизайнер САМ в клипах, вручную):
    ///   AttackEvent — момент удара в анимации атаки (наносим урон, см. AttackSystem/CreatureView.PlayAttack);
    ///   CastEvent   — момент применения способности в анимации каста (резолв эффектов/запуск снаряда,
    ///                 см. RunResolveAbilityQueueSystem/CreatureView.PlayAbilityCast);
    ///   FinishEvent — ОБЩИЙ конец анимации (атака / каст способности / смерть — какая сейчас играет) —
    ///                 снимает блокировку хода/таймера. ОДНА функция на конец ЛЮБОГО клипа: CreatureView
    ///                 сам подставляет нужный обработчик в Finish перед каждым Play* (только одна анимация
    ///                 с блокировкой играет на существе одновременно — конфликтов не бывает).
    /// </summary>
    public sealed class CreatureAnimationRelay : MonoBehaviour
    {
        public System.Action AttackHit;
        public System.Action CastPoint;
        public System.Action Finish;

        public void AttackEvent() => AttackHit?.Invoke();
        public void CastEvent()   => CastPoint?.Invoke();
        public void FinishEvent() => Finish?.Invoke();

        // ── Алиасы под старые проектные / стоковые имена (см. историю правок) — те же колбэки. ──
        // В FBX-клипах ассет-паков ивенты зашиты при импорте и не переименовываются (read-only), напр.
        // 'infantry_04_attack_A' у TT_Peasant шлёт 'AttackEvent' (уже покрыто выше) — HitEvent/старые
        // OnAttackHit/OnAttackFinished/OnDeathFinished/DeathEvent оставлены синонимами, чтобы уже
        // размеченные клипы не сломались при переходе на новую конвенцию.
        public void OnAttackHit()      => AttackHit?.Invoke();
        public void HitEvent()         => AttackHit?.Invoke();
        public void OnAttackFinished() => Finish?.Invoke();
        public void OnDeathFinished()  => Finish?.Invoke();
        public void DeathEvent()       => Finish?.Invoke();

        // Частые «шумовые» ивенты стоковых клипов (шаги/выстрел и т.п.) — глушим, чтобы не сыпались ошибки.
        public void FootL() { }
        public void FootR() { }
        public void FootStep() { }
        public void Land() { }
        public void ShootEvent() { }
    }
}
