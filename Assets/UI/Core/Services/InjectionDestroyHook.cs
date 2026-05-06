using System;
using UnityEngine;

namespace AwesomeUI.Service
{
    public class InjectionDestroyHook : MonoBehaviour
    {
        public Action OnDestroyed;

        private void OnDestroy()
        {
            OnDestroyed?.Invoke();
        }
    }
}