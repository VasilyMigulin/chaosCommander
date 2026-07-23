using Game.Core.Instance.Avatar;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Показывает косметический аватар: плоский Icon на Image И/ИЛИ анимированный Prefab под контейнером.
    /// Одна вьюха на все места (профиль/HUD/магазин) — что назначено в префабе, то и покажется:
    ///   • только _iconImage → мелкий/плоский аватар (магазин, HUD);
    ///   • только _prefabRoot → крупный анимированный (профиль);
    ///   • оба → и иконка, и фигурка.
    /// Данные — AvatarInstanceData (резолвится по itemId через AvatarConfig).
    /// </summary>
    public class AvatarView : MonoBehaviour
    {
        [Tooltip("Плоская иконка аватара (магазин/HUD/мелкий показ).")]
        [SerializeField] private Image _iconImage;
        [Tooltip("Контейнер для анимированного префаба (крупный показ). Необязательно.")]
        [SerializeField] private Transform _prefabRoot;

        GameObject _spawned;

        /// <summary>Показать аватар по данным. null → прячем.</summary>
        public void SetAvatar(AvatarInstanceData avatar)
        {
            if (_iconImage != null)
            {
                _iconImage.sprite = avatar != null ? avatar.Icon : null;
                _iconImage.enabled = avatar != null && avatar.Icon != null;
            }

            if (_prefabRoot != null)
            {
                if (_spawned != null) { Destroy(_spawned); _spawned = null; }
                if (avatar != null && avatar.Prefab != null)
                    _spawned = Instantiate(avatar.Prefab, _prefabRoot);
            }
        }

        /// <summary>Показать аватар по каталожному itemId ("avatar_prince") — резолв через AvatarConfig.</summary>
        public void SetAvatar(string itemId)
        {
            var cfg = Game.Core.Configs.AvatarConfig.Instance;
            SetAvatar(cfg != null ? cfg.Get(itemId) : null);
        }

        void OnDestroy()
        {
            if (_spawned != null) Destroy(_spawned);
        }
    }
}
