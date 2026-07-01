using TMPro;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Аватар игрока на доске. Визуальный скин назначается позже (донатная система).
    /// Лейблы HP/золота/маны опциональны (заглушка без префаба остаётся валидной).
    /// Для врага показываем HP+ресурсы над аватаром; у локального игрока эти данные идут
    /// в ресурс-панель UI, поэтому над-аватарные лейблы можно скрыть (см. SetStats).
    /// </summary>
    public class AvatarPlayerView : MonoBehaviour
    {
        [Header("Stats (optional)")]
        [SerializeField] TextMeshProUGUI _hpText;
        [SerializeField] TextMeshProUGUI _goldText;
        [SerializeField] TextMeshProUGUI _manaText;
        [Tooltip("Корень лейблов статов — скрывается для локального игрока (его HP/ресурсы в панели).")]
        [SerializeField] GameObject _statsRoot;

        [Header("Billboard")]
        [Tooltip("Рут, который должен смотреть в камеру (обычно world-space canvas статов). Если не задан — берётся _statsRoot.")]
        [SerializeField] Transform _billboardRoot;

        public int OwnerId { get; private set; }

        int _hp = int.MinValue, _maxHp, _gold = int.MinValue, _mana = int.MinValue;

        public void Init(int ownerId)
        {
            OwnerId = ownerId;
            gameObject.name = $"AvatarPlayer_P{ownerId}";
        }

        void LateUpdate()
        {
            var root = _billboardRoot != null ? _billboardRoot
                     : (_statsRoot != null ? _statsRoot.transform : null);
            if (root == null) return;

            // Берём ЛОКАЛЬНУЮ камеру у селектора (не кэшируем Camera.main — на 2-м клиенте она залипает на
            // Side1, активной по умолчанию до выбора). Перечитываем каждый кадр (дёшево) → биллборд
            // подхватит правильную камеру сразу, как только игрок определится со стороной.
            var cam = BattleCameraSelector.ActiveCamera != null ? BattleCameraSelector.ActiveCamera : Camera.main;
            if (cam == null) return;

            // Билборд: рут смотрит «в ту же сторону», что и камера (текст всегда лицом к игроку, без зеркала).
            root.forward = cam.transform.forward;
        }

        /// <summary>Пуш статов из ECS (PlayerStatsViewSystem). isLocal=true → лейблы над аватаром
        /// скрываются (данные показывает ресурс-панель). Обновляет текст только при изменении.</summary>
        public void SetStats(int hp, int maxHp, int gold, int mana, bool isLocal)
        {
            if (_statsRoot != null && _statsRoot.activeSelf == isLocal)
                _statsRoot.SetActive(!isLocal);   // у локального скрываем над-аватарные лейблы

            if (isLocal) return;

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
    }
}
