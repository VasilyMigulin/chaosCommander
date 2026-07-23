using Game.Core.Events;
using Game.Core.Service;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Оверлей туториала: затемняет экран и вырезает «дырку» вокруг цели (TutorialAnchor с нужным id).
    /// Дырка делается ЧЕТЫРЬМЯ панелями (сверху/снизу/слева/справа) — без шейдеров и масок, работает на
    /// любом канвасе. Панели перехватывают клики, поэтому оверлей ещё и ограничивает ввод:
    ///   • BlockAll=true  (инфо-шаг)    — перекрыт ВЕСЬ экран, игрок только читает и жмёт «Далее»;
    ///   • BlockAll=false (шаг-действие) — сквозь дырку кликать можно, всё остальное перекрыто.
    /// Это же и защита от софт-лока: без блокировки игрок разыгрывает карты «наперёд», а шаг потом ждёт
    /// действия, которое уже нечем совершить.
    ///
    /// Кладётся на СВОЙ канвас поверх боевого (sortingOrder выше) — боевой UI не трогаем.
    /// Плашка подсказки с кнопкой «Далее» должна быть ВЫШЕ этого оверлея, иначе кнопка не нажмётся.
    ///
    /// Префаб: _root (весь оверлей, скрыт), четыре Image-панели затемнения (_dimTop/_dimBottom/_dimLeft/
    /// _dimRight, все raycastTarget=ON), опц. _frame (рамка вокруг дырки, пульсирует) и _holeBlocker
    /// (прозрачный Image поверх дырки, raycastTarget=ON — включается на инфо-шагах).
    /// </summary>
    public class TutorialHighlightView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [Header("Панели затемнения (raycastTarget = ON)")]
        [SerializeField] private RectTransform _dimTop;
        [SerializeField] private RectTransform _dimBottom;
        [SerializeField] private RectTransform _dimLeft;
        [SerializeField] private RectTransform _dimRight;

        [Header("Опционально")]
        [Tooltip("Рамка вокруг дырки — пульсирует, чтобы взгляд цеплялся.")]
        [SerializeField] private RectTransform _frame;
        [Tooltip("Прозрачный перехватчик поверх дырки: включается на ИНФО-шагах (ввод запрещён полностью).")]
        [SerializeField] private GameObject _holeBlocker;

        [Header("Настройки")]
        [Tooltip("Отступ дырки от границ цели, px.")]
        [SerializeField] private float _padding = 10f;
        [SerializeField] private float _pulseScale = 0.04f;
        [SerializeField] private float _pulseSpeed = 3f;
        [Tooltip("Цвет затемнения (для панелей, создаваемых автоматически).")]
        [SerializeField] private Color _dimColor = new Color(0f, 0f, 0f, 0.72f);

        RectTransform _self;
        Canvas _canvas;
        RectTransform _target;
        bool _blockAll;
        readonly Vector3[] _corners = new Vector3[4];

        void Awake()
        {
            _self = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            Stretch(_self);        // оверлей всегда во весь экран — от него считается дырка
            EnsureBuilt();         // панели затемнения собираем сами, если их не назначили в префабе
            if (_root != null) _root.SetActive(false);
        }

        // Ничего собирать руками не нужно: хватает пустого объекта с этим компонентом. Если поля назначены
        // в префабе (своя графика/цвета) — используются они, автосборка их не трогает.
        void EnsureBuilt()
        {
            if (_root == null)
            {
                var go = new GameObject("Root", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                Stretch((RectTransform)go.transform);
                _root = go;
            }

            if (_dimTop    == null) _dimTop    = CreatePanel("DimTop",    _dimColor);
            if (_dimBottom == null) _dimBottom = CreatePanel("DimBottom", _dimColor);
            if (_dimLeft   == null) _dimLeft   = CreatePanel("DimLeft",   _dimColor);
            if (_dimRight  == null) _dimRight  = CreatePanel("DimRight",  _dimColor);

            // Перехватчик кликов внутри дырки — прозрачный, но ловит райкасты. Создаётся ПОСЛЕДНИМ,
            // значит рисуется поверх панелей.
            if (_holeBlocker == null)
            {
                var blocker = CreatePanel("HoleBlocker", new Color(0f, 0f, 0f, 0f));
                _holeBlocker = blocker.gameObject;
                _holeBlocker.SetActive(false);
            }
        }

        RectTransform CreatePanel(string panelName, Color color)
        {
            var go = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root.transform, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);   // якорь в центре — так считает Layout()

            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // SubscribePersistent: оверлей живёт на сцене боя, но шина чистится при выходе (GameEventBus.Clear),
        // обычная подписка потерялась бы после первого боя — как в TutorialHintView.
        void OnEnable()  => GameEventBus.SubscribePersistent<TutorialHighlightUIEvent>(OnHighlight);
        void OnDisable() => GameEventBus.UnsubscribePersistent<TutorialHighlightUIEvent>(OnHighlight);

        void OnHighlight(TutorialHighlightUIEvent e)
        {
            if (!e.Show)
            {
                _target = null;
                if (_root != null) _root.SetActive(false);
                return;
            }

            _target = TutorialAnchor.Find(e.Anchor);
            _blockAll = e.BlockAll;

            // ШАГ-ДЕЙСТВИЕ без цели — оверлей НЕ показываем. Затемнение без дырки только мешало бы: игрок
            // должен кликать по доске (она в мировом пространстве, якорь на неё повесить нельзя).
            // ИНФО-шаг без цели — наоборот, гасим экран целиком: там ввод и должен быть перекрыт.
            if (_target == null && !e.BlockAll)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }

            if (_target == null && e.Anchor != TutorialAnchorId.None)
                Debug.LogWarning($"[TutorialHighlight] якорь '{e.Anchor}' не найден — затемняю экран целиком.", this);

            if (_root != null) _root.SetActive(true);
            if (_holeBlocker != null) _holeBlocker.SetActive(_blockAll);
            SetBlocking(_blockAll);
            Layout();
        }

        // Инфо-шаг → панели перехватывают клики (играть нельзя). Шаг-действие → затемнение ЧИСТО визуальное:
        // действие вроде «перетащи из руки на линию» затрагивает две области сразу, одной дыркой не накрыть.
        void SetBlocking(bool block)
        {
            SetRaycast(_dimTop, block);
            SetRaycast(_dimBottom, block);
            SetRaycast(_dimLeft, block);
            SetRaycast(_dimRight, block);
        }

        static void SetRaycast(RectTransform panel, bool on)
        {
            if (panel == null) return;
            var img = panel.GetComponent<Graphic>();
            if (img != null) img.raycastTarget = on;
        }

        // Пересчитываем каждый кадр: раскладка руки/доски двигается, цель может уехать.
        void LateUpdate()
        {
            if (_root == null || !_root.activeSelf) return;
            Layout();
            Pulse();
        }

        void Layout()
        {
            if (_self == null) return;
            Vector2 half = _self.rect.size * 0.5f;

            // Нет цели → дырки нет: панели сходятся в центре и перекрывают всё.
            Rect hole = new Rect(0f, 0f, 0f, 0f);
            if (_target != null && TryLocalRect(_target, out var r)) hole = r;

            float xMin = Mathf.Clamp(hole.xMin - _padding, -half.x, half.x);
            float xMax = Mathf.Clamp(hole.xMax + _padding, -half.x, half.x);
            float yMin = Mathf.Clamp(hole.yMin - _padding, -half.y, half.y);
            float yMax = Mathf.Clamp(hole.yMax + _padding, -half.y, half.y);

            //  ┌──────────top──────────┐
            //  │left │   ДЫРКА   │right│
            //  └─────────bottom────────┘
            Place(_dimTop,    new Vector2(0f, (yMax + half.y) * 0.5f),        new Vector2(half.x * 2f, half.y - yMax));
            Place(_dimBottom, new Vector2(0f, (-half.y + yMin) * 0.5f),       new Vector2(half.x * 2f, yMin + half.y));
            Place(_dimLeft,   new Vector2((-half.x + xMin) * 0.5f, (yMin + yMax) * 0.5f), new Vector2(xMin + half.x, yMax - yMin));
            Place(_dimRight,  new Vector2((xMax + half.x) * 0.5f, (yMin + yMax) * 0.5f),  new Vector2(half.x - xMax, yMax - yMin));

            if (_frame != null)
            {
                bool hasHole = xMax > xMin && yMax > yMin;
                _frame.gameObject.SetActive(hasHole);
                if (hasHole)
                {
                    _frame.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
                    _frame.sizeDelta = new Vector2(xMax - xMin, yMax - yMin);
                }
            }

            if (_holeBlocker != null && _holeBlocker.activeSelf && _holeBlocker.transform is RectTransform hb)
            {
                hb.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
                hb.sizeDelta = new Vector2(Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
            }
        }

        // Границы цели в локальных координатах оверлея (через экран — корректно для любого режима канваса).
        bool TryLocalRect(RectTransform target, out Rect local)
        {
            local = default;
            var cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _canvas.worldCamera : null;

            target.GetWorldCorners(_corners);

            // Габариты по ВСЕМ ЧЕТЫРЁМ углам: карты в руке развёрнуты веером, а у повёрнутого прямоугольника
            // углы 0 и 2 перестают быть минимумом/максимумом — по ним дырка съезжает мимо цели.
            float xMin = float.MaxValue, yMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, _corners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_self, screen, cam, out var p)) return false;
                if (p.x < xMin) xMin = p.x;
                if (p.x > xMax) xMax = p.x;
                if (p.y < yMin) yMin = p.y;
                if (p.y > yMax) yMax = p.y;
            }

            local = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        static void Place(RectTransform panel, Vector2 center, Vector2 size)
        {
            if (panel == null) return;
            panel.anchoredPosition = center;
            panel.sizeDelta = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
        }

        void Pulse()
        {
            if (_frame == null || !_frame.gameObject.activeSelf) return;
            float s = 1f + Mathf.Sin(Time.unscaledTime * _pulseSpeed) * _pulseScale;
            _frame.localScale = new Vector3(s, s, 1f);
        }
    }
}
