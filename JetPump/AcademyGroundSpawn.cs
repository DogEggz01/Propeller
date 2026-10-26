using HarmonyLib;

namespace JetPump;

[HarmonyPatch(typeof(ShopItemSpawner), "SpawnItem")]
internal static class AcademyGroundSpawn
{
	private static bool Prefix(ShopItemSpawner __instance)
	{
		AcademyGroundPlacement component = __instance.GetComponent<AcademyGroundPlacement>();
		if ((bool)component)
		{
			return component.TryPlace(__instance);
		}
		return true;
	}
}
