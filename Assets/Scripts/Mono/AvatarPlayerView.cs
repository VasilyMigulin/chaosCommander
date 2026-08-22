using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Game.Core.Shared;
using TMPro;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Аватар игрока на доске. Визуальный скин назначается позже (донатная система).
    /// Лейблы HP/золота/маны опциональны (заглушка без префаба остаётся валидной).
    /// У ЛОКАЛЬНОГО игрока весь канвас над его собственным аватаром скрывается целиком — ВСЕ его данные
    /// (HP/золото/мана/рука/колода/ауры) показывает BattlePanel. Над аватаром остаётся видимым только
    /// то, что относится к ОППОНЕНТУ (см. SetStats — единственная точка, где решается видимость).
    /// </summary>
    public class AvatarPlayerView : MonoBehaviour
    {
        [Header("Canvas root — ЕДИНЫЙ объект на всё (статы + ауры)")]
        [Tooltip("Сам GameObject с компонентом World Space Canvas над аватаром (статы + аура-бар внутри него). "
               + "ОДИН объект отвечает за ВСЁ сразу: (1) целиком скрывается для локального игрока — его данные "
               + "показывает BattlePanel; (2) его Transform крутится к камере (билборд); (3) с него же берётся "
               + "Canvas для Event Camera (нужна для hold на AuraStatusSlotView). Раньше это были 3 разных поля "
               + "(_statsRoot/_billboardRoot/_worldCanvas), которые легко рассинхронизировались при правках "
               + "префаба — сведено к одному, чтобы не путать, что выключается, а что крутится.")]
        [SerializeField] GameObject _canvasRoot;
        Canvas _canvas;   // кэш — снимается с _canvasRoot при первом обращении

        [Header("Cosmetic avatar visual")]
        [Tooltip("Контейнер (якорь) визуала аватара. Сюда спавнится префаб НАДЕТОГО аватара, заменяя то, что " +
                 "стоит внутри по умолчанию. Положи в _viewRoot тот дочерний GameObject-модель («View»), " +
                 "который надо подменять. Принимаем GameObject, а не AvatarInstanceData — Mono не тянет Configs/Instance.")]
        [SerializeField] Transform _viewRoot;
        GameObject _visual;

        [Header("Stats (optional)")]
        [SerializeField] TextMeshProUGUI _hpText;
        [SerializeField] TextMeshProUGUI _goldText;
        [SerializeField] TextMeshProUGUI _manaText;

        [Header("Hand/Deck count")]
        [SerializeField] TextMeshProUGUI _handCountText;
        [SerializeField] TextMeshProUGUI _deckCountText;

        [Header("Active auras (чары на поле)")]
        [Tooltip("Компонент, реализующий IAuraStatusReceiver (AuraStatusBarView из AwesomeUI.Feature.Battle) — "
               + "интерфейс, не конкретный тип, чтобы Game.Core.Mono не тянул ссылку на UI-сборку. Должен лежать "
               + "ВНУТРИ _canvasRoot — прячется вместе со всем канвасом, отдельно видимость не трогаем.")]
        [SerializeField] MonoBehaviour _auraBarBehaviour;
        IAuraStatusReceiver _auraBar;

        public int OwnerId { get; private set; }

        int _hp = int.MinValue, _maxHp, _gold = int.MinValue, _mana = int.MinValue;
        int _handCount = int.MinValue, _deckCount = int.MinValue;

        [Header("Combat animation (опционально — Attack/Hit/Death на аниматоре текущей косметики)")]
        [Tooltip("Страховка: макс. длительность анимации атаки/смерти, если клип не вызвал Animation Event " +
                 "'FinishEvent' (см. CreatureAnimationRelay). Та же конвенция, что у CreatureView.")]
        [SerializeField] float attackMaxSeconds = 2f;
        [SerializeField] float deathMaxSeconds  = 2f;

        Animator _combatAnimator;   // ищется на текущем _visual (или дефолт-ребёнке _viewRoot) — см. RefreshCombatAnimator
        readonly HashSet<int> _combatAnimParams = new HashSet<int>();

        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int HitHash    = Animator.StringToHash("Hit");
        static readonly int DeathHash  = Animator.StringToHash("Death");

        Action _onAttackHit;
        Action _currentFinish;   // общий обработчик конца анимации — атака или смерть (что сейчас играет)
        Coroutine _attackFallback;
        Coroutine _deathFallback;
        Tween _hitPunchTween;

        // Отдельный кэш ТОЛЬКО для детекта «получен урон» (PlayHit) — независим от _hp, который кэширует
        // именно ТЕКСТ ниже и у локального игрока вообще не обновляется (см. SetStats). Смешать их сломало
        // бы отрисовку HP-текста оппонента (hp != _hp перестал бы срабатывать при обычном изменении HP).
        int _lastHpForHit = int.MinValue;

        public void Init(int ownerId)
        {
            OwnerId = ownerId;
            gameObject.name = $"AvatarPlayer_P{ownerId}";
        }

        /// <summary>
        /// Подменить визуал аватара на префаб надетого. null → оставить дефолт (что стоит в _viewRoot).
        /// Принимает GameObject (не AvatarInstanceData): Mono не тянет Configs/Instance — резолв avatarId →
        /// AvatarConfig → Prefab делает вызывающий слой (ECS/презентер) и передаёт сюда готовый префаб.
        /// </summary>
        public void SetAvatarVisual(GameObject prefab)
        {
            if (_viewRoot == null || prefab == null) return;
            // Заменяем: сносим прежний визуал (дефолтный child или ранее заспавненный), ставим новый.
            for (int i = _viewRoot.childCount - 1; i >= 0; i--)
                Destroy(_viewRoot.GetChild(i).gameObject);
            _visual = Instantiate(prefab, _viewRoot);
            _visual.transform.localPosition = Vector3.zero;
            _visual.transform.localRotation = Quaternion.identity;
            RefreshCombatAnimator();   // косметика сменилась в рантайме — перецепить Animator/relay под неё
        }

        void Awake()
        {
            // ВРЕМЕННО: разовая проверка wiring (не спамит — один раз при создании инстанса).
            _auraBar = _auraBarBehaviour as IAuraStatusReceiver;
            Debug.Log($"[Aura] {name} Awake: canvasRootAssigned={_canvasRoot != null} auraBarBehaviourAssigned={_auraBarBehaviour != null} behaviourType={(_auraBarBehaviour != null ? _auraBarBehaviour.GetType().Name : "null")} castOk={_auraBar != null}");

            RefreshCombatAnimator();   // дефолт-визуал (на случай если ApplyEquippedAvatar/SetAvatarVisual не вызовется)
        }

        void LateUpdate()
        {
            if (_canvasRoot == null) return;

            // Берём ЛОКАЛЬНУЮ камеру у селектора (не кэшируем Camera.main — на 2-м клиенте она залипает на
            // Side1, активной по умолчанию до выбора). Перечитываем каждый кадр (дёшево) → и биллборд, и Event
            // Camera подхватят правильную камеру сразу, как только игрок определится со стороной.
            var cam = BattleCameraSelector.ActiveCamera != null ? BattleCameraSelector.ActiveCamera : Camera.main;
            if (cam == null) return;

            // Билборд: ВЕСЬ канвас смотрит «в ту же сторону», что и камера (текст/ауры лицом к игроку, без зеркала).
            _canvasRoot.transform.forward = cam.transform.forward;

            // Event Camera для World Space Canvas — без неё GraphicRaycaster неверно проецирует указатель в
            // мировые координаты (hold на AuraStatusSlotView не сработает/сработает не туда).
            if (_canvas == null) _canvas = _canvasRoot.GetComponent<Canvas>();
            if (_canvas != null && _canvas.worldCamera != cam) _canvas.worldCamera = cam;
        }

        /// <summary>Пуш статов из ECS (PlayerStatsViewSystem). isLocal=true → ВЕСЬ канвас над аватаром (статы +
        /// ауры) скрывается целиком (данные локального показывает BattlePanel). Над аватаром остаётся только
        /// оппонент. Обновляет текст только при изменении.</summary>
        public void SetStats(int hp, int maxHp, int gold, int mana, int handCount, int deckCount, bool isLocal)
        {
            if (_canvasRoot != null && _canvasRoot.activeSelf == isLocal)
                _canvasRoot.SetActive(!isLocal);   // у локального скрываем ВЕСЬ канвас целиком (статы + ауры)

            // Реакция 3D-модели на удар — НЕЗАВИСИМО от isLocal: модель аватара видна всегда, даже когда
            // текстовый канвас над ней скрыт (HP локального игрока показывает BattlePanel, не этот канвас).
            if (hp < _lastHpForHit && _lastHpForHit != int.MinValue) PlayHit();
            _lastHpForHit = hp;

            if (isLocal) return;

            if (handCount != _handCount)
            {
                _handCount = handCount;
                if (_handCountText != null) _handCountText.text = handCount.ToString();
            }
            if (deckCount != _deckCount)
            {
                _deckCount = deckCount;
                if (_deckCountText != null) _deckCountText.text = deckCount.ToString();
            }

            if (hp != _hp || maxHp != _maxHp)
            {
                _hp = hp; _maxHp = maxHp;
                if (_hpText != null) _hpText.text = $"{hp}/{maxHp}";
            }
            if (gold != _gold)
            {
                _gold = gold;
                if (_goldText != null) _goldText.text = gold.ToString();
            }
            if (mana != _mana)
            {
                _mana = mana;
                if (_manaText != null) _manaText.text = mana.ToString();
            }
        }

        /// <summary>Пуш активных аур (чар на поле) из ECS (PlayerStatsViewSystem), каждый кадр — безвредно и
        /// для локального игрока: весь канвас (включая аура-бар) скрыт целиком через _canvasRoot (см. SetStats),
        /// обновление скрытых данных ничего не рисует. turnsRemaining[i] соответствует visuals[i]; &lt;0 —
        /// чара постоянная (без таймера).</summary>
        public void SetAuras(CardVisualData[] visuals, int[] turnsRemaining, int[] stackCounts)
        {
            _auraBar ??= _auraBarBehaviour as IAuraStatusReceiver;
            _auraBar?.SetAuras(visuals, turnsRemaining, stackCounts);
        }

        // ──── Боевая анимация (та же конвенция, что CreatureView: Animator-триггеры Attack/Hit/Death,
        // Animation Event'ы AttackEvent/FinishEvent через CreatureAnimationRelay). Косметика БЕЗ этих
        // триггеров/клипов (сегодняшний дефолт) — колбэки вызываются мгновенно, как у существа без вью. ────

        /// <summary>Перецепляет Animator/relay под ТЕКУЩИЙ визуал (_visual, либо дефолт-ребёнок _viewRoot,
        /// если косметика не надета) — звать при Awake и при каждой смене косметики (SetAvatarVisual),
        /// иначе кэш аниматора остался бы от предыдущего/пустого визуала.</summary>
        void RefreshCombatAnimator()
        {
            Transform root = _visual != null ? _visual.transform : _viewRoot;
            _combatAnimator = root != null ? root.GetComponentInChildren<Animator>() : null;

            _combatAnimParams.Clear();
            if (_combatAnimator == null) return;

            foreach (var p in _combatAnimator.parameters)
                _combatAnimParams.Add(p.nameHash);

            var relay = _combatAnimator.GetComponent<CreatureAnimationRelay>();
            if (relay == null) relay = _combatAnimator.gameObject.AddComponent<CreatureAnimationRelay>();
            relay.AttackHit = OnAttackHit;
            relay.Finish    = OnFinishEvent;
        }

        bool HasCombatParam(int hash) => _combatAnimParams.Contains(hash);

        /// <summary>Атака аватара (row0 своей стороны, см. AvatarAttackSystem). onHit — Animation Event
        /// "AttackEvent" (момент удара), onFinished — "FinishEvent" (конец анимации). Нет Animator/
        /// параметра "Attack" → оба колбэка вызываются мгновенно (полная обратная совместимость).</summary>
        public void PlayAttack(Action onHit, Action onFinished)
        {
            _onAttackHit   = onHit;
            _currentFinish = onFinished;

            if (_combatAnimator != null && HasCombatParam(AttackHash))
            {
                _combatAnimator.SetTrigger(AttackHash);

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
            OnAttackHit();       // null-guard внутри — не задвоит, если ивент клипа уже пришёл
            OnFinishEvent();
            _attackFallback = null;
        }

        /// <summary>Реакция на полученный урон (см. SetStats) — триггер "Hit" + лёгкий флинч масштабом,
        /// виден даже без клипа. Не гейтит ничего (как CreatureView.PlayHit — чистая реакция без колбэка).</summary>
        public void PlayHit()
        {
            if (_combatAnimator != null && HasCombatParam(HitHash))
                _combatAnimator.SetTrigger(HitHash);

            Transform punchTarget = _visual != null ? _visual.transform : _viewRoot;
            if (punchTarget == null) return;

            _hitPunchTween?.Kill(true);
            _hitPunchTween = punchTarget.DOPunchScale(Vector3.one * -0.1f, 0.18f, 6, 0.6f);
        }

        /// <summary>Визуальная реакция на поражение (GameOverCheckSystem, после HP≤0 проигравшего) — БЕЗ
        /// гейтинга каскада, в отличие от CreatureView.PlayDeath/DeathAnimPendingTag: матч уже завершается
        /// терминально (MatchState.IsOver), ничего дальше не обязано ждать конец этой анимации. Аватар не
        /// прячется после (в отличие от существа) — матч и так закрывается попапом результата.</summary>
        public void PlayDeath(Action onFinished = null)
        {
            if (_combatAnimator != null && HasCombatParam(DeathHash))
            {
                _currentFinish = onFinished;
                _combatAnimator.SetTrigger(DeathHash);

                if (_deathFallback != null) StopCoroutine(_deathFallback);
                _deathFallback = StartCoroutine(DeathFallback());
            }
            else
            {
                onFinished?.Invoke();
            }
        }

        IEnumerator DeathFallback()
        {
            yield return new WaitForSeconds(deathMaxSeconds);
            OnFinishEvent();
            _deathFallback = null;
        }

        // ──── Animation Events (вызываются через CreatureAnimationRelay на объекте _combatAnimator) ────

        public void OnAttackHit()
        {
            var cb = _onAttackHit;
            _onAttackHit = null;
            cb?.Invoke();
        }

        public void OnFinishEvent()
        {
            var cb = _currentFinish;
            _currentFinish = null;
            cb?.Invoke();
        }
    }
}
