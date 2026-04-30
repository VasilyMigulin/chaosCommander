using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Ecs.Handlers
{
    public class ServerRunHandler : EcsRunHandler
    {
        public ServerRunHandler(IGameStateContext state) : base(state)
        {
        } 
    }
}