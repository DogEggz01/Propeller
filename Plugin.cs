using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

[assembly: AssemblyCompany("Propeller")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyFileVersion("1.2.2.0")]
[assembly: AssemblyInformationalVersion("1.2.2")]
[assembly: AssemblyProduct("Propeller")]
[assembly: AssemblyTitle("Propeller")]
[assembly: AssemblyVersion("1.2.2.0")]

namespace JetPump;

[BepInPlugin("DogEggz.JetPump", "Propeller", "1.2.2")]
[BepInDependency("com.nandbrew.nandcommand", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("pr0skynesis.dinghies", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
	public const string Guid = "DogEggz.JetPump";

	internal static ManualLogSource Log;

	internal static bool Ready;

	private static ConfigEntry<float> waterOffset;

	private static ConfigEntry<float> trimAngle;

	private static ConfigEntry<bool> showMarkers;

	private Harmony harmony;

	internal static bool ShowMarkers
	{
		get
		{
			if (showMarkers != null)
			{
				return showMarkers.Value;
			}
			return false;
		}
	}

	internal static float VerticalOffset => Mathf.Round(Finite(waterOffset, 0f, -2f, 2f) * 100f) / 100f;

	internal static float TrimAngle => Finite(trimAngle, 80f, 0f, 180f);

	private static float Finite(ConfigEntry<float> entry, float fallback, float min, float max)
	{
		float value = entry?.Value ?? fallback;
		if (!PumpRecord.Finite(value))
		{
			return fallback;
		}
		return Mathf.Clamp(value, min, max);
	}

	internal static void BindSettings(ConfigFile config)
	{
		if (waterOffset != null)
		{
			waterOffset.SettingChanged -= SnapOffset;
		}
		waterOffset = config.Bind("Propulsion", "Vertical offset (metres)", 0f, new ConfigDescription("Moves thrust and all water-detection points together along the boat's vertical axis. Positive raises, negative lowers. Steps of 0.01 m; model position stays unchanged.", new AcceptableValueRange<float>(-2f, 2f), new ConfigurationManagerAttributes
		{
			ShowRangeAsPercent = false,
			CustomDrawer = DrawOffset
		}));
		waterOffset.SettingChanged += SnapOffset;
		SnapOffset(waterOffset, EventArgs.Empty);
		showMarkers = config.Bind("Display", "Show propulsion markers", defaultValue: false, "Show the current/last boat's live physics centre of mass and installed propellers' thrust points and directions, through the hull, inside or outside the shipyard.");
		trimAngle = config.Bind("Trim", "Bow-up angle (degrees)", 80f, new ConfigDescription("Average bow-up target during forward propulsion, shared by all propellers on a boat. Zero targets level with pitch compensation, 90 vertical and 180 inverted. Values above 90 permit intentional inversion.", new AcceptableValueRange<float>(0f, 180f), new ConfigurationManagerAttributes
		{
			ShowRangeAsPercent = false
		}));
	}

	private static void SnapOffset(object sender, EventArgs args)
	{
		ConfigEntry<float> configEntry = (ConfigEntry<float>)sender;
		float num = Mathf.Round(Finite(configEntry, 0f, -2f, 2f) * 100f) / 100f;
		if (configEntry.Value != num)
		{
			configEntry.Value = num;
		}
	}

	private static void DrawOffset(ConfigEntryBase entry)
	{
		ConfigEntry<float> configEntry = (ConfigEntry<float>)entry;
		GUILayout.BeginHorizontal();
		float num = GUILayout.HorizontalSlider(configEntry.Value, -2f, 2f, GUILayout.MinWidth(130f));
		if (GUILayout.Button("−", GUILayout.Width(25f)))
		{
			num -= 0.01f;
		}
		if (GUILayout.Button("+", GUILayout.Width(25f)))
		{
			num += 0.01f;
		}
		num = Mathf.Clamp(Mathf.Round(num * 100f) / 100f, -2f, 2f);
		GUILayout.Label(num.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " m", GUILayout.Width(70f));
		if (num != configEntry.Value)
		{
			configEntry.Value = num;
		}
		GUILayout.EndHorizontal();
	}

	private void Awake()
	{
		Log = base.Logger;
		BindSettings(base.Config);
		try
		{
			PumpAssets.Load(Path.GetDirectoryName(base.Info.Location));
			harmony = new Harmony("DogEggz.JetPump");
			harmony.PatchAll(typeof(Plugin).Assembly);
			PumpInput.PatchCheatspeed(harmony);
			PumpTiller.Patch(harmony);
			base.gameObject.AddComponent<PairingMenu>();
			base.gameObject.AddComponent<PumpMarkers>();
			Ready = true;
			base.Logger.LogInfo("Propeller 1.2.2 loaded: Propeller 604 (30,000 N), controller 605, Huge Propeller 606 (800,000 N).");
		}
		catch (Exception ex)
		{
			Ready = false;
			harmony?.UnpatchSelf();
			PumpAssets.Unload();
			base.Logger.LogError("Propeller initialization aborted: " + ex);
		}
	}

	private void OnDestroy()
	{
		Ready = false;
		if (waterOffset != null)
		{
			waterOffset.SettingChanged -= SnapOffset;
		}
		PairingMenu.Close();
		harmony?.UnpatchSelf();
		PumpWorld.Reset();
		PumpAssets.Unload();
	}
}

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

[HarmonyPatch(typeof(PrefabsDirectory), "Start")]
internal static class RegisterPumpPrefabs
{
	private static void Prefix(PrefabsDirectory __instance)
	{
		try
		{
			PumpAssets.Register(__instance);
		}
		catch (Exception data)
		{
			Plugin.Ready = false;
			Plugin.Log.LogError(data);
		}
	}
}

[HarmonyPatch(typeof(ShipItem), "Awake")]
internal static class AddPumpRuntime
{
	private static void Prefix(ShipItem __instance)
	{
		if (Plugin.Ready && PumpAssets.IsOurItem(__instance) && !__instance.GetComponent<PumpItem>())
		{
			__instance.gameObject.AddComponent<PumpItem>();
		}
	}
}

internal sealed class ConfigurationManagerAttributes
{
	public bool? ShowRangeAsPercent;

	public Action<ConfigEntryBase> CustomDrawer;
}

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

[HarmonyPatch(typeof(SaveLoadManager), "Awake")]
internal static class PumpSessionStart
{
	private static void Prefix()
	{
		PumpWorld.Reset();
	}
}

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

[HarmonyPatch(typeof(SaveLoadManager), "LoadModData")]
internal static class PumpLoad
{
	private static void Postfix()
	{
		PumpWorld.Load();
	}
}

[HarmonyPatch(typeof(SaveLoadManager), "SaveModData")]
internal static class PumpSave
{
	private static void Prefix()
	{
		PumpWorld.Save();
	}
}

public sealed class PumpRecord
{
	public int Id;

	public int Kind;

	public int PeerId;

	public int BoatId;

	public bool Mounted;

	public int Power = 5;

	public float Throttle;

	public bool Enabled;

	public bool RequireImmersion;

	public bool RequirePairChoice;

	public bool SafetyEnabled;

	public int Number;

	public string Wheel = "";

	public int Group;

	public bool HasPose;

	public float X;

	public float Y;

	public float Z;

	public float Qx;

	public float Qy;

	public float Qz;

	public float Qw = 1f;

	public float AppliedPower
	{
		get
		{
			if (!Enabled)
			{
				return 0f;
			}
			return (float)Power * Throttle;
		}
	}

	public void Normalize()
	{
		Power = ClampPower(Power);
		Throttle = (Finite(Throttle) ? Math.Max(0f, Math.Min(1f, Throttle)) : 0f);
		if (PeerId < 0)
		{
			PeerId = 0;
		}
		if (BoatId <= 0)
		{
			BoatId = 0;
			Mounted = false;
		}
		Wheel = Wheel ?? "";
		if (Wheel.Length > 0)
		{
			Wheel = "*";
		}
		Group = ((Kind == 605) ? Math.Max(0, Math.Min(3, Group)) : 0);
		if (!Mounted)
		{
			Enabled = false;
			Wheel = "";
		}
		if (!Mounted || !Finite(X) || !Finite(Y) || !Finite(Z) || !Finite(Qx) || !Finite(Qy) || !Finite(Qz) || !Finite(Qw) || Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw < 0.01f)
		{
			HasPose = false;
		}
	}

	internal static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	public static int ClampPower(int power)
	{
		return Math.Max(5, Math.Min(100, (int)Math.Round((double)power / 5.0) * 5));
	}

	public void AdjustThrottle(bool forward, bool decrease, float seconds)
	{
		if (forward != decrease && !(seconds <= 0f) && Finite(seconds))
		{
			Throttle = Math.Max(0f, Math.Min(1f, Throttle + (forward ? 1f : (-1f)) * seconds * 0.2f));
		}
	}

	public void StopForRemoval()
	{
		Enabled = false;
		Mounted = false;
		Wheel = "";
		HasPose = false;
	}
}

public sealed class PumpState
{
	public const int PumpId = 604;

	public const int ControllerId = 605;

	public const int HugePumpId = 606;

	public const string AllHelms = "*";

	public const int GroupCount = 3;

	public readonly Dictionary<int, PumpRecord> Items = new Dictionary<int, PumpRecord>();

	public bool Writable = true;

	public bool MigratedNumbers;

	private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

	public static bool IsPump(int kind)
	{
		if (kind != 604)
		{
			return kind == 606;
		}
		return true;
	}

	public static bool IsItem(int kind)
	{
		if (!IsPump(kind))
		{
			return kind == 605;
		}
		return true;
	}

	public static float RatedThrust(int kind)
	{
		return kind switch
		{
			604 => 30000f, 
			606 => 800000f, 
			_ => 0f, 
		};
	}

	public static string Label(PumpRecord pump)
	{
		return ((pump.Kind == 606) ? "H.PROP " : "PROP ") + pump.Number.ToString("00");
	}

	public PumpRecord GetOrCreate(int id, int kind)
	{
		if (id <= 0 || !IsItem(kind))
		{
			throw new ArgumentException("Invalid item identity.");
		}
		if (!Items.TryGetValue(id, out var value))
		{
			Dictionary<int, PumpRecord> items = Items;
			PumpRecord obj = new PumpRecord
			{
				Id = id,
				Kind = kind,
				SafetyEnabled = (kind == 605)
			};
			value = obj;
			items.Add(id, obj);
		}
		if (value.Kind != kind)
		{
			throw new InvalidOperationException("Saved item identity belongs to a different prefab.");
		}
		return value;
	}

	public PumpRecord Peer(PumpRecord item)
	{
		if (item != null && Items.TryGetValue(item.PeerId, out var value) && value.PeerId == item.Id && ((item.Kind == 605 && IsPump(value.Kind)) || (value.Kind == 605 && IsPump(item.Kind))))
		{
			return value;
		}
		return null;
	}

	public bool Pair(int controllerId, int pumpId, int boatId)
	{
		if (!Items.TryGetValue(controllerId, out var value) || !Items.TryGetValue(pumpId, out var value2) || value.Kind != 605 || !IsPump(value2.Kind) || boatId <= 0 || !value.Mounted || !value2.Mounted || value.BoatId != boatId || value2.BoatId != boatId)
		{
			return false;
		}
		if (value2.PeerId != 0 && value2.PeerId != controllerId)
		{
			PumpRecord pumpRecord = Peer(value2);
			if (pumpRecord != null && pumpRecord.Mounted)
			{
				return false;
			}
			if (pumpRecord != null)
			{
				pumpRecord.PeerId = 0;
				pumpRecord.Wheel = "";
				pumpRecord.RequirePairChoice = true;
			}
		}
		PumpRecord pumpRecord2 = Peer(value);
		if (pumpRecord2 != null && pumpRecord2 != value2)
		{
			pumpRecord2.PeerId = 0;
			pumpRecord2.Enabled = false;
		}
		value.PeerId = value2.Id;
		value2.PeerId = value.Id;
		value.Wheel = "";
		value.RequirePairChoice = false;
		value2.RequirePairChoice = false;
		value2.Enabled = false;
		value2.Throttle = 0f;
		return true;
	}

	public bool ValidPair(PumpRecord box)
	{
		PumpRecord pumpRecord = Peer(box);
		if (box != null && box.Kind == 605 && pumpRecord != null && box.Mounted && pumpRecord.Mounted && box.BoatId > 0)
		{
			return pumpRecord.BoatId == box.BoatId;
		}
		return false;
	}

	public void RemoveFromWall(PumpRecord item)
	{
		item.StopForRemoval();
		PumpRecord pumpRecord = Peer(item);
		if (pumpRecord != null)
		{
			pumpRecord.Enabled = false;
			pumpRecord.Wheel = "";
		}
	}

	public void StopController(PumpRecord box)
	{
		if (box != null && box.Kind == 605)
		{
			box.Wheel = "";
			PumpRecord pumpRecord = Peer(box);
			if (pumpRecord != null)
			{
				pumpRecord.Enabled = false;
			}
		}
	}

	public void StopPump(PumpRecord pump)
	{
		if (pump != null && IsPump(pump.Kind))
		{
			pump.Enabled = false;
			PumpRecord pumpRecord = Peer(pump);
			if (pumpRecord != null)
			{
				pumpRecord.Wheel = "";
			}
		}
	}

	public void Install(PumpRecord item, int boatId)
	{
		if (boatId <= 0)
		{
			return;
		}
		if (item.BoatId != 0 && item.BoatId != boatId)
		{
			PumpRecord pumpRecord = Peer(item);
			if (pumpRecord != null)
			{
				pumpRecord.PeerId = 0;
				pumpRecord.Enabled = false;
				pumpRecord.Wheel = "";
				pumpRecord.RequirePairChoice = true;
			}
			item.PeerId = 0;
			item.RequirePairChoice = true;
			item.Enabled = false;
			item.Throttle = 0f;
			item.Wheel = "";
			item.HasPose = false;
			item.Number = 0;
		}
		item.BoatId = boatId;
		item.Mounted = true;
		if (IsPump(item.Kind) && item.Number == 0)
		{
			item.Number = AvailableNumber(boatId);
		}
	}

	private int AvailableNumber(int boatId)
	{
		HashSet<int> hashSet = new HashSet<int>();
		foreach (PumpRecord value in Items.Values)
		{
			if (IsPump(value.Kind) && value.BoatId == boatId && value.Number > 0)
			{
				hashSet.Add(value.Number);
			}
		}
		int i;
		for (i = 1; hashSet.Contains(i); i++)
		{
		}
		return i;
	}

	public void Forget(int id)
	{
		if (Items.TryGetValue(id, out var value))
		{
			PumpRecord pumpRecord = Peer(value);
			if (pumpRecord != null)
			{
				pumpRecord.PeerId = 0;
				pumpRecord.Enabled = false;
				pumpRecord.Wheel = "";
				pumpRecord.RequirePairChoice = true;
			}
			Items.Remove(id);
		}
	}

	public void RetainSavedItems(HashSet<int> savedIds)
	{
		if (!Writable || savedIds == null)
		{
			return;
		}
		foreach (int item in new List<int>(Items.Keys))
		{
			if (!savedIds.Contains(item))
			{
				Forget(item);
			}
		}
		if (MigratedNumbers)
		{
			RebuildNumbers();
		}
	}

	private void RebuildNumbers()
	{
		List<PumpRecord> list = new List<PumpRecord>();
		foreach (PumpRecord value2 in Items.Values)
		{
			if (IsPump(value2.Kind))
			{
				list.Add(value2);
			}
		}
		list.Sort((PumpRecord a, PumpRecord b) => (a.Number == b.Number) ? a.Id.CompareTo(b.Id) : a.Number.CompareTo(b.Number));
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		foreach (PumpRecord item in list)
		{
			if (item.BoatId <= 0)
			{
				item.Number = 0;
				continue;
			}
			dictionary.TryGetValue(item.BoatId, out var value);
			item.Number = value + 1;
			dictionary[item.BoatId] = item.Number;
		}
	}

	public void SafetyShutdown(int boatId)
	{
		if (!Writable || boatId <= 0)
		{
			return;
		}
		foreach (PumpRecord value in Items.Values)
		{
			if (value.Kind == 605 && value.BoatId == boatId && value.SafetyEnabled && ValidPair(value))
			{
				Peer(value).Enabled = false;
			}
		}
	}

	public List<PumpRecord> Members(PumpRecord box, Func<PumpRecord, bool> usable)
	{
		List<PumpRecord> list = new List<PumpRecord>();
		if (box == null || box.Kind != 605)
		{
			return list;
		}
		if (box.Group == 0 || !usable(box))
		{
			list.Add(box);
			return list;
		}
		foreach (PumpRecord value in Items.Values)
		{
			if (value.Kind == 605 && value.BoatId == box.BoatId && value.Group == box.Group && (value == box || usable(value)))
			{
				list.Add(value);
			}
		}
		list.Sort((PumpRecord a, PumpRecord b) => a.Id.CompareTo(b.Id));
		return list;
	}

	public void SetEnabled(PumpRecord box, bool on, Func<PumpRecord, bool> usable)
	{
		foreach (PumpRecord item in Members(box, usable))
		{
			if (ValidPair(item))
			{
				Peer(item).Enabled = on;
			}
		}
	}

	public void SetImmersion(PumpRecord box, bool required, Func<PumpRecord, bool> usable)
	{
		foreach (PumpRecord item in Members(box, usable))
		{
			if (ValidPair(item))
			{
				Peer(item).RequireImmersion = required;
			}
		}
	}

	public void SetPower(PumpRecord box, int power, Func<PumpRecord, bool> usable)
	{
		power = PumpRecord.ClampPower(power);
		foreach (PumpRecord item in Members(box, usable))
		{
			if (ValidPair(item))
			{
				Peer(item).Power = power;
			}
		}
	}

	public void SetSafety(PumpRecord box, bool armed, Func<PumpRecord, bool> usable)
	{
		foreach (PumpRecord item in Members(box, usable))
		{
			item.SafetyEnabled = armed;
		}
	}

	public bool SetWheel(PumpRecord box, bool on, Func<PumpRecord, bool> usable)
	{
		if (!ValidPair(box))
		{
			return false;
		}
		List<PumpRecord> list = Members(box, usable);
		foreach (PumpRecord value in Items.Values)
		{
			if (value.Kind == 605 && value.BoatId == box.BoatId)
			{
				value.Wheel = "";
			}
		}
		if (on)
		{
			foreach (PumpRecord item in list)
			{
				item.Wheel = "*";
			}
		}
		return true;
	}

	public void AdjustThrottle(List<PumpRecord> boxes, bool forward, bool decrease, float seconds)
	{
		PumpRecord pumpRecord = null;
		foreach (PumpRecord box in boxes)
		{
			if (ValidPair(box) && (pumpRecord == null || box.Id < pumpRecord.Id))
			{
				pumpRecord = box;
			}
		}
		if (pumpRecord == null)
		{
			return;
		}
		PumpRecord pumpRecord2 = Peer(pumpRecord);
		pumpRecord2.AdjustThrottle(forward, decrease, seconds);
		foreach (PumpRecord box2 in boxes)
		{
			if (ValidPair(box2))
			{
				Peer(box2).Throttle = pumpRecord2.Throttle;
			}
		}
	}

	public void SetGroup(PumpRecord box, int group, Func<PumpRecord, bool> usable)
	{
		if (box == null || box.Kind != 605)
		{
			return;
		}
		group = Math.Max(0, Math.Min(3, group));
		if (group == box.Group)
		{
			return;
		}
		if (box.Group != 0 && box.Wheel.Length > 0 && Members(box, usable).Count > 1)
		{
			box.Wheel = "";
		}
		box.Group = group;
		if (group == 0 || !usable(box) || !ValidPair(box))
		{
			return;
		}
		List<PumpRecord> list = Members(box, usable);
		PumpRecord pumpRecord = null;
		foreach (PumpRecord item in list)
		{
			if (item != box && ValidPair(item))
			{
				pumpRecord = item;
				break;
			}
		}
		if (pumpRecord == null)
		{
			return;
		}
		PumpRecord pumpRecord2 = Peer(pumpRecord);
		PumpRecord pumpRecord3 = Peer(box);
		pumpRecord3.Enabled = pumpRecord2.Enabled;
		pumpRecord3.RequireImmersion = pumpRecord2.RequireImmersion;
		pumpRecord3.Power = pumpRecord2.Power;
		pumpRecord3.Throttle = pumpRecord2.Throttle;
		box.SafetyEnabled = pumpRecord.SafetyEnabled;
		if (box.Wheel.Length <= 0 && pumpRecord.Wheel.Length <= 0)
		{
			return;
		}
		foreach (PumpRecord item2 in list)
		{
			item2.Wheel = "*";
		}
	}

	public void CycleGroup(PumpRecord box, Func<PumpRecord, bool> usable)
	{
		if (box != null)
		{
			SetGroup(box, (box.Group + 1) % 4, usable);
		}
	}

	public string Encode()
	{
		StringBuilder stringBuilder = new StringBuilder("JetPump/7\n");
		List<int> list = new List<int>(Items.Keys);
		list.Sort();
		foreach (int item in list)
		{
			PumpRecord pumpRecord = Items[item];
			stringBuilder.Append(pumpRecord.Id).Append('|').Append(pumpRecord.Kind)
				.Append('|')
				.Append(pumpRecord.PeerId)
				.Append('|')
				.Append(pumpRecord.BoatId)
				.Append('|')
				.Append(pumpRecord.Mounted ? 1 : 0)
				.Append('|')
				.Append(pumpRecord.Power)
				.Append('|')
				.Append(pumpRecord.Throttle.ToString("R", Invariant))
				.Append('|')
				.Append(pumpRecord.Enabled ? 1 : 0)
				.Append('|')
				.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pumpRecord.Wheel ?? "")))
				.Append('|')
				.Append(pumpRecord.RequirePairChoice ? 1 : 0)
				.Append('|')
				.Append(pumpRecord.Number)
				.Append('|')
				.Append(pumpRecord.HasPose ? 1 : 0);
			float[] array = new float[7] { pumpRecord.X, pumpRecord.Y, pumpRecord.Z, pumpRecord.Qx, pumpRecord.Qy, pumpRecord.Qz, pumpRecord.Qw };
			foreach (float num in array)
			{
				stringBuilder.Append('|').Append(num.ToString("R", Invariant));
			}
			stringBuilder.Append('|').Append(pumpRecord.RequireImmersion ? 1 : 0).Append('|')
				.Append(pumpRecord.SafetyEnabled ? 1 : 0);
			stringBuilder.Append('|').Append(pumpRecord.Group);
			stringBuilder.Append('\n');
		}
		return stringBuilder.ToString();
	}

	public static PumpState Decode(string encoded)
	{
		PumpState pumpState = new PumpState();
		if (string.IsNullOrEmpty(encoded))
		{
			return pumpState;
		}
		string[] array = encoded.Split('\n');
		bool flag = array[0].TrimEnd('\r') == "JetPump/1";
		bool flag2 = array[0].TrimEnd('\r') == "JetPump/2";
		bool flag3 = array[0].TrimEnd('\r') == "JetPump/3";
		bool flag4 = array[0].TrimEnd('\r') == "JetPump/7";
		bool flag5 = flag4 || array[0].TrimEnd('\r') == "JetPump/6";
		bool flag6 = flag5 || array[0].TrimEnd('\r') == "JetPump/4" || array[0].TrimEnd('\r') == "JetPump/5";
		if (!flag && !flag2 && !flag3 && !flag6)
		{
			pumpState.Writable = false;
			return pumpState;
		}
		pumpState.MigratedNumbers = flag | flag2;
		try
		{
			for (int i = 1; i < array.Length; i++)
			{
				if (string.IsNullOrWhiteSpace(array[i]))
				{
					continue;
				}
				string[] array2 = array[i].TrimEnd('\r').Split('|');
				if (array2.Length != (flag ? 11 : (flag4 ? 22 : (flag5 ? 21 : (flag6 ? 20 : 19)))))
				{
					throw new FormatException("Record length");
				}
				PumpRecord pumpRecord = new PumpRecord
				{
					Id = int.Parse(array2[0], Invariant),
					Kind = int.Parse(array2[1], Invariant),
					PeerId = int.Parse(array2[2], Invariant),
					BoatId = int.Parse(array2[3], Invariant),
					Mounted = (array2[4] == "1"),
					Power = int.Parse(array2[5], Invariant),
					Throttle = float.Parse(array2[6], Invariant),
					Enabled = (array2[7] == "1"),
					Wheel = Encoding.UTF8.GetString(Convert.FromBase64String(array2[8])),
					RequirePairChoice = (array2[9] == "1"),
					Number = int.Parse(array2[10], Invariant)
				};
				if (!flag)
				{
					pumpRecord.HasPose = array2[11] == "1";
					pumpRecord.X = float.Parse(array2[12], Invariant);
					pumpRecord.Y = float.Parse(array2[13], Invariant);
					pumpRecord.Z = float.Parse(array2[14], Invariant);
					pumpRecord.Qx = float.Parse(array2[15], Invariant);
					pumpRecord.Qy = float.Parse(array2[16], Invariant);
					pumpRecord.Qz = float.Parse(array2[17], Invariant);
					pumpRecord.Qw = float.Parse(array2[18], Invariant);
				}
				if (flag6)
				{
					if (array2[19] != "0" && array2[19] != "1")
					{
						throw new FormatException("Immersion switch");
					}
					pumpRecord.RequireImmersion = array2[19] == "1";
				}
				if (flag5)
				{
					if (array2[20] != "0" && array2[20] != "1")
					{
						throw new FormatException("Safety switch");
					}
					pumpRecord.SafetyEnabled = pumpRecord.Kind == 605 && array2[20] == "1";
				}
				if (flag4)
				{
					if (array2[21].Length != 1 || array2[21][0] < '0' || array2[21][0] > '3')
					{
						throw new FormatException("Control group");
					}
					pumpRecord.Group = array2[21][0] - 48;
				}
				if (pumpRecord.Id <= 0 || !IsItem(pumpRecord.Kind) || pumpState.Items.ContainsKey(pumpRecord.Id))
				{
					throw new FormatException("Record identity");
				}
				pumpRecord.Normalize();
				if (IsPump(pumpRecord.Kind) && (pumpRecord.Number < 0 || (pumpRecord.BoatId > 0 && pumpRecord.Number == 0)))
				{
					throw new FormatException("Pump label");
				}
				pumpState.Items.Add(pumpRecord.Id, pumpRecord);
			}
			if (pumpState.MigratedNumbers)
			{
				pumpState.RebuildNumbers();
			}
			HashSet<string> hashSet = new HashSet<string>();
			foreach (PumpRecord value2 in pumpState.Items.Values)
			{
				if (IsPump(value2.Kind) && value2.BoatId > 0 && !hashSet.Add(value2.BoatId + ":" + value2.Number))
				{
					throw new FormatException("Duplicate boat label");
				}
			}
			List<PumpRecord> list = new List<PumpRecord>(pumpState.Items.Values);
			list.Sort((PumpRecord a, PumpRecord b) => a.Id.CompareTo(b.Id));
			Dictionary<int, PumpRecord> dictionary = new Dictionary<int, PumpRecord>();
			foreach (PumpRecord item in list)
			{
				PumpRecord pumpRecord2 = pumpState.Peer(item);
				if (pumpRecord2 == null || !pumpRecord2.Mounted || !item.Mounted || pumpRecord2.BoatId != item.BoatId)
				{
					item.Enabled = false;
					item.Wheel = "";
				}
				if (item.Wheel.Length != 0)
				{
					if (!dictionary.TryGetValue(item.BoatId, out var value))
					{
						dictionary.Add(item.BoatId, item);
					}
					else if (value.Group == 0 || item.Group != value.Group)
					{
						item.Wheel = "";
					}
				}
			}
		}
		catch (Exception)
		{
			pumpState.Items.Clear();
			pumpState.Writable = false;
		}
		return pumpState;
	}
}

internal sealed class PumpMarkers : MonoBehaviour
{
	private struct Marker
	{
		internal Vector3 Point;

		internal Vector3 Direction;

		internal string Label;
	}

	private readonly List<Marker> markers = new List<Marker>();

	private Camera source;

	private Transform boatRoot;

	private Rigidbody body;

	private Texture2D dot;

	private GUIStyle labelStyle;

	private static readonly Color MassColor = new Color(1f, 0.84f, 0.12f);

	private static readonly Color ForceColor = new Color(0.15f, 0.95f, 1f);

	private void LateUpdate()
	{
		Refresh();
	}

	internal void Refresh()
	{
		markers.Clear();
		if (!Plugin.Ready || !Plugin.ShowMarkers || !GameState.playing || PumpWorld.Loading || GameState.currentlyLoading || GameState.recovering)
		{
			boatRoot = null;
			body = null;
			return;
		}
		Transform transform = (GameState.currentBoat ? GameState.currentBoat.parent : GameState.lastBoat);
		if (transform != boatRoot || !body)
		{
			boatRoot = transform;
			body = (transform ? transform.GetComponent<Rigidbody>() : null);
		}
		if (!body)
		{
			return;
		}
		SaveableObject component = boatRoot.GetComponent<SaveableObject>();
		if (!component)
		{
			return;
		}
		Transform transform2 = GameState.currentBoat;
		if (!transform2 || transform2.parent != boatRoot)
		{
			BoatRefs component2 = boatRoot.GetComponent<BoatRefs>();
			transform2 = (component2 ? component2.boatModel : null);
		}
		foreach (PumpRecord value in PumpWorld.State.Items.Values)
		{
			if (PumpState.IsPump(value.Kind) && value.Mounted && value.BoatId == component.sceneIndex)
			{
				PumpItem pumpItem = PumpWorld.Find(value.Id);
				if (((bool)pumpItem && pumpItem.TryThrustGeometry(out var point, out var direction)) || TrySavedGeometry(value, transform2, out point, out direction))
				{
					markers.Add(new Marker
					{
						Point = point,
						Direction = direction,
						Label = PumpState.Label(value)
					});
				}
			}
		}
	}

