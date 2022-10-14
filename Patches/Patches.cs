using System.Collections;
using System.Collections.Generic;
using BonelabMultiplayerMockup.Nodes;
using BonelabMultiplayerMockup.Object;
using BonelabMultiplayerMockup.Packets;
using BonelabMultiplayerMockup.Packets.Gun;
using BonelabMultiplayerMockup.Packets.Player;
using BonelabMultiplayerMockup.Utils;
using BoneLib;
using HarmonyLib;
using Il2CppSystem;
using MelonLoader;
using SLZ.AI;
using SLZ.Bonelab;
using SLZ.Interaction;
using SLZ.Marrow.Pool;
using SLZ.Marrow.Proxy;
using SLZ.Marrow.Warehouse;
using SLZ.Props.Weapons;
using SLZ.Rig;
using SLZ.UI;
using SLZ.Zones;
using TriangleNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using Avatar = SLZ.VRMK.Avatar;

namespace BonelabMultiplayerMockup.Patches
{
    public class PatchVariables
    {
        public static bool shouldIgnoreGrabbing = false;
        public static bool shouldIgnoreGunEvents = false;
        public static bool shouldIgnoreSpawn = false;
    }

    public class PatchCoroutines
    {
        public static IEnumerator WaitForAvatarSwitch()
        {
            yield return new WaitForSecondsRealtime(1f);
            var swapAvatarMessageData = new AvatarChangeData()
            {
                userId = DiscordIntegration.currentUser.Id,
                barcode = Player.GetRigManager().GetComponentInChildren<RigManager>()._avatarCrate._barcode._id
            };
            var packetByteBuf = PacketHandler.CompressMessage(NetworkMessageType.AvatarChangePacket,
                swapAvatarMessageData);
            Node.activeNode.BroadcastMessage((byte)NetworkChannel.Reliable, packetByteBuf.getBytes());
            BonelabMultiplayerMockup.PopulateCurrentAvatarData();
            if (BonelabMultiplayerMockup.debugRep != null)
            {
                BonelabMultiplayerMockup.debugRep.SetAvatar(Player.GetRigManager().GetComponentInChildren<RigManager>()._avatarCrate._barcode._id);
            }
        }

        public static IEnumerator WaitForNPCSync(GameObject npc)
        {
            yield return new WaitForSecondsRealtime(2);
            SyncedObject syncedObject = SyncedObject.GetSyncedComponent(npc);
            if (!syncedObject)
            {
                SyncedObject.Sync(npc);
            }
            else
            {
                syncedObject.BroadcastOwnerChange();
            }

        }

        public static IEnumerator WaitForAttachSync(GameObject toSync) 
        {
            if (PatchVariables.shouldIgnoreGrabbing)
            {
                yield break;
            }

            PatchVariables.shouldIgnoreGrabbing = true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var syncedObject = SyncedObject.GetSyncedComponent(toSync);
            if (syncedObject == null)
                SyncedObject.Sync(toSync);
            else
                syncedObject.BroadcastOwnerChange();
            PatchVariables.shouldIgnoreGrabbing = false;
        }
        
        
    }

    public class Patches
    {
        [HarmonyPatch(typeof(AIBrain), "OnPostSpawn", typeof(GameObject))]
        private class AiSpawnPatch
        {
            public static void Postfix(AIBrain __instance, GameObject go)
            {
                if (DiscordIntegration.hasLobby)
                {
                    if (DiscordIntegration.isHost)
                    {
                        // Shouldnt matter if the NPC is already synced (Which it should be.) This is a failsafe.
                        MelonCoroutines.Start(PatchCoroutines.WaitForNPCSync(go));
                    }
                }
            }
        }
        
        public class NewGameManager
        {
            public static string NewGameStart = "de3f04bcdb8729147bff9d8812500a4a";
            private bool _isGameNew = NewGameStart.Equals(SceneManager.GetActiveScene().name);
            
            public void CheckGameStart(SceneAmmoUI.SceneNames sceneNames)
            {
                
                if (DiscordIntegration.hasLobby && DiscordIntegration.isHost)
                {
                    if (_isGameNew)
                    {
                        if (DiscordIntegration.isHost)
                        {
                            DebugLogger.Msg("Hosting | New Co-op session Initiating...");
                            //TODO: find way to force player to noose and have noose ignore other players.
                        }
                        else DebugLogger.Msg("Joining | New Co-op session Initiating...");
                             //TODO: find a way to force player to spawn in crowd.

                    }
                }
            }
        }

