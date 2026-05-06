using UnityEngine;
using SourceSlot = AwesomeUI.Core.Slot.SourceSlot;

namespace AwesomeUI.Core.Layout
{
    public abstract class SourceLayout : MonoBehaviour
    {
        [SerializeField] protected bool IsOpen; 
        protected SourceSlot[] _slots;

        public virtual SourceLayout Init()
        { 
            var slots = GetComponentsInChildren<SourceSlot>(true);
            _slots = new SourceSlot[slots.Length];

            for (int i = 0; i < slots.Length; i++) _slots[i] = slots[i].Init();

            gameObject.SetActive(IsOpen);

            return this;
        }

        public virtual void OnInject()
        {

        }

        public virtual void ResetSlots()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].UpdateView();
            }
        } 

        public virtual void OnOpen()
        {
            gameObject.SetActive(true);
        }

        public virtual void OnClose()
        {
            gameObject.SetActive(false);
        }

        public virtual void Dispose()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Dispose();
            }
        }
    }
}