namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Наносит Amount урона игроку-владельцу источника способности (само-урон).
    /// Используется картами вроде «Сатанинский круг», «Долгий обряд», «Адовый червь».
    /// </summary>
    public struct DealDamageOwnerEffectComponent
    {
        public int Amount;
    }
}
