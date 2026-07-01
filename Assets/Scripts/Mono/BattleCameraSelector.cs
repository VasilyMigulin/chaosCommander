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

        /// <summary>Ставит обе камеры симметрично относительно центра доски и направляет в центр.</summary>
        private void PlaceCameras()
        {
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
