using Generated;
using ProceduralCubemapApi.Common.Networking;
using RuntimeImageSync = ProceduralCubemapApi.Common.Networking.RuntimeImageSync;
using RuntimeOperationSync = ProceduralCubemapApi.Common.Networking.RuntimeOperationSync;

namespace ProceduralCubemapApi.Client.Networking
{
    internal static class RuntimeSyncCallbacks
    {
        [NetworkCallback(typeof(RuntimeOperationSync),
            NetworkCallbackFilter.FromServer |
            NetworkCallbackFilter.IsClient)]
        internal static void OnRuntimeOperation(
            RuntimeOperationSync packet)
        {
            RuntimeSyncReceiver receiver =
                RuntimeSyncReceiver.Instance;

            if (receiver != null)
                receiver.Enqueue(packet);
        }


        [NetworkCallback(
            typeof(RuntimeImageSync),
            NetworkCallbackFilter.FromServer |
            NetworkCallbackFilter.IsClient)]
        internal static void OnRuntimeImages(
            RuntimeImageSync packet)
        {
            RuntimeSyncReceiver receiver =
                RuntimeSyncReceiver.Instance;

            if (receiver != null)
                receiver.Enqueue(packet);
        }


        [NetworkCallback(
            typeof(RuntimeRevisionDecision),
            NetworkCallbackFilter.FromServer |
            NetworkCallbackFilter.IsClient)]
        internal static void OnRuntimeRevisionDecision(
            RuntimeRevisionDecision packet)
        {
            RuntimeSyncReceiver receiver =
                RuntimeSyncReceiver.Instance;

            if (receiver != null)
                receiver.Enqueue(packet);
        }
    }
}
