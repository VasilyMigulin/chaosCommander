using System.Collections.Generic;
using Game.Core.Instance.Card;
using UnityEngine;

namespace Game.Core.Configs
{
    /// <summary>
    /// Корневой конфиг всех карт игры.
    /// Цепочка поиска: CardConfig → ExpansionConfig → CardInstanceData → CardModel.
    /// Инжектируется в ECS через EcsCustomInject.
    /// </summary>
    [CreateAssetMenu(fileName = "CardConfig", menuName = "Data/CardConfig")]
    public sealed class CardConfig : ScriptableObject
    {
        public List<ExpansionConfig> Expansions = new List<ExpansionConfig>();

        readonly Dictionary<string, ExpansionConfig> _lookup = new Dictionary<string, ExpansionConfig>();
        bool _ready;

        void OnEnable() => Rebuild();

        public void Rebuild()
        {
            _lookup.Clear();
            foreach (var exp in Expansions)
            {
                if (exp == null || string.IsNullOrEmpty(exp.ExpansionId)) continue;
                if (_lookup.ContainsKey(exp.ExpansionId))
                {
                    Debug.LogWarning($"[CardConfig] Duplicate ExpansionId '{exp.ExpansionId}', skipping");
                    continue;
                }
                _lookup[exp.ExpansionId] = exp;
                exp.Rebuild();
            }
            _ready = true;
        }

        /// <summary>
        /// Найти CardInstanceData по ExpansionId + CardId.
        /// Используй instance.CardData для доступа к CardModel.
        /// Вернёт null если не найдена.
        /// </summary>
        public CardInstanceData Get(string expansionId, int cardId)
        {
            if (!_ready) Rebuild();
            return _lookup.TryGetValue(expansionId, out var exp) ? exp.Get(cardId) : null;
        }
    }
}
