using HarmonyLib;
using UnityEngine;

namespace JetPump;

[HarmonyPatch(typeof(GoPointer), "MainButtonDown")]
internal static class PumpClickWhileSteering
{
	private static int activatedFrame = -1;

	private static void Postfix(GoPointer __instance, GoPointerButton ___stickyClickedButton, GoPointerButton ___pointedAtButton, ref bool __result)
	{
		if (!__result || __instance.GetHeldItem() is ShipItemHammer || !PumpWorld.IsHelm(___stickyClickedButton))
		{
			return;
		}
		PumpControl pumpControl = ___pointedAtButton as PumpControl;
		Rigidbody rigidbody = (((bool)pumpControl && pumpControl.CanUse) ? PumpWorld.BoatBody(pumpControl.Owner) : null);
		if ((bool)rigidbody && !(PumpWorld.HelmBody(___stickyClickedButton) != rigidbody))
		{
			if (activatedFrame != Time.frameCount)
			{
				activatedFrame = Time.frameCount;
				pumpControl.Activate();
			}
			__result = false;
		}
	}
}
