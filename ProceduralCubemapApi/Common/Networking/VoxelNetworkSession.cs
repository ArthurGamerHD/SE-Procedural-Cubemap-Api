using System;
using System.Collections.Generic;
using Generated;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace ProceduralCubemapApi.Common.Networking
{
    /// <summary>
    /// Owns the generated ADK transport for the lifetime of this mod session.
    /// </summary>
    internal sealed class VoxelNetworkSession : IDisposable
    {
        // Random, non-mnemonic channel reserved for this API.
        private const ushort NETWORK_PORT = 47629;
        private const int MAX_WIRE_PACKET_BYTES = 16 * 1024;
        private const int MAX_RUNTIME_PAYLOAD_BYTES = 128 * 1024 * 1024;
        internal const int MAX_RUNTIME_IMAGE_BYTES = MAX_RUNTIME_PAYLOAD_BYTES - 1024 * 1024;
        private const int MAX_FRAGMENT_CHUNKS = (MAX_RUNTIME_PAYLOAD_BYTES + MAX_WIRE_PACKET_BYTES - 1) /
                                                MAX_WIRE_PACKET_BYTES +
                                                64;

        private NetworkManager _network;


        internal void Init()
        {
            if (_network != null)
                return;

            NetworkManager network = 
                new NetworkManager(new NetworkParameters(
                    NETWORK_PORT,
                    MAX_WIRE_PACKET_BYTES, 
                    MAX_RUNTIME_PAYLOAD_BYTES, 
                    MAX_FRAGMENT_CHUNKS));

            try
            {
                network.Init();

                _network =
                    network;
            }
            catch
            {
                network.Dispose();

                throw;
            }
        }


        internal void BroadcastToConnectedPlayers(
            NetworkPackage packet)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            if (_network == null)
            {
                throw new InvalidOperationException(
                    "Runtime network session is not initialized.");
            }

            if (MyAPIGateway.Session == null ||
                !MyAPIGateway.Session.IsServer)
            {
                throw new InvalidOperationException(
                    "Only the authoritative server can broadcast runtime sync.");
            }

            var players =
                new List<IMyPlayer>(
                    MyAPIGateway.Session.SessionSettings.MaxPlayers);

            MyAPIGateway.Players.GetPlayers(
                players);

            ulong serverId =
                MyAPIGateway.Multiplayer.ServerId;

            int recipients =
                0;

            for (int index = 0;
                index < players.Count;
                index++)
            {
                IMyPlayer player =
                    players[index];

                if (player == null ||
                    player.IsBot ||
                    player.SteamUserId == serverId)
                {
                    continue;
                }

                try
                {
                    _network.TransmitToPlayer(
                        packet,
                        player.SteamUserId);

                    recipients++;
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[Voxel Cubemap API] Runtime packet " +
                        packet.Id +
                        " could not be sent to player " +
                        player.SteamUserId +
                        ": " +
                        e);
                }
            }

            MyLog.Default.WriteLineAndConsole(
                "[Voxel Cubemap API] Broadcast runtime packet " +
                packet.Id +
                " to " +
                recipients +
                " remote player(s).");
        }


        public void Dispose()
        {
            if (_network == null)
                return;

            _network.Dispose();

            _network =
                null;
        }
    }
}
