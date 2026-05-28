namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранится на effect entity. Содержит цель эффекта и кастующего.
    /// Используется Apply-системами для доступа к цели без привязки
    /// payload-компонента к конкретному player/creature entity.
    /// </summary>
    public struct TargetEntityComponent
    {
        /// <summary>Entity на которую применяется эффект (игрок, существо и т.д.).</summary>
        public int TargetEntity;
        /// <summary>Entity кастующего — для эффектов, которым важно различить своего/чужого.</summary>
        public int OwnerEntity;
    }
}
