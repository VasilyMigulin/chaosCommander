using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.Core.Ability
{
    // Правила НЕ имеют OOP-контейнера: их список лежит в AbilityRuleContainerComponent на сущности.
    // (Условия — реактивные, поэтому их композит ниже остаётся OOP.)

    // === class (OOP) === Составное условие — группа листьев ICondition, реактивное
    // (подписывается на Changed детей). Это НЕ ICondition (как Rule — не IRule).
    // Обычный случай: одно выполнено, другое ещё нет → не готово (AllOf). NotCondition — опциональная инверсия.
    [Serializable]
    public sealed class Condition
    {
        public enum Mode { AllOf, AnyOf }

        [SerializeReference] public List<ICondition> Children = new();
        public Mode Op = Mode.AllOf;

        public bool IsReady { get; private set; }
        public event Action Changed;

        public void Init(AbilityContext ctx)
        {
            foreach (var child in Children)
            {
                child.Init(ctx);
                child.Changed += Recompute;
            }
            Recompute();
        }

        void Recompute()
        {
            bool now = Op == Mode.AllOf
                ? Children.All(c => c.IsReady)
                : Children.Any(c => c.IsReady);

            // ВРЕМЕННО (баг: не подсвечивается рыжим) — видим, дошёл ли пересчёт до КОМПОЗИТА и есть ли подписчики на Changed.
            UnityEngine.Debug.Log($"[CondHighlight] Condition.Recompute this={GetHashCode()} now={now} wasReady={IsReady} childCount={Children.Count} hasChangedSubscribers={Changed != null}");
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            foreach (var child in Children)
            {
                child.Changed -= Recompute;
                child.Dispose();
            }
        }
    }

    // === class (OOP) === Опциональная инверсия: готов, когда Inner НЕ готов.
    [Serializable]
    public sealed class NotCondition : ICondition
    {
        [SerializeReference] public ICondition Inner;

        public bool IsReady => Inner != null && !Inner.IsReady;
        public event Action Changed;

        Action _onInnerChanged;

        public void Init(AbilityContext ctx)
        {
            if (Inner == null) return;
            _onInnerChanged = () => Changed?.Invoke();
            Inner.Init(ctx);
            Inner.Changed += _onInnerChanged;
        }

        public void Dispose()
        {
            if (Inner == null) return;
            Inner.Changed -= _onInnerChanged;
            Inner.Dispose();
        }
    }
}
