using Sandbox.ModAPI;

using System;

using VRage.Utils;
using VoxelCubemapApi.Api;

using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;

namespace VoxelCubemapApi.Server.Api
{
    /// <summary>
    /// Publishes the delegate API over Space Engineers local mod messages.
    /// </summary>
    internal sealed class VoxelCubemapIntermodApiServer
    {
        private static readonly Version ApiVersion =
            new Version(0, 5, 0);

        private readonly ApiData m_api;
        private bool m_registered;


        public VoxelCubemapIntermodApiServer(
            Func<long, ApiData> getModificationTemplate)
        {
            if (getModificationTemplate == null)
            {
                throw new ArgumentNullException(
                    "getModificationTemplate");
            }


            m_api =
                new ApiData
                {
                    {
                        "GetApiVersion",
                        new Func<Version>(
                            GetApiVersion)
                    },
                    {
                        "GetModificationTemplate",
                        getModificationTemplate
                    }
                };
        }


        public void Register()
        {
            if (m_registered)
                return;

            MyAPIGateway.Utilities.RegisterMessageHandler(
                VoxelCubemapApiClient.RegistrationChannel,
                OnApiRequest);

            m_registered =
                true;
        }


        public void Close()
        {
            if (!m_registered)
                return;

            MyAPIGateway.Utilities.UnregisterMessageHandler(
                VoxelCubemapApiClient.RegistrationChannel,
                OnApiRequest);

            m_registered =
                false;
        }


        private static Version GetApiVersion()
        {
            return ApiVersion;
        }


        private void OnApiRequest(
            object payload)
        {
            if (!(payload is long))
                return;

            long replyChannel =
                (long)payload;

            if (replyChannel ==
                VoxelCubemapApiClient.RegistrationChannel)
            {
                return;
            }


            try
            {
                // Mod messages share objects by reference. Return a per-client
                // dictionary so the server's delegate table cannot be mutated.
                MyAPIGateway.Utilities.SendModMessage(
                    replyChannel,
                    new ApiData(
                        m_api));
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[Voxel Cubemap API] Failed to reply on channel " +
                    replyChannel +
                    ": " +
                    e);
            }
        }
    }
}
