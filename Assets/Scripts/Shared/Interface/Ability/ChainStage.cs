using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Одна стадия составной (цепочечной) способности: СВОЙ таргетинг + СВОИ эффекты. Стадии
    /// выполняются по порядку, между ними мир «оседает» (применяется урон/смерти), а результаты
    /// (напр. число погибших) переносятся в следующую стадию через контекст. Лежит в Shared.Interface
    /// (как Rule), чтобы и Game.Core.Ability (авторинг), и Components (контейнер) на неё ссылались.
    /// </summary>
    [Serializable]
    public sealed class ChainStage
    {
        public enum TargetingMode { NonTarget, Target, Field }

        public TargetingMode Mode = TargetingMode.NonTarget;

        // Target-режим:
        public TargetSelection Selection = TargetSelection.Random;
        public int Count = 1;

        // Зона выбора целей: Board (существа/игроки) | Hand/Deck/Grave (карты в зоне). Для «сбросить из руки» — Hand.
        public TargetZone Zone = TargetZone.Board;

        // Field-режим:
        public FieldArea Area = FieldArea.All;

        // Target/Field: какие цели валидны.
        [SerializeReference] public List<ITargetFilter> Filters = new();

        // Что делает стадия.
        [SerializeReference] public List<IEffect> Effects = new();

        [Tooltip("NonTarget-стадия технически целит в самого КАСТЕРА (ResolveTargets: цели нет → подставляется " +
                 "игрок-владелец) — бить визуально почти всегда нечего, общий Vfx способности на такой стадии " +
                 "обычно лишний (см. RunChainSystem.EmitStageVfx — по умолчанию НЕ проигрывается на NonTarget). " +
                 "Включи, если этой конкретной NonTarget-стадии VFX всё-таки нужен (напр. вспышка НА кастере).")]
        public bool ForceVfxOnNonTarget = false;
    }
}
