using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Авто-расстановка и выбор камер боя.
    /// Обе камеры ставятся симметрично относительно центра доски и гарантированно смотрят в центр:
    /// горизонтальное смещение <see cref="distance"/> к своей стороне, высота <see cref="height"/>,
    /// поле зрения <see cref="fieldOfView"/>. Затем включается камера ЛОКАЛЬНОЙ стороны, вторая гаснет.
    ///
    /// Настройка: повесить на любой объект BattleScene, назначить обе камеры. Позиции в сцене вручную
    /// выставлять НЕ нужно — они перезаписываются кодом (одинаково у обоих игроков).
    /// </summary>
    public class BattleCameraSelector : MonoBehaviour
    {
        [Header("Cameras")]
        [SerializeField] private Camera side1Camera;
        [SerializeField] private Camera side2Camera;

        /// <summary>Камера ЛОКАЛЬНОЙ стороны (выбрана в Select). Биллборды (AvatarPlayerView) берут её
        /// отсюда, а не Camera.main — иначе на 2-м клиенте кэшат Side1 (активную по умолчанию) до выбора.</summary>
        public static Camera ActiveCamera { get; private set; }

        [Header("Board (опционально — иначе ищется в сцене)")]
        [SerializeField] private BoardView boardView;

        [Header("Placement")]
        [SerializeField] private float distance    = 5f;   // горизонт. отступ от центра к своей стороне
        [SerializeField] private float height      = 10f;  // высота над доской
        [SerializeField] private float fieldOfView = 60f;

        [Header("Manual Override (вкл. → Position/Rotation/FOV ниже подставляются как есть, height/" +
                 "distance/LookAt выше игнорируются). Крутить в Play Mode на самой камере и переносить сюда " +
                 "числа — быстрее, чем подбирать через height/distance.")]
        [SerializeField] private bool useManualPlacement = false;
        [SerializeField] private Vector3 manualPositionSide1 = new Vector3(0f, 10f, -5f);
        [SerializeField] private Vector3 manualRotationSide1 = new Vector3(60f, 0f, 0f);
        [Tooltip("По умолчанию — зеркало Side1 относительно центра доски (Z инвертирован, Y-поворот +180°). " +
                 "Можно переопределить независимо, если у второй стороны свой рабочий стол/асимметрия.")]
        [SerializeField] private bool mirrorSide2 = true;
        [SerializeField] private Vector3 manualPositionSide2 = new Vector3(0f, 10f, 5f);
        [SerializeField] private Vector3 manualRotationSide2 = new Vector3(60f, 180f, 0f);

        private void Awake()
        {
            // До выбора держим активной только одну камеру, иначе два AudioListener спамят варнингами.
            if (side2Camera != null) side2Camera.gameObject.SetActive(false);
            if (side1Camera != null) side1Camera.gameObject.SetActive(true);

            GameEventBus.Subscribe<PlayerAssignedEvent>(OnPlayerAssigned);
        }

        private void OnDestroy()
        {
            GameEventBus.Unsubscribe<PlayerAssignedEvent>(OnPlayerAssigned);
        }

        private void OnPlayerAssigned(PlayerAssignedEvent evt)
        {
            if (!evt.IsLocalPlayer) return;   // нас интересует только сторона ЛОКАЛЬНОГО игрока

            PlaceCameras();
            Select(evt.Side);
        }

        /// <summary>Ставит обе камеры. Manual override вкл. → берёт готовые Position/Rotation/FOV как есть
        /// (BoardView не нужен вовсе); иначе — симметрично относительно центра доски и LookAt, как раньше.</summary>
        private void PlaceCameras()
        {
            if (useManualPlacement)
            {
                PlaceManual(side1Camera, manualPositionSide1, manualRotationSide1);

                // mirrorSide2: Z инвертирован (сторона 2 — с другого края доски), Y-поворот +180° (лицом
                // навстречу), наклон/высота (X/Z-поворот, Y-позиция) те же — тот же принцип, что и
                // CreatureView.SetOwnerFacing (ownerId==2 → 180°).
                Vector3 pos2 = mirrorSide2
                    ? new Vector3(manualPositionSide1.x, manualPositionSide1.y, -manualPositionSide1.z)
                    : manualPositionSide2;
                Vector3 rot2 = mirrorSide2
                    ? new Vector3(manualRotationSide1.x, manualRotationSide1.y + 180f, manualRotationSide1.z)
                    : manualRotationSide2;
                PlaceManual(side2Camera, pos2, rot2);
                return;
            }

            if (boardView == null) boardView = FindObjectOfType<BoardView>();
            if (boardView == null)
            {
                Debug.LogWarning("[BattleCameraSelector] BoardView не найден — камеры не расставлены");
                return;
            }

            Vector3 center = boardView.BoardCenter;

            // Сторона 1 — со стороны меньших Z, сторона 2 — со стороны больших Z (зеркально).
            Place(side1Camera, center, -distance);
            Place(side2Camera, center, +distance);
        }

        private void Place(Camera cam, Vector3 center, float zOffset)
        {
            if (cam == null) return;

            cam.transform.position = center + new Vector3(0f, height, zOffset);
            cam.transform.LookAt(center, Vector3.up);   // гарантированно смотрит в центр доски
            cam.fieldOfView = fieldOfView;
        }

        private void PlaceManual(Camera cam, Vector3 position, Vector3 eulerRotation)
        {
            if (cam == null) return;
            cam.transform.position = position;
            cam.transform.rotation = Quaternion.Euler(eulerRotation);
            cam.fieldOfView = fieldOfView;
        }

        private void Select(int side)
        {
            bool useSide1 = side == 1;

            if (side1Camera != null) side1Camera.gameObject.SetActive(useSide1);
            else Debug.LogWarning("[BattleCameraSelector] Side1Camera не назначена");

            if (side2Camera != null) side2Camera.gameObject.SetActive(!useSide1);
            else Debug.LogWarning("[BattleCameraSelector] Side2Camera не назначена");

            ActiveCamera = useSide1 ? side1Camera : side2Camera;   // биллборды берут локальную камеру отсюда

            Debug.Log($"[BattleCameraSelector] Локальная сторона={side} → камера {(useSide1 ? "Side1" : "Side2")}");
        }
    }
}