	internal static bool TrySavedGeometry(PumpRecord record, Transform model, out Vector3 point, out Vector3 direction)
	{
		point = (direction = Vector3.zero);
		if (!model || !record.HasPose)
		{
			return false;
		}
		GameObject gameObject = ((record.Kind == 606) ? PumpAssets.HugePump : PumpAssets.Pump);
		if (!gameObject)
		{
			return false;
		}
		Transform transform = null;
		Transform[] componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform2 in componentsInChildren)
		{
			if (transform2.name == "ThrustPoint")
			{
				transform = transform2;
				break;
			}
		}
		if (!transform)
		{
			return false;
		}
		Quaternion normalized = new Quaternion(record.Qx, record.Qy, record.Qz, record.Qw).normalized;
		Vector3 position = new Vector3(record.X, record.Y, record.Z) + normalized * gameObject.transform.InverseTransformPoint(transform.position);
		point = model.TransformPoint(position) + model.up * Plugin.VerticalOffset;
		direction = -model.TransformDirection(normalized * gameObject.transform.InverseTransformDirection(transform.forward)).normalized;
		return true;
	}

	private void OnGUI()
	{
		if (Event.current.type != EventType.Repaint || !Plugin.Ready || !Plugin.ShowMarkers || !body || !GameState.playing || PumpWorld.Loading || GameState.currentlyLoading || GameState.recovering)
		{
			return;
		}
		if (!source || !source.isActiveAndEnabled)
		{
			source = Camera.main;
		}
		if (!source)
		{
			return;
		}
		if (!dot)
		{
			MakeDot();
		}
		if (labelStyle == null)
		{
			labelStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 28,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleLeft
			};
		}
		int depth = GUI.depth;
		Color color = GUI.color;
		GUI.depth = 1000;
		DrawMarker(body.worldCenterOfMass, Vector3.zero, "Center of mass", MassColor);
		foreach (Marker marker in markers)
		{
			DrawMarker(marker.Point, marker.Direction, marker.Label, ForceColor);
		}
		GUI.color = color;
		GUI.depth = depth;
	}

	private bool Project(Vector3 world, out Vector2 screen)
	{
		Vector3 vector = source.WorldToScreenPoint(world);
		screen = new Vector2(vector.x, (float)Screen.height - vector.y);
		if (vector.z > source.nearClipPlane && vector.x >= 0f && vector.x <= (float)Screen.width && vector.y >= 0f)
		{
			return vector.y <= (float)Screen.height;
		}
		return false;
	}

	private void DrawMarker(Vector3 point, Vector3 direction, string label, Color color)
	{
		if (!Project(point, out var screen))
		{
			return;
		}
		if (direction.sqrMagnitude > 0.01f && Project(point + direction, out var screen2))
		{
			Vector2 vector = screen2 - screen;
			if (vector.sqrMagnitude > 4f)
			{
				Vector2 vector2 = screen + vector.normalized * 76f;
				Line(screen, vector2, color);
				Vector2 vector3 = -vector.normalized * 16f;
				Vector2 vector4 = new Vector2(0f - vector3.y, vector3.x) * 0.5f;
				Line(vector2, vector2 + vector3 + vector4, color);
				Line(vector2, vector2 + vector3 - vector4, color);
			}
		}
		GUI.color = Color.black;
		GUI.DrawTexture(new Rect(screen.x - 18f, screen.y - 18f, 36f, 36f), dot);
		GUI.color = color;
		GUI.DrawTexture(new Rect(screen.x - 12f, screen.y - 12f, 24f, 24f), dot);
		GUI.color = Color.white;
		labelStyle.normal.textColor = Color.black;
		GUI.Label(new Rect(screen.x + 24f, screen.y - 20f, 300f, 48f), label, labelStyle);
		labelStyle.normal.textColor = color;
		GUI.Label(new Rect(screen.x + 22f, screen.y - 22f, 300f, 48f), label, labelStyle);
	}

	private static void Line(Vector2 from, Vector2 to, Color color)
	{
		Matrix4x4 matrix = GUI.matrix;
		GUI.color = color;
		GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y - from.y, to.x - from.x) * 57.29578f, from);
		GUI.DrawTexture(new Rect(from.x, from.y - 2f, Vector2.Distance(from, to), 4f), Texture2D.whiteTexture);
		GUI.matrix = matrix;
	}

	private void MakeDot()
	{
		dot = new Texture2D(32, 32, TextureFormat.RGBA32, mipChain: false)
		{
			name = "PropellerMarker",
			hideFlags = HideFlags.HideAndDontSave
		};
		for (int i = 0; i < 32; i++)
		{
			for (int j = 0; j < 32; j++)
			{
				float num = Vector2.Distance(new Vector2(j, i), new Vector2(15.5f, 15.5f));
				dot.SetPixel(j, i, new Color(1f, 1f, 1f, Mathf.Clamp01(15.5f - num)));
			}
		}
		dot.Apply(updateMipmaps: false, makeNoLongerReadable: true);
	}

	private void OnDisable()
	{
		markers.Clear();
		source = null;
		boatRoot = null;
		body = null;
		if ((bool)dot)
		{
			Object.Destroy(dot);
		}
		dot = null;
	}
}
