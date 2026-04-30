using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Handlers
{
    public abstract class EcsRunHandler
    {
        public EcsWorld World;
        public bool IsRun;
        protected EcsSystems _delSystems;
        protected EcsSystems _generalSystems;

        protected EcsData _data;
        protected List<EcsSystems> _allSystems;
        public EcsRunHandler(IGameStateContext state)
        {
            World = new EcsWorld();
            _data = new EcsData();
            _allSystems = new List<EcsSystems>();
            _generalSystems = new EcsSystems(World, state);
             

#if UNITY_EDITOR
            _generalSystems.Add(new Leopotam.EcsLite.UnityEditor.EcsWorldDebugSystem());
#endif
            //_allSystems;
        }

        public virtual void Init(params object[] injectData)
        {
            for (int i = 0; i < _allSystems.Count; i++)
            {
                var systems = _allSystems[i];
                systems.Inject(injectData);
                systems.Init();
            }

            _systemsCount = _allSystems.Count;
        }

        protected int _systemsCount;

        public virtual void Run()
        {
            for (int i = 0; i < _systemsCount; i++)
            {
                _allSystems[i].Run();
            }
        }
        public virtual void LateRun()
        {

        }
        public virtual void FixedRun()
        {

        }

        public virtual void Dispose()
        {
            _allSystems.ForEach(_x => _x.Destroy()); 
            World.Destroy();
            World = null;
        }

        public static EcsRunHandler Create(IGameStateContext context)
        {
            return context.IsServer ? new ServerRunHandler(context) : new ClientRunHandler(context);
        }
    }

    public class EcsData
    {

    }  
}