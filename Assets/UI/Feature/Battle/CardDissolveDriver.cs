using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Драйвер UI-dissolve карты (шейдер "UI/CardDissolve"). Повесь на объект карты; в _targets укажи
    /// Graphic'и (Image арт/рамка), чей материал — на шейдере UI/CardDissolve (или оставь пусто — возьмёт
    /// все Graphic в детях). PlayCardView зовёт Play(true) при заходе пальца на поле, Play(false) на отмене.
    /// Если не привязан к карте — PlayCardView использует фолбэк (альфа CanvasGroup).
    /// </summary>
    public sealed class CardDissolveDriver : MonoBehaviour
    {
        [SerializeField] Graphic[] _targets;

        static readonly int Id = Shader.PropertyToID("_DissolveAmount");
        Material[] _mats;
        float _value;

        void Awake()
        {
            if (_targets == null || _targets.Length == 0)
                _targets = GetComponentsInChildren<Graphic>(true);

            _mats = new Material[_targets.Length];
            for (int i = 0; i < _targets.Length; i++)
                if (_targets[i] != null && _targets[i].material != null)
                    _mats[i] = _targets[i].material = new Material(_targets[i].material);   // инстанс

            Set(0f);
        }

        public void Play(bool on, float dur)
        {
            DOTween.Kill(this);
            DOTween.To(() => _value, Set, on ? 1f : 0f, dur).SetTarget(this);
        }

        /// <summary>Мгновенно вернуть карту в нерастворённое состояние (повторный показ слота —
        /// возврат командира в руку после смерти; без анимации).</summary>
        public void ResetInstant()
        {
            DOTween.Kill(this);
            Set(0f);
        }

        void Set(float v)
        {
            _value = v;
            if (_mats == null) return;   // ResetInstant до Awake (SetCard на свежем слоте)
            foreach (var m in _mats)
                if (m != null && m.HasProperty(Id)) m.SetFloat(Id, v);
        }
    }
}
