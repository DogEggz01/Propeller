using HarmonyLib;
using UnityEngine;

namespace JetPump;

[HarmonyPatch(typeof(GPButtonSteeringWheel), "ExtraLateUpdate")]
internal static class PumpWheelThrottle
{
	private static void Postfix(GPButtonSteeringWheel __instance)
	{
		if (PumpWorld.InputAllowed)
		{
			PumpInput.HandleWheelInput(__instance, GameInput.GetKey(InputName.MoveUp), GameInput.GetKey(InputName.MoveDown), Time.deltaTime);
		}
	}
}
