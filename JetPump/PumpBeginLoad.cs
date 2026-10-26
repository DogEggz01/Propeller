using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(SaveLoadManager), "LoadGame")]
internal static class PumpBeginLoad
{
	private static void Prefix()
	{
		PumpWorld.Reset();
		PumpWorld.Loading = true;
		GameState.modData?.Remove("DogEggz.JetPump.v1");
	}
}
