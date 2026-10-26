using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(GoPointer), "GetPointedAtItem")]
internal static class PumpHammerTarget
{
	private static void Postfix(GoPointer __instance, GoPointerButton ___pointedAtButton, ref ShipItem __result)
	{
		if (__instance.GetHeldItem() is ShipItemHammer && ___pointedAtButton is PumpControl pumpControl && (bool)pumpControl.Owner)
		{
			__result = pumpControl.Owner.Item;
		}
	}
}
