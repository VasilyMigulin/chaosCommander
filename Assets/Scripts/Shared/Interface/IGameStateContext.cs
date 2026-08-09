using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface IGameStateContext
    {
        public bool IsServer { get; }
        public EcsWorld World { get; }
        void AddEntity(int entity, string localKey = null, string networkKey = null);
        void CastEvent<TEvent>(TEvent e) where TEvent : struct;
        bool TryGetEntity(string key, out int entity);
        bool TryGetPlayerEntity(out int playerEntity);
        bool TryGetOpponentEntity(out int opponentEntity);
        string GetNetEntityKey(int entity);

        /// <summary>
        /// Self-heal ресинк: выкинуть из мап ВСЕ записи КАРТ (сущности снесены и пересоздаются с теми же
        /// net-ключами), сохранив записи игроков (их сущности живы). Без этого AddEntity с ContainsKey-гардом
        /// молча не перезапишет протухшие EcsPackedEntity. Дефолт — no-op (Tutorial и др. ресинк не используют).
        /// </summary>
        void ResetCardEntityMaps() { }
    }
}
