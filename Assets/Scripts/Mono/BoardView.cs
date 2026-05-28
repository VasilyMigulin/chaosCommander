using UnityEngine;
using System.Collections.Generic;

namespace Game.Core.Mono
{
    public class BoardView : MonoBehaviour
    {
        [SerializeField] int width = 5;
        [SerializeField] int height = 2;
        [SerializeField] float cellSize = 1f;
        [SerializeField] CellView cellPrefab;
        [SerializeField] AvatarPlayerView avatarPlayerPrefab;

        // row=0..1, col=0..4, ownerId (1 or 2)
        readonly Dictionary<(int row, int col, int owner), CellView> _cellMap
            = new Dictionary<(int, int, int), CellView>();

        // отдельные клетки для аватаров
        readonly Dictionary<int, CellView> _avatarCells
            = new Dictionary<int, CellView>();

        // инстансы аватаров игроков
        readonly Dictionary<int, AvatarPlayerView> _avatarViews
            = new Dictionary<int, AvatarPlayerView>();

        void Awake()
        {
            BuildBoard();
        }

        void BuildBoard()
        {
            // Player 1 side: owner=1, z = 0..1
            // Player 2 side: owner=2, z = 3..4 (mirrored so row 0 is front)
            for (int owner = 1; owner <= 2; owner++)
            {
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        float zBase = owner == 1 ? 0f : (height + 1) * cellSize;
                        float zDir = owner == 1 ? 1f : -1f;

                        Vector3 pos = new Vector3(
                            col * cellSize,
                            0.1f,
                            zBase + row * cellSize * zDir
                        );

                        CellView cell = Instantiate(cellPrefab, pos, Quaternion.identity, transform);

                        cell.name = $"Cell_P{owner}_R{row}_C{col}";
                        cell.SetChessColor((col + row) % 2 == 0);
                        cell.SetCoords(row, col, owner);

                        _cellMap[(row, col, owner)] = cell;
                    }
                }

                CreateAvatarCell(owner);
            }
        }

        void CreateAvatarCell(int owner)
        {
            float centerX = ((width - 1) * cellSize) * 0.5f;

            // позиция перед первым рядом игрока
            float z;

            if (owner == 1)
            {
                z = -cellSize;
            }
            else
            {
                z = (height + 1) * cellSize + cellSize;
            }

            Vector3 pos = new Vector3(centerX, 0.1f, z);

            CellView avatarCell = Instantiate(
                cellPrefab,
                pos,
                Quaternion.identity,
                transform
            );

            avatarCell.name = $"AvatarCell_P{owner}";

            // можно выбрать любой цвет
            avatarCell.SetChessColor(false);

            // специальные координаты
            avatarCell.SetCoords(-1, -1, owner);

            // помечаем как место аватара
            avatarCell.SetAvatarPlace();

            _avatarCells[owner] = avatarCell;

            SpawnAvatarView(owner, avatarCell.transform.position);
        }

        void SpawnAvatarView(int owner, Vector3 cellPosition)
        {
            if (avatarPlayerPrefab == null)
            {
                // Префаб не назначен — создаём пустой GameObject-заглушку
                var go = new GameObject($"AvatarPlayer_P{owner}");
                go.transform.SetParent(transform, false);
                go.transform.position = cellPosition;
                var view = go.AddComponent<AvatarPlayerView>();
                view.Init(owner);
                _avatarViews[owner] = view;
                return;
            }

            var avatar = Instantiate(avatarPlayerPrefab, cellPosition, Quaternion.identity, transform);
            avatar.Init(owner);
            _avatarViews[owner] = avatar;
        }

        public CellView GetCell(int row, int col, int ownerId)
        {
            _cellMap.TryGetValue((row, col, ownerId), out var cell);
            return cell;
        }

        public CellView GetAvatarCell(int ownerId)
        {
            _avatarCells.TryGetValue(ownerId, out var cell);
            return cell;
        }

        public AvatarPlayerView GetAvatarView(int ownerId)
        {
            _avatarViews.TryGetValue(ownerId, out var view);
            return view;
        }

        public void ClearAllHighlights(int ownerId)
        {
            foreach (var kv in _cellMap)
            {
                if (kv.Key.owner == ownerId)
                    kv.Value.SetHighlight(CellHighlight.None);
            }
        }
    }
}