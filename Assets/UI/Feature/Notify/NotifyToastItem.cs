using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Один тост: появляется (fade+scale), держится, уезжает и самоуничтожается. Спавнится
    /// NotifyPopupView в вертикальный контейнер (несколько тостов складываются стопкой).
    ///
    /// Префаб: _text (TMP), опц. _background (Image — красится по типу), _cg (CanvasGroup).
    /// </summary>
    public class NotifyToastItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _text;
        [SerializeField] private Image _background;
        [SerializeField] private CanvasGroup _cg;
        [SerializeField] private float _inDur = 0.25f;
        [SerializeField] private float _hold = 2.2f;
        [SerializeField] private float _outDur = 0.3f;

        public void Play(NotifyData data, Color color)
        {
            if (_text != null) _text.text = data.Text;
            if (_background != null) _background.color = color;
            if (_cg == null) _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();

            var rt = (RectTransform)transform;
            _cg.alpha = 0f;
            rt.localScale = Vector3.one * 0.9f;

            var seq = DOTween.Sequence().SetUpdate(true);
            seq.Append(_cg.DOFade(1f, _inDur));
            seq.Join(rt.DOScale(1f, _inDur).SetEase(Ease.OutBack));
            seq.AppendInterval(_hold);
            seq.Append(_cg.DOFade(0f, _outDur));
            seq.OnComplete(() => { if (this != null) Destroy(gameObject); });
        }
    }
}
