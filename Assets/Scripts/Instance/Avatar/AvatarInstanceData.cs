using UnityEngine;

namespace Game.Core.Instance.Avatar
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Один КОСМЕТИЧЕСКИЙ аватар игрока (профильная картинка/фигурка), зеркально CardInstanceData.
    /// Наследует InstanceData — общая семья «данные-инстанс». НЕ путать с боевым героем на доске
    /// (AvatarPlayerView/AvatarViewComponent) — то другое.
    ///
    /// Контракт id: каталожный itemId в PlayFab = "avatar_" + AvatarId (напр. AvatarId="prince" →
    /// "avatar_prince"). По нему магазин/профиль резолвят аватар через AvatarConfig.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAvatar", menuName = "Data/AvatarInstanceData")]
    public class AvatarInstanceData : InstanceData
    {
        [Tooltip("Короткий id (напр. 'prince'). Каталожный itemId = 'avatar_' + этот id.")]
        public string AvatarId;

        [Tooltip("Отображаемое имя — ключ локализации (ui.avatar.*) или сырой текст (резолвится как у карт).")]
        public string DisplayName;

        [Tooltip("Спрайт для ПЛОСКИХ мест: магазин, HUD, мелкий аватар.")]
        public Sprite Icon;

        [Tooltip("Анимированный префаб для КРУПНОГО показа (спавнит PrefabAvatarView). Необязательно.")]
        public GameObject Prefab;

        /// <summary>Каталожный itemId ("avatar_prince"). Префикс закреплён контрактом (CardItemId.IsAvatar,
        /// BackendConfig.AvatarIdPrefix) — тут литерал, т.к. Instance-сборка не ссылается на Backend.
        /// Реализация общего контракта InstanceData.</summary>
        public override string ItemId => "avatar_" + AvatarId;

        // Миниатюра аватара = его Icon (или заданный _miniature). Магазин/награды берут единообразно .Miniature.
        public override Sprite Miniature => _miniature != null ? _miniature : Icon;
    }
}
