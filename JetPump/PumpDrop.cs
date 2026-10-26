using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItem), "OnDrop")]
internal static class PumpDrop
{
	private static void Postfix(ShipItem __instance, bool ___inRangeOfWall)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			component.Mount.Drop((__instance.sold & ___inRangeOfWall) && !__instance.forceDisableRedOutline && (bool)__instance.itemRigidbodyC && __instance.itemRigidbodyC.attached);
		}
	}
}
