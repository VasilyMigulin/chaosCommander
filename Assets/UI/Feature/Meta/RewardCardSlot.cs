using System;
using DG.Tweening;
using Game.Core.Model.Card;
using Game.Core.Service;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Дисплейный слот карты (без кнопки) — показ выпавших/полученных карт (reveal бустера, попап наград).
    /// ПОЛНАЯ карта через вложенный StaticCardView (единый шаблон, как в магазине/аукционе) — заполняется
    /// CardModel (см. MetaCardResolver.ResolveModel).
    ///
    /// ПОСЛЕДОВАТЕЛЬНОЕ открытие: SetFaceDown() кладёт карту рубашкой вверх (_backRoot), Flip() переворачивает
    /// лицом (squish по X) и в этот момент проигрывает VFX своей редкости.
    ///
    /// VFX: объекты ЗАРАНЕЕ лежат В ЭТОМ ЖЕ ПРЕФАБЕ и ВЫКЛЮЧЕНЫ (UIParticle/MobSakai нельзя инстанцировать
    /// на лету). Код включает нужный по редкости, перезапускает партиклы и гасит через _vfxLifetime.
    ///
    /// Префаб: _cardView (StaticCardView — InspectCardView), _countText ("xN", опц.), _glow (свечение epic+, опц.),
    /// _backRoot (рубашка ПОВЕРХ лица, опц.), _rarityVfx[] (редкость → выключенный объект VFX), _defaultVfx (опц.).
    /// </summary>
    public class RewardCardSlot : MonoBehaviour
    {
        /// <summary>Редкость → ГОТОВЫЙ объект VFX внутри этого префаба (выключен). Не префаб для Instantiate!</summary>
        [Serializable]
        public class RarityVfx { public EnumService.Rarity Rarity; public GameObject Vfx; }

        [Tooltip("Полная карта — StaticCardView на дочернем объекте (напр. префаб InspectCardView).")]
        [SerializeField] private StaticCardView _cardView;
        [Tooltip("Кол-во ('xN') поверх карты. Опц.")]
        [SerializeField] private TextMeshProUGUI _countText;
        [Tooltip("Свечение для epic+ карт (подсветка в ревиле бустера). Опц.")]
        [SerializeField] private GameObject _glow;
        [Tooltip("Рубашка карты (поверх лица) — показывается, пока карта не перевёрнута. Опц.")]
        [SerializeField] private GameObject _backRoot;

        [Header("VFX по редкости (лежат в этом префабе выключенными)")]
        [SerializeField] private RarityVfx[] _rarityVfx;
        [Tooltip("Фолбэк-VFX, если для редкости карты нет своего. Опц.")]
        [SerializeField] private GameObject _defaultVfx;

        bool _flipping;
        EnumService.Rarity _rarity;

        /// <summary>Включить/выключить свечение редкости (для reveal-эффекта).</summary>
        public void SetGlow(bool on) { if (_glow != null) _glow.SetActive(on); }

        public void SetData(CardModel model, int count)
        {
            if (_cardView != null && model != null) _cardView.SetModel(model);   // полная карта рисуется отсюда
            _rarity = model != null ? model.Rarity : EnumService.Rarity.Common;

            if (_countText != null)
            {
                _countText.text = count > 1 ? $"x{count}" : "";
                _countText.gameObject.SetActive(count > 1);
            }
            SetGlow(false);    // по умолчанию без свечения (ревил включит для epic+)
            HideAllVfx();      // на случай, если в префабе VFX оставили включённым
            SetFaceDown();     // ВСЕГДА рубашкой вверх — раскрытие делает Flip() (в веере по тапу, иначе сразу)
        }

        /// <summary>Положить карту рубашкой вверх (перед последовательным ревилом).</summary>
        public void SetFaceDown()
        {
            transform.localScale = Vector3.one;
            if (_backRoot == null) return;
            // uGUI рисует по порядку иерархии: кто НИЖЕ в списке — поверх. Поднимаем рубашку последней,
            // иначе лицо карты (CardView) нарисуется поверх неё и карта будет выглядеть открытой.
            _backRoot.transform.SetAsLastSibling();
            _backRoot.SetActive(true);
        }

        /// <summary>Перевернуть карту лицом (squish по X → скрыть рубашку → раскрыть) + VFX редкости.
        /// Без рубашки — сразу onComplete (но VFX всё равно играет).</summary>
        public void Flip(float duration = 0.28f, Action onComplete = null)
        {
            PlayVfx();   // взрыв в момент раскрытия

            if (_flipping || _backRoot == null || !_backRoot.activeSelf) { onComplete?.Invoke(); return; }
            _flipping = true;
            float sx = transform.localScale.x; if (sx <= 0.001f) sx = 1f;
            float half = Mathf.Max(0.01f, duration * 0.5f);
            transform.DOScaleX(0f, half).SetEase(Ease.InQuad).SetUpdate(true).OnComplete(() =>
            {
                if (_backRoot != null) _backRoot.SetActive(false);   // ребро → лицо
                transform.DOScaleX(sx, half).SetEase(Ease.OutQuad).SetUpdate(true).OnComplete(() =>
                {
                    _flipping = false;
                    onComplete?.Invoke();
                });
            });
        }

        // ── VFX ──────────────────────────────────────────────────────────────────

        /// <summary>Проиграть VFX редкости этой карты (объект из префаба: включить + перезапустить партиклы).
        /// НЕ гасим по таймеру — обрыв съедает послесвечение; эффект уходит вместе с картой при закрытии ревила.</summary>
        public void PlayVfx()
        {
            var go = VfxFor(_rarity);
            if (go == null) return;

            go.SetActive(true);

            // Перезапуск с очисткой — иначе повторный показ не проиграется заново.
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                systems[i].Play(true);
            }
        }

        GameObject VfxFor(EnumService.Rarity rarity)
        {
            if (_rarityVfx != null)
                foreach (var v in _rarityVfx)
                    if (v != null && v.Vfx != null && v.Rarity == rarity) return v.Vfx;
            return _defaultVfx;
        }

        void HideAllVfx()
        {
            if (_rarityVfx != null)
                foreach (var v in _rarityVfx)
                    if (v != null && v.Vfx != null) v.Vfx.SetActive(false);
            if (_defaultVfx != null) _defaultVfx.SetActive(false);
        }
    }
}
