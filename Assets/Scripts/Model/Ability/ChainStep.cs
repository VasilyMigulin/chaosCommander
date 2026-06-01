using System.Collections.Generic;
using UnityEngine;
using Game.Core.Model.Effect;
using Game.Core.Service;
using Game.Core.Shared.Interface;

namespace Game.Core.Model.Ability
{
    /// <summary>
    /// Шаг цепочки эффектов способности. Запускается ПОСЛЕ того, как
    /// предыдущий шаг полностью применён. Чистая модель — реализует
    /// IChainStep, чтобы ECS-слой не зависел от Model.Ability напрямую.
    /// </summary>
    [System.Serializable]
    public class ChainStep : IChainStep
    {
        /// <summary>Откуда взять цель для эффектов этого шага.</summary>
        public ChainTargetSource TargetSource = ChainTargetSource.PreviousTarget;

        /// <summary>
        /// Форма области вокруг исходных целей шага.
        /// Single — без расширения. При Row/Cross/Column/Adjacent — фильтр существ
        /// в области по TargetMaskOverride (если он задан) либо None.
        /// </summary>
        public TargetShape Shape = TargetShape.Single;

        /// <summary>
        /// Опциональный фильтр для области эффекта: если задан, используется при
        /// ExpandByShape для отсева существ; иначе область не фильтрует по союзникам/врагам.
        /// </summary>
        public TargetMask TargetMaskOverride = TargetMask.None;

        /// <summary>Эффекты этого шага (применяются параллельно к одной цели).</summary>
        [SerializeReference] public List<AbilityEffect> Effects = new List<AbilityEffect>();

        /// <summary>
        /// Опциональное предикат-условие шага. Если задано и Evaluate()==false —
        /// шаг полностью пропускается (цепочка продвигается к следующему). null = безусловно.
        /// </summary>
        [SerializeReference] public ChainCondition Condition;

        // ── IChainStep ────────────────────────────────────────────────────────
        ChainTargetSource IChainStep.TargetSource          => TargetSource;
        TargetShape       IChainStep.Shape                 => Shape;
        TargetMask        IChainStep.TargetMaskOverride    => TargetMaskOverride;
        IChainCondition   IChainStep.Condition             => Condition;
        IReadOnlyList<IAbilityEffect> IChainStep.Effects   => Effects;
    }
}
