using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(SaveLoadManager), "LoadModData")]
internal static class PumpLoad
{
	private static void Postfix()
	{
		PumpWorld.Load();
	}
}
