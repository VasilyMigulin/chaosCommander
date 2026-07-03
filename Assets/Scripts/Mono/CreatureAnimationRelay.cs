using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Реле Animation Events. Animator висит на ДОЧЕРНЕМ объекте, и Unity вызывает анимационные ивенты
    /// именно на объекте с аниматором — родительский CreatureView их не получает. Поэтому это реле
    /// добавляется на объект аниматора (в CreatureView.Awake), имеет методы с теми же именами, что и в
    /// клипе ("OnAttackHit"/"OnAttackFinished"), и пробрасывает вызовы обратно в CreatureView через
    /// делегаты. Имена методов ДОЛЖНЫ совпадать с function name в Animation Event клипа.
    /// </summary>
    public sealed class CreatureAnimationRelay : MonoBehaviour
    {
        public System.Action AttackHit;
        public System.Action AttackFinished;
        public System.Action DeathFinished;

        // Имена — как в Animation Event клипов (атака/смерть).
        public void OnAttackHit()      => AttackHit?.Invoke();
        public void OnAttackFinished() => AttackFinished?.Invoke();
        public void OnDeathFinished()  => DeathFinished?.Invoke();

        // ── Алиасы под СТОКОВЫЕ имена ивентов покупных анимаций ──
        // В FBX-клипах ассет-паков ивенты зашиты при импорте и не переименовываются (read-only), напр.
        // 'infantry_04_attack_A' у TT_Peasant шлёт 'AttackEvent' → «has no receiver». Принимаем стоковые
        // имена как синонимы. NB: в одном клипе не должно быть И проектного, И стокового имени (двойной вызов).
        public void AttackEvent()      => AttackHit?.Invoke();
        public void HitEvent()         => AttackHit?.Invoke();
        public void DeathEvent()       => DeathFinished?.Invoke();

        // Частые «шумовые» ивенты стоковых клипов (шаги/каст и т.п.) — глушим, чтобы не сыпались ошибки.
        public void FootL() { }
        public void FootR() { }
        public void FootStep() { }
        public void Land() { }
        public void CastEvent() { }
        public void ShootEvent() { }
    }
}
