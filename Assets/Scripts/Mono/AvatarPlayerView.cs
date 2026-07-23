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
        }

        void Awake()
        {
            // ВРЕМЕННО: разовая проверка wiring (не спамит — один раз при создании инстанса).
            _auraBar = _auraBarBehaviour as IAuraStatusReceiver;
            Debug.Log($"[Aura] {name} Awake: canvasRootAssigned={_canvasRoot != null} auraBarBehaviourAssigned={_auraBarBehaviour != null} behaviourType={(_auraBarBehaviour != null ? _auraBarBehaviour.GetType().Name : "null")} castOk={_auraBar != null}");
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
    }
}
