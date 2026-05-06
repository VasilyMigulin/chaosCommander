using AwesomeUI.Core;
using AwesomeUI.Feature;
using UnityEngine;
using Game.Core.Shared.Interface;
using Game.Core.Photon;
using System.Threading.Tasks;

namespace Game.Core.States
{
    public class MenuState : State, IMenuStateContext
    {
        Task sessionTask;

        public override void Awake()
        {
            base.Awake();

            PhotonInitializer.Initialize();
        }

        public override void Start()
        {
            UIModule.Open<MainMenuCanvas>();
            UIModule.Inject(this, this);
        }

        public void StartMatchMaking()
        {
            sessionTask = PhotonInitializer.Instance.Matchmaking.FindMatchAsync();
        }

        public override void Update()
        {

        } 
    }
}