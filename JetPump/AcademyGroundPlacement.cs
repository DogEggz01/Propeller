using UnityEngine;

namespace JetPump;

internal sealed class AcademyGroundPlacement : MonoBehaviour
{
	private Transform island;

	private Vector3 counterPoint;

	internal void Initialize(Transform root, float counter)
	{
		island = root;
		counterPoint = root.InverseTransformPoint(new Vector3(base.transform.position.x, counter, base.transform.position.z));
	}

	internal bool TryPlace(ShopItemSpawner slot)
	{
		if (!island || !AcademyStock.Ground(base.transform.position, island.TransformPoint(counterPoint).y, out var surface))
		{
			return false;
		}
		AcademyStock.Place(slot, slot.itemPrefab, base.transform.position, surface, base.transform.rotation);
		return true;
	}
}
