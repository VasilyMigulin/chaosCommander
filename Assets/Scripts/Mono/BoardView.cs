using UnityEngine;
using System.Collections.Generic;

namespace Game.Core.Mono
{
    public class BoardView : MonoBehaviour
    {
        [SerializeField] int width = 5;
        [SerializeField] int height = 2;
        [SerializeField] float cellSize = 1f;

        /// <summary>Колонок на доске одного игрока.</summary>
        public int Width  => width;
        /// <summary>Рядов на доске одного игрока.</summary>
        public int Height => height;
        /// <summary>Мировой размер клетки.</summary>
        public float CellSize => cellSize;
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

            // Разворот лицом к полю. Owner 1 стоит у низких Z и смотрит на поле (+Z) уже при identity;
            // owner 2 стоит у высоких Z, поэтому его модель надо развернуть на 180° — иначе она спавнилась
            // как у owner 1 (identity) и стояла СПИНОЙ к доске (баг «аватар 2-го клиента смотрит не туда»).
            // Доска общая (не зеркалится), поэтому разворот — мировой факт, одинаковый на обоих клиентах.
            // Билборд статов (_statsRoot) выставляется в LateUpdate в МИРОВЫХ координатах → на него не влияет.
            Quaternion facing = owner == 1 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

            var avatar = Instantiate(avatarPlayerPrefab, cellPosition, facing, transform);
            avatar.Init(owner);
            _avatarViews[owner] = avatar;
        }

        /// <summary>Высота плоскости доски (клетки стоят на y=0.1).</summary>
        public float BoardY => 0.1f;

        /// <summary>Точка пересечения луча камеры (через экранную позицию пойнтера) с плоскостью доски.
        /// Для драг-размещения существа: куда «показывает палец» на поле.</summary>
        public bool TryBoardPoint(Camera cam, Vector2 screen, out Vector3 point)
        {
            point = default;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, BoardY, 0f));
            if (!plane.Raycast(ray, out float enter)) return false;
            point = ray.GetPoint(enter);
            return true;
        }

        /// <summary>Точка в пределах игрового поля (прямоугольник по клеткам, + margin в мировых единицах).
        /// Нужно драг-превью: плоскость в TryBoardPoint бесконечна, «над полем» решаем границами клеток.</summary>
        public bool IsWithinBoard(Vector3 point, float margin = 0.5f)
        {
            if (_cellMap.Count == 0) return false;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var kv in _cellMap)
            {
                var p = kv.Value.transform.position;
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            float half = cellSize * 0.5f + margin;
            return point.x >= minX - half && point.x <= maxX + half
                && point.z >= minZ - half && point.z <= maxZ + half;
        }

        /// <summary>Ближайшая по X клетка фронт-ряда (row 0) стороны ownerSide к мировой точке. -1 если нет.</summary>
        public int NearestFrontCol(int ownerSide, Vector3 worldPoint)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int col = 0; col < width; col++)
            {
                var cell = GetCell(0, col, ownerSide);
                if (cell == null) continue;
                float d = Mathf.Abs(cell.transform.position.x - worldPoint.x)
                        + Mathf.Abs(cell.transform.position.z - worldPoint.z) * 0.25f;
                if (d < bestD) { bestD = d; best = col; }
            }
            return best;
        }

        /// <summary>Мировой центр игрового поля (усреднение позиций всех клеток, без аватар-клеток).</summary>
        public Vector3 BoardCenter
        {
            get
            {
                if (_cellMap.Count == 0) return transform.position;
                Vector3 sum = Vector3.zero;
                foreach (var kv in _cellMap) sum += kv.Value.transform.position;
                return sum / _cellMap.Count;
            }
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

        /// <summary>Снять подсветку со ВСЕХ клеток обеих сторон.</summary>
        public void ClearAllHighlights()
        {
            foreach (var kv in _cellMap)
                kv.Value.SetHighlight(CellHighlight.None);
        }
    }
}