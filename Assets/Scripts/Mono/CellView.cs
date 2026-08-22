using System.Collections;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Mono
{
    public class CellView : MonoBehaviour
    {
        [SerializeField] Renderer cellRenderer;
        [SerializeField] Color lightColor = Color.white;
        [SerializeField] Color darkColor = Color.black;
        [SerializeField] Color avatarColor = Color.blue;

        [Header("Подсветка — фолбэк подкраской меша")]
        [Tooltip("Используется ТОЛЬКО для режимов, под которые не назначен GameObject в highlightVisuals.")]
        [SerializeField] Color highlightMoveColor = new Color(0.3f, 0.8f, 0.3f, 1f);
        [SerializeField] Color highlightAttackColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        [SerializeField] Color highlightTargetColor = new Color(0.9f, 0.6f, 0.1f, 1f);
        [SerializeField] Color selectedColor = new Color(0.3f, 0.6f, 1f, 1f);
        [SerializeField] Color readyColor = new Color(0.95f, 0.85f, 0.35f, 1f);

        // ── СЛОЙ 1 — ситуативная подсветка (ход/атака/выбор/цель). Один режим за раз, живёт до
        // следующего SetHighlight/ClearAllHighlights. Раньше это была подкраска материала; теперь —
        // включение/выключение отдельных GameObject'ов (рамки, свечения, декали), чтобы «красоту»
        // можно было рисовать художнику, а не подбирать цветом меша. ──
        [Header("Подсветка — визуалы (GameObject на клетке)")]
        [Tooltip("Один GameObject (или несколько) на режим подсветки. Режим без назначенного объекта " +
                 "падает на фолбэк-подкраску меша — можно мигрировать по одному.")]
        [SerializeField] HighlightVisual[] highlightVisuals;

        // ── СЛОЙ 2 — «этим существом ещё можно походить». НЕЗАВИСИМ от слоя 1: ClearAllHighlights
        // дёргают из шести систем на каждый клик/каст, и любой ситуативный слот такую подсветку бы
        // стирал. Показывается, только когда слой 1 пуст (под выбором/целью не мельтешит).
        // Ведёт CellReadyHighlightSystem. ──
        [Header("Подсветка — «ещё можно ходить»")]
        [SerializeField] GameObject readyVisual;

        // ── Мисклик при выборе цели способности (пустая клетка / не подходящее существо) — короткая
        // красная вспышка ПОВЕРХ текущей подсветки (слой 1 не трогаем: способность продолжает ждать
        // валидный клик, Target-подсветка остаётся как была). Отдельный слой специально НЕЗАВИСИМ от
        // _mode/SetHighlight — RunAbilityTargetSelectionSystem.ShowHighlights не перевызывается на каждый
        // клик, так что мигание не рискует быть затёртым/оборванным гонкой с обычной подсветкой.
        [Header("Мисклик при выборе цели — красная вспышка")]
        [Tooltip("GameObject, который мигает при невалидном клике во время выбора цели (Selected). Не " +
                 "назначен → фолбэк быстрым покраснением меша (тот же Renderer, что и обычная подсветка).")]
        [SerializeField] GameObject invalidFlashVisual;
        [SerializeField] Color invalidFlashColor = new Color(1f, 0.15f, 0.15f, 1f);

        Coroutine _flashRoutine;

        // ── СЛОЙ 0 — обычный вид клетки (декор в покое). Антагонист подсветок: горит, пока НИ ОДНА
        // подсветка (слой 1 или слой 2) не активна, и гаснет под любой из них — рамки/свечения рисуются
        // не поверх декора, а вместо него. Включается обратно сам, как только всё погасло. ──
        [Header("Обычный вид (без подсветок)")]
        [SerializeField] GameObject defaultView;

        [Header("Creature Fit Bounds (автоскейл моделей под клетку)")]
        [Tooltip("Bounding box, в который вписывается модель существа при спавне (SpawnCreatureViewSystem → " +
                 "CreatureView.FitToBounds): footprint X/Z + допустимая высота Y. Отдельный коллайдер ОТ " +
                 "клик-хитбокса выше (у того Y — тонкая плита под клик, для роста модели не годится) — " +
                 "просто гизмо для художника, в физике/кликах не участвует. Не назначен → автоскейл " +
                 "выключен для этой клетки, модель остаётся в масштабе префаба.")]
        [SerializeField] BoxCollider _fitBounds;

        /// <summary>Мировые баунды бокса подгонки (см. _fitBounds); null — гизмо не назначен на этой клетке.</summary>
        public Bounds? FitBounds => _fitBounds != null ? _fitBounds.bounds : (Bounds?)null;

        public int Row { get; private set; }
        public int Col { get; private set; }
        public int OwnerId { get; private set; }

        Color _baseColor;
        CellHighlight _mode = CellHighlight.None;
        bool _ready;

        // === struct (данные инспектора) === режим подсветки → его визуал.
        // Массивом, а не полем-на-режим: новые режимы добавляются в enum без правки этого класса,
        // и на один режим можно повесить несколько объектов (рамка + партиклы).
        [System.Serializable]
        public struct HighlightVisual
        {
            public CellHighlight Mode;
            public GameObject Root;
        }

        // ── Удержание → карточка-инспектор существа на этой клетке (та же логика, что раньше ошибочно
        // висела на CreatureView — существа НЕкликабельны в этом проекте, кликабельны только клетки, клетка
        // сама знает Row/Col/OwnerId). Короткий тап (hold не сработал) — обычный CellSelectedEvent, как раньше,
        // просто перенесённый с OnMouseDown на OnMouseUp, чтобы успеть отличить тап от удержания. ──
        [SerializeField] float _holdThreshold = 0.45f;

        bool _pressed;
        bool _holdFired;
        float _pressTime;

        void Update()
        {
            if (!_pressed || _holdFired) return;
            if (Time.unscaledTime - _pressTime < _holdThreshold) return;

            _holdFired = true;
            GameEventBus.Publish(new CreatureHoldUIEvent { Row = Row, Col = Col, OwnerId = OwnerId, Show = true });
        }

        public void SetCoords(int row, int col, int ownerId)
        {
            Row = row;
            Col = col;
            OwnerId = ownerId;
        }

        public void SetChessColor(bool isLight)
        {
            if (cellRenderer == null)
                cellRenderer = GetComponent<Renderer>();

            _baseColor = isLight ? lightColor : darkColor;
            ApplyVisuals();
        }

        /// <summary>Ситуативная подсветка (слой 1): ход/атака/выбор/цель. Слой «можно ходить» не трогает.</summary>
        public void SetHighlight(CellHighlight mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            ApplyVisuals();
        }

        /// <summary>Слой 2: на клетке существо, которым ещё можно походить в этом ходу.
        /// ПЕРЕЖИВАЕТ ClearAllHighlights — снимается только этим методом (CellReadyHighlightSystem).</summary>
        public void SetReady(bool on)
        {
            if (_ready == on) return;
            _ready = on;
            ApplyVisuals();
        }

        /// <summary>Мисклик во время выбора цели способности: короткая красная вспышка (2 мигания по
        /// умолчанию), НЕ отменяет текущую подсветку (слой 1) — только сигналит «сюда нельзя», ожидание
        /// цели продолжается. Повторный вызов до завершения предыдущей вспышки перезапускает её.</summary>
        public void FlashInvalid(int blinks = 2, float interval = 0.09f)
        {
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine(blinks, interval));
        }

        IEnumerator FlashRoutine(int blinks, float interval)
        {
            for (int i = 0; i < blinks; i++)
            {
                SetFlash(true);
                yield return new WaitForSecondsRealtime(interval);
                SetFlash(false);
                yield return new WaitForSecondsRealtime(interval);
            }
            _flashRoutine = null;
        }

        void SetFlash(bool on)
        {
            if (invalidFlashVisual != null) { invalidFlashVisual.SetActive(on); return; }
            if (cellRenderer == null) return;
            if (on) cellRenderer.material.color = invalidFlashColor;
            else    ApplyVisuals();   // вернуть цвет, который реально должен сейчас гореть (_mode/_ready)
        }

        public void SetAvatarPlace()
        {
            _baseColor = avatarColor;
            ApplyVisuals();
        }

        void ApplyVisuals()
        {
            // Слой 2 виден, только когда слой 1 пуст: под рамкой хода/атаки/цели он был бы визуальным шумом.
            bool readyOn = _ready && _mode == CellHighlight.None;

            bool modeHasRoot = false;
            if (highlightVisuals != null)
                foreach (var v in highlightVisuals)
                {
                    if (v.Root == null) continue;
                    bool on = v.Mode == _mode && _mode != CellHighlight.None;
                    if (on) modeHasRoot = true;
                    if (v.Root.activeSelf != on) v.Root.SetActive(on);
                }

            bool readyHasRoot = readyVisual != null;
            if (readyHasRoot && readyVisual.activeSelf != readyOn) readyVisual.SetActive(readyOn);

            // Слой 0: обычный вид виден, только когда не горит НИ ОДНА подсветка. Считаем по состоянию
            // слоёв (_mode/readyOn), а не по наличию Root'ов: у режима без объекта работает фолбэк-подкраска
            // меша — это тоже «подсветка включилась», декор должен уступить и ей.
            if (defaultView != null)
            {
                bool defaultOn = _mode == CellHighlight.None && !readyOn;
                if (defaultView.activeSelf != defaultOn) defaultView.SetActive(defaultOn);
            }

            // ФОЛБЭК подкраской меша — чтобы подсветка работала на непереведённом префабе (и пока художник
            // рисует объекты). Как только под режим назначен GameObject, меш остаётся базового цвета.
            if (cellRenderer == null) return;
            if (readyOn && !readyHasRoot) { cellRenderer.material.color = readyColor; return; }
            cellRenderer.material.color = modeHasRoot ? _baseColor : ColorOf(_mode);
        }

        Color ColorOf(CellHighlight mode) => mode switch
        {
            CellHighlight.Move   => highlightMoveColor,
            CellHighlight.Attack => highlightAttackColor,
            CellHighlight.Target => highlightTargetColor,
            CellHighlight.Select => selectedColor,
            _                    => _baseColor,
        };

        void OnMouseDown()
        {
            _pressed = true;
            _holdFired = false;
            _pressTime = Time.unscaledTime;
        }

        // Короткий тап (hold не сработал) — обычный CellSelectedEvent (выбор/атака/движение), как раньше —
        // просто перенесён с OnMouseDown на OnMouseUp. Если hold сработал — жест уже «использован»
        // инспектором, обычный клик не шлём.
        void OnMouseUp()
        {
            if (!_pressed) return;
            _pressed = false;

            if (_holdFired)
            {
                GameEventBus.Publish(new CreatureHoldUIEvent { Row = Row, Col = Col, OwnerId = OwnerId, Show = false });
                return;
            }

            GameEventBus.Publish(new CellSelectedEvent { Row = Row, Col = Col, OwnerId = OwnerId });
        }

        // Палец увели с клетки, не отпуская — считаем как отпускание, иначе удержание может «залипнуть».
        void OnMouseExit()
        {
            if (!_pressed) return;
            _pressed = false;
            if (_holdFired)
                GameEventBus.Publish(new CreatureHoldUIEvent { Row = Row, Col = Col, OwnerId = OwnerId, Show = false });
        }
    }

    public enum CellHighlight { None, Move, Attack, Select, Target }
}
