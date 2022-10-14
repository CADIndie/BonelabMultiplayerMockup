using BonelabMultiplayerMockup.Object;
using BonelabMultiplayerMockup.Packets;
using BonelabMultiplayerMockup.Packets.Player;
using Discord;
using MelonLoader;

namespace BonelabMultiplayerMockup.Nodes
{
    public class Server : Node
    {
        // Boilerplate connection code, thanks Entanglement.
        
        public static Server instance;


        public Server()
        {
            MakeLobby();
        }

        public static void StartServer()
        {
            if (instance != null)
                return;
            BonelabMultiplayerMockup.PopulateCurrentAvatarData();
            activeNode = instance = new Server();
        }

        public override void UserConnectedEvent(long lobbyId, long userId)
        {
            DiscordIntegration.activity.Party.Size.CurrentSize = 1 + connectedUsers.Count;
            DiscordIntegration.activityManager.UpdateActivity(DiscordIntegration.activity, res => { });

            foreach (var valuePair in DiscordIntegration.byteIds)
            {
                if (valuePair.Value == userId) continue;

                var addMessageData = new ShortIdData
                {
                    userId = valuePair.Value,
                    byteId = valuePair.Key
                };
                var packetByteBuf =
                    PacketHandler.CompressMessage(NetworkMessageType.ShortIdUpdatePacket, addMessageData);
                BroadcastMessage((byte)NetworkChannel.Reliable, packetByteBuf.getBytes());
            }

            var idMessageData = new ShortIdData
            {
                userId = userId,
                byteId = DiscordIntegration.RegisterUser(userId)
            };
            var secondBuff = PacketHandler.CompressMessage(NetworkMessageType.ShortIdUpdatePacket, idMessageData);
            BroadcastMessage((byte)NetworkChannel.Reliable, secondBuff.getBytes());

            var joinCatchupData = new JoinCatchupData
            {
                lastId = SyncedObject.lastId,
                lastGroupId = SyncedObject.lastGroupId
            };
            var catchupBuff = PacketHandler.CompressMessage(NetworkMessageType.IdCatchupPacket, joinCatchupData);
            SendMessage(userId, (byte)NetworkChannel.Reliable, catchupBuff.getBytes());

            
        }

        private void MakeLobby()
        {
            var lobbyTransaction = DiscordIntegration.lobbyManager.GetLobbyCreateTransaction();
            lobbyTransaction.SetCapacity(10);
            lobbyTransaction.SetLocked(false);
            lobbyTransaction.SetType(LobbyType.Private);
            DiscordIntegration.lobbyManager.CreateLobby(lobbyTransaction, onDiscordLobbyCreate);
        }

        private void onDiscordLobbyCreate(Result result, ref Lobby lobby)
        {
            if (result != Result.Ok) return;

            DiscordIntegration.lobby = lobby;

            DiscordIntegration.activity.Party = new ActivityParty
            {
                Id = lobby.Id.ToString(),
                Size = new PartySize { CurrentSize = 1, MaxSize = 10 }
            };
            DiscordIntegration.activity.Details = "Hosting a HBMP server!";
            DiscordIntegration.activity.State = "Playing Bonelab Campaign | Coop!";
            //DiscordIntegration.activity.DetailsCoop = "Hosting a HBMP server!";
            //DiscordIntegration.activity.StateCoop = "Playig Bonelab Story | Coop!";
            DiscordIntegration.activity.Secrets = new ActivitySecrets
            {
                Join = DiscordIntegration.lobbyManager.GetLobbyActivitySecret(lobby.Id)
            };
            DiscordIntegration.UpdateActivity();

            ConnectToDiscordServer();

            DiscordIntegration.lobbyManager.OnNetworkMessage += OnDiscordMessageRecieved;
            DiscordIntegration.lobbyManager.OnMemberConnect += OnDiscordUserJoined;
            DiscordIntegration.lobbyManager.OnMemberDisconnect += OnDiscordUserLeft;
        }

        public void BroadcastMessageExcept(byte channel, byte[] data, long toIgnore)
        {
            connectedUsers.ForEach(user =>
            {
                if (user != toIgnore) SendMessage(user, channel, data);
            });
        }

        public override void BroadcastMessage(byte channel, byte[] data)
        {
            BroadcastMessageP2P(channel, data);
        }

        public void CloseLobby()
        {
            foreach (var byteId in DiscordIntegration.byteIds.Keys)
            {
                var disconnectMessageData = new DisconnectData
                {
                    userId = DiscordIntegration.GetLongId(byteId)
                };

                var packetByteBuf =
                    PacketHandler.CompressMessage(NetworkMessageType.DisconnectPacket, disconnectMessageData);

                instance.BroadcastMessage((byte)NetworkChannel.Reliable, packetByteBuf.getBytes());
            }

            DiscordIntegration.Tick();
            DiscordIntegration.lobbyManager.DeleteLobby(DiscordIntegration.lobby.Id, result => { });
            DiscordIntegration.lobby = new Lobby();

            CleanData();
        }

        public override void UserDisconnectEvent(long lobbyId, long userId)
        {
            DiscordIntegration.activity.Party.Size.CurrentSize = 1 + connectedUsers.Count;
            DiscordIntegration.activityManager.UpdateActivity(DiscordIntegration.activity, res => { });
        }

        public override void Shutdown()
        {
            if (DiscordIntegration.hasLobby && !DiscordIntegration.isHost)
            {
                MelonLogger.Msg("Unable to close the server as a client!");
                return;
            }

            CloseLobby();
            DiscordIntegration.DefaultRichPresence();

            instance = null;
            activeNode = Client.instance;
        }
    }
}