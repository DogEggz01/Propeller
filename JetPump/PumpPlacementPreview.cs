using HarmonyLib;
using UnityEngine;

namespace JetPump;

[HarmonyPatch(typeof(ShipItem), "Update")]
internal static class PumpPlacementPreview
{
	[HarmonyPriority(0)]
	private static void Postfix(ShipItem __instance, Transform ___itemRigidbody, ref bool ___inRangeOfWall, ref Vector3 ___attachPos, ref Quaternion ___attachRot)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component && (bool)__instance.held)
		{
			___inRangeOfWall = component.Mount.Preview(___itemRigidbody, out ___attachPos, out ___attachRot);
			if (___inRangeOfWall)
			{
				component.Mount.ShowPreview(___attachPos, ___attachRot);
			}
			else
			{
				component.Mount.HidePreview();
			}
		}
	}
}
