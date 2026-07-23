using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Game.Core.Backend;
using Game.Core.Configs;
using Game.Core.Service;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Экран открытия бустера: пачка → веер рубашкой вверх → ПОСЛЕДОВАТЕЛЬНЫЙ переворот карт (тап за тапом
    /// или авто), с VFX-взрывом по редкости под каждой картой → «Забрать».
    ///   1) по центру пачка (_packObject) — покачивание, тап открывает;
    ///   2) пачка «разрывается» (punch+fade), опц. _burstParticles;
    ///   3) карты вылетают веером РУБАШКОЙ вверх (DOTween);
    ///   4) РЕВИЛ: тап по _revealTapArea (или авто) переворачивает следующую карту — играет VFX её редкости,
    ///      rare+ подсвечиваются (SetGlow); когда все открыты — кнопка «Забрать».
    ///
    /// Префаб: _root, _packObject (+_packButton), _packHint (TMP), _cardsRoot (RectTransform-центр веера),
    /// _cardPrefab (RewardCardSlot С рубашкой _backRoot), _collectButton, _burstParticles (опц.), _cardConfig,
    /// _revealTapArea (Button на весь экран, ловит тап открытия — опц.). Параметры веера/сетки — в инспекторе.
    /// VFX по редкости живут В ПРЕФАБЕ КАРТЫ (RewardCardSlot) — он играет их сам при перевороте.
    /// </summary>
    public class BoosterRevealView : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private CardConfig _cardConfig;

        [Header("Refs")]
        [SerializeField] private GameObject _root;
        [Tooltip("Статичная пачка-заглушка (фолбэк, если из BoosterConfig не пришёл префаб-вьюшка).")]
        [SerializeField] private GameObject _packObject;
        [SerializeField] private Button _packButton;
        [Tooltip("Контейнер для ПРЕФАБА-вьюшки пачки (из BoosterConfig). Если задан + пришёл packPrefab — пачка инстанцируется сюда, а _packObject прячется.")]
        [SerializeField] private Transform _packHolder;
        [SerializeField] private TextMeshProUGUI _packHint;
        [Tooltip("Подпись «×N» на пачке — сколько бустеров открываем (мульти). При одном скрывается. Опц.")]
        [SerializeField] private TextMeshProUGUI _packCountText;
        [SerializeField] private RectTransform _cardsRoot;
        [SerializeField] private RewardCardSlot _cardPrefab;
        [SerializeField] private Button _collectButton;
        [SerializeField] private ParticleSystem _burstParticles;

        [Header("Fan tuning")]
        [SerializeField] private float _cardSpacing = 190f;   // расстояние между картами по X
        [SerializeField] private float _arc = 26f;            // прогиб веера (Y)
        [SerializeField] private float _tilt = 6f;            // наклон крайних карт (град/шаг)
        [SerializeField] private float _dealDuration = 0.45f; // длительность вылета одной карты
        [SerializeField] private float _stagger = 0.08f;      // задержка между картами
        [SerializeField] private float _packBurst = 0.35f;    // длительность вскрытия пачки

        [Header("Reveal (последовательный)")]
        [Tooltip("Тап открывает следующую карту (нужен _revealTapArea). Выкл → авто-переворот с задержкой _autoStagger.")]
        [SerializeField] private bool _tapToReveal = true;
        [Tooltip("Полноэкранная кнопка-ловушка тапа на фазе открытия (если _tapToReveal). Опц.")]
        [SerializeField] private Button _revealTapArea;
        [SerializeField] private float _flipDuration = 0.28f;
        [Tooltip("Пауза между авто-переворотами (если тап выключен).")]
        [SerializeField] private float _autoStagger = 0.4f;

        // VFX по редкости живут В ПРЕФАБЕ КАРТЫ (RewardCardSlot) и проигрываются им при перевороте —
        // UIParticle (MobSakai) нельзя инстанцировать на лету, объекты должны быть заранее в префабе.

        [Header("Grid (мульти-открытие: много карт сразу)")]
        [Tooltip("Если РАЗНЫХ карт больше этого числа — раскладка сеткой, все сразу, без пачки и без тапа по одной.")]
        [SerializeField] private int _gridThreshold = 6;
        [Tooltip("Колонок в сетке. 0 = авто (≈√N, максимум 5).")]
        [SerializeField] private int _gridColumns = 0;
        [SerializeField] private float _gridSpacingX = 150f;
        [SerializeField] private float _gridSpacingY = 210f;
        [Tooltip("Масштаб карты в сетке (меньше = больше влезает).")]
        [SerializeField] private float _gridScale = 0.5f;
        [SerializeField] private float _gridStagger = 0.05f;
        [SerializeField] private float _gridDealDuration = 0.35f;

        struct RevealItem { public RewardCardSlot Slot; public bool RarePlus; }

        readonly List<RewardCardSlot> _spawned = new();
        readonly List<Tween> _tweens = new();
        readonly List<RevealItem> _reveal = new();
        int _revealIndex;
        Coroutine _autoRoutine;

        RewardBundle _pending;
        Action _onClosed;
        Sequence _packIdle;

        GameObject _packInstance;    // инстанс префаба-вьюшки пачки (если пришёл из BoosterConfig)
        GameObject _activePack;      // активная пачка: инстанс ИЛИ статичный _packObject
        Button _activePackButton;    // кнопка тапа активной пачки
        bool _grid;                  // текущий ревил в грид-режиме (мульти-открытие: много карт сразу)

        void Awake()
        {
            // _packButton (тап по пачке) вешаем в ResolvePack — пачка может быть динамическим префабом.
            if (_collectButton != null) _collectButton.onClick.AddListener(Collect);
            if (_revealTapArea != null) _revealTapArea.onClick.AddListener(OnRevealTap);
            if (_root != null) _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (_activePackButton != null) _activePackButton.onClick.RemoveListener(Open);
            if (_collectButton != null) _collectButton.onClick.RemoveListener(Collect);
            if (_revealTapArea != null) _revealTapArea.onClick.RemoveListener(OnRevealTap);
            KillTweens();
        }

        /// <summary>Совместимость: показать с фолбэк-пачкой (без префаба-вьюшки), один бустер.</summary>
        public void Show(RewardBundle reward, Action onClosed = null) => Show(reward, null, 1, onClosed);

        /// <summary>Показать с префабом-вьюшкой пачки, один бустер.</summary>
        public void Show(RewardBundle reward, GameObject packPrefab, Action onClosed = null) => Show(reward, packPrefab, 1, onClosed);

        /// <summary>Показать экран открытия. packPrefab — префаб-вьюшка пачки из BoosterConfig (null → фолбэк
        /// _packObject); boosterCount — сколько бустеров открываем (подпись «×N» на пачке, при 1 скрыта).
        /// Карты берутся из reward.Cards. onClosed — по «Забрать».</summary>
        public void Show(RewardBundle reward, GameObject packPrefab, int boosterCount, Action onClosed = null)
        {
            _pending = reward;
            _onClosed = onClosed;
            Clear();

            if (_root != null) _root.SetActive(true);
            if (_collectButton != null) _collectButton.gameObject.SetActive(false);
            if (_revealTapArea != null) _revealTapArea.gameObject.SetActive(false);

            int n = _pending != null && _pending.Cards != null ? _pending.Cards.Count : 0;
            _grid = n > _gridThreshold;   // много РАЗНЫХ карт (мульти) → сетка вместо веера. Пачка есть в обоих случаях.

            // Подпись «×N» на пачке — сколько бустеров вскрываем (при одном скрыта).
            if (_packCountText != null)
            {
                bool many = boosterCount > 1;
                _packCountText.gameObject.SetActive(many);
                if (many) _packCountText.text = $"×{boosterCount}";
            }

            if (_packHint != null) { _packHint.text = UIStrings.BoosterTap; _packHint.gameObject.SetActive(true); }
            ResolvePack(packPrefab);

            if (_activePack != null)
            {
                _activePack.SetActive(true);
                _activePack.transform.localScale = Vector3.one;
                _packIdle = DOTween.Sequence().SetUpdate(true);
                _packIdle.Append(_activePack.transform.DOScale(1.05f, 0.8f).SetEase(Ease.InOutSine));
                _packIdle.Append(_activePack.transform.DOScale(1.0f, 0.8f).SetEase(Ease.InOutSine));
                _packIdle.SetLoops(-1);
            }
            else
            {
                Open();   // без пачки — сразу веер
            }
        }

        // Выбрать активную пачку: префаб-вьюшка (инстанс в _packHolder) ИЛИ статичный _packObject. Вешаем тап.
        void ResolvePack(GameObject packPrefab)
        {
            if (_activePackButton != null) { _activePackButton.onClick.RemoveListener(Open); _activePackButton = null; }
            if (_packInstance != null) { Destroy(_packInstance); _packInstance = null; }

            if (packPrefab != null && _packHolder != null)
            {
                if (_packObject != null) _packObject.SetActive(false);   // прячем статичную заглушку
                _packInstance = Instantiate(packPrefab, _packHolder);
                _packInstance.SetActive(true);
                _activePack = _packInstance;
                // Тап-кнопка: сперва на самом префабе пачки, иначе на PackHolder (он остаётся активным),
                // и только потом статичная _packButton (она может лежать на скрытом _packObject).
                _activePackButton = _packInstance.GetComponentInChildren<Button>(true);
                if (_activePackButton == null) _activePackButton = _packHolder.GetComponent<Button>();
                if (_activePackButton == null) _activePackButton = _packButton;
            }
            else
            {
                _activePack = _packObject;      // фолбэк — статичная пачка
                _activePackButton = _packButton;
            }

            if (_activePackButton != null) _activePackButton.onClick.AddListener(Open);
        }

        void Open()
        {
            _packIdle?.Kill();
            _packIdle = null;
            if (_packHint != null) _packHint.gameObject.SetActive(false);
            if (_packCountText != null) _packCountText.gameObject.SetActive(false);

            if (_activePack != null)
            {
                var t = _activePack.transform;
                var pack = _activePack;
                var seq = DOTween.Sequence().SetUpdate(true);
                seq.Append(t.DOScale(1.25f, _packBurst * 0.4f).SetEase(Ease.OutBack));
                seq.Append(t.DOScale(0f, _packBurst * 0.6f).SetEase(Ease.InBack));
                seq.OnComplete(() =>
                {
                    if (pack != null) pack.SetActive(false);
                    if (_burstParticles != null) _burstParticles.Play();
                    Deal();
                });
                _tweens.Add(seq);
            }
            else
            {
                if (_burstParticles != null) _burstParticles.Play();
                Deal();
            }
        }

        // Раздача: ВЕЕР (одиночное открытие, ≤ порога) ИЛИ СЕТКА (мульти — много карт сразу).
        void Deal()
        {
            if (_pending == null || _pending.Cards == null || _cardsRoot == null || _cardPrefab == null)
            {
                ShowCollect();
                return;
            }
            if (_grid) DealGrid();
            else DealFan();
        }

        // Веер РУБАШКОЙ вверх; по завершении раскладки — фаза последовательного открытия (тап за тапом).
        void DealFan()
        {
            int n = _pending.Cards.Count;
            float mid = (n - 1) / 2f;

            for (int i = 0; i < n; i++)
            {
                var card = _pending.Cards[i];
                var model = MetaCardResolver.ResolveModel(_cardConfig, card.ItemId);

                var slot = Instantiate(_cardPrefab, _cardsRoot);
                slot.gameObject.SetActive(true);
                slot.SetData(model, card.Amount);
                slot.SetFaceDown();               // рубашкой вверх — раскроется в фазе ревила
                _spawned.Add(slot);

                var rt = (RectTransform)slot.transform;
                var cg = slot.GetComponent<CanvasGroup>();
                if (cg == null) cg = slot.gameObject.AddComponent<CanvasGroup>();

                float t = i - mid;
                Vector2 target = new Vector2(t * _cardSpacing, -(t * t) * _arc);
                float rotZ = -t * _tilt;

                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one * 0.5f;
                rt.localRotation = Quaternion.identity;
                cg.alpha = 0f;

                _reveal.Add(new RevealItem
                {
                    Slot = slot,
                    RarePlus = model != null && model.Rarity >= EnumService.Rarity.Epic
                });

                float delay = i * _stagger;
                _tweens.Add(rt.DOAnchorPos(target, _dealDuration).SetEase(Ease.OutBack).SetDelay(delay).SetUpdate(true));
                _tweens.Add(rt.DOScale(1f, _dealDuration).SetDelay(delay).SetUpdate(true));
                _tweens.Add(rt.DOLocalRotate(new Vector3(0, 0, rotZ), _dealDuration).SetDelay(delay).SetUpdate(true));
                _tweens.Add(cg.DOFade(1f, _dealDuration * 0.6f).SetDelay(delay).SetUpdate(true));
            }

            // Раскладка легла → запускаем фазу открытия.
            float total = (n - 1) * _stagger + _dealDuration + 0.1f;
            _tweens.Add(DOVirtual.DelayedCall(total, StartReveal, false).SetUpdate(true));
        }

        // Сетка (мульти-открытие): все карты СРАЗУ лицом, каскадом снизу вверх, без пачки и тапа по одной.
        void DealGrid()
        {
            int n = _pending.Cards.Count;
            int cols = _gridColumns > 0 ? _gridColumns : Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(n)), 1, 5);
            int rows = Mathf.CeilToInt((float)n / cols);
            float midCol = (cols - 1) / 2f;
            float midRow = (rows - 1) / 2f;

            for (int i = 0; i < n; i++)
            {
                var card = _pending.Cards[i];
                var model = MetaCardResolver.ResolveModel(_cardConfig, card.ItemId);

                var slot = Instantiate(_cardPrefab, _cardsRoot);
                slot.gameObject.SetActive(true);
                slot.SetData(model, card.Amount);   // рубашкой вверх — раскроется при приземлении
                _spawned.Add(slot);
                bool rarePlus = model != null && model.Rarity >= EnumService.Rarity.Epic;

                int col = i % cols;
                int row = i / cols;   // row 0 — нижний ряд: сетка растёт снизу вверх
                Vector2 target = new Vector2((col - midCol) * _gridSpacingX, (row - midRow) * _gridSpacingY);

                var rt = (RectTransform)slot.transform;
                var cg = slot.GetComponent<CanvasGroup>();
                if (cg == null) cg = slot.gameObject.AddComponent<CanvasGroup>();

                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one * (_gridScale * 0.6f);
                rt.localRotation = Quaternion.identity;
                cg.alpha = 0f;

                float delay = i * _gridStagger;
                _tweens.Add(rt.DOAnchorPos(target, _gridDealDuration).SetEase(Ease.OutBack).SetDelay(delay).SetUpdate(true));
                _tweens.Add(rt.DOScale(_gridScale, _gridDealDuration).SetDelay(delay).SetUpdate(true));
                _tweens.Add(cg.DOFade(1f, _gridDealDuration * 0.6f).SetDelay(delay).SetUpdate(true));

                // Приземлилась → свечение + переворот (Flip сам играет VFX редкости). Ждём КОНЦА полёта,
                // иначе DOScaleX флипа подерётся с DOScale раздачи.
                var landed = slot;
                _tweens.Add(DOVirtual.DelayedCall(delay + _gridDealDuration + 0.05f,
                    () =>
                    {
                        if (landed == null) return;
                        landed.SetGlow(rarePlus);
                        landed.Flip(_flipDuration);
                    }, false).SetUpdate(true));
            }

            // Всё разложено и перевёрнуто → «Забрать» (фазы тапа по одной тут нет).
            float total = (n - 1) * _gridStagger + _gridDealDuration + 0.05f + _flipDuration + 0.15f;
            _tweens.Add(DOVirtual.DelayedCall(total, ShowCollect, false).SetUpdate(true));
        }

        // ── Фаза открытия ────────────────────────────────────────────────────────

        void StartReveal()
        {
            _revealIndex = 0;
            if (_reveal.Count == 0) { ShowCollect(); return; }

            if (_tapToReveal && _revealTapArea != null)
            {
                _revealTapArea.gameObject.SetActive(true);
                _revealTapArea.interactable = true;   // тап откроет первую карту
            }
            else
            {
                _autoRoutine = StartCoroutine(AutoRevealLoop());
            }
        }

        // Тап по фону: открыть следующую карту (блокируем тап на время переворота).
        void OnRevealTap()
        {
            if (!_tapToReveal) return;
            if (_revealIndex >= _reveal.Count) return;

            if (_revealTapArea != null) _revealTapArea.interactable = false;
            var item = _reveal[_revealIndex];
            _revealIndex++;
            RevealOne(item, () =>
            {
                if (_revealIndex >= _reveal.Count) FinishReveal();
                else if (_revealTapArea != null) _revealTapArea.interactable = true;   // готов к следующему тапу
            });
        }

        IEnumerator AutoRevealLoop()
        {
            while (_revealIndex < _reveal.Count)
            {
                RevealOne(_reveal[_revealIndex], null);
                _revealIndex++;
                yield return new WaitForSecondsRealtime(_flipDuration + _autoStagger);
            }
            _autoRoutine = null;
            FinishReveal();
        }

        // Открыть одну карту: свечение (epic+) → переворот (VFX редкости слот играет сам внутри Flip).
        void RevealOne(RevealItem item, Action onDone)
        {
            if (item.Slot != null)
            {
                item.Slot.SetGlow(item.RarePlus);
                item.Slot.Flip(_flipDuration, onDone);
            }
            else onDone?.Invoke();
        }

        void FinishReveal()
        {
            if (_revealTapArea != null) _revealTapArea.gameObject.SetActive(false);
            ShowCollect();
        }

        void ShowCollect()
        {
            if (_collectButton != null) _collectButton.gameObject.SetActive(true);
        }

        void Collect()
        {
            var cb = _onClosed;
            Hide();
            cb?.Invoke();
        }

        public void Hide()
        {
            KillTweens();
            Clear();
            if (_root != null) _root.SetActive(false);
        }

        void Clear()
        {
            if (_autoRoutine != null) { StopCoroutine(_autoRoutine); _autoRoutine = null; }
            foreach (var s in _spawned)
                if (s != null) Destroy(s.gameObject);
            _spawned.Clear();
            _reveal.Clear();
            _revealIndex = 0;
            if (_revealTapArea != null) _revealTapArea.gameObject.SetActive(false);

            // Снести инстанс префаба-вьюшки пачки (если был).
            if (_activePackButton != null) { _activePackButton.onClick.RemoveListener(Open); _activePackButton = null; }
            if (_packInstance != null) { Destroy(_packInstance); _packInstance = null; }
            _activePack = null;
        }

        void KillTweens()
        {
            _packIdle?.Kill();
            _packIdle = null;
            if (_autoRoutine != null) { StopCoroutine(_autoRoutine); _autoRoutine = null; }
            foreach (var t in _tweens) t?.Kill();
            _tweens.Clear();
        }
    }
}
