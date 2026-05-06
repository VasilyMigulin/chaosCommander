using UnityEngine;

namespace AwesomeUI.Core.Window
{
    public abstract class SourceWindow : MonoBehaviour
    {
        [SerializeField] protected bool IsOpenOnInit;

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