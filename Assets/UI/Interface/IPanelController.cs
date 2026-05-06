using System;
using UnityEngine;

namespace AwesomeUI.Interface
{
    public interface IPanelController
    {
        T OpenPanel<T>(params Action[] callback) where T : IPanel;
        T ClosePanel<T>() where T : IPanel;
    }
}
