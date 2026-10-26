using System;
using System.IO;
using UnityEngine;

namespace JetPump;

internal static class PumpAssets
{
	internal const string BundleName = "dogeggz.jetpump.assets";

	internal const string Contract = "JetPumpAssetContract_v14";

	internal static AssetBundle Bundle;

	internal static GameObject Pump;

	internal static GameObject Controller;

	internal static GameObject HugePump;

	internal static void Load(string folder)
	{
		string text = Path.Combine(folder, "assets", "dogeggz.jetpump.assets");
		Bundle = AssetBundle.LoadFromFile(text);
		if (!Bundle)
		{
			throw new FileNotFoundException("Propeller AssetBundle could not be loaded", text);
		}
		Pump = Bundle.LoadAsset<GameObject>("Assets/JetPump/Generated/604 propeller.prefab");
		Controller = Bundle.LoadAsset<GameObject>("Assets/JetPump/Generated/605 Pump Controller.prefab");
		HugePump = Bundle.LoadAsset<GameObject>("Assets/JetPump/Generated/606 Huge Propeller.prefab");
		Validate(HugePump, 606);
		Validate(Pump, 604);
		Validate(Controller, 605);
	}

	private static void Validate(GameObject prefab, int id)
	{
		if (!prefab || !prefab.GetComponent<ShipItem>() || !prefab.GetComponent<SaveablePrefab>() || prefab.GetComponent<SaveablePrefab>().prefabIndex != id || !prefab.GetComponent<ShipItem>().wallAttachment || !prefab.transform.Find("JetPumpAssetContract_v14"))
		{
			throw new InvalidOperationException("Invalid bundled item " + id);
		}
	}

	internal static bool IsOurItem(ShipItem item)
	{
		SaveablePrefab component = item.GetComponent<SaveablePrefab>();
		if ((bool)component && PumpState.IsItem(component.prefabIndex))
		{
			return item.transform.Find("JetPumpAssetContract_v14");
		}
		return false;
	}

	internal static void Register(PrefabsDirectory directory)
	{
		GameObject[] array = directory.directory;
		if (array == null)
		{
			throw new InvalidOperationException("Missing prefab directory");
		}
		int[] array2 = new int[3] { 604, 605, 606 };
		for (int i = 0; i < array2.Length; i++)
		{
			int num = array2[i];
			GameObject gameObject = num switch
			{
				605 => Controller, 
				604 => Pump, 
				_ => HugePump, 
			};
			if (num < array.Length && (bool)array[num] && array[num] != gameObject)
			{
				throw new InvalidOperationException("Propeller refuses occupied prefab " + num + " (" + array[num].name + ").");
			}
		}
		if (array.Length <= 606)
		{
			Array.Resize(ref array, 607);
		}
		array[604] = Pump;
		array[605] = Controller;
		array[606] = HugePump;
		directory.directory = array;
	}

	internal static void Unload()
	{
		if ((bool)Bundle)
		{
			Bundle.Unload(unloadAllLoadedObjects: false);
		}
		Bundle = null;
		Pump = (Controller = (HugePump = null));
	}
}
