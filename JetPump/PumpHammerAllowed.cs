using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItemHammer), "AllowOnItemClick")]
internal static class PumpHammerAllowed
{
	private static bool Prefix(GoPointerButton lookedAtButton, ref bool __result)
	{
		if (!(lookedAtButton is PumpControl pumpControl) || !pumpControl.Owner)
		{
			return true;
		}
		__result = ShipItemHammer.CanNail(pumpControl.Owner.Item);
		return false;
	}
}
