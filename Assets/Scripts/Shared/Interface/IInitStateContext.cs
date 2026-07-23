namespace Game.Core.Shared.Interface
{
    public interface IInitStateContext
    {
        /// <summary>Вызывается из TitlePanel по «Tap to continue» — запускает стандартный пайплайн
        /// (туториал первого захода / показ логина).</summary>
        void OnContinue();

        /// <summary>Вызывается из LoginPanel после успешной авторизации.</summary>
        void OnLoginSuccess();

        /// <summary>ТЕСТ: пропустить вход через PlayFab — LoginPanel вместо формы/сетевых вызовов сразу
        /// зовёт OnLoginSuccess (см. InitState.SkipLoginForTesting). Выбор языка первого старта не трогает.</summary>
        bool TestSkipLogin { get; }
    }
}
