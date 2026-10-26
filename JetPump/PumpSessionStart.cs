using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(SaveLoadManager), "Awake")]
internal static class PumpSessionStart
{
	private static void Prefix()
	{
		PumpWorld.Reset();
	}
}
