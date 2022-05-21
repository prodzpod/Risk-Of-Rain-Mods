﻿using System;
using System.Collections.Generic;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using UnityEngine;

namespace BubbetsItems.Items.VoidLunar
{
	public class Hydrophily : ItemBase
	{
		protected override void MakeTokens()
		{
			base.MakeTokens();
			var name = GetType().Name.ToUpper();
			SimpleDescriptionToken = name + "_DESC_SIMPLE";
			AddToken(name + "_NAME", "Hydrophily");
			AddToken(name + "_DESC", "");
			AddToken(name + "_DESC_SIMPLE", "100% (+100% per stack) of healing except regen converges into temporary barriers. While barrier is active health cannot be healed. Corrupts all Corpseblooms.");
			AddToken(name + "_PICKUP", "Converts healing into barrier, disables all healing while barrier is active. Corrupts all Corpseblooms.");
			AddToken(name + "_LORE", "");
		}

		protected override void MakeConfigs()
		{
			base.MakeConfigs();
			AddScalingFunction("[h] * [a] * 1", "Barrier Gain", desc: "[a] = item count; [h] = healing amount");
		}

		protected override void FillVoidConversions(List<ItemDef.Pair> pairs)
		{
			base.FillVoidConversions(pairs);
			AddVoidPairing(nameof(RoR2Content.Items.RepeatHeal));
		}

		//[HarmonyILManipulator, HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.ServerFixedUpdate))]
		public static void DisableShieldRecharge(ILContext il)
		{
			var c = new ILCursor(il);
			c.GotoNext(x => x.MatchLdfld<HealthComponent>(nameof(HealthComponent.isShieldRegenForced)));
			c.GotoNext(MoveType.After, x => x.MatchLdloc(out _));
			//var jump = c.Next;
			//c.Index++;
			c.Emit(OpCodes.Ldarg_0);
			c.EmitDelegate<Func<HealthComponent, bool>>(hc =>
			{
				var body = hc.body;
				if (!body) return false;
				var inv = body.inventory;
				if (!inv) return false;
				var inst = GetInstance<Hydrophily>();
				var amount = inv.GetItemCount(inst.ItemDef);
				return amount > 0;
			});
			c.Emit(OpCodes.Or);
			
			//c.Emit(OpCodes.Brfalse, jump.Operand);
		}
		
		[HarmonyPrefix, HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.Heal))]
		public static bool StopHealing(HealthComponent __instance, float amount, ProcChainMask procChainMask, bool nonRegen)
		{
			var body = __instance.body;
			if (!body) return true;
			var inv = body.inventory;
			if (!inv) return true;
			var inst = GetInstance<Hydrophily>();
			var iamount = inv.GetItemCount(inst.ItemDef);
			if (iamount <= 0) return true;
			if (!nonRegen) return __instance.barrier <= 0.01;
			var info = inst.scalingInfos[0];
			info.WorkingContext.h = amount;
			__instance.AddBarrier(info.ScalingFunction(iamount));
			return false;
		}
	}
}