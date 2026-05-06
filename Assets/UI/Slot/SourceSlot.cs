using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Core.Slot
{
    abstract public class SourceSlot : MonoBehaviour
    {
        public bool IsButton;
        public bool IsAnimation;
        public bool EnableIcon = true;
        protected Image _icon;
        protected Image _background; 
        protected Button _btnClick;
        protected Animation _loading;
        public virtual SourceSlot Init()
        {  
            if (IsButton)
            {
                if (TryGetComponent<Button>(out var button))
                {
                    _btnClick = button;
                }
                else
                {
                    _btnClick = gameObject.AddComponent<Button>();
                }

                _btnClick.onClick.AddListener(OnClick);
            }

            if (EnableIcon && transform.childCount > 0)
            {
                var child = transform.GetChild(0);
                if (child.TryGetComponent<Image>(out var image))
                {
                    _icon = image;
                    _icon.enabled = true;
                }
            }

            if (IsAnimation)
            {
                if (!_loading && !TryGetComponent(out _loading))
                    _loading = gameObject.AddComponent<Animation>();
            }
            _background = GetComponent<Image>();

            return this;
        }
        public abstract void Unject();

        public virtual void OnInject()
        {

        }
        public abstract void OnActive();
        public abstract void OnClick();
        public abstract void UpdateView();
        public virtual void Dispose()
        {
            _btnClick?.onClick.RemoveListener(OnClick);
        }
        public virtual void Close()
        {
            gameObject.SetActive(false);
        }
    }
}