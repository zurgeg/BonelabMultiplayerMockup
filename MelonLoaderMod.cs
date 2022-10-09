﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using BonelabMultiplayerMockup.Messages;
using BonelabMultiplayerMockup.Messages.Handlers.Object;
using BonelabMultiplayerMockup.Messages.Handlers.Player;
using BonelabMultiplayerMockup.Nodes;
using BonelabMultiplayerMockup.Object;
using BonelabMultiplayerMockup.Patches;
using BonelabMultiplayerMockup.Representations;
using BonelabMultiplayerMockup.Utils;
using BoneLib;
using HBMP.DataType;
using MelonLoader;
using SLZ.Rig;
using UnityEngine;

namespace BonelabMultiplayerMockup
{
    public static class BuildInfo
    {
        public const string Name = "BonelabMultiplayerMockup"; // Name of the Mod.  (MUST BE SET)
        public const string Author = "notnotnotswipez"; // Author of the Mod.  (Set as null if none)
        public const string Company = null; // Company that made the Mod.  (Set as null if none)
        public const string Version = "1.0.0"; // Version of the Mod.  (MUST BE SET)
        public const string DownloadLink = null; // Download Link for the Mod.  (Set as null if none)
    }

    public class BonelabMultiplayerMockup : MelonMod
    {
        public static PlayerRepresentation debugRep = null;
        public static Dictionary<byte, GameObject> boneDictionary = new Dictionary<byte, GameObject>();
        private static byte currentBoneId = 0;
        private int updateCount = 0;
        private int desiredFrames = 2;
        public static string sceneName = "";
        public static bool debug = false;
        

        public static void PopulateCurrentAvatarData()
        {
            boneDictionary.Clear();
            currentBoneId = 0;
            PopulateBoneDictionary(Player.GetRigManager().GetComponentInChildren<RigManager>().avatar.gameObject.transform);
        }

        private static void PopulateBoneDictionary(Transform parent)
        {
            var childCount = parent.childCount;

            for (var i = 0; i < childCount; i++)
            {
                var child = parent.GetChild(i).gameObject;
                boneDictionary.Add(currentBoneId++, child);

                if (child.transform.childCount > 0) PopulateBoneDictionary(child.transform);
            }
        }
        
        public override void OnApplicationStart()
        {
            GameSDK.LoadGameSDK();
            MessageHandler.RegisterHandlers();
            DiscordIntegration.Init();
            Client.StartClient();
            DataDirectory.Initialize();
            
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (DiscordIntegration.hasLobby)
            {
                if (Player.GetRigManager() != null)
                {
                    
                    GameObject rigManager = Player.GetRigManager();
                    if (rigManager.scene.name != BonelabMultiplayerMockup.sceneName)
                    {
                        DebugLogger.Msg("Cleaned data");
                        DebugLogger.Msg("Loaded scene with name: " + rigManager.scene.name);
                        SyncedObject.CleanData();
                        PopulateCurrentAvatarData();
                        BonelabMultiplayerMockup.sceneName = rigManager.scene.name;
                        MelonCoroutines.Start(PatchCoroutines.WaitForAvatarSwitch());
                    }
                    else
                    {
                        DebugLogger.Msg("Loaded a new scene zone!");
                        MelonCoroutines.Start(PatchCoroutines.WaitForAvatarSwitch());
                    }
                }
            }
        }

        public override void OnUpdate()
        {
            if (Input.GetKey(KeyCode.S))
            {
                Server.StartServer();
            }

            updateCount++;
            if (updateCount >= desiredFrames)
            {
                if (DiscordIntegration.hasLobby)
                {
                    SendBones();
                    SyncedObject.UpdateSyncedNPCs();
                }

                updateCount = 0;
            }
        }

        public override void OnFixedUpdate()
        {

            if (DiscordIntegration.hasLobby)
            {
                
                if (SyncedObject.syncedObjectIds.Count > 0)
                {
                    foreach (SyncedObject syncedObject in SyncedObject.syncedObjectIds.Values) {
                        try
                        {
                            syncedObject.UpdatePos();
                        }
                        catch (Exception e)
                        {
                            // Ignore it, if something goes wrong we dont want everything in the list to break
                        }
                    }
                    
                    if (SyncedObject.queuedObjectsToDelete.Count > 0)
                    {
                        for (int i = 0; i < SyncedObject.queuedObjectsToDelete.Count; i++)
                        {
                            List<SyncedObject> syncedObjects = SyncedObject.GetAllSyncables(
                                SyncedObject.relatedSyncedObjects[SyncedObject.queuedObjectsToDelete[i]][0].gameObject);
                            ushort lastId = 0;
                            foreach (var synced in syncedObjects)
                            {
                                lastId = synced.groupId;
                                synced.DestroySyncable(true);
                            }

                            ushort groupId = SyncedObject.queuedObjectsToDelete[i];
                            GroupDestroyMessageData groupDestroyMessageData = new GroupDestroyMessageData()
                            {
                                groupId = groupId,
                                backupObjectId = lastId
                            };

                            PacketByteBuf message = MessageHandler.CompressMessage(NetworkMessageType.GroupDestroyMessage, groupDestroyMessageData);
                            Node.activeNode.BroadcastMessage((byte)NetworkChannel.Object, message.getBytes());
                        }

                        SyncedObject.queuedObjectsToDelete.Clear();
                    }
                }
            }
        }

        private static void SendBones()
        {
            
            for (byte i = 0; i < boneDictionary.Count; i++)
            {
                GameObject bone = boneDictionary[i];
                if (bone == null)
                {
                    // Assume its all wrong
                    break;
                }

                var simplifiedTransform = new SimplifiedTransform(bone.transform.position,
                    Quaternion.Euler(bone.transform.eulerAngles));

                PlayerSyncMessageData playerSyncMessageData = new PlayerSyncMessageData()
                {
                    userId = DiscordIntegration.currentUser.Id,
                    boneId = i,
                    transform = simplifiedTransform
                };

                PacketByteBuf message = MessageHandler.CompressMessage(NetworkMessageType.PlayerUpdateMessage, playerSyncMessageData);
                Node.activeNode.BroadcastMessage((byte)NetworkChannel.Unreliable, message.getBytes());
            }
        }

        public override void OnLateUpdate()
        {
            DiscordIntegration.Tick();
        }
    }
}