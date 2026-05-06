using UnityEngine;
using Game.Core.Events;

namespace Game.Core.Mono
{
    public class CellView : MonoBehaviour
    {
        [SerializeField] Renderer cellRenderer;
        [SerializeField] Color lightColor = Color.white;
        [SerializeField] Color darkColor = Color.black;
        [SerializeField] Color highlightMoveColor = new Color(0.3f, 0.8f, 0.3f, 1f);
        [SerializeField] Color highlightAttackColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        [SerializeField] Color selectedColor = new Color(0.3f, 0.6f, 1f, 1f);

        public int Row { get; private set; }
        public int Col { get; private set; }
        public int OwnerId { get; private set; }

        Color _baseColor;

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
                CellHighlight.Select => selectedColor,
                _                    => _baseColor,
            };
        }

        void OnMouseDown()
        {
            GameEventBus.Publish(new CellSelectedEvent { Row = Row, Col = Col, OwnerId = OwnerId });
        }
    }

    public enum CellHighlight { None, Move, Attack, Select }
}