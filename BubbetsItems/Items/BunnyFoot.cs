﻿using System;
using BubbetsItems.ItemBehaviors;
using EntityStates;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace BubbetsItems.Items
{
	public class BunnyFoot : ItemBase
	{
		public static BunnyFoot? Instance;
		public BunnyFoot() => Instance = this;

		protected override void MakeConfigs()
		{
			base.MakeConfigs();
			AddScalingFunction("[a] * 1.5", "Air Control");
			AddScalingFunction("0.25", "On Ground Mercy");
			AddScalingFunction("1", "Jump velocity retention");
			AddScalingFunction("[a] * 0.5", "Jump Acceleration");
		}

		protected override void MakeTokens()
		{
			base.MakeTokens();
			AddToken("BUNNYFOOT_NAME", "Bunny Foot");
			AddToken("BUNNYFOOT_DESC", "You gain the ability to bunny hop. Air control: {0}");
			AddToken("BUNNYFOOT_PICKUP", "Your little feets start quivering.");
			AddToken("BUNNYFOOT_LORE", "haha source go brrrr");
		}

		[HarmonyILManipulator, HarmonyPatch(typeof(ProjectileGrappleController.GripState), nameof(ProjectileGrappleController.GripState.FixedUpdateBehavior))]
		public static void FixGrapple(ILContext il)
		{
			// enable air control after grapple
			var c = new ILCursor(il);
			c.GotoNext(
				MoveType.After,
				x => x.MatchCall<Vector3>("op_Multiply"),
				x => x.MatchLdcI4(1),
				x => x.MatchLdcI4(1)
			);
			c.Index--;
			c.Remove();
			c.Emit(OpCodes.Ldc_I4_0);
		}

		[HarmonyILManipulator, HarmonyPatch(typeof(GenericCharacterMain), nameof(GenericCharacterMain.ApplyJumpVelocity))]
		public static void FixJump(ILContext il)
		{
			// Clamp the jump speed add to not add speed when strafing over the speedlimit
			var c = new ILCursor(il);
			
			// if (vault)
			c.GotoNext(
				x => x.MatchStfld<CharacterMotor>("velocity")
			);
			c.Emit(OpCodes.Ldarg_1);
			c.EmitDelegate<Func<Vector3, CharacterBody, Vector3>>(DoJumpFix);
			c.Index++;
			
			// if (vault) else
			c.GotoNext(
				x => x.MatchStfld<CharacterMotor>("velocity")
			);
			c.Emit(OpCodes.Ldarg_1);
			c.EmitDelegate<Func<Vector3, CharacterBody, Vector3>>(DoJumpFix);
		}

		private static Vector3 DoJumpFix(Vector3 vector, CharacterBody characterBody)
		{
			/*
			var horizontal = vector + Vector3.down * vector.y;
			var cmi = characterBody.characterMotor as IPhysMotor;
			var vhorizontal = cmi.velocity + Vector3.down * cmi.velocity.y;
			if (vhorizontal.sqrMagnitude > horizontal.sqrMagnitude) horizontal = vhorizontal;
			horizontal.y = vector.y;*/

			var bh = characterBody.GetComponent<BunnyFootBehavior>();
			if (!bh) return vector;

			var count = characterBody.inventory.GetItemCount(Instance!.ItemDef);
			var grounded = true;

			var velocity = bh.hitGroundVelocity;
			var wishDir = vector.normalized;
			var wishSpeed = vector.magnitude;
			if (!characterBody.characterMotor.isGrounded)
			{
				//wishDir = velo.normalized;
				//wishSpeed = velo.magnitude;
				velocity = (characterBody.characterMotor as IPhysMotor).velocity;
				grounded = false;
			}

			var addvel = Accelerate(velocity, wishDir, wishSpeed,
				wishSpeed * Instance.scalingInfos[2].ScalingFunction(count),
				Instance.scalingInfos[3].ScalingFunction(count), 1f);

			addvel.y = vector.y;
			
			if (!grounded) return addvel;

			return Time.time - bh.hitGroundTime > Instance!.scalingInfos[1].ScalingFunction(characterBody.inventory.GetItemCount(Instance.ItemDef)) ? vector : addvel;
		}

		[HarmonyILManipulator, HarmonyPatch(typeof(CharacterMotor), nameof(CharacterMotor.PreMove))]
		public static void PatchMovement(ILContext il)
		{
			var c = new ILCursor(il);
			c.GotoNext(
				x => x.MatchMul(),
				x => x.MatchCall<Vector3>(nameof(Vector3.MoveTowards))
			);
			c.RemoveRange(2);
			c.Emit(OpCodes.Ldarg_0);
			c.EmitDelegate<Func<Vector3, Vector3, float, float, CharacterMotor, Vector3>>(DoAirMovement);
		}

		private static Vector3 DoAirMovement(Vector3 velocity, Vector3 target, float num, float deltaTime, CharacterMotor motor)
		{
			var count = motor.body?.inventory?.GetItemCount(Instance.ItemDef) ?? 0; 
			if (count <= 0 || motor.disableAirControlUntilCollision || motor.Motor.GroundingStatus.IsStableOnGround)
				return Vector3.MoveTowards(velocity, target, num * deltaTime);

			var newTarget = target;
			if (!motor.isFlying)
				newTarget.y = 0;

			var wishDir = newTarget.normalized;
			var wishSpeed = motor.walkSpeed * wishDir.magnitude;

			return Accelerate(velocity, wishDir, wishSpeed, Instance.scalingInfos[0].ScalingFunction(count), motor.acceleration, deltaTime);
		}

		//Ripped from sbox or gmod, i dont remember
		private static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float speedLimit, float acceleration, float deltaTime)
		{
			if ( speedLimit > 0 && wishSpeed > speedLimit )
				wishSpeed = speedLimit;

			// See if we are changing direction a bit
			var currentspeed = Vector3.Dot(velocity, wishDir );

			// Reduce wishspeed by the amount of veer.
			var addspeed = wishSpeed - currentspeed;

			// If not going to add any speed, done.
			if ( addspeed <= 0 )
				return velocity;

			// Determine amount of acceleration.
			var accelspeed = acceleration * deltaTime * wishSpeed; // * SurfaceFriction;

			// Cap at addspeed
			if ( accelspeed > addspeed )
				accelspeed = addspeed;

			return velocity + wishDir * accelspeed;
		}
	}
}