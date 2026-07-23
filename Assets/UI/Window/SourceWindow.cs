using UnityEngine;

namespace AwesomeUI.Core.Window
{
    public abstract class SourceWindow : MonoBehaviour
    {
        [SerializeField] protected bool IsOpenOnInit;

        [Tooltip("Id окна для механизмов по id (напр. скрытие HUD). Пусто → имя класса.")]
        [SerializeField] private string _windowId;

        /// <summary>Id окна (пусто → имя класса). Аналог SourcePanel.PanelId — для поиска окна по id.</summary>
        public string WindowId => string.IsNullOrEmpty(_windowId) ? GetType().Name : _windowId;

        public virtual SourceWindow Init()
        {
            gameObject.SetActive(IsOpenOnInit);
            return this;
        }

        public virtual void OnInject() { }
        public abstract void Unject();

        public virtual void OnOpen()
        {
            gameObject.SetActive(true);
        }

        public virtual void OnClose()
        {
            gameObject.SetActive(false);
        }

        public virtual void Dispose() { }
    }
}