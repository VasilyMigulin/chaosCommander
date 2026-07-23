using Game.Core.Events;

namespace Game.Core.Progression
{
    // Трекеры на событиях, которые ДОБАВЛЕНЫ в ECS специально под задачи (призыв/заполнение/чары/хрип).
    // Владелец в каждом событии сравнивается с TaskTrackingService.LocalPlayerId — считаем только своё,
    // событие может приходить на обоих клиентах (двойного подсчёта нет: чужой owner отфильтровывается).

    /// <summary>«Призовите X существ»: существо вышло на поле (CreatureInvokedEvent) под нашим владением.</summary>
    public sealed class SummonCreaturesTracker : TaskTracker
    {
        public override string Type => TaskTypes.SummonCreatures;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<CreatureInvokedEvent>(OnInvoked);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<CreatureInvokedEvent>(OnInvoked);
        void OnInvoked(CreatureInvokedEvent e)
        {
            int local = TaskTrackingService.LocalPlayerId;
            if (local >= 0 && e.OwnerId == local) Report(1);
        }
    }

    /// <summary>«Заполнить свою сторону X раз»: своя передняя линия заполнилась (edge-detect в BoardFillTrackSystem).</summary>
    public sealed class FillBoardTracker : TaskTracker
    {
        public override string Type => TaskTypes.FillBoard;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<OwnSideFilledTrackedEvent>(OnFilled);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<OwnSideFilledTrackedEvent>(OnFilled);
        void OnFilled(OwnSideFilledTrackedEvent e)
        {
            int local = TaskTrackingService.LocalPlayerId;
            if (local >= 0 && e.OwnerId == local) Report(1);
        }
    }

    /// <summary>«Контролируйте чары X ходов»: на старте своего хода +1 за каждую свою чару.</summary>
    public sealed class CharmTurnsTracker : TaskTracker
    {
        public override string Type => TaskTypes.CharmTurns;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<CharmsControlledTrackedEvent>(OnTurn);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<CharmsControlledTrackedEvent>(OnTurn);
        void OnTurn(CharmsControlledTrackedEvent e)
        {
            int local = TaskTrackingService.LocalPlayerId;
            if (local >= 0 && e.OwnerId == local && e.Count > 0) Report(e.Count);
        }
    }

    /// <summary>«Активируйте „При смерти" X раз»: сработал хрип нашего существа (в т.ч. в ход противника).</summary>
    public sealed class DeathrattleTracker : TaskTracker
    {
        public override string Type => TaskTypes.Deathrattle;
        public override void Subscribe()   => GameEventBus.SubscribePersistent<DeathrattleTrackedEvent>(OnRattle);
        public override void Unsubscribe() => GameEventBus.UnsubscribePersistent<DeathrattleTrackedEvent>(OnRattle);
        void OnRattle(DeathrattleTrackedEvent e)
        {
            int local = TaskTrackingService.LocalPlayerId;
            if (local >= 0 && e.OwnerId == local) Report(1);
        }
    }
}
