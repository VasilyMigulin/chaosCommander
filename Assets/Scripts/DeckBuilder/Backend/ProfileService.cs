using System;

namespace Game.Core.Backend
{
    /// <summary>
    /// Профиль игрока (статистика результатов) — читается серверной функцией GetProfile.
    /// Сервер агрегирует победы/поражения/достижения/открытые бустеры и т.п. по игроку.
    /// </summary>
    public static class ProfileService
    {
        public static void GetProfile(Action<PlayerProfileData> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetProfile, onSuccess, onError);
    }
}
