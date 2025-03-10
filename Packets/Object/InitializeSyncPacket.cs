using System;
using System.Collections.Generic;
using BonelabMultiplayerMockup.Nodes;
using BonelabMultiplayerMockup.Object;
using BonelabMultiplayerMockup.Packets.Player;
using BonelabMultiplayerMockup.Patches;
using BonelabMultiplayerMockup.Utils;
using UnityEngine;

namespace BonelabMultiplayerMockup.Packets.Object
{
    public class InitializeSyncPacket : NetworkPacket
    {
        private static List<ushort> groupIdsQueued = new List<ushort>();

        public override PacketByteBuf CompressData(MessageData messageData)
        {
            var initializeSyncData = (InitializeSyncData)messageData;
            var packetByteBuf = new PacketByteBuf();
            packetByteBuf.WriteByte(DiscordIntegration.GetByteId(initializeSyncData.userId));
            packetByteBuf.WriteUShort(initializeSyncData.objectId);
            packetByteBuf.WriteUShort(initializeSyncData.finalId);
            packetByteBuf.WriteUShort(initializeSyncData.groupId);
            packetByteBuf.WriteString(initializeSyncData.objectName + ";" + initializeSyncData.barcode);
            packetByteBuf.create();

            return packetByteBuf;
        }

        public override void ReadData(PacketByteBuf packetByteBuf, long sender)
        {
            var userId = DiscordIntegration.GetLongId(packetByteBuf.ReadByte());
            var objectId = packetByteBuf.ReadUShort();
            var finalId = packetByteBuf.ReadUShort();
            var groupId = packetByteBuf.ReadUShort();
            var split = packetByteBuf.ReadString();
            var objectName = split.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)[0];
            var barcode = split.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)[1];

            DebugLogger.Msg("Received sync request for: " + objectName);

            bool invalidPacket = false;
            SyncedObject.lastId = objectId;

            if (!SyncedObject.syncedObjectIds.ContainsKey(objectId))
            {
                var foundCopy = GameObject.Find(objectName);
                if (!foundCopy)
                {
                    DebugLogger.Error("Could not find object: " + objectName);
                    DebugLogger.Msg("This is the first time we've seen this, attempting to spawn...");
                    if (barcode == "empty")
                    {
                        DebugLogger.Error(
                            "Recieved object request for syncing but object has no barcode and was not found.");
                        return;
                    }
                    DebugLogger.Msg("Barcode: " + barcode);
                    PatchVariables.shouldIgnoreSpawn = true;
                    PoolManager.SpawnGameObject(barcode, new Vector3(0, 0, 0), Quaternion.identity, o =>
                    {
                        DebugLogger.Msg("Spawned object! "+" "+barcode);
                        DebugLogger.Msg("Started sync ID as: "+objectId);
                        SyncedObject.FutureSync(o, groupId, userId);
                        foreach (var syncedObjectComponent in o.GetComponentsInChildren<SyncedObject>())
                        {
                            syncedObjectComponent.mainReference = o;
                        }
                        DebugLogger.Msg("Ended sync Id at: "+SyncedObject.lastId);
                        DebugLogger.Msg("Ended sync at Group ID: "+SyncedObject.lastGroupId);

                        if (DiscordIntegration.isHost)
                        {
                            var joinCatchupData = new JoinCatchupData
                            {
                                lastId = SyncedObject.lastId,
                                lastGroupId = SyncedObject.lastGroupId
                            };
                            var catchupBuff = PacketHandler.CompressMessage(NetworkMessageType.IdCatchupPacket, joinCatchupData);
                            Node.activeNode.BroadcastMessage((byte)NetworkChannel.Reliable, catchupBuff.getBytes());
                        }

                        PatchVariables.shouldIgnoreSpawn = false;
                    });
                    return;
                }

                if (SyncedObject.syncedObjects.Contains(foundCopy) || foundCopy.GetComponent<SyncedObject>())
                {
                    DebugLogger.Msg("Ignored request to sync a matching object.");
                    return;
                }
                SyncedObject.FutureSync(foundCopy, groupId, userId);
                if (DiscordIntegration.isHost)
                {
                    var joinCatchupData = new JoinCatchupData
                    {
                        lastId = SyncedObject.lastId,
                        lastGroupId = SyncedObject.lastGroupId
                    };
                    var catchupBuff = PacketHandler.CompressMessage(NetworkMessageType.IdCatchupPacket, joinCatchupData);
                    Node.activeNode.BroadcastMessage((byte)NetworkChannel.Reliable, catchupBuff.getBytes());
                }
            }
            else
            {
                DebugLogger.Error("Received request to sync object with id: " + objectId +
                                  ", but that slot is already taken up!");
            }
        }
    }

    public class InitializeSyncData : MessageData
    {
        public string barcode;
        public ushort groupId;
        public ushort objectId;
        public ushort finalId;
        public string objectName;
        public long userId;
    }
}