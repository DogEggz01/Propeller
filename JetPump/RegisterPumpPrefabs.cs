using System;
using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(PrefabsDirectory), "Start")]
internal static class RegisterPumpPrefabs
{
	private static void Prefix(PrefabsDirectory __instance)
	{
		try
		{
			PumpAssets.Register(__instance);
		}
		catch (Exception data)
		{
			Plugin.Ready = false;
			Plugin.Log.LogError(data);
		}
	}
}
