using Sandbox.ModAPI;
using VoxelCubemapApi.Common;
using VRage.Game.Components;

namespace VoxelCubemapApi.Client.Networking
{
    /// <summary>
    /// Owns runtime-sync receiving exclusively on remote clients.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, -999)]
    internal sealed class RuntimeSyncClientSession : MySessionComponentBase
    {
        private RuntimeSyncReceiver _receiver;


        public override void LoadData()
        {
            TryInitialize();
        }


        public override void BeforeStart()
        {
            TryInitialize();
        }


        public override void UpdateBeforeSimulation()
        {
            TryInitialize();

            if (_receiver != null)
                _receiver.Update();
        }


        protected override void UnloadData()
        {
            if (_receiver == null)
                return;

            _receiver.Dispose();
            _receiver = null;
        }


        private void TryInitialize()
        {
            if (_receiver != null ||
                MyAPIGateway.Session == null ||
                MyAPIGateway.Session.IsServer)
            {
                return;
            }

            VoxelCubemapApiServer session =
                VoxelCubemapApiServer.Instance;

            if (session == null)
                return;

            _receiver =
                new RuntimeSyncReceiver(
                    session.Modifications,
                    session.RuntimePackages,
                    () => session.IsUnloading);
        }
    }
}