        [HarmonyPatch(typeof(AssetPoolee), "OnSpawn")]
        private class PoolPatch
        {
            public static void Postfix(AssetPoolee __instance)
            {
                if (DiscordIntegration.hasLobby)
                {
                    bool isNPC = PoolManager.GetComponentOnObject<AIBrain>(__instance.gameObject) != null;
                    bool foundSpawnGun = false; 
                    bool isHost = DiscordIntegration.isHost;
                    if (Player.GetComponentInHand<SpawnGun>(Player.leftHand))
                    {
                        foundSpawnGun = true;
                    }

                    if (Player.GetComponentInHand<SpawnGun>(Player.rightHand))
                    {
                        foundSpawnGun = true;
                    }
                    DebugLogger.Msg("Spawned object via pool: "+__instance.name);

                    if (SyncedObject.isSyncedObject(__instance.gameObject))
                    {
                        DebugLogger.Error("THIS OBJECT IS ALREADY SYNCED, THIS SHOULD BE REMOVED.");
                    }

                    List<SyncedObject> syncedObjects = SyncedObject.GetAllSyncables(
                        __instance.gameObject);
                    foreach (var synced in syncedObjects)
                    {
                        synced.DestroySyncable(true);
                    }
                    
                    if (SyncedObject.isSyncedObject(__instance.gameObject))
                    {
                        DebugLogger.Error("THIS OBJECT IS NO LONGER SYNCED.");
                    }

                    if (!foundSpawnGun && !DiscordIntegration.isHost && isNPC)
                    {
                        DebugLogger.Msg("Didnt spawn NPC via spawn gun and is not the host of the lobby, despawning.");
                        DebugLogger.Msg("PROBABLY CAMPAIGN NPC! WAIT FOR THE HOST TO SPAWN THIS NPC IN!");
                        __instance.Despawn();
                    }
                    else
                    {
                        if (PoolManager.GetComponentOnObject<Rigidbody>(__instance.gameObject))
                        {
                            if (isNPC)
                            {
                                if (foundSpawnGun || isHost)
                                {
                                    var syncedObject = SyncedObject.GetSyncedComponent(__instance.gameObject);
                                    if (syncedObject == null)
                                        SyncedObject.Sync(__instance.gameObject);
                                    else
                                        syncedObject.BroadcastOwnerChange();
                                    DebugLogger.Msg("Synced NPC: " + __instance.gameObject.name);
                                }
                            }
                            else
                            {
                                if (foundSpawnGun)
                                {
                                    var syncedObject = SyncedObject.GetSyncedComponent(__instance.gameObject);
                                    if (syncedObject == null)
                                        SyncedObject.Sync(__instance.gameObject);
                                    else
                                        syncedObject.BroadcastOwnerChange();
                                    DebugLogger.Msg("Synced spawn gun spawned Object: " + __instance.gameObject.name);
                                }
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Gun), "OnFire")]
        private class GunFirePatch
        {
            public static void Postfix(Gun __instance)
            {
                if (DiscordIntegration.hasLobby)
                {
                    if (PatchVariables.shouldIgnoreGunEvents)
                    {
                        return;
                    }

                    DebugLogger.Msg("Gun fired");
                    SyncedObject syncedObject = SyncedObject.GetSyncedComponent(__instance.gameObject);
                    if (syncedObject)
                    {
                        var gunStateMessageData = new GunStateData()
                        {
                            objectid = syncedObject.currentId,
                            state = 0
                        };
                        var packetByteBuf = PacketHandler.CompressMessage(NetworkMessageType.GunStatePacket,
                            gunStateMessageData);
                        Node.activeNode.BroadcastMessage((byte)NetworkChannel.Object, packetByteBuf.getBytes());
                    }
                }
            }
        }

        [HarmonyPatch(typeof(RigManager), "SwapAvatar", new[] { typeof(Avatar) })]
        private class AvatarSwapPatch
        {
            public static void Postfix(RigManager __instance)
            {
                if (DiscordIntegration.hasLobby)
                {
                    DebugLogger.Msg("Swapped Avatar");
                    MelonCoroutines.Start(PatchCoroutines.WaitForAvatarSwitch());
                }
            }
        }
        
        [HarmonyPatch(typeof(RigManager), "SwitchAvatar", new[] { typeof(Avatar) })]
        private class AvatarSwitchPatch
        {
            public static void Postfix(RigManager __instance)
            {
                if (DiscordIntegration.hasLobby)
                {
                    DebugLogger.Msg("Switched Avatar");
                    MelonCoroutines.Start(PatchCoroutines.WaitForAvatarSwitch());
                }
            }
        }

        [HarmonyPatch(typeof(Hand), "AttachObject", typeof(GameObject))]
        private class HandGrabPatch
        {
            public static void Postfix(Hand __instance, GameObject objectToAttach)
            {
                if (DiscordIntegration.hasLobby)
                {
                    DebugLogger.Msg("Grabbed: " + objectToAttach.name);
                    MelonCoroutines.Start(PatchCoroutines.WaitForAttachSync(objectToAttach));
                }
            }
        }

        [HarmonyPatch(typeof(ForcePullGrip), "OnFarHandHoverUpdate")]
        public class ForcePullPatch
        {
            public static void Prefix(ForcePullGrip __instance, ref bool __state, Hand hand)
            {
                __state = __instance.pullCoroutine != null;
            }

            public static void Postfix(ForcePullGrip __instance, ref bool __state, Hand hand)
            {
                if (!(__instance.pullCoroutine != null && !__state))
                    return;
                MelonCoroutines.Start(PatchCoroutines.WaitForAttachSync(__instance.gameObject));
            }
        }
    }
}