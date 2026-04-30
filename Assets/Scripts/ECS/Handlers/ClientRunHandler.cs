using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Ecs.Handlers
{
    public class ClientRunHandler : EcsRunHandler
    {
        public ClientRunHandler(IGameStateContext state) : base(state)
        {
        } 
    }
}