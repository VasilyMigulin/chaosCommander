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
        [SerializeField] Color highlightMoveColor = new Color(0.3f, 0.8f, 0.3f, 1f);
        [SerializeField] Color highlightAttackColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        [SerializeField] Color highlightTargetColor = new Color(0.9f, 0.6f, 0.1f, 1f);
        [SerializeField] Color selectedColor = new Color(0.3f, 0.6f, 1f, 1f);

        public int Row { get; private set; }
        public int Col { get; private set; }
        public int OwnerId { get; private set; }

        Color _baseColor;

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
            if (cellRenderer != null)
                cellRenderer.material.color = _baseColor;
        }

        public void SetHighlight(CellHighlight mode)
        {
            if (cellRenderer == null) return;
            cellRenderer.material.color = mode switch
            {
                CellHighlight.Move   => highlightMoveColor,
                CellHighlight.Attack => highlightAttackColor,
                CellHighlight.Target => highlightTargetColor,
                CellHighlight.Select => selectedColor,
                _                    => _baseColor,
            };
        }

        public void SetAvatarPlace()
        {
            _baseColor = avatarColor;
            if (cellRenderer != null)
                cellRenderer.material.color = _baseColor;
        }

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