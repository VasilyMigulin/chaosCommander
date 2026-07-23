using System;
using System.Text;
using AwesomeUI.Core.Slot;
using Game.Core.Backend;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Слот задачи — общий для ЕЖЕДНЕВНЫХ и ЕЖЕНЕДЕЛЬНЫХ (DailyPanel спавнит один и тот же префаб в обе
    /// секции): название + прогресс + награда + кнопка «Забрать». Кнопка активна только когда задача
    /// выполнена и не заклеймлена.
    ///
    /// Награда: миниатюра + число (_rewardIcon/_rewardAmountText, как в ячейке дня — резолвит MetaIcon из
    /// RewardBundle). Текст-сводка _rewardText — опционально, если хочешь словами вместо иконки.
    ///
    /// Префаб: Button на корне (IsButton=true, EnableIcon=false), _titleText, _progressText ("2/3"),
    /// _progressBar (Slider, min=0 max=1, Interactable=OFF — это индикатор), _rewardIcon + _rewardAmountText,
    /// _claimedBadge; опц. _rewardText.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class DailyTaskSlot : SourceSlot
    {
        [SerializeField] private TextMeshProUGUI _titleText;
        [Tooltip("Контейнер прогресса (слайдер + «2/3»). Когда задача ЗАБРАНА — выключается, вместо него бадж " +
                 "«готово». Прогресс уже не нужен, а бадж встаёт на его место. Пусто → прогресс не прячется.")]
        [SerializeField] private GameObject _progressRoot;
        [SerializeField] private TextMeshProUGUI _progressText;
        [Tooltip("Полоса прогресса: Slider (min=0, max=1). Interactable выключи — это индикатор, не контрол.")]
        [SerializeField] private Slider _progressBar;
        [Header("Награда — миниатюра + число")]
        [SerializeField] private Image _rewardIcon;
        [SerializeField] private TextMeshProUGUI _rewardAmountText;
        [Tooltip("Галочка ПОВЕРХ иконки награды — когда награда забрана.")]
        [SerializeField] private GameObject _rewardClaimedCheck;
        [Tooltip("Опц.: текст-сводка награды словами («+100 Бабосик»). Обычно не нужен — есть иконка+число.")]
        [SerializeField] private TextMeshProUGUI _rewardText;

        [Header("Бейджи состояния (на месте прогресса)")]
        [Tooltip("«Завершено» — цель достигнута, но НЕ забрано (можно клеймить). LocalizedText: ui.task.completed.")]
        [SerializeField] private GameObject _completedBadge;
        [Tooltip("«Получено» — награда забрана. LocalizedText: ui.task.claimed.")]
        [SerializeField] private GameObject _claimedBadge;

        DailyService.TaskState _task;
        Action<DailyService.TaskState> _onClaim;

        public void SetData(DailyService.TaskState task, Action<DailyService.TaskState> onClaim)
        {
            _task = task;
            _onClaim = onClaim;
            UpdateView();
        }

        public override void UpdateView()
        {
            if (_task == null) return;

            // Три состояния: идёт прогресс → «Завершено» (готово к клейму) → «Получено» (забрано).
            bool claimed    = _task.Claimed;
            bool completed  = _task.Claimable;              // цель достигнута, ещё не забрано (Claimable ⇒ !Claimed)
            bool inProgress = !claimed && !completed;

            // Название: у забранной задачи — ПЕРЕЧЁРКНУТО (TMPro FontStyles.Strikethrough).
            if (_titleText != null)
            {
                _titleText.text = UIStrings.TaskLabel(_task.Type);
                _titleText.fontStyle = claimed ? FontStyles.Strikethrough : FontStyles.Normal;
            }

            if (_progressText != null) _progressText.text = $"{Mathf.Min(_task.Progress, _task.Target)}/{_task.Target}";
            if (_progressBar != null)
                _progressBar.SetValueWithoutNotify(_task.Target > 0 ? Mathf.Clamp01((float)_task.Progress / _task.Target) : 0f);

            // Награда: миниатюра + число (главная награда бандла). Текст-сводка — опционально.
            var icon = MetaIcon.RewardBundleIcon(_task.Reward, out int amount);
            if (_rewardIcon != null) { _rewardIcon.sprite = icon; _rewardIcon.enabled = icon != null; }
            if (_rewardAmountText != null) _rewardAmountText.text = amount > 1 ? amount.ToString() : "";
            if (_rewardText != null) _rewardText.text = RewardSummary(_task.Reward);
            if (_rewardClaimedCheck != null) _rewardClaimedCheck.SetActive(claimed);   // галочка на иконке награды

            // Прогресс виден только пока идёт; завершено/забрано → на его месте соответствующий бейдж.
            if (_progressRoot != null)   _progressRoot.SetActive(inProgress);
            if (_completedBadge != null) _completedBadge.SetActive(completed);
            if (_claimedBadge != null)   _claimedBadge.SetActive(claimed);

            if (_btnClick != null) _btnClick.interactable = completed;   // клеймить можно только «Завершено»
        }

        public override void OnClick()
        {
            if (_task == null || !_task.Claimable || _task.Claimed) return;
            _onClaim?.Invoke(_task);
        }

        static string RewardSummary(RewardBundle reward)
        {
            if (reward == null) return "";
            var sb = new StringBuilder();
            if (reward.Currencies != null)
                foreach (var c in reward.Currencies)
                    sb.Append(sb.Length > 0 ? " " : "").Append('+').Append(c.Amount).Append(' ').Append(UIStrings.CurrencyName(c.Code));
            if (reward.Boosters != null && reward.Boosters.Count > 0)
                sb.Append(sb.Length > 0 ? " " : "").Append("+бустер");
            return sb.ToString();
        }

        public override void OnUse() { }
        public override void Unject() { _onClaim = null; }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }
    }
}
