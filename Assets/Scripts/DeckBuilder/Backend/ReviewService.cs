using System;

namespace Game.Core.Backend
{
    /// <summary>
    /// REVIEW-билд (для издателя). Одним серверным вызовом помечает аккаунт как ревью (тег "review_account" +
    /// флаг с датой в UserReadOnlyData — чтобы потом сегментом в Game Manager массово сбросить/удалить) и
    /// выдаёт стартовые бустеры в РЕАЛЬНЫЙ инвентарь (издатель проверяет открытие). Идемпотентно: бустеры
    /// выдаются один раз. Серверная часть гейтится Title Data reviewSetup=true. Зовётся из InitState при
    /// IsReviewBuild. Коллекцию открывает клиент отдельно (PlayerLibrary.FillFullCollection).
    /// </summary>
    public static class ReviewService
    {
        [Serializable] public class ReviewResult : BackendResult { public int BoostersGranted; }

        public static void SetupReviewAccount(Action<ReviewResult> onDone = null, Action<string> onError = null)
            => FunctionService.Call<ReviewResult>(BackendConfig.Fn.SetupReviewAccount, onDone, onError);
    }
}
