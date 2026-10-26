using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(SaveablePrefab), "PrepareSaveData")]
internal static class PumpSavePose
{
	private static void Prefix(SaveablePrefab __instance)
	{
		__instance.GetComponent<PumpItem>()?.Mount.CapturePose();
	}
}
