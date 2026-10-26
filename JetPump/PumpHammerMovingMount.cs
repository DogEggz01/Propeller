using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItemHammer), "NailItem")]
internal static class PumpHammerMovingMount
{
	private static bool Prefix(ShipItem item)
	{
		PumpItem pumpItem = (item ? item.GetComponent<PumpItem>() : null);
		if (!pumpItem)
		{
			return true;
		}
		bool onboardPanel = !pumpItem.IsPump && item.sold && !item.held
			&& !pumpItem.Mount.Cached && !pumpItem.Mount.Stored
			&& PumpWorld.BoatId(item.currentActualBoat) > 0;
		if (PumpWorld.InputAllowed && (pumpItem.Installed || onboardPanel))
		{
			item.nailed = true;
			if ((bool)UISoundPlayer.instance)
			{
				UISoundPlayer.instance.PlayUISound(UISounds.winchUnclick, 1f, 0.6f);
			}
		}
		return false;
	}
}
