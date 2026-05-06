using System; 

namespace AwesomeUI.Interface
{
    public interface IPanel
    {
        bool isOpen { get; }
        void OnOpen(params Action[] onComplete);
        void OnCLose(params Action[] onComplete);
    }
}