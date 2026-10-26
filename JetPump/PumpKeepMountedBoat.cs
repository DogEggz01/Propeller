using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItem), "ExtraFixedUpdate")]
internal static class PumpKeepMountedBoat
{
	private static bool Prefix(ShipItem __instance)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			return !component.Mount.ProtectOwnership;
		}
		return true;
	}
}
