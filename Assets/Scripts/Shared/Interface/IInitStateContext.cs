namespace Game.Core.Shared.Interface
{
    public interface IInitStateContext
    {
        /// <summary>Вызывается из LoginPanel после успешной авторизации.</summary>
        void OnLoginSuccess();
    }
}
