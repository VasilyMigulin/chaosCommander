using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Configs;
using Game.Core.Model.Card;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Живой перерендер описания СВОИХ чар в руке под текущий CharmDurationBonusService (Зачарованный):
    /// печатное «Действует N ходов» показывается как «N+бонус», пока карта ещё в руке. Реальный
    /// TurnsRemaining применяется отдельно и позже — в момент розыгрыша (RunMoveCardToBoardSystem), это
    /// чисто превью. Паттерн — как CardTierSystem/PlayerStatsViewSystem: дёшево, каждый кадр, дифф по
    /// уже показанному бонусу — событие уходит, только когда бонус карты РЕАЛЬНО поменялся.
    /// </summary>
    public sealed class CharmHandDurationPreviewSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        // CharmTimerComponent как гейт «есть печатная длительность» — вместо резолва CardCharmModel.TurnsAlive:
        // Ecs.Systems НЕ ссылается на сборку конкретного типа карты (Game.Core.Model.Card.Charm), как и
        // CardTierSystem работает только через базовый CardModel. Пока чара в руке, TurnsRemaining ещё не
        // тронут бонусом (тот применяется в RunMoveCardToBoardSystem) — там ровно печатное значение.
        readonly EcsFilterInject<Inc<HandTag, OwnCardTag, CharmTag, CardModelComponent, OwnerComponent, CharmTimerComponent>> _handCharms = default;
        readonly EcsPoolInject<CardModelComponent>    _modelPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool  = default;
        readonly EcsPoolInject<OwnerComponent>        _ownerPool = default;
        readonly EcsPoolInject<CharmTimerComponent>   _charmTimerPool = default;
        readonly EcsPoolInject<FixedCharmDurationTag> _fixedDurationPool = default;

        readonly Dictionary<int, int> _lastShownBonus = new Dictionary<int, int>();

        public void Run(IEcsSystems systems)
        {
            foreach (var e in _handCharms.Value)
            {
                if (_fixedDurationPool.Value.Has(e)) continue;   // FixTurns — превью не показывает бонус, его и не будет

                var model = ResolveModel(e);
                if (model == null) continue;

                int ownerId = _ownerPool.Value.Get(e).OwnerId;
                int bonus = CharmDurationBonusService.Get(ownerId);

                if (_lastShownBonus.TryGetValue(e, out int last) && last == bonus) continue;
                _lastShownBonus[e] = bonus;

                int baseTurns = _charmTimerPool.Value.Get(e).TurnsRemaining;   // печатное значение, пока в руке
                string desc = RenderDescription(e, baseTurns + bonus);
                if (desc == null) continue;   // нет текущего описания вовсе — нечего подменять
                if (_viewPool.Value.Has(e)) _viewPool.Value.Get(e).Description = desc;   // снэпшот — верный текст при пересоздании вьюхи
                GameEventBus.Publish(new CardDescriptionChangedUIEvent { CardEntity = e, Description = desc });
            }
        }

        // Подменяем ТОЛЬКО последнюю строку (длительность) в уже ЖИВОМ описании — не перерендериваем с нуля
        // из статичного model.Description. Собранная графтом чара («Проклятье для принцессы») держит в
        // CardViewDataComponent смёрженный текст донор-эффектов ПОВЕРХ печатного шаблона базового тира —
        // полный перерендер из model.Description стирал бы графт при первом же кадре в руке (баг 2026-08-21).
        string RenderDescription(int cardEntity, int effectiveTurns)
        {
            if (!_viewPool.Value.Has(cardEntity)) return null;
            string current = _viewPool.Value.Get(cardEntity).Description ?? string.Empty;
            string lang = CardTextLocalization.Language;
            string duration = CardTextLocalization.DurationLine(effectiveTurns, lang);

            int lastNl = current.LastIndexOf('\n');
            string body = lastNl >= 0 ? current.Substring(0, lastNl) : string.Empty;
            return string.IsNullOrEmpty(body) ? duration : body + "\n" + duration;
        }

        CardModel ResolveModel(int e)
        {
            ref var m = ref _modelPool.Value.Get(e);
            var inst = _cardConfig.Value.Get(m.ExpansionId, m.ModelId);
            return inst != null ? inst.CardData : null;
        }
    }
}
