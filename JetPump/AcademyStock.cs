using HarmonyLib;
using UnityEngine;

namespace JetPump;

[HarmonyPatch(typeof(ShopItemSpawner), "Start")]
internal static class AcademyStock
{
	internal const string SceneName = "island 8 A Academy";

	internal const string ExtraName = "JetPump Academy controller left";

	internal const string ThirdName = "Propeller Academy controller far left";

	internal const string HugeName = "Propeller Academy huge";

	[HarmonyPrefix]
	[HarmonyPriority(0)]
	private static void Prefix(ShopItemSpawner __instance)
	{
		if (!Plugin.Ready || __instance.gameObject.scene.name != "island 8 A Academy")
		{
			return;
		}
		string name = __instance.name;
		if (name != "shop item (6)" && name != "shop item (7)" && name != "shop item (11)")
		{
			return;
		}
		Transform parent = __instance.transform.parent;
		if (!parent || (bool)parent.Find("Propeller Academy huge") || !PumpAssets.Pump || !PumpAssets.Controller || !PumpAssets.HugePump)
		{
			return;
		}
		Transform transform = parent.Find("shopkeeper (mage)");
		ShopItemSpawner shopItemSpawner = Slot(parent, "shop item (6)");
		ShopItemSpawner shopItemSpawner2 = Slot(parent, "shop item (7)");
		ShopItemSpawner shopItemSpawner3 = Slot(parent, "shop item (11)");
		if ((bool)transform && (bool)transform.GetComponent<Shopkeeper>() && Fish(shopItemSpawner) && Fish(shopItemSpawner2) && Fish(shopItemSpawner3))
		{
			Vector3 position = shopItemSpawner.transform.position;
			Vector3 position2 = shopItemSpawner2.transform.position;
			Vector3 position3 = shopItemSpawner3.transform.position;
			Vector3 normalized = Vector3.ProjectOnPlane(position - position3, Vector3.up).normalized;
			if (!(normalized.sqrMagnitude < 0.99f))
			{
				Vector3 vector = -Vector3.Cross(normalized, Vector3.up);
				Vector3 vector2 = normalized * 0.18f;
				Quaternion rotation = Quaternion.LookRotation(Vector3.down, -vector);
				float num = Surface(shopItemSpawner3);
				Vector3 position4 = (position + position2 + normalized * 0.015f) * 0.5f + vector2 + vector * 0.55f - normalized * 0.15f;
				Place(shopItemSpawner, PumpAssets.Pump, position + vector2 + normalized * 0.015f, Surface(shopItemSpawner), rotation);
				Place(shopItemSpawner2, PumpAssets.Pump, position2 + vector2, Surface(shopItemSpawner2), rotation);
				Place(shopItemSpawner3, PumpAssets.Controller, position3 + vector2, num, rotation);
				Extra(parent, "JetPump Academy controller left", shopItemSpawner3, PumpAssets.Controller, position3 + vector2 - normalized * 0.26f, num, rotation);
				Extra(parent, "Propeller Academy controller far left", shopItemSpawner3, PumpAssets.Controller, position3 + vector2 - normalized * 0.52f, num, rotation);
				Extra(parent, "Propeller Academy huge", shopItemSpawner3, PumpAssets.HugePump, position4, num, rotation).gameObject.AddComponent<AcademyGroundPlacement>().Initialize(parent, num);
				Plugin.Log?.LogInfo("Academy mage: three controllers, two Propellers and one Huge Propeller stocked.");
			}
		}
	}

	private static ShopItemSpawner Slot(Transform root, string name)
	{
		Transform transform = root.Find(name);
		if (!transform)
		{
			return null;
		}
		return transform.GetComponent<ShopItemSpawner>();
	}

	private static bool Fish(ShopItemSpawner slot)
	{
		SaveablePrefab saveablePrefab = (((bool)slot && (bool)slot.itemPrefab) ? slot.itemPrefab.GetComponent<SaveablePrefab>() : null);
		if ((bool)saveablePrefab)
		{
			return saveablePrefab.prefabIndex == 31;
		}
		return false;
	}

	private static float Surface(ShopItemSpawner slot)
	{
		MeshRenderer component = slot.GetComponent<MeshRenderer>();
		MeshFilter component2 = slot.GetComponent<MeshFilter>();
		if (!component || !component2 || !component2.sharedMesh)
		{
			return slot.transform.position.y;
		}
		return component.bounds.min.y;
	}

	internal static bool Ground(Vector3 position, float counter, out float surface)
	{
		Physics.SyncTransforms();
		surface = float.NegativeInfinity;
		RaycastHit[] array = Physics.RaycastAll(new Vector3(position.x, counter - 0.05f, position.z), Vector3.down, 5f, -5, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < array.Length; i++)
		{
			RaycastHit raycastHit = array[i];
			if (raycastHit.normal.y > 0.5f && raycastHit.point.y < counter - 0.4f && !raycastHit.collider.GetComponentInParent<ShipItem>())
			{
				surface = Mathf.Max(surface, raycastHit.point.y);
			}
		}
		return !float.IsNegativeInfinity(surface);
	}

	private static ShopItemSpawner Extra(Transform root, string name, ShopItemSpawner template, GameObject prefab, Vector3 position, float surface, Quaternion rotation)
	{
		if ((bool)root.Find(name))
		{
			return root.Find(name).GetComponent<ShopItemSpawner>();
		}
		GameObject gameObject = new GameObject(name);
		gameObject.SetActive(value: false);
		gameObject.transform.SetParent(root, worldPositionStays: false);
		gameObject.transform.localScale = template.transform.localScale;
		ShopItemSpawner shopItemSpawner = gameObject.AddComponent<ShopItemSpawner>();
		shopItemSpawner.availableAtNight = template.availableAtNight;
		shopItemSpawner.priceMult = template.priceMult;
		Place(shopItemSpawner, prefab, position, surface, rotation);
		gameObject.SetActive(value: true);
		return shopItemSpawner;
	}

	internal static void Place(ShopItemSpawner spawner, GameObject prefab, Vector3 position, float surface, Quaternion rotation)
	{
		BoxCollider component = prefab.GetComponent<BoxCollider>();
		Vector3 a = component.size * 0.5f;
		float num = float.PositiveInfinity;
		for (int i = -1; i <= 1; i += 2)
		{
			for (int j = -1; j <= 1; j += 2)
			{
				for (int k = -1; k <= 1; k += 2)
				{
					num = Mathf.Min(num, (rotation * (component.center + Vector3.Scale(a, new Vector3(i, j, k)))).y);
				}
			}
		}
		position.y = surface - num + 0.003f;
		spawner.transform.SetPositionAndRotation(position, rotation);
		spawner.itemPrefab = prefab;
	}
}
