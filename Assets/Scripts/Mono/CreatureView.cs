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
        [SerializeField] float abilityCastMaxSeconds = 1.2f; // страховка: PlayAbilityCast форсит CastEvent/FinishEvent, если клип их не вызвал

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

        // Выставляется в начале PlayDeath() — гейтит PlayHit() в SetStats() от гонки Hit/Death на одном
        // Animator (см. комментарий в SetStats).
        bool _isDying;

        Coroutine _attackFallback;
        Coroutine _deathFallback;
        Coroutine _abilityCastFallback;

        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int DeathHash  = Animator.StringToHash("Death");
        static readonly int RunHash    = Animator.StringToHash("IsRunning");
        static readonly int HitHash    = Animator.StringToHash("Hit");
        static readonly int SummonHash = Animator.StringToHash("Summon");
        static readonly int CastHash   = Animator.StringToHash("Cast");

        [SerializeField] float summonSeconds = 0.6f;   // длительность «окна призыва»: визуал + гейт до OnCast

        [Header("VFX attach points (для SummonVfxSpec и будущих эффектов)")]
        [Tooltip("Именованные точки атача VFX на модели (Body/Head/HandLeft/…). Не проставленная точка " +
                 "фолбэчится на корень вью. Root добавлять не нужно — это сам transform.")]
        [SerializeField] List<VfxAttachPoint> _vfxAttachPoints = new();

        [Serializable]
        public struct VfxAttachPoint
        {
            public Game.Core.Shared.Interface.CreatureAttachPoint Point;
            public Transform Transform;
        }

        /// <summary>Transform точки атача (Body/Head/…); фолбэк — корень вью, если точка не проставлена.</summary>
        public Transform GetAttachPoint(Game.Core.Shared.Interface.CreatureAttachPoint point)
        {
            if (point != Game.Core.Shared.Interface.CreatureAttachPoint.Root && _vfxAttachPoints != null)
                foreach (var p in _vfxAttachPoints)
                    if (p.Point == point && p.Transform != null) return p.Transform;
            return transform;
        }

        /// <summary>Инстанс VFX (parent != null — следует за точкой атача). autoDestroy — прибить по времени
        /// жизни частиц (как VfxPresenter.AutoDestroy); false — вызывающий гасит сам (follow-аура призыва).</summary>
        GameObject SpawnVfxInstance(GameObject prefab, Vector3 pos, Transform parent, bool autoDestroy = true)
        {
            if (prefab == null) return null;
            var go = parent != null
                ? Instantiate(prefab, pos, Quaternion.identity, parent)
                : Instantiate(prefab, pos, Quaternion.identity);
            if (autoDestroy)
            {
                float life = 2.5f;
                var ps = go.GetComponentInChildren<ParticleSystem>();
                if (ps != null)
                {
                    var main = ps.main;
                    life = main.duration + main.startLifetime.constantMax;
                }
                Destroy(go, life);
            }
            return go;
        }

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
        Action _currentFinish;      // ОБЩИЙ обработчик конца анимации — атака/каст способности/смерть (что сейчас играет)
        Action _currentCastPoint;   // обработчик момента применения способности (PlayAbilityCast)

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
                relay.AttackHit = OnAttackHit;
                relay.CastPoint = OnCastPointEvent;
                relay.Finish    = OnFinishEvent;
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
                // Гонка Hit/Death на ОДНОМ Animator: в кадре смертельного удара DieSystem уже мог вызвать
                // PlayDeath() (SetTrigger("Death")) РАНЬШЕ, чем CreatureStatsViewSystem дойдёт сюда и увидит
                // упавшее HP — SetTrigger("Hit") поверх Death в том же кадре мешал переходу в Death (подтверждено
                // логами: оба триггера ставились в один и тот же Time.frameCount). _isDying защищает от этого.
                if (tookDamage && !_isDying) { FlashDamage(); PlayHit(); }   // реакция цели на удар (#3)
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

        // Hold-детект существа (OnMouseDown/OnMouseUp) убран отсюда — существа НЕкликабельны в этом проекте,
        // клики ловит только CellView (у клетки свой коллайдер, она знает Row/Col/OwnerId). Тот же паттерн
        // (короткий тап → CellSelectedEvent, удержание → CreatureHoldUIEvent) перенесён в CellView.cs.

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
            _onAttackHit   = onHit;
            _currentFinish = onFinished;
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
                _onAttackHit = null; _currentFinish = null;
            }
        }

        IEnumerator AttackFallback()
        {
            yield return new WaitForSeconds(attackMaxSeconds);
            OnAttackHit();       // вызовет _onAttackHit, только если ивент ещё не сработал (null-guard внутри)
            OnFinishEvent();
            _attackFallback = null;
        }

        /// <summary>Анимация появления на столе (#2). Триггер аниматора "Summon" или «поп»-масштаб без клипа.
        /// Держит IsSummoning — окно, после которого RunPendingOnCastSystem даёт OnCast. Опциональная спека
        /// vfx (SummonVfxSpec, авторится на CardCreatureModel) добавляет три фазы: Pre-эффект на клетке
        /// (PreLeadSeconds &gt; 0 — существо скрыто, пока «открывается портал»; окно призыва удлиняется),
        /// Resolve-эффект в точке атача существа, Finish-аккорд в конце (клетка/существо).</summary>
        public void PlaySummon(Game.Core.Shared.Interface.SummonVfxSpec vfx = null)
        {
            IsSummoning = true;

            if (_summonRoutine != null) StopCoroutine(_summonRoutine);
            _summonRoutine = StartCoroutine(SummonFlow(vfx));
        }

        IEnumerator SummonFlow(Game.Core.Shared.Interface.SummonVfxSpec vfx)
        {
            Vector3 cellPos = transform.position;   // существо стоит на клетке — её позиция для Pre/Finish(Cell)

            // ── Pre: эффект на клетке; существо опционально прячется, пока «открывается портал» ──
            if (vfx?.PrePrefab != null) SpawnVfxInstance(vfx.PrePrefab, cellPos, null);
            float pre = vfx != null ? Mathf.Max(0f, vfx.PreLeadSeconds) : 0f;
            Vector3 prefabScale = transform.localScale;
            if (pre > 0f)
            {
                transform.DOKill();
                transform.localScale = prefabScale * 0.0001f;   // скрыто (SetActive нельзя — убил бы корутину)
                yield return new WaitForSeconds(pre);
                transform.localScale = prefabScale;
            }

            // ── Summon-анимация (как раньше) ──
            if (animator != null && HasParam(SummonHash))
            {
                animator.SetTrigger(SummonHash);
            }
            else
            {
                // Нет клипа призыва — «поп»-появление из уменьшенного масштаба к масштабу префаба.
                transform.DOKill();
                transform.localScale = prefabScale * 0.4f;
                transform.DOScale(prefabScale, summonSeconds).SetEase(Ease.OutBack);
            }

            // ── Resolve: эффект на существе (точка атача), с задержкой от старта анимации ──
            GameObject resolveGo = null;
            float elapsed = 0f;
            if (vfx?.ResolvePrefab != null)
            {
                float delay = Mathf.Clamp(vfx.ResolveDelaySeconds, 0f, summonSeconds);
                if (delay > 0f) yield return new WaitForSeconds(delay);
                elapsed = delay;
                var at = GetAttachPoint(vfx.ResolveAttach);
                resolveGo = SpawnVfxInstance(vfx.ResolvePrefab, at.position, vfx.ResolveFollow ? at : null,
                                             autoDestroy: !vfx.ResolveFollow);
            }

            if (summonSeconds - elapsed > 0f) yield return new WaitForSeconds(summonSeconds - elapsed);

            // ── Finish: заключительный аккорд (клетка или существо) ──
            if (vfx?.FinishPrefab != null)
            {
                Vector3 at = vfx.FinishAnchor == Game.Core.Shared.Interface.SummonVfxAnchor.Cell
                    ? cellPos
                    : GetAttachPoint(vfx.FinishAttach).position;
                SpawnVfxInstance(vfx.FinishPrefab, at, null);
            }

            // Follow-аура жила всё окно призыва — гасим с небольшим запасом (частицы догаснут).
            if (resolveGo != null) Destroy(resolveGo, 0.75f);

            IsSummoning = false;
            _summonRoutine = null;
        }

        /// <summary>Драг-розыгрыш (#пайплайн-под-пальцем): существо появляется в мировой точке fromWorld
        /// (где было превью под пальцем) и плавно «въезжает» в клетку toWorld, играя анимацию Invoke.
        /// Без поп-масштаба (существо уже «целое» — оно материализовалось под пальцем). Держит IsSummoning
        /// (гейт OnCast) на summonSeconds. Спека vfx проигрывается БЕЗ Pre-фазы (существо уже видно
        /// под пальцем — портал не имеет смысла), Resolve/Finish — как у PlaySummon.</summary>
        public void PlaySlideInvoke(Vector3 fromWorld, Vector3 toWorld, Game.Core.Shared.Interface.SummonVfxSpec vfx = null)
        {
            IsSummoning = true;

            transform.DOKill();
            transform.position = fromWorld;
            transform.DOMove(toWorld, 0.25f).SetEase(Ease.OutCubic);

            if (animator != null && HasParam(SummonHash))
                animator.SetTrigger(SummonHash);

            if (_summonRoutine != null) StopCoroutine(_summonRoutine);
            _summonRoutine = StartCoroutine(SlideInvokeGate(toWorld, vfx));
        }

        IEnumerator SlideInvokeGate(Vector3 cellPos, Game.Core.Shared.Interface.SummonVfxSpec vfx)
        {
            GameObject resolveGo = null;
            float elapsed = 0f;
            if (vfx?.ResolvePrefab != null)
            {
                float delay = Mathf.Clamp(vfx.ResolveDelaySeconds, 0f, summonSeconds);
                if (delay > 0f) yield return new WaitForSeconds(delay);
                elapsed = delay;
                var at = GetAttachPoint(vfx.ResolveAttach);
                resolveGo = SpawnVfxInstance(vfx.ResolvePrefab, at.position, vfx.ResolveFollow ? at : null,
                                             autoDestroy: !vfx.ResolveFollow);
            }

            if (summonSeconds - elapsed > 0f) yield return new WaitForSeconds(summonSeconds - elapsed);

            if (vfx?.FinishPrefab != null)
            {
                Vector3 at = vfx.FinishAnchor == Game.Core.Shared.Interface.SummonVfxAnchor.Cell
                    ? cellPos
                    : GetAttachPoint(vfx.FinishAttach).position;
                SpawnVfxInstance(vfx.FinishPrefab, at, null);
            }
            if (resolveGo != null) Destroy(resolveGo, 0.75f);

            IsSummoning = false;
            _summonRoutine = null;
        }

        /// <summary>Есть ли у существа анимация каста способности (параметр "Cast" в аниматоре) — использует
        /// RunResolveAbilityQueueSystem (при VfxSpec.PlayCasterAnimation=true), чтобы решить, ждать ли
        /// анимацию перед резолвом или применить эффекты мгновенно (как раньше). Неактивный объект (существо
        /// уже умерло/скрыто ДО резолва — напр. deathrattle) не отдаёт анимацию: инстант-резолв безопаснее
        /// затяжного анти-софтлок таймаута на невидимом объекте.</summary>
        public bool HasCastAnimation => animator != null && HasParam(CastHash) && gameObject.activeInHierarchy;

        /// <summary>
        /// Способность РЕЗОЛВИТСЯ на ЭТОМ существе (любой триггер — не только battlecry: OnTurnEnd/OnAttack/
        /// OnDie/…). Играет анимацию "Cast": onCastPoint — момент применения эффекта/запуска снаряда
        /// (Animation Event "CastEvent"), onFinished — конец анимации, снимает блокировку хода/таймера
        /// (Animation Event "FinishEvent"). Вызывай только когда HasCastAnimation=true — иначе (нет клипа
        /// каста) поведение то же самое: сразу оба колбэка (мгновенный резолв, полная обратная совместимость).
        /// </summary>
        public void PlayAbilityCast(Action onCastPoint, Action onFinished)
        {
            if (HasCastAnimation)
            {
                _currentCastPoint = onCastPoint;
                _currentFinish    = onFinished;
                animator.SetTrigger(CastHash);

                if (_abilityCastFallback != null) StopCoroutine(_abilityCastFallback);
                _abilityCastFallback = StartCoroutine(AbilityCastFallback());
            }
            else
            {
                onCastPoint?.Invoke();
                onFinished?.Invoke();
            }
        }

        IEnumerator AbilityCastFallback()
        {
            yield return new WaitForSeconds(abilityCastMaxSeconds);
            OnCastPointEvent();   // null-guard внутри — не задвоит, если CastEvent уже пришёл
            OnFinishEvent();
            _abilityCastFallback = null;
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
            _isDying = true;   // ДО всего остального — гейтит PlayHit() из SetStats() в этом же кадре (см. SetStats)

            if (animator != null && HasParam(DeathHash))
            {
                _currentFinish = HideAfterDeath;
                animator.SetTrigger(DeathHash);
                // Гасим вьюшку, когда анимация смерти закончится (Animation Event "FinishEvent" через реле).
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
            OnFinishEvent();   // null-guard внутри — вызовет HideAfterDeath, только если ивент клипа не пришёл
        }

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

        /// <summary>Вызывается через Animation Event "CastEvent" — момент применения способности
        /// (PlayAbilityCast).</summary>
        public void OnCastPointEvent()
        {
            var cb = _currentCastPoint;
            _currentCastPoint = null;
            cb?.Invoke();
        }

        /// <summary>Вызывается через Animation Event "FinishEvent" — конец ТЕКУЩЕЙ анимации (атака/каст
        /// способности/смерть, какая сейчас играет — только одна одновременно).</summary>
        public void OnFinishEvent()
        {
            // ВРЕМЕННО: если этот лог появляется РАНЬШЕ лога "DeathFallback ТАЙМАУТ" — FinishEvent пришёл из
            // клипа нормально (проблема не в анимации/событии). Если появляется ТОЛЬКО одновременно с таймаутом
            // (~2 сек) — Animation Event из клипа не приходит вообще.
            UnityEngine.Debug.Log($"[PlayDeath] {name} OnFinishEvent вызван (hasCallback={_currentFinish != null}), t={Time.unscaledTime:0.00}");
            var cb = _currentFinish;
            _currentFinish = null;
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