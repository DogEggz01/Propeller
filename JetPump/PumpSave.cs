using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(SaveLoadManager), "SaveModData")]
internal static class PumpSave
{
	private static void Prefix()
	{
		PumpWorld.Save();
	}
}
