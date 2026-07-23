using System.Collections.Generic;
using Game.Core.Service;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Якорь подсветки туториала: помечает элемент боевого UI как цель (счётчик золота, слот командира,
    /// рука, кнопка «Завершить ход» и т.д.). Сам себя регистрирует в статическом реестре — оверлей
    /// TutorialHighlightView по id находит RectTransform и вырезает вокруг него дырку в затемнении.
    ///
    /// Вешается прямо на боевой UI (ничего не дублируем): один компонент + выбор id из выпадашки.
    /// Дубликат id — предупреждение в лог, в реестре останется последний зарегистрированный.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TutorialAnchor : MonoBehaviour
    {
        [Tooltip("Какую цель туториала обозначает этот элемент.")]
        [SerializeField] private TutorialAnchorId _id = TutorialAnchorId.None;

        static readonly Dictionary<TutorialAnchorId, RectTransform> _registry = new();

        /// <summary>Найти якорь по id. null — якорь не выставлен в сцене (подсветки не будет).</summary>
        public static RectTransform Find(TutorialAnchorId id)
        {
            if (id == TutorialAnchorId.None) return null;
            return _registry.TryGetValue(id, out var rt) && rt != null ? rt : null;
        }

        void OnEnable()
        {
            if (_id == TutorialAnchorId.None)
            {
                Debug.LogWarning($"[TutorialAnchor] '{name}': id не выбран — якорь бесполезен.", this);
                return;
            }
            if (_registry.TryGetValue(_id, out var exists) && exists != null && exists != transform)
                Debug.LogWarning($"[TutorialAnchor] id '{_id}' уже занят объектом '{exists.name}' — перезаписываю на '{name}'.", this);

            _registry[_id] = (RectTransform)transform;
        }

        void OnDisable()
        {
            if (_id == TutorialAnchorId.None) return;
            if (_registry.TryGetValue(_id, out var rt) && rt == (RectTransform)transform)
                _registry.Remove(_id);
        }
    }
}
