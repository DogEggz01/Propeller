using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItem), "OnEnterInventory")]
internal static class PumpInventory
{
	private static void Prefix(ShipItem __instance)
	{
		__instance.GetComponent<PumpItem>()?.Removed();
	}
}
