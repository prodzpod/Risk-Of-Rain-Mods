﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;
using RoR2.ContentManagement;
using RoR2.ExpansionManagement;
using UnityEngine;

namespace BubbetsItems
{
    public abstract class SharedBase
    {
        protected virtual void MakeConfigs() {}
        protected virtual void MakeTokens(){}
        protected virtual void MakeBehaviours(){} 
        protected virtual void DestroyBehaviours(){}

        //public virtual void MakeInLobbyConfig(ModConfigEntry modConfigEntry){}
        public virtual void MakeInLobbyConfig(Dictionary<ConfigCategoriesEnum, List<object>> dict){} // Has to be list of object because this class cannot have reference to inlobbyconfig, incase its not loaded
        
        public virtual void MakeRiskOfOptions()
        {
            sharedInfo.MakeRiskOfOptions();
        }

        public ConfigEntry<bool>? Enabled;
        public static readonly List<SharedBase> Instances = new List<SharedBase>();
        public static readonly Dictionary<PickupIndex, SharedBase> PickupIndexes = new Dictionary<PickupIndex, SharedBase>();
        private static readonly Dictionary<Type, SharedBase> InstanceDict = new();
        public PickupIndex PickupIndex;
        
        protected PatchClassProcessor? PatchProcessor;
        protected static readonly List<ContentPack> ContentPacks = new List<ContentPack>();

        private static ExpansionDef? _sotvExpansion;
        public SharedInfo sharedInfo;

        public static ExpansionDef? SotvExpansion
        {
            get
            {
                if (_sotvExpansion == null)
                    _sotvExpansion = ExpansionCatalog.expansionDefs.FirstOrDefault(x => x.nameToken == "DLC1_NAME");
                return _sotvExpansion;
            }
        }
        
        //This is probably bad practice
        public virtual bool RequiresSotv => (this as ItemBase)?.voidPairing is not null;
        
        public virtual string GetFormattedDescription(Inventory? inventory, string? token = null, bool forceHideExtended = false)
        {
            return "Not Implemented";
        }
        
        public static void Initialize(ManualLogSource manualLogSource, ConfigFile configFile, SerializableContentPack? serializableContentPack = null, Harmony? harmony = null, string tokenPrefix = "")
        {
            var sharedInfo = new SharedInfo(manualLogSource, configFile, harmony, tokenPrefix);

            foreach (var type in Assembly.GetCallingAssembly().GetTypes())
            {
                //if(type == typeof(SharedBase) || type == typeof(ItemBase) || type == typeof(EquipmentBase)) continue;
                if (!typeof(SharedBase).IsAssignableFrom(type)) continue; // || typeof(SharedBase) == type || typeof(ItemBase) == type || typeof(EquipmentBase) == type) continue;
                if (type.IsAbstract) continue;
                var shared = Activator.CreateInstance(type) as SharedBase;
                if (shared == null)
                {
                    manualLogSource.LogError($"Failed to make instance of {type}");
                    continue;
                }

                shared!.sharedInfo = sharedInfo;
                shared.MakeConfigs();
                
                if (!shared.Enabled?.Value ?? false) continue;
                shared.MakeBehaviours();
                if (harmony != null)
                {
                    shared.PatchProcessor = new PatchClassProcessor(harmony, shared.GetType());
                    shared.PatchProcessor.Patch();
                }
                Instances.Add(shared);
                InstanceDict[type] = shared;
                if(serializableContentPack)
                    shared.FillDefsFromSerializableCP(serializableContentPack!);
            }

            if (!serializableContentPack) return;
            serializableContentPack!.itemDefs = serializableContentPack.itemDefs.Where(x => ItemBase.Items.Any(y => MatchName(x.name, y.GetType().Name))).ToArray();
            var eliteEquipments = serializableContentPack.eliteDefs.Select(x => x.eliteEquipmentDef);
            serializableContentPack.equipmentDefs = serializableContentPack.equipmentDefs.Where(x => EquipmentBase.Equipments.Any(y => MatchName(x.name, y.GetType().Name))).Union(eliteEquipments).ToArray();
        }
        public static T? GetInstance<T>() where T : SharedBase
        {
            InstanceDict.TryGetValue(typeof(T), out var t);
            return t as T;
        }

        [SystemInitializer(typeof(EquipmentCatalog), typeof(PickupCatalog))]
        public static void InitializePickups()
        {
            foreach (var instance in Instances)
            {
                instance.FillDefsFromContentPack();
            }
            foreach (var instance in Instances)
            {
                instance.FillPickupIndex();
            }
        }

        protected static bool MatchName(string scriptableObject, string sharedBase)
        {
            return scriptableObject.StartsWith(sharedBase) || scriptableObject.StartsWith("ItemDef" + sharedBase) || scriptableObject.StartsWith("EquipmentDef" + sharedBase);
        }

