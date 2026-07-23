using System.Collections.Generic;
using Game.Core.Instance.Card;
using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Авторинг пула чёрного рынка В UNITY: расписание ротации + слоты/цены по редкостям + карты пула
    /// (ССЫЛКАМИ на CardInstanceData). Экспортёр (Tools → Backend → Export Black Market Config) выгружает
    /// Title Data JSON "blackMarketConfig" с ПРАВИЛЬНЫМИ нижними itemId (берёт их из карты) — руками JSON
    /// не редактируешь и с регистром не ошибёшься.
    ///
    /// Карты пула группируются по редкости САМОЙ карты (CardData.Rarity) при экспорте. Пул может быть больше
    /// числа слотов — сервер детерминированно выбирает нужное кол-во на ротацию.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Black Market Pool", fileName = "BlackMarketPool")]
    public class BlackMarketPoolAsset : ScriptableObject
    {
        [System.Serializable]
        public struct RaritySetup
        {
            public EnumService.Rarity Rarity;
            [Tooltip("Сколько карт этой редкости в наборе ротации (напр. common=4, exotic=1).")]
            public int Slots;
            public string PriceCode;   // "GD"/"GM"
            public int PriceAmount;
        }

        [Header("Ротация (UTC)")]
        [Tooltip("День недели сброса: 0=Вс..6=Сб (среда=3). 8:00 МСК = 5:00 UTC.")]
        public int WeeklyDayUtc = 3;
        public int HourUtc = 0;

        [Header("Слоты и цены по редкостям")]
        public RaritySetup[] Rarities;

        [Header("Пул карт (группируются по редкости при экспорте)")]
        public List<CardInstanceData> Pool = new List<CardInstanceData>();
    }
}
