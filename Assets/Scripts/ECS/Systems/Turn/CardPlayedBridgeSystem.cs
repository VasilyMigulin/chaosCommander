using System;
using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Мост: новый пайплайн каста публикует CardCastEvent (RunInvokeCreature/RunCastRouter), а легаси-трекеры
    /// (MatchCounterTrackerSystem → CountsByModelId «разыграно», LastPlayedSpellTrackerSystem → Попадос)
    /// слушают CardPlayedEvent, который иначе никто не публикует. Здесь по CardCastEvent публикуем
    /// CardPlayedEvent (имя/владелец из компонентов карты). CardCastEvent у существ идёт только при !NotCast
    /// (реальный розыгрыш, не призыв) → «разыграно» считается корректно. Без этого MatchPlayedSelf=0
    /// (Позвать рой не растёт) и Попадос не видит последний спелл.
    ///
    /// СИНК: CardCastEvent идёт на обоих клиентах (реплей розыгрыша тоже его публикует) → счётчики зеркальны.
    /// </summary>
    public sealed class CardPlayedBridgeSystem : IEcsInitSystem, IEcsDestroySystem, IDisposable
    {
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardCastEvent>(OnCast);
            _subscribed = true;
        }

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardCastEvent>(OnCast);
            _subscribed = false;
        }

        // СИНХРОННО: публикуем CardPlayedEvent прямо в обработчике каста (Publish копирует список → вложенный
        // вызов безопасен). Иначе инкремент «разыграно» уезжал бы на следующий кадр (очередь+Run рано в пайплайне),
        // а резолв той же способности в этом кадре читал бы счётчик нулевым → Позвать рой даёт n=0.
        void OnCast(CardCastEvent e)
        {
            int card = e.CardEntity;
            if (!_modelPool.Value.Has(card)) { UnityEngine.Debug.Log($"[Cast] card={card} БЕЗ CardModelComponent → счётчик/Попадос пропущены"); return; }
            ref var m = ref _modelPool.Value.Get(card);
            int playerId = _ownerPool.Value.Has(card) ? _ownerPool.Value.Get(card).OwnerId : -1;
            UnityEngine.Debug.Log($"[Cast] card={card} model={m.ModelId} name='{m.CardName}' ownerId={playerId}");   // ВРЕМЕННО: что кастуется (облако?)

            GameEventBus.Publish(new CardPlayedEvent
            {
                CardName     = m.CardName,
                CardEntity   = card,
                PlayerId     = playerId,
                TargetCell   = -1,
                TargetEntity = -1,
            });
        }
    }
}