        public static void AddContentPack(ContentPack contentPack)
        {
            if (!ContentPacks.Contains(contentPack))
                ContentPacks.Add(contentPack);
        }
        
        ~SharedBase()
        {
            DestroyBehaviours();
        }
        
        public void CheatForItem()
        {
            var master = PlayerCharacterMasterController.instances[0].master;
            PickupDropletController.CreatePickupDroplet(PickupIndex, master.GetBody().corePosition + Vector3.up * 1.5f, Vector3.one * 25f);
            //master.inventory.GiveItem(ItemDef.itemIndex);
        }

        [SystemInitializer(typeof(ExpansionCatalog))]
        public static void FillAllExpansionDefs()
        {
            foreach (var instance in Instances)
            {
                instance.FillRequiredExpansions();
            }
        }

        [SystemInitializer(typeof(BodyCatalog))]
        public static void FillIDRS()
        {
            foreach (var instance in Instances)
            {
                instance.FillItemDisplayRules();
            }
        }

        protected virtual void FillItemDisplayRules()
        { //*TODO remove method body, as this is just debug placement rules
            foreach (var key in IDRHelper.enumToBodyObjName.Keys)
            {
                var prefab = ((this as ItemBase)?.ItemDef as BubItemDef)?.displayModelPrefab ? ((this as ItemBase)?.ItemDef as BubItemDef)?.displayModelPrefab : ((this as EquipmentBase)?.EquipmentDef as BubEquipmentDef)?.displayModelPrefab;
                if (!prefab) continue;
                AddDisplayRules(key, new []{new ItemDisplayRule()
                {
                    childName = "Chest",
                    localScale = Vector3.one,
                    followerPrefab = prefab 
                }});
            }//*/
        }

        [SystemInitializer( typeof(ItemCatalog), typeof(EquipmentCatalog))]
        public static void MakeAllTokens()
        {
            foreach (var item in Instances)
            {
                try
                {
                    item.sharedInfo.Logger?.LogMessage($"Making tokens for {item}.");
                    item.MakeTokens();
                }
                catch (Exception e)
                {
                    item.sharedInfo.Logger?.LogError(e);
                }
            }
        }

        protected void AddToken(string key, string value)
        {
            Language.english.SetStringByToken(sharedInfo.TokenPrefix + key, value);
        }
        
        /* other languages get unloaded on language change, and these keys would be discarded
        protected void AddToken(string language, string key, string value)
        {
            Language.languagesByName[language].SetStringByToken(_tokenPrefix + key, value);
        }*/
        protected virtual void FillDefsFromSerializableCP(SerializableContentPack serializableContentPack) {}
        protected abstract void FillDefsFromContentPack();
        protected abstract void FillPickupIndex();
        protected abstract void FillRequiredExpansions();
        public abstract void AddDisplayRules(VanillaCharacterIDRS which, ItemDisplayRule[] displayRules);

        [SuppressMessage("ReSharper", "NotAccessedField.Global")]
        public class SharedInfo
        {
            public readonly ConfigEntry<bool> ExpandedTooltips;
            public readonly ConfigEntry<bool> DescInPickup;
            public readonly ConfigEntry<bool> ForceHideScalingInfoInPickup;
            public ConfigFile ConfigFile;
            public Harmony? Harmony;
            public readonly ManualLogSource Logger;
            public readonly string TokenPrefix;
            private bool _riskOfOptionsMade;
            
            public SharedInfo(ManualLogSource manualLogSource, ConfigFile configFile, Harmony? harmony, string tokenPrefix)
            {
                Logger = manualLogSource;
                ConfigFile = configFile;
                Harmony = harmony;
                TokenPrefix = tokenPrefix;
                
                ExpandedTooltips = configFile.Bind(ConfigCategoriesEnum.General, "Expanded Tooltips", true, "Enables the scaling function in the tooltip.");
                DescInPickup = configFile.Bind(ConfigCategoriesEnum.General, "Description In Pickup", true, "Used the description in the pickup for my items.");
                ForceHideScalingInfoInPickup = configFile.Bind(ConfigCategoriesEnum.General, "Disable Scaling Info In Pickup", true, "Should the scaling infos be hidden from pickups.");
            }

            public void MakeRiskOfOptions()
            {
                if (_riskOfOptionsMade) return;
                ModSettingsManager.AddOption(new CheckBoxOption(ExpandedTooltips));
                ModSettingsManager.AddOption(new CheckBoxOption(DescInPickup));
                ModSettingsManager.AddOption(new CheckBoxOption(ForceHideScalingInfoInPickup));
                _riskOfOptionsMade = true;
            }
        }
    }
}