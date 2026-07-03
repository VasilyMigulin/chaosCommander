using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using Game.Core.Events;

namespace Game.Core.Mono
{
    public class CreatureView : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] float moveSpeed = 4f; // клеток/сек
        [SerializeField] float attackMaxSeconds = 2f; // страховка: макс. длительность атаки, если клип не вызвал ивенты
        [SerializeField] float deathMaxSeconds  = 2f; // страховка: через сколько гасить вьюшку, если клип смерти не вызвал ивент

        [Header("Stats UI (опционально, привязать в префабе)")]
        [SerializeField] TMP_Text _healthText;   // текущее HP
        [SerializeField] TMP_Text _attackText;   // атака
        [SerializeField] TMP_Text _speedText;    // оставшиеся действия (Speed.Remaining)

        [Tooltip("Контейнер статов для биллборда (поворот к камере). Если пусто — берётся Canvas с _healthText.")]
        [SerializeField] Transform _statsRoot;
        [SerializeField] bool _billboardStats = true;   // всегда поворачивать статы к камере игрока

        // Кэш последних значений: текст обновляем только при изменении + подсветка при потере HP.
        int _lastHealth = int.MinValue, _lastAttack = int.MinValue, _lastSpeed = int.MinValue;
        Color _healthBaseColor = Color.white;
        Coroutine _damageFlash;
        Camera _statsCam;   // кэш активной камеры для биллборда

        Coroutine _attackFallback;
        Coroutine _deathFallback;

        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int DeathHash  = Animator.StringToHash("Death");
        static readonly int RunHash    = Animator.StringToHash("IsRunning");
        static readonly int HitHash    = Animator.StringToHash("Hit");
        static readonly int SummonHash = Animator.StringToHash("Summon");
        static readonly int CastHash   = Animator.StringToHash("Cast");

        [SerializeField] float summonSeconds = 0.6f;   // длительность «окна призыва»: визуал + гейт до OnCast

        [Header("Team tint (свои/чужие)")]
        [Tooltip("Оттенок СВОИХ существ (белый = натуральный цвет модели).")]
        [SerializeField] Color _ownTint = Color.white;
        [Tooltip("Оттенок ВРАЖЕСКИХ существ (умножается на цвет материала) — чтобы одинаковые существа игроков не путались.")]
        [SerializeField] Color _enemyTint = new Color(1f, 0.6f, 0.6f, 1f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");   // URP
        static readonly int TintColorId = Shader.PropertyToID("_Color");        // Built-in/legacy
        int _teamTintState = -1;   // -1 не применялся, 0 свой, 1 враг — красим только на смене

        /// <summary>Идёт ли сейчас анимация призыва (гейт для RunPendingOnCastSystem — OnCast ждёт её конца).</summary>
        public bool IsSummoning { get; private set; }
        Coroutine _summonRoutine;

        Action _onAttackHit;
        Action _onAttackFinished;

        private int _row;
        private int _col;
        private int _ownerId;

        /// <summary>
        /// Командная подкраска (свои/чужие): чтобы одинаковые существа разных игроков различались.
        /// Идемпотентно (кэш состояния — TeamTintSystem может звать каждый кадр). Красит через
        /// MaterialPropertyBlock (без инстансинга материалов — совместимо с подменой в MaterializeEffect,
        /// цвет считается от ЧИСТОГО sharedMaterial → переключение свой↔чужой при краже контроля корректно).
        /// Текст статов (TMP-рендереры) не трогаем.
        /// </summary>
        public void SetTeamTint(bool isEnemy)
        {
            int state = isEnemy ? 1 : 0;
            if (_teamTintState == state) return;
            _teamTintState = state;

            Color tint = isEnemy ? _enemyTint : _ownTint;
            var mpb = new MaterialPropertyBlock();

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<TMP_Text>() != null) continue;   // статы/лейблы не красим
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    int id = m.HasProperty(BaseColorId) ? BaseColorId
                           : (m.HasProperty(TintColorId) ? TintColorId : 0);
                    if (id == 0) continue;

                    mpb.Clear();
                    r.GetPropertyBlock(mpb, i);
                    mpb.SetColor(id, m.GetColor(id) * tint);
                    r.SetPropertyBlock(mpb, i);
                }
            }
        }

        /// <summary>Устанавливает координаты клетки под существом для обработки кликов.</summary>
        public void SetCell(int row, int col, int ownerId)
        {
            _row     = row;
            _col     = col;
            _ownerId = ownerId;
        }

        /// <summary>
        /// Базовый разворот в покое: владелец 1 смотрит «вперёд» (+Z, на оппонента), владелец 2 —
        /// в противоположную сторону (180°). Применяется при спавне. Если базовая ориентация префаба
        /// иная — поменять угол здесь.
        /// </summary>
        public void SetOwnerFacing(int ownerId)
        {
            transform.rotation = Quaternion.Euler(0f, ownerId == 2 ? 180f : 0f, 0f);
        }

        /// <summary>Поворачивает существо лицом к мировой точке (по горизонтали).</summary>
        public void FaceTowards(Vector3 worldPos, bool instant = false)
        {
            Vector3 dir = worldPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            if (instant)
                transform.rotation = target;
            else
                transform.DORotateQuaternion(target, 0.15f);
        }

        readonly HashSet<int> _animParams = new HashSet<int>();

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                foreach (var p in animator.parameters)
                    _animParams.Add(p.nameHash);

                // Реле на объект с аниматором: Animation Events вызываются на нём, а не на CreatureView.
                var relay = animator.GetComponent<CreatureAnimationRelay>();
                if (relay == null) relay = animator.gameObject.AddComponent<CreatureAnimationRelay>();
                relay.AttackHit      = OnAttackHit;
                relay.AttackFinished = OnAttackFinished;
                relay.DeathFinished  = OnDeathFinished;
            }

            if (_healthText != null) _healthBaseColor = _healthText.color;

            // Если контейнер статов не задан — берём Canvas, на котором лежит HP-текст.
            if (_statsRoot == null && _healthText != null)
            {
                var canvas = _healthText.GetComponentInParent<Canvas>();
                if (canvas != null) _statsRoot = canvas.transform;
            }
        }

        // Биллборд: статы всегда повёрнуты к активной камере клиента. У каждого игрока своя
        // камера (вторая выключена BattleCameraSelector), поэтому берём активную (Camera.main →
        // фолбэк на любую включённую) и перекэшируем, если она сменилась/выключилась.
        void LateUpdate()
        {
            if (!_billboardStats || _statsRoot == null) return;

            if (_statsCam == null || !_statsCam.isActiveAndEnabled)
            {
                // Приоритет — локальная камера от селектора (авторитет, без зависимости от тега MainCamera).
                _statsCam = BattleCameraSelector.ActiveCamera;
                if (_statsCam == null) _statsCam = Camera.main;
                if (_statsCam == null)
                {
                    var cams = Camera.allCameras;   // возвращает только включённые
                    if (cams.Length > 0) _statsCam = cams[0];
                }
                if (_statsCam == null) return;
            }

            // Фронт канваса (его +Z = лицевая сторона текста) направляем туда же, куда смотрит
            // камера → текст читается без зеркала.
            _statsRoot.forward = _statsCam.transform.forward;
        }

        /// <summary>
        /// Обновляет тексты статов (HP/атака/скорость). Вызывает CreatureStatsViewSystem каждый кадр —
        /// текст меняется только при изменении значения, а при УМЕНЬШЕНИИ HP даёт подсветку «получен урон».
        /// </summary>
        public void SetStats(int health, int attack, int speed)
        {
            if (_healthText != null && health != _lastHealth)
            {
                bool tookDamage = health < _lastHealth && _lastHealth != int.MinValue;
                _healthText.text = health.ToString();
                if (tookDamage) { FlashDamage(); PlayHit(); }   // реакция цели на удар (#3)
                _lastHealth = health;
            }
            if (_attackText != null && attack != _lastAttack)
            {
                _attackText.text = attack.ToString();
                _lastAttack = attack;
            }
            if (_speedText != null && speed != _lastSpeed)
            {
                _speedText.text = speed.ToString();
                _lastSpeed = speed;
            }
        }

        // Кратковременная подсветка HP при получении урона: красный + «пунч» масштаба.
        void FlashDamage()
        {
            if (_healthText == null) return;
            _healthText.transform.DOKill();
            _healthText.transform.localScale = Vector3.one;
            _healthText.transform.DOPunchScale(Vector3.one * 0.4f, 0.3f, 8, 1f);

            if (_damageFlash != null) StopCoroutine(_damageFlash);
            _damageFlash = StartCoroutine(DamageFlashRoutine());
        }

        IEnumerator DamageFlashRoutine()
        {
            _healthText.color = Color.red;
            yield return new WaitForSeconds(0.25f);
            if (_healthText != null) _healthText.color = _healthBaseColor;
            _damageFlash = null;
        }

        bool HasParam(int hash) => _animParams.Contains(hash);

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

            FaceTowards(targetPos);   // смотрим в сторону, куда идём
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
            // Если есть аниматор с триггером атаки — играем (урон/финиш придут через Animation Events
            // → CreatureAnimationRelay). Иначе сразу применяем урон и завершаем.
            if (animator != null && HasParam(AttackHash))
            {
                animator.SetTrigger(AttackHash);

                // Страховка: если у клипа НЕТ Animation Event'ов (или они не настроены) — не зависаем
                // в AttackAnimPendingTag навсегда. По таймауту добиваем onHit/onFinished (там null-guard,
                // так что двойного вызова с реальными ивентами не будет).
                if (_attackFallback != null) StopCoroutine(_attackFallback);
                _attackFallback = StartCoroutine(AttackFallback());
            }
            else
            {
                onHit?.Invoke();
                onFinished?.Invoke();
            }
        }

        IEnumerator AttackFallback()
        {
            yield return new WaitForSeconds(attackMaxSeconds);
            OnAttackHit();       // вызовет _onAttackHit, только если ивент ещё не сработал (null-guard внутри)
            OnAttackFinished();
            _attackFallback = null;
        }

        /// <summary>Анимация появления на столе (#2). Триггер аниматора "Summon" или «поп»-масштаб без клипа.
        /// Держит IsSummoning на summonSeconds — это окно, после которого RunPendingOnCastSystem даёт OnCast.</summary>
        public void PlaySummon()
        {
            IsSummoning = true;

            if (animator != null && HasParam(SummonHash))
            {
                animator.SetTrigger(SummonHash);
            }
            else
            {
                // Нет клипа призыва — «поп»-появление из уменьшенного масштаба к масштабу префаба.
                transform.DOKill();
                Vector3 target = transform.localScale;   // масштаб префаба (не жёстко 1)
                transform.localScale = target * 0.4f;
                transform.DOScale(target, summonSeconds).SetEase(Ease.OutBack);
            }

            if (_summonRoutine != null) StopCoroutine(_summonRoutine);
            _summonRoutine = StartCoroutine(SummonGate());
        }

        IEnumerator SummonGate()
        {
            yield return new WaitForSeconds(summonSeconds);
            IsSummoning = false;
            _summonRoutine = null;
        }

        /// <summary>Драг-розыгрыш (#пайплайн-под-пальцем): существо появляется в мировой точке fromWorld
        /// (где было превью под пальцем) и плавно «въезжает» в клетку toWorld, играя анимацию Invoke.
        /// Без поп-масштаба (существо уже «целое» — оно материализовалось под пальцем). Держит IsSummoning
        /// (гейт OnCast) на summonSeconds.</summary>
        public void PlaySlideInvoke(Vector3 fromWorld, Vector3 toWorld)
        {
            IsSummoning = true;

            transform.DOKill();
            transform.position = fromWorld;
            transform.DOMove(toWorld, 0.25f).SetEase(Ease.OutCubic);

            if (animator != null && HasParam(SummonHash))
                animator.SetTrigger(SummonHash);

            if (_summonRoutine != null) StopCoroutine(_summonRoutine);
            _summonRoutine = StartCoroutine(SummonGate());
        }

        /// <summary>Анимация «при разыгрывании» (battlecry) для OnCast (#2). Триггер "Cast" или пульс масштабом.</summary>
        public void PlayCast()
        {
            if (animator != null && HasParam(CastHash))
                animator.SetTrigger(CastHash);
            else
                transform.DOPunchScale(Vector3.one * 0.12f, 0.25f, 6, 0.6f);
        }

        /// <summary>Реакция цели на удар (#3): триггер аниматора "Hit" + короткий «флинч» масштабом,
        /// который виден ДАЖЕ без клипа. Пунч масштаба не конфликтует с DOMove/DORotate (разные свойства),
        /// поэтому DOKill не нужен.</summary>
        public void PlayHit()
        {
            if (animator != null && HasParam(HitHash))
                animator.SetTrigger(HitHash);

            transform.DOPunchScale(Vector3.one * -0.12f, 0.18f, 6, 0.6f);   // лёгкое «сжатие» от удара
        }

        public void PlayDeath()
        {
            if (animator != null && HasParam(DeathHash))
            {
                animator.SetTrigger(DeathHash);
                // Гасим вьюшку, когда анимация смерти закончится (Animation Event OnDeathFinished через реле).
                // Страховка по таймауту — если ивента в клипе нет, всё равно выключим.
                if (_deathFallback != null) StopCoroutine(_deathFallback);
                _deathFallback = StartCoroutine(DeathFallback());
            }
            else
            {
                // Нет клипа смерти — НЕ «телепортируем» существо в никуда (баг #1: мгновенно исчезает),
                // а плавно ужимаем и гасим. Вьюшку не уничтожаем — просто выключаем (HideAfterDeath).
                transform.DOKill();
                transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.InBack)
                    .OnComplete(HideAfterDeath);
            }
        }

        IEnumerator DeathFallback()
        {
            yield return new WaitForSeconds(deathMaxSeconds);
            HideAfterDeath();
        }

        /// <summary>Animation Event конца анимации смерти (через реле).</summary>
        public void OnDeathFinished() => HideAfterDeath();

        void HideAfterDeath()
        {
            if (_deathFallback != null) { StopCoroutine(_deathFallback); _deathFallback = null; }
            gameObject.SetActive(false);
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
            if (animator != null && HasParam(RunHash))
                animator.SetBool(RunHash, value);
        }
    }
}