﻿using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using MonoMod.Cil;
using RoR2;
using RoR2.EntityLogic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace BubbetsItems
{
	public static class VoidLunarShopController
	{
		//Only exists on server context
		public static GameObject ShopInstance;
		private static GameObject? _shopPrefab;
		private static ExplicitPickupDropTable voidCoinTable;
		private static InteractableSpawnCard voidBarrelSpawncard;
		private static GameObject? _rerollPrefab;
		private static GameObject? _terminalPrefab;
		public static GameObject ShopPrefab => _shopPrefab ??= BubbetsItemsPlugin.AssetBundle.LoadAsset<GameObject>("LunarVoidShop");
		public static GameObject RerollPrefab => _rerollPrefab ??= BubbetsItemsPlugin.AssetBundle.LoadAsset<GameObject>("Reroll");
		public static GameObject TerminalPrefab => _terminalPrefab ??= BubbetsItemsPlugin.AssetBundle.LoadAsset<GameObject>("LunarVoidTerminal");

		public static void Init()
		{
			Stage.onStageStartGlobal += SceneLoaded;
			RoR2Application.onLoad += MakeTokens;
		}

		public static Dictionary<PlayerCharacterMasterController, float> voidCoinChances = new();

		private static void MakeTokens()
		{
			Language.english.SetStringByToken("BUB_VOIDLUNARSHOP_NAME", "Void Bud");
			Language.english.SetStringByToken("BUB_VOIDLUNARSHOP_CONTEXT", "Open Void Bud");
			Language.english.SetStringByToken("BUB_VOIDLUNARSHOP_REROLL_NAME", "Slab");
			Language.english.SetStringByToken("BUB_VOIDLUNARSHOP_REROLL_CONTEXT", "Refresh Shop");
			
			if(!Chainloader.PluginInfos.ContainsKey("com.Anreol.ReleasedFromTheVoid")) EnableVoidCoins();
		}

		public static void SceneLoaded(Stage stage)
		{
			if (!NetworkServer.active) return;
			if (stage.sceneDef.nameToken != "MAP_BAZAAR_TITLE") return;
			if (!Run.instance.IsExpansionEnabled(BubbetsItemsPlugin.BubSotvExpansion)) return;

			//NetworkServer.Spawn(GameObject.Instantiate(RerollPrefab));
			//*
			ShopInstance = GameObject.Instantiate(ShopPrefab, new Vector3(284.3365f, -445.1391f, -139.8904f), Quaternion.Euler(0, 330, 0));
			NetworkServer.Spawn(ShopInstance);
			var terminals = new List<GameObject>();
			terminals.Add(GameObject.Instantiate(TerminalPrefab));
			terminals[0].transform.SetParent(ShopInstance.transform);
			terminals[0].transform.localPosition = new Vector3(11, 1, 0);
			terminals[0].transform.rotation = Quaternion.Euler(0, 300, 0);
			terminals.Add(GameObject.Instantiate(TerminalPrefab));
			terminals[1].transform.SetParent(ShopInstance.transform);
			terminals[1].transform.localPosition = new Vector3(4.35f, 0.96f, -2.84f);
			terminals[1].transform.rotation = Quaternion.Euler(0, 330, 0);
			terminals.Add(GameObject.Instantiate(TerminalPrefab));
			terminals[2].transform.SetParent(ShopInstance.transform);
			terminals[2].transform.localPosition = new Vector3(-2.478197f, 1.026f, -0.0069f);
			var reroll = GameObject.Instantiate(RerollPrefab);
			reroll.transform.SetParent(ShopInstance.transform);
			reroll.transform.localPosition = new Vector3(18, -2.2f, 20);
			reroll.transform.rotation = Quaternion.Euler(0, 200f, 0);
			var rerollrepeat = reroll.GetComponentInChildren<Repeat>();
			var rerollcounter = reroll.GetComponentInChildren<Counter>();
			for (var i = 0; i < terminals.Count; i++)
			{
				var i1 = i;
				rerollrepeat.repeatedEvent.AddListener(() => terminals[i1].GetComponentInChildren<DelayedEvent>().CallDelayedIfActiveAndEnabled(i1 * 0.25f + 0.25f));
				terminals[i1].GetComponent<PurchaseInteraction>().onPurchase.AddListener(_ => rerollcounter.Add(1));
				NetworkServer.Spawn(terminals[i]);
			}
			rerollcounter.threshold = terminals.Count;
			NetworkServer.Spawn(reroll);

			/*NetworkServer.Spawn(ShopInstance);
			foreach (var identity in ShopInstance.GetComponentsInChildren<NetworkIdentity>().Where(x => x != ShopInstance.GetComponent<NetworkIdentity>()))
			{
				identity.Reset();
				identity.OnStartServer(false);
				identity.RebuildObservers(true);
			}*/
			/*/
			var scenePaths = BubbetsItemsPlugin.AssetBundle.GetAllScenePaths();
			SceneManager.LoadScene(scenePaths[0], LoadSceneMode.Additive);
			//*/
		}
		
		public static void EnableVoidCoins()
		{
			voidCoinTable = Addressables.LoadAssetAsync<ExplicitPickupDropTable>("RoR2/DLC1/Common/DropTables/dtVoidCoin.asset").WaitForCompletion();
			voidBarrelSpawncard = Addressables.LoadAssetAsync<InteractableSpawnCard>("RoR2/DLC1/VoidCoinBarrel/iscVoidCoinBarrel.asset").WaitForCompletion();
			voidBarrelSpawncard.prefab.GetComponent<ModelLocator>().gameObject.AddComponent<ChestBehavior>().dropTable = voidCoinTable;
			voidBarrelSpawncard.directorCreditCost = 7;
			
			Run.onRunStartGlobal += _ => { voidCoinChances.Clear(); };
			GlobalEventManager.onCharacterDeathGlobal += delegate(DamageReport damageReport)
			{
				if (!damageReport.victimBody.bodyFlags.HasFlag(CharacterBody.BodyFlags.Void) && !damageReport.victimBody.HasBuff(DLC1Content.Buffs.EliteVoid)) return;
				CharacterMaster characterMaster = damageReport.attackerMaster;
				if (characterMaster)
				{
					if (characterMaster.minionOwnership.ownerMaster)
					{
						characterMaster = characterMaster.minionOwnership.ownerMaster;
					}
					PlayerCharacterMasterController component = characterMaster.GetComponent<PlayerCharacterMasterController>();
					var chance = 50f;
					
					if (voidCoinChances.TryGetValue(component, out var chanceg))
						chance = chanceg;
					else
						voidCoinChances.Add(component, chance);
					
					if (!component || !Util.CheckRoll(chance)) return;
					
					PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(DLC1Content.MiscPickups.VoidCoin.miscPickupIndex), damageReport.victim.transform.position, Vector3.up * 10f);
					voidCoinChances[component] = chance * 0.5f;
				}
			};
		}
	}
}