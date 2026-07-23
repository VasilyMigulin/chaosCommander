namespace Game.Core.Events
{
    /// <summary>
    /// Раскрытие командиров перед мулиганом (VS-экран). Публикуется на каждом клиенте из
    /// RPC_RevealCommanders — идентичности уже развёрнуты «свой/оппонент» относительно локального игрока.
    /// Командир оппонента ещё НЕ существует в локальном ECS (его карты приедут снапшотом после мулигана),
    /// поэтому VS-окно резолвит модель по exp/id через CardConfig.
    /// </summary>
    public struct CommandersRevealedUIEvent : IGameEvent
    {
        public string LocalExpansionId;
        public int    LocalCardId;
        public string OpponentExpansionId;
        public int    OpponentCardId;

        // Косметические аватары (itemId, напр. "avatar_prince"). Пусто → дефолт. Едут тем же каналом,
        // что и командиры (SubmitLocalCommander → RevealCommanders): оппонент увидит НАШ аватар, мы — его.
        public string LocalAvatarId;
        public string OpponentAvatarId;
    }
}
