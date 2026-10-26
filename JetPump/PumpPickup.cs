using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItem), "OnPickup")]
internal static class PumpPickup
{
	private static void Prefix(ShipItem __instance)
	{
		__instance.GetComponent<PumpItem>()?.Removed();
	}
}
