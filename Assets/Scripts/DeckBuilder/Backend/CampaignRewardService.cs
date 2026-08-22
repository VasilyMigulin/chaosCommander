using System;
using UnityEngine;

namespace Game.Core.Backend
{
    /// <summary>
    /// Разовая награда за ПОЛНОЕ прохождение стори-кампании (Title Data "campaignRewards", CampaignId →
    /// {boosterId, count}). Сервер (ClaimCampaignReward) идемпотентен — повторный вызов вернёт
    /// Reason="already_claimed", ничего не выдав; клиент всё равно держит свой одноразовый флаг
    /// (см. BattleState.OnPveMatchEnded), чтобы не звонить на каждую последующую победу впустую.
    /// </summary>
    public static class CampaignRewardService
    {
        [Serializable] public class ClaimRequest { public string CampaignId; }

        /// <summary>PlayerPrefs-ключ клиентского флага «награда получена» — единый формат для записи
        /// (BattleState, после Success/already_claimed) и чтения (напр. BoosterPanel, лочит слот бустера,
        /// пока флага нет). НЕ путать с прогрессом боёв (CampaignProgress.Completed) — тот может стать true
        /// раньше, чем сервер реально подтвердит выдачу (напр. если кампания пройдена ДО того, как каталог/
        /// Title Data для награды были залиты) — в этом случае бустер должен оставаться locked.</summary>
        public static string ClaimedKey(string campaignId) => "campaign_reward_claimed_" + campaignId;

        /// <summary>Клиент уже видел подтверждённую сервером выдачу награды за эту кампанию.</summary>
        public static bool IsClaimed(string campaignId)
            => !string.IsNullOrEmpty(campaignId) && PlayerPrefs.GetInt(ClaimedKey(campaignId), 0) == 1;

        /// <summary>CampaignId — CampaignConfig.name (имя ассета кампании в Unity), см. CampaignProgress.</summary>
        public static void Claim(string campaignId, Action<RewardResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<ClaimRequest, RewardResponse>(
                BackendConfig.Fn.ClaimCampaignReward, new ClaimRequest { CampaignId = campaignId },
                r =>
                {
                    if (r != null && r.Success) PlayerWallet.ApplyIfPresent(r.Wallet);
                    onSuccess?.Invoke(r);
                }, onError);

        /// <summary>Пройдены ли ВСЕ энкаунтеры кампании — тот же прогресс (PlayerPrefs[PveMode.DoneKey]),
        /// что использует CampaignProgress в UI, но без зависимости от UI-сборки (см. ClaimIfCompleted).</summary>
        static bool IsCompleted(Game.Core.Configs.CampaignConfig campaign)
        {
            if (campaign == null || campaign.Encounters == null) return false;
            foreach (var e in campaign.Encounters)
            {
                if (e == null) continue;
                if (PlayerPrefs.GetInt(Game.Core.Service.PveMode.DoneKey(e.name), 0) != 1) return false;
            }
            return true;
        }

        /// <summary>Запросить награду, если кампания пройдена и клиент ещё не видел подтверждённой выдачи.
        /// Безопасно звать откуда угодно (по факту победы — BattleState, при открытии экрана кампании —
        /// StoryModePanel и т.п.): IsClaimed фильтрует повторные сетевые запросы, сервер идемпотентен сам.
        /// Нужен как РЕТРОАКТИВНЫЙ путь — игрок мог пройти кампанию ДО того, как награда/каталог появились
        /// (тогда обычный триггер, победа с firstWin=true, для него больше никогда не наступит: прогресс
        /// уже "пройдено").</summary>
        public static void ClaimIfCompleted(Game.Core.Configs.CampaignConfig campaign)
        {
            if (campaign == null) return;
            string campaignId = campaign.name;
            if (IsClaimed(campaignId) || !IsCompleted(campaign)) return;

            Claim(campaignId,
                onSuccess: r =>
                {
                    if (r != null && (r.Success || r.Reason == "already_claimed"))
                    {
                        PlayerPrefs.SetInt(ClaimedKey(campaignId), 1);
                        PlayerPrefs.Save();
                    }
                    if (r != null && r.Success)
                        Debug.Log($"[CampaignRewardService] '{campaignId}' — награда получена ({r.Reward?.Boosters?.Count ?? 0} бустеров).");
                    else
                        Debug.LogWarning($"[CampaignRewardService] клейм '{campaignId}': {(r != null ? r.Reason : "no response")}");
                },
                onError: err => Debug.LogWarning($"[CampaignRewardService] клейм '{campaignId}' ERROR: {err}"));
        }
    }
}
