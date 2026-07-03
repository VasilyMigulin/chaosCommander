namespace Game.Core.Events
{
    /// <summary>
    /// «VS-раскрытие закончилось — показываем мулиган». Публикуется в RPC_StartGame (после паузы показа
    /// командиров). Само окно мулигана открывается ещё в EcsHandler.Init (по MulliganStartedEvent), но
    /// накрыто крышкой (LoadingOverlay → VS-экран); это событие снимает крышку (VS закрывается) —
    /// синхронно на обоих клиентах, поэтому мулиган у обоих проявляется одновременно.
    /// </summary>
    public struct MulliganPhaseBeginUIEvent : IGameEvent { }
}
