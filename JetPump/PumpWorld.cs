using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace JetPump;

internal static class PumpWorld
{
	internal const string SaveKey = "DogEggz.JetPump.v1";

	internal static PumpState State = new PumpState();

	internal static int Session;

	internal static bool Loading;

	internal static readonly List<PumpItem> Live = new List<PumpItem>();

	internal static Type TillerType;

	internal static FieldInfo TillerRudder;

	private static readonly List<PumpItem> linked = new List<PumpItem>();

	internal static bool SimulationAllowed
	{
		get
		{
			if (Plugin.Ready && !Loading && State.Writable && GameState.playing && !GameState.currentlyLoading && !GameState.loadingBoatLocalItems && !GameState.justStarted && !GameState.recovering && !GameState.currentShipyard)
			{
				return Time.timeScale > 0f;
			}
			return false;
		}
	}

	internal static bool InputAllowed
	{
		get
		{
			if (SimulationAllowed && !GameState.sleeping && !GameState.inBed && !GameState.inCursorMenu && !GameState.wasInSettingsMenu && !BoatCamera.on)
			{
				return !PairingMenu.IsOpen;
			}
			return false;
		}
	}

	internal static void Reset()
	{
		PairingMenu.Close();
		PumpTrim.ResetAll();
		PumpMount.ResetCache();
		State = new PumpState();
		Session++;
		Loading = false;
	}

	internal static void Load()
	{
		bool loading = Loading;
		string value = null;
		GameState.modData?.TryGetValue("DogEggz.JetPump.v1", out value);
		State = PumpState.Decode(value);
		if (loading && State.Writable)
		{
			ReconcileSavedNumbers();
		}
		Session++;
		Loading = false;
		if (!State.Writable)
		{
			Plugin.Log.LogError("JetPump save data is unsupported or invalid. Original data retained; propulsion disabled for this session.");
		}
	}

