using System.Collections.Generic;
using Game.Core.Instance.Card;
using Game.Core.Model.Card;
using UnityEngine;

namespace Game.Core.Configs
{
    /// <summary>
    /// Хранит все карты одного аддона/расширения через CardInstanceData.
    /// Цепочка: ExpansionConfig -> CardInstanceData -> CardModel.
    /// CardModel при необходимости клонируется в рантайме, оригинал не трогается.
    /// </summary>
    [CreateAssetMenu(fileName = "NewExpansion", menuName = "Data/ExpansionConfig")]
    public sealed class ExpansionConfig : ScriptableObject
    {
        [KeyDropdown(KeyRegistry.SectionExpansion)]
        public string ExpansionId;

        [Tooltip("Экспаншен ТОЛЬКО для стори-режима: карты резолвятся движком (config.Get — колоды/энкаунтеры/" +
                 "владение), но НЕ попадают в коллекцию (PlayerLibrary.FillFullCollection их пропускает). " +
                 "Для боссов и стори-эксклюзивных карт вне PvP. Бустеры и так их не выбьют (свой пул в Title Data).")]
        public bool StoryOnly;

        public List<CardInstanceData> Cards = new List<CardInstanceData>();

        readonly Dictionary<int, CardInstanceData> _lookup = new Dictionary<int, CardInstanceData>();
        bool _ready;

        void OnEnable() => Rebuild();

        public void Rebuild()
        {
            _lookup.Clear();
            foreach (var instance in Cards)
            {
                if (instance == null || instance.CardData == null) continue;
                int id = instance.CardData.Id;
                if (_lookup.ContainsKey(id))
                {
                    Debug.LogWarning($"[ExpansionConfig:{ExpansionId}] Duplicate CardId {id} ('{instance.CardData.Name}'), skipping");
                    continue;
                }
                instance.CardData.ExpansionId = ExpansionId;
                _lookup[id] = instance;
            }
            _ready = true;
        }

        /// <summary>
        /// Найти CardInstanceData по CardId. Вернёт null если не найдена.
        /// Используй instance.CardData для доступа к модели.
        /// </summary>
        public CardInstanceData Get(int cardId)
        {
            if (!_ready) Rebuild();
            return _lookup.TryGetValue(cardId, out var inst) ? inst : null;
        }
    }
}
