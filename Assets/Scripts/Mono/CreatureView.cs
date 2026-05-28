using UnityEngine;
using System;
using DG.Tweening;
using Game.Core.Events;

namespace Game.Core.Mono
{
    public class CreatureView : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] float moveSpeed = 4f; // клеток/сек

        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int DeathHash  = Animator.StringToHash("Death");
        static readonly int RunHash    = Animator.StringToHash("IsRunning");

        Action _onAttackHit;
        Action _onAttackFinished;

        private int _row;
        private int _col;
        private int _ownerId;

        /// <summary>Устанавливает координаты клетки под существом для обработки кликов.</summary>
        public void SetCell(int row, int col, int ownerId)
        {
            _row     = row;
            _col     = col;
            _ownerId = ownerId;
        }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
        }

        private void OnMouseDown()
        {
            GameEventBus.Publish(new CellSelectedEvent { Row = _row, Col = _col, OwnerId = _ownerId });
        }

        /// <summary>
        /// Плавно перемещает существо к позиции <paramref name="targetPos"/>,
        /// включая анимацию бега, и вызывает <paramref name="onFinished"/> по завершению.
        /// </summary>
        public void PlayMove(Vector3 targetPos, Action onFinished)
        {
            float dist     = Vector3.Distance(transform.position, targetPos);
            float duration = dist > 0.001f ? dist / moveSpeed : 0.05f;

            SetRunning(true);

            transform.DOMove(targetPos, duration)
                .SetEase(Ease.Linear)
                .OnComplete(() =>
                {
                    SetRunning(false);
                    onFinished?.Invoke();
                });
        }

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

        // ──── Private ─────────────────────────────────────────────────────────

        void SetRunning(bool value)
        {
            if (animator != null)
                animator.SetBool(RunHash, value);
        }
    }
}