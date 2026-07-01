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
    }
}
