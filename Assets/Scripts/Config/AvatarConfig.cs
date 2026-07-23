using System.Collections.Generic;
using Game.Core.Instance.Avatar;
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Корневой конфиг всех КОСМЕТИЧЕСКИХ аватаров, зеркально CardConfig. Резолвит аватар по каталожному
    /// itemId ("avatar_prince") — им пользуются магазин (иконка товара), профиль/HUD (надетый аватар).
    ///
    /// Загружается лениво из Resources (как CampaignProgress), чтобы вьюхам не приходилось тащить ссылку
    /// через инспектор. Ассет клади в Assets/Resources/AvatarConfig.asset.
    /// </summary>
    [CreateAssetMenu(fileName = "AvatarConfig", menuName = "Data/AvatarConfig")]
    public sealed class AvatarConfig : ScriptableObject
    {
        public const string ResourcesPath = "AvatarConfig";   // Assets/Resources/AvatarConfig.asset

        static AvatarConfig _instance;
        /// <summary>Единый конфиг из Resources (лениво). null → ассета нет (аватары не резолвятся).</summary>
        public static AvatarConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AvatarConfig>(ResourcesPath);
                    if (_instance == null)
                        Debug.LogWarning($"[AvatarConfig] Нет ассета Resources/{ResourcesPath} — аватары не резолвятся.");
                }
                return _instance;
            }
        }

        [SerializeField] private AvatarInstanceData[] _avatars;

        Dictionary<string, AvatarInstanceData> _byItemId;

        void OnEnable() => _byItemId = null;   // пересобрать lookup при перезагрузке ассета

        void EnsureLookup()
        {
            if (_byItemId != null) return;
            _byItemId = new Dictionary<string, AvatarInstanceData>();
            if (_avatars == null) return;
            foreach (var a in _avatars)
            {
                if (a == null || string.IsNullOrEmpty(a.ItemId)) continue;
                if (!_byItemId.ContainsKey(a.ItemId)) _byItemId[a.ItemId] = a;
                else Debug.LogWarning($"[AvatarConfig] Дубль itemId '{a.ItemId}' — пропуск.");
            }
        }

        /// <summary>Найти аватар по каталожному itemId ("avatar_prince"). null — не найден.</summary>
        public AvatarInstanceData Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            EnsureLookup();
            return _byItemId.TryGetValue(itemId, out var a) ? a : null;
        }

        /// <summary>Все аватары (для экрана выбора/экипировки).</summary>
        public IReadOnlyList<AvatarInstanceData> All => _avatars ?? System.Array.Empty<AvatarInstanceData>();
    }
}
