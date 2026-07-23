using UnityEngine;

namespace AwesomeUI.Core
{
    /// <summary>
    /// Глобальный обработчик «назад» для мобилок. Автоматически добавляется на UIHandler
    /// (см. UIModule.Initialize) — вручную вешать не нужно.
    ///   • Android — аппаратная/жестовая «назад» приходит как KeyCode.Escape;
    ///   • iOS/тач — свайп от ЛЕВОГО края экрана вправо (системного «назад» в Unity на iOS нет,
    ///     детектим краевой свайп сами).
    /// Оба ведут к UIModule.Back() → предыдущая панель активного канваса (журнал истории в SourceCanvas).
    ///
    /// В корне истории (возвращаться некуда) Back() вернёт false — по флагу можно выйти из приложения.
    /// </summary>
    public class BackGestureHandler : MonoBehaviour
    {
        [Tooltip("Ширина зоны у левого края (в пикселях), с которой начинается свайп «назад».")]
        [SerializeField] private float _edgePixels = 40f;
        [Tooltip("Минимальная длина свайпа вправо (в пикселях), чтобы засчитать «назад».")]
        [SerializeField] private float _minSwipe = 90f;
        [Tooltip("Выйти из приложения, если «назад» в корне (нечего закрывать). Обычно для Android.")]
        [SerializeField] private bool _quitAppAtRoot = false;

        bool _fromEdge;
        Vector2 _start;

        void Update()
        {
            // Android hardware/gesture back → Escape.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                TriggerBack();
                return;
            }

            // Краевой свайп (iOS-подобный «назад»).
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    _fromEdge = t.position.x <= _edgePixels;
                    _start = t.position;
                }
                else if (_fromEdge && t.phase == TouchPhase.Ended)
                {
                    bool horizontal = Mathf.Abs(t.position.y - _start.y) < _minSwipe;
                    if (horizontal && t.position.x - _start.x >= _minSwipe)
                        TriggerBack();
                    _fromEdge = false;
                }
            }
        }

        void TriggerBack()
        {
            bool handled = UIModule.Back();
            if (!handled && _quitAppAtRoot)
                Application.Quit();
        }
    }
}