	internal static void ReconcileSavedNumbers()
	{
		SaveLoadManager instance = SaveLoadManager.instance;
		if (!instance)
		{
			return;
		}
		List<SaveablePrefab> currentPrefabs = instance.GetCurrentPrefabs();
		SaveableObject[] array = AccessTools.Field(typeof(SaveLoadManager), "currentObjects").GetValue(instance) as SaveableObject[];
		if (currentPrefabs == null || array == null)
		{
			return;
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (SaveablePrefab item in currentPrefabs)
		{
			if ((bool)item)
			{
				hashSet.Add(item.instanceId);
			}
		}
		SaveableObject[] array2 = array;
		foreach (SaveableObject saveableObject in array2)
		{
			if (!saveableObject || !saveableObject.localItems)
			{
				continue;
			}
			List<SavePrefabData> cachedItems = saveableObject.localItems.GetCachedItems();
			if (cachedItems == null)
			{
				continue;
			}
			foreach (SavePrefabData item2 in cachedItems)
			{
				hashSet.Add(item2.instanceId);
			}
		}
		State.RetainSavedItems(hashSet);
	}

	internal static void Save()
	{
		if (!State.Writable || Loading)
		{
			return;
		}
		foreach (PumpItem item in Live)
		{
			if ((bool)item)
			{
				item.EnforceNails();
				item.Mount.CapturePose();
			}
		}
		if (GameState.modData == null)
		{
			GameState.modData = new Dictionary<string, string>();
		}
		GameState.modData["DogEggz.JetPump.v1"] = State.Encode();
	}

	internal static PumpItem Find(int id)
	{
		for (int i = 0; i < Live.Count; i++)
		{
			if ((bool)Live[i] && !Live[i].Retiring && !Live[i].Mount.Cached && Live[i].isActiveAndEnabled && (bool)Live[i].Saveable && Live[i].Saveable.instanceId == id)
			{
				return Live[i];
			}
		}
		return null;
	}

	internal static void RetireDuplicate(PumpItem incoming, int id)
	{
		if (id <= 0)
		{
			return;
		}
		for (int num = Live.Count - 1; num >= 0; num--)
		{
			PumpItem pumpItem = Live[num];
			if ((bool)pumpItem && !(pumpItem == incoming) && !pumpItem.Retiring && (bool)pumpItem.Saveable && pumpItem.Saveable.instanceId == id && pumpItem.Saveable.prefabIndex == incoming.Saveable.prefabIndex)
			{
				pumpItem.Retiring = true;
				pumpItem.Item.DestroyItem();
			}
		}
	}

	internal static int BoatId(Transform actualBoat)
	{
		SaveableObject saveableObject = (actualBoat ? actualBoat.GetComponentInParent<SaveableObject>() : null);
		if (!saveableObject)
		{
			return 0;
		}
		return saveableObject.sceneIndex;
	}

	internal static bool PairOperational(PumpItem box, out PumpItem pump)
	{
		pump = null;
		if (!box)
		{
			return false;
		}
		box.EnforceNails();
		if (!box.CanOperate || !State.ValidPair(box.Record))
		{
			return false;
		}
		pump = Find(box.Record.PeerId);
		if ((bool)pump)
		{
			pump.EnforceNails();
		}
		if ((bool)pump && pump.Installed && pump.Item.nailed)
		{
			return pump.Item.currentActualBoat == box.Item.currentActualBoat;
		}
		return false;
	}

	internal static void AutoPair(PumpItem box)
	{
		if (!box || !box.CanOperate || box.Record.PeerId != 0 || box.Record.RequirePairChoice)
		{
			return;
		}
		PumpItem pumpItem = null;
		foreach (PumpItem item in Live)
		{
			if ((bool)item && item.IsPump && item.Installed && item.Record.PeerId == 0 && !item.Record.RequirePairChoice && !(item.Item.currentActualBoat != box.Item.currentActualBoat))
			{
				if ((bool)pumpItem)
				{
					return;
				}
				pumpItem = item;
			}
		}
		if ((bool)pumpItem)
		{
			State.Pair(box.Record.Id, pumpItem.Record.Id, box.Record.BoatId);
		}
	}

	internal static bool Usable(PumpRecord record)
	{
		PumpItem pumpItem = ((record == null) ? null : Find(record.Id));
		PumpItem pump;
		if ((bool)pumpItem)
		{
			return PairOperational(pumpItem, out pump);
		}
		return false;
	}

	internal static Type OptionalType(string assembly, string type)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly2 in assemblies)
		{
			if (assembly2.GetName().Name == assembly)
			{
				return assembly2.GetType(type, throwOnError: false);
			}
		}
		return null;
	}

	internal static bool IsHelm(GoPointerButton button)
	{
		if ((bool)button)
		{
			if (!(button is GPButtonSteeringWheel))
			{
				if (TillerType != null)
				{
					return TillerType.IsInstanceOfType(button);
				}
				return false;
			}
			return true;
		}
		return false;
	}

	internal static Rigidbody HelmBody(GoPointerButton helm)
	{
		if (!IsHelm(helm))
		{
			return null;
		}
		GPButtonSteeringWheel gPButtonSteeringWheel = helm as GPButtonSteeringWheel;
		Rudder rudder = null;
		if ((bool)gPButtonSteeringWheel)
		{
			rudder = (gPButtonSteeringWheel.attachedRudder ? gPButtonSteeringWheel.attachedRudder.GetComponent<Rudder>() : null);
		}
		else
		{
			rudder = TillerRudder?.GetValue(helm) as Rudder;
			if (!rudder && (bool)helm.transform.parent)
			{
				rudder = helm.transform.parent.GetComponent<Rudder>();
			}
		}
		if ((bool)rudder && (bool)rudder.shipRigidbody)
		{
			return rudder.shipRigidbody;
		}
		if (!gPButtonSteeringWheel)
		{
			return null;
		}
		return gPButtonSteeringWheel.GetComponentInParent<Rigidbody>();
	}

	internal static Rigidbody BoatBody(PumpItem box)
	{
		Transform transform = (box ? box.Item.currentActualBoat : null);
		if (!transform || !transform.parent)
		{
			return null;
		}
		return transform.parent.GetComponent<Rigidbody>();
	}

	internal static bool HasHelm(PumpItem box)
	{
		Rigidbody rigidbody = BoatBody(box);
		if (!rigidbody)
		{
			return false;
		}
		GoPointerButton[] componentsInChildren = box.Item.currentActualBoat.parent.GetComponentsInChildren<GoPointerButton>(includeInactive: false);
		foreach (GoPointerButton goPointerButton in componentsInChildren)
		{
			if (IsHelm(goPointerButton) && HelmBody(goPointerButton) == rigidbody)
			{
				return true;
			}
		}
		return false;
	}

	internal static List<PumpItem> Linked(GoPointerButton helm)
	{
		linked.Clear();
		Rigidbody rigidbody = HelmBody(helm);
		if (!rigidbody)
		{
			return linked;
		}
		foreach (PumpItem item in Live)
		{
			if ((bool)item && !item.IsPump && item.Record != null && item.Record.Wheel.Length > 0 && BoatBody(item) == rigidbody && PairOperational(item, out var _))
			{
				linked.Add(item);
			}
		}
		return linked;
	}
}
