using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShipItem), "DestroyItem")]
internal static class PumpDestroy
{
	private static void Prefix(ShipItem __instance)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			if (!component.Retiring && !component.Mount.Cached && !PumpWorld.Loading && !GameState.currentlyLoading && component.EnsureState())
			{
				component.Removed();
				PumpWorld.State.Forget(component.Saveable.instanceId);
			}
			component.Retiring = true;
		}
	}
}
