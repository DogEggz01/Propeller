using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItem), "Awake")]
internal static class AddPumpRuntime
{
	private static void Prefix(ShipItem __instance)
	{
		if (Plugin.Ready && PumpAssets.IsOurItem(__instance) && !__instance.GetComponent<PumpItem>())
		{
			__instance.gameObject.AddComponent<PumpItem>();
		}
	}
}
