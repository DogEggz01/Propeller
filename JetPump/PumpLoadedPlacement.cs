using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(SaveablePrefab), "Load")]
internal static class PumpLoadedPlacement
{
	private static void Prefix(SaveablePrefab __instance, SavePrefabData data, bool ___loaded)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component && !___loaded)
		{
			PumpWorld.RetireDuplicate(component, data.instanceId);
		}
	}

	private static void Postfix(SaveablePrefab __instance)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			component.Mount.LoadedFromSave = true;
			component.Mount.ResetBinding();
			PumpMount.ResetCache();
		}
	}
}
