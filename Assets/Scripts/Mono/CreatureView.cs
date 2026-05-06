using UnityEngine;
using System;

namespace Game.Core.Mono
{
    public class CreatureView : MonoBehaviour
    {
        [SerializeField] Animator animator;

        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int DeathHash  = Animator.StringToHash("Death");
        static readonly int IdleHash   = Animator.StringToHash("Idle");

        Action _onAttackHit;
        Action _onAttackFinished;

        /// <summary>
        /// Запускает анимацию атаки.
        /// onHit — вызывается в момент удара (через Animation Event "OnAttackHit").
        /// onFinished — вызывается когда анимация полностью завершена.
        /// </summary>
        public void PlayAttack(Action onHit, Action onFinished)
        {
            _onAttackHit      = onHit;
            _onAttackFinished = onFinished;
            if (animator != null)
                animator.SetTrigger(AttackHash);
            else
            {
                // Если аниматора нет — сразу вызываем коллбэки
                onHit?.Invoke();
                onFinished?.Invoke();
            }
        }

        public void PlayDeath()
        {
            if (animator != null)
                animator.SetTrigger(DeathHash);
        }

        // ──── Animation Events ────────────────────────────────────────────────
        // Должны быть добавлены в Animation Clip на нужном кадре

        /// <summary>Вызывается через Animation Event в момент удара.</summary>
        public void OnAttackHit()
        {
            var cb = _onAttackHit;
            _onAttackHit = null;
            cb?.Invoke();
        }

        /// <summary>Вызывается через Animation Event в конце анимации атаки.</summary>
        public void OnAttackFinished()
        {
            var cb = _onAttackFinished;
            _onAttackFinished = null;
            cb?.Invoke();
        }
    }
}