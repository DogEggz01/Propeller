using HarmonyLib;
using UnityEngine;

namespace JetPump;

[HarmonyPatch(typeof(GoPointer), "PositionItemInBuyUI")]
internal static class PropellerCarry
{
	private static bool Prefix(GoPointer __instance, PickupableItem item)
	{
		SaveablePrefab saveablePrefab = (item ? item.GetComponent<SaveablePrefab>() : null);
		if (!Plugin.Ready || !saveablePrefab || saveablePrefab.prefabIndex != 606)
		{
			return true;
		}
		Transform transform = __instance.transform;
		Vector3 b = transform.position + transform.forward * item.holdDistance + transform.up * item.holdHeight;
		item.transform.position = Vector3.Lerp(item.transform.position, b, Time.deltaTime * 9f);
		if ((bool)BuyItemUI.instance && (bool)BuyItemUI.instance.itemPos)
		{
			item.transform.rotation = BuyItemUI.instance.itemPos.rotation;
		}
		return false;
	}
}
