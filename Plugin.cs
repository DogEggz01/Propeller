using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace JetPump
{
    // Plugin setup, configuration, asset registration and persistent world state.

    [BepInPlugin(Guid, "Propeller", "1.2.3")]
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
        internal static bool ShowMarkers => showMarkers != null && showMarkers.Value;
        internal static float VerticalOffset => Mathf.Round(Finite(waterOffset, 0f, -2f, 2f) * 100f) / 100f;
        private static float Finite(ConfigEntry<float> entry, float fallback, float min, float max)
        {
            float value = entry == null ? fallback : entry.Value;
            return PumpRecord.Finite(value) ? Mathf.Clamp(value, min, max) : fallback;
        }
        internal static float TrimAngle
        {
            get { return Finite(trimAngle, 80f, 0f, 180f); }
        }
        internal static void BindSettings(ConfigFile config)
        {
            if (waterOffset != null) waterOffset.SettingChanged -= SnapOffset;
            waterOffset = config.Bind("Propulsion", "Vertical offset (metres)", 0f, new ConfigDescription(
                "Moves thrust and all water-detection points together along the boat's vertical axis. Positive raises, negative lowers. Steps of 0.01 m; model position stays unchanged.",
                new AcceptableValueRange<float>(-2f, 2f), new ConfigurationManagerAttributes { ShowRangeAsPercent = false, CustomDrawer = DrawOffset }));
            waterOffset.SettingChanged += SnapOffset;
            SnapOffset(waterOffset, EventArgs.Empty);
            if (showMarkers != null) showMarkers.SettingChanged -= MarkersChanged;
            showMarkers = config.Bind("Display", "Show propulsion markers", false, "Show the current/last boat's live physics centre of mass and installed propellers' thrust points and directions, through the hull, inside or outside the shipyard.");
            showMarkers.SettingChanged += MarkersChanged;
            trimAngle = config.Bind("Trim", "Bow-up angle (degrees)", 80f, new ConfigDescription(
                "Average bow-up target during forward propulsion, shared by all propellers on a boat. Zero targets level with pitch compensation, 90 vertical and 180 inverted. Values above 90 permit intentional inversion.",
                new AcceptableValueRange<float>(0f, 180f), new ConfigurationManagerAttributes { ShowRangeAsPercent = false }));
        }
        private static void MarkersChanged(object sender, EventArgs args)
        {
            if (PumpMarkers.Instance) PumpMarkers.Instance.enabled = ShowMarkers;
        }
        private static void SnapOffset(object sender, EventArgs args)
        {
            var entry = (ConfigEntry<float>)sender;
            float value = Mathf.Round(Finite(entry, 0f, -2f, 2f) * 100f) / 100f;
            if (entry.Value != value) entry.Value = value;
        }
        private static void DrawOffset(ConfigEntryBase entry)
        {
            var offset = (ConfigEntry<float>)entry;
            GUILayout.BeginHorizontal();
            float value = GUILayout.HorizontalSlider(offset.Value, -2f, 2f, GUILayout.MinWidth(130f));
            if (GUILayout.Button("−", GUILayout.Width(25f))) value -= .01f;
            if (GUILayout.Button("+", GUILayout.Width(25f))) value += .01f;
            value = Mathf.Clamp(Mathf.Round(value * 100f) / 100f, -2f, 2f);
            GUILayout.Label(value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " m", GUILayout.Width(70f));
            if (value != offset.Value) offset.Value = value;
            GUILayout.EndHorizontal();
        }
        private Harmony harmony;
        private void Awake()
        {
            Log = Logger;
            BindSettings(Config);
            try
            {
                PumpAssets.Load(Path.GetDirectoryName(Info.Location));
                harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                PumpInput.PatchCheatspeed(harmony);
                PumpTiller.Patch(harmony);
                gameObject.AddComponent<PairingMenu>();
                gameObject.AddComponent<PumpMarkers>();
                Ready = true;
                Logger.LogInfo("Propeller 1.2.3 loaded: Propeller 604 (30,000 N), controller 605, Huge Propeller 606 (800,000 N).");
            }
            catch (Exception e)
            {
                Ready = false;
                harmony?.UnpatchSelf();
                PumpAssets.Unload();
                Logger.LogError("Propeller initialization aborted: " + e);
            }
        }
        private void OnDestroy()
        {
            Ready = false;
            if (waterOffset != null) waterOffset.SettingChanged -= SnapOffset;
            if (showMarkers != null) showMarkers.SettingChanged -= MarkersChanged;
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
        internal static GameObject Pump, Controller, HugePump;
        internal static void Load(string folder)
        {
            string path = Path.Combine(folder, "assets", BundleName);
            Bundle = AssetBundle.LoadFromFile(path);
            if (!Bundle) throw new FileNotFoundException("Propeller AssetBundle could not be loaded", path);
            Pump = Bundle.LoadAsset<GameObject>("Assets/JetPump/Generated/604 propeller.prefab");
            Controller = Bundle.LoadAsset<GameObject>("Assets/JetPump/Generated/605 Pump Controller.prefab");
            HugePump = Bundle.LoadAsset<GameObject>("Assets/JetPump/Generated/606 Huge Propeller.prefab");
            Validate(HugePump, PumpState.HugePumpId);
            Validate(Pump, PumpState.PumpId);
            Validate(Controller, PumpState.ControllerId);
        }
        private static void Validate(GameObject prefab, int id)
        {
            if (!prefab || !prefab.GetComponent<ShipItem>() || !prefab.GetComponent<SaveablePrefab>() ||
                prefab.GetComponent<SaveablePrefab>().prefabIndex != id || !prefab.GetComponent<ShipItem>().wallAttachment || !prefab.transform.Find(Contract))
                throw new InvalidOperationException("Invalid bundled item " + id);
        }
        internal static bool IsOurItem(ShipItem item)
        {
            var save = item.GetComponent<SaveablePrefab>();
            return save && PumpState.IsItem(save.prefabIndex) &&
                item.transform.Find(Contract);
        }
        internal static void Register(PrefabsDirectory directory)
        {
            var entries = directory.directory;
            if (entries == null) throw new InvalidOperationException("Missing prefab directory");
            foreach (int id in new[] { PumpState.PumpId, PumpState.ControllerId, PumpState.HugePumpId })
            {
                var expected = id == PumpState.PumpId ? Pump : id == PumpState.ControllerId ? Controller : HugePump;
                if (id < entries.Length && entries[id] && entries[id] != expected)
                    throw new InvalidOperationException("Propeller refuses occupied prefab " + id + " (" + entries[id].name + ").");
            }
            if (entries.Length <= PumpState.HugePumpId) Array.Resize(ref entries, PumpState.HugePumpId + 1);
            entries[PumpState.PumpId] = Pump;
            entries[PumpState.ControllerId] = Controller;
            entries[PumpState.HugePumpId] = HugePump;
            directory.directory = entries;
        }
        internal static void Unload() { if (Bundle) Bundle.Unload(false); Bundle = null; Pump = Controller = HugePump = null; }
    }

    [HarmonyPatch(typeof(PrefabsDirectory), "Start")]
    internal static class RegisterPumpPrefabs
    {
        private static void Prefix(PrefabsDirectory __instance)
        {
            try { PumpAssets.Register(__instance); }
            catch (Exception e) { Plugin.Ready = false; Plugin.Log.LogError(e); }
        }
    }

    [HarmonyPatch(typeof(ShipItem), "Awake")]
    internal static class AddPumpRuntime
    {
        private static void Prefix(ShipItem __instance)
        {
            if (Plugin.Ready && PumpAssets.IsOurItem(__instance) && !__instance.GetComponent<PumpItem>())
                __instance.gameObject.AddComponent<PumpItem>();
        }
    }

    // Configuration Manager reads this optional metadata by name; no dependency.
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
        // Our items by their ShipItem, for the patches that run on every item in the game.
        internal static readonly Dictionary<ShipItem, PumpItem> ByItem = new Dictionary<ShipItem, PumpItem>(new SameItem());
        private sealed class SameItem : IEqualityComparer<ShipItem>
        {
            public bool Equals(ShipItem a, ShipItem b) => ReferenceEquals(a, b);
            public int GetHashCode(ShipItem item) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item);
        }
        internal static bool SimulationAllowed => Plugin.Ready && !Loading && State.Writable && GameState.playing &&
            !GameState.currentlyLoading && !GameState.loadingBoatLocalItems && !GameState.justStarted &&
            !GameState.recovering && !GameState.currentShipyard && Time.timeScale > 0f;
        internal static bool InputAllowed => SimulationAllowed && !GameState.sleeping && !GameState.inBed &&
            !GameState.inCursorMenu && !GameState.wasInSettingsMenu && !BoatCamera.on && !PairingMenu.IsOpen;

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
            bool completedGameLoad = Loading;
            string data = null;
            GameState.modData?.TryGetValue(SaveKey, out data);
            State = PumpState.Decode(data);
            if (completedGameLoad && State.Writable) ReconcileSavedNumbers();
            Session++;
            Loading = false;
            if (!State.Writable) Plugin.Log.LogError("JetPump save data is unsupported or invalid. Original data retained; propulsion disabled for this session.");
        }
        internal static void ReconcileSavedNumbers()
        {
            var manager = SaveLoadManager.instance;
            if (!manager) return;
            var saved = manager.GetCurrentPrefabs();
            var objects = AccessTools.Field(typeof(SaveLoadManager), "currentObjects").GetValue(manager) as SaveableObject[];
            if (saved == null || objects == null) return;
            var ids = new HashSet<int>();
            foreach (var item in saved) if (item) ids.Add(item.instanceId);
            foreach (var boat in objects)
            {
                if (!boat || !boat.localItems) continue;
                var cached = boat.localItems.GetCachedItems();
                if (cached != null) foreach (var item in cached) ids.Add(item.instanceId);
            }
            // Called after vanilla loads both registered and cached items, never
            // inferred from the live/visible objects during normal simulation.
            State.RetainSavedItems(ids);
        }
        internal static void Save()
        {
            if (!State.Writable || Loading) return;
            foreach (var item in Live) if (item) { item.EnforceNails(); item.Mount.CapturePose(); }
            if (GameState.modData == null) GameState.modData = new Dictionary<string, string>();
            GameState.modData[SaveKey] = State.Encode();
        }
        internal static PumpItem Find(int id)
        {
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] && !Live[i].Retiring && !Live[i].Mount.Cached && Live[i].isActiveAndEnabled && Live[i].Saveable && Live[i].Saveable.instanceId == id) return Live[i];
            return null;
        }
        internal static void RetireDuplicate(PumpItem incoming, int id)
        {
            if (id <= 0) return;
            // A native cached/save load is authoritative for this exact saved identity.
            // Never compare pump label numbers or remove another independently spawned item.
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var old = Live[i];
                if (!old || old == incoming || old.Retiring || !old.Saveable || old.Saveable.instanceId != id || old.Saveable.prefabIndex != incoming.Saveable.prefabIndex) continue;
                old.Retiring = true;
                old.Item.DestroyItem(); // Normal proxy/mass/save-list cleanup; retain the shared record.
            }
        }
        internal static int BoatId(Transform actualBoat)
        {
            var save = actualBoat ? actualBoat.GetComponentInParent<SaveableObject>() : null;
            return save ? save.sceneIndex : 0;
        }
        internal static bool PairOperational(PumpItem box, out PumpItem pump)
        {
            pump = null;
            if (!box) return false;
            box.EnforceNails();
            if (!box.CanOperate || !State.ValidPair(box.Record)) return false;
            pump = Find(box.Record.PeerId);
            if (pump) pump.EnforceNails();
            return pump && pump.Installed && pump.Item.nailed && pump.Item.currentActualBoat == box.Item.currentActualBoat;
        }
        internal static void AutoPair(PumpItem box)
        {
            if (!box || !box.CanOperate || box.Record.PeerId != 0 || box.Record.RequirePairChoice) return;
            PumpItem candidate = null;
            foreach (var other in Live)
            {
                if (!other || !other.IsPump || !other.Installed || other.Record.PeerId != 0 || other.Record.RequirePairChoice || other.Item.currentActualBoat != box.Item.currentActualBoat) continue;
                if (candidate) return; // Ambiguity requires the setup choice, never collection order.
                candidate = other;
            }
            if (candidate) State.Pair(box.Record.Id, candidate.Record.Id, box.Record.BoatId);
        }
        // A controller takes part in shared changes only while it can be operated.
        internal static bool Usable(PumpRecord record)
        {
            var box = record == null ? null : Find(record.Id);
            return box && PairOperational(box, out _);
        }

        // Optional mods: read only an assembly that is already loaded, so a missing mod logs nothing
        // (AccessTools.TypeByName warns for every type it can't find).
        internal static Type OptionalType(string assembly, string type)
        {
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                if (loaded.GetName().Name == assembly) return loaded.GetType(type, false);
            return null;
        }

        // Helms: the game's wheel, and the Dinghies tiller when that mod is loaded (PumpTiller).
        internal static Type TillerType;
        internal static FieldInfo TillerRudder;
        internal static bool IsHelm(GoPointerButton button) =>
            button && (button is GPButtonSteeringWheel || (TillerType != null && TillerType.IsInstanceOfType(button)));
        // A helm belongs to the boat whose hull its rudder turns, so a cutter carried on a ship keeps its own tiller.
        internal static Rigidbody HelmBody(GoPointerButton helm)
        {
            if (!IsHelm(helm)) return null;
            var wheel = helm as GPButtonSteeringWheel;
            Rudder rudder = null;
            if (wheel) rudder = wheel.attachedRudder ? wheel.attachedRudder.GetComponent<Rudder>() : null;
            else
            {
                rudder = TillerRudder?.GetValue(helm) as Rudder;
                if (!rudder && helm.transform.parent) rudder = helm.transform.parent.GetComponent<Rudder>();
            }
            if (rudder && rudder.shipRigidbody) return rudder.shipRigidbody;
            return wheel ? wheel.GetComponentInParent<Rigidbody>() : null;
        }
        internal static Rigidbody BoatBody(PumpItem box)
        {
            var actual = box ? box.Item.currentActualBoat : null;
            return actual && actual.parent ? actual.parent.GetComponent<Rigidbody>() : null;
        }
        internal static bool HasHelm(PumpItem box)
        {
            var body = BoatBody(box);
            if (!body) return false;
            foreach (var button in box.Item.currentActualBoat.parent.GetComponentsInChildren<GoPointerButton>(false))
                if (IsHelm(button) && HelmBody(button) == body) return true;
            return false;
        }
        // Working controllers with wheel control on the helm's boat. The list is reused; don't keep it.
        private static readonly List<PumpItem> linked = new List<PumpItem>();
        internal static List<PumpItem> Linked(GoPointerButton helm)
        {
            linked.Clear();
            var body = HelmBody(helm);
            if (!body) return linked;
            foreach (var box in Live)
                if (box && !box.IsPump && box.Record != null && box.Record.Wheel.Length > 0 && BoatBody(box) == body && PairOperational(box, out _))
                    linked.Add(box);
            return linked;
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "Awake")]
    internal static class PumpSessionStart { private static void Prefix() { PumpWorld.Reset(); } }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadGame))]
    internal static class PumpBeginLoad
    {
        // Another mod's prefix can refuse the load: the session then stays as it is.
        private static void Prefix(bool __runOriginal)
        {
            if (!__runOriginal) return;
            PumpWorld.Reset();
            PumpWorld.Loading = true;
            GameState.modData?.Remove(PumpWorld.SaveKey);
        }
    }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadModData))]
    internal static class PumpLoad { private static void Postfix() { PumpWorld.Load(); } }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.SaveModData))]
    internal static class PumpSave { private static void Prefix() { PumpWorld.Save(); } }

    // Plain state is independent of Unity objects and survives boat-local unloading.
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
        // Controller only: CONTROL GROUP slider, 0 = NONE, 1-3 share switches with the same group on this boat.
        public int Group;
        // Boat-model-local pose, independent of recovery's temporary world rotation.
        public bool HasPose;
        public float X, Y, Z, Qx, Qy, Qz, Qw = 1f;
        public float AppliedPower => Enabled ? Power * Throttle : 0f;

        public void Normalize()
        {
            Power = ClampPower(Power);
            Throttle = Finite(Throttle) ? Math.Max(0f, Math.Min(1f, Throttle)) : 0f;
            if (PeerId < 0) PeerId = 0;
            if (BoatId <= 0) { BoatId = 0; Mounted = false; }
            Wheel = Wheel ?? "";
            // A saved wheel path (older saves) loads as a link to every wheel and tiller of the boat.
            if (Wheel.Length > 0) Wheel = PumpState.AllHelms;
            Group = Kind == PumpState.ControllerId ? Math.Max(0, Math.Min(PumpState.GroupCount, Group)) : 0;
            if (!Mounted) { Enabled = false; Wheel = ""; }
            if (!Mounted || !Finite(X) || !Finite(Y) || !Finite(Z) || !Finite(Qx) || !Finite(Qy) || !Finite(Qz) || !Finite(Qw) ||
                Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw < .01f) HasPose = false;
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static int ClampPower(int power) => Math.Max(5, Math.Min(100, (int)Math.Round(power / 5.0) * 5));
        public void AdjustThrottle(bool forward, bool decrease, float seconds)
        {
            if (forward == decrease || seconds <= 0f || !Finite(seconds)) return;
            Throttle = Math.Max(0f, Math.Min(1f, Throttle + (forward ? 1f : -1f) * seconds * 0.2f));
        }
        public void StopForRemoval() { Enabled = false; Mounted = false; Wheel = ""; HasPose = false; }
    }

    public sealed class PumpState
    {
        public const int PumpId = 604;
        public const int ControllerId = 605;
        public const int HugePumpId = 606;
        public const string AllHelms = "*";
        public const int GroupCount = 3;
        public static bool IsPump(int kind) => kind == PumpId || kind == HugePumpId;
        public static bool IsItem(int kind) => IsPump(kind) || kind == ControllerId;
        public static float RatedThrust(int kind) => kind == HugePumpId ? 800000f : kind == PumpId ? 30000f : 0f;
        public static string Label(PumpRecord pump) => (pump.Kind == HugePumpId ? "H.PROP " : "PROP ") + pump.Number.ToString("00");
        public readonly Dictionary<int, PumpRecord> Items = new Dictionary<int, PumpRecord>();
        public bool Writable = true;
        public bool MigratedNumbers;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public PumpRecord GetOrCreate(int id, int kind)
        {
            if (id <= 0 || !IsItem(kind)) throw new ArgumentException("Invalid item identity.");
            if (!Items.TryGetValue(id, out var record))
            {
                Items.Add(id, record = new PumpRecord { Id = id, Kind = kind, SafetyEnabled = kind == ControllerId });
            }
            if (record.Kind != kind) throw new InvalidOperationException("Saved item identity belongs to a different prefab.");
            return record;
        }

        public PumpRecord Peer(PumpRecord item)
        {
            if (item != null && Items.TryGetValue(item.PeerId, out var other) && other.PeerId == item.Id && ((item.Kind == ControllerId && IsPump(other.Kind)) || (other.Kind == ControllerId && IsPump(item.Kind))))
                return other;
            return null;
        }

        public bool Pair(int controllerId, int pumpId, int boatId)
        {
            if (!Items.TryGetValue(controllerId, out var box) || !Items.TryGetValue(pumpId, out var pump) ||
                box.Kind != ControllerId || !IsPump(pump.Kind) || boatId <= 0 ||
                !box.Mounted || !pump.Mounted || box.BoatId != boatId || pump.BoatId != boatId) return false;
            if (pump.PeerId != 0 && pump.PeerId != controllerId)
            {
                var previousBox = Peer(pump);
                if (previousBox != null && previousBox.Mounted) return false;
                if (previousBox != null) { previousBox.PeerId = 0; previousBox.Wheel = ""; previousBox.RequirePairChoice = true; }
            }
            var old = Peer(box);
            if (old != null && old != pump) { old.PeerId = 0; old.Enabled = false; }
            box.PeerId = pump.Id;
            pump.PeerId = box.Id;
            box.Wheel = "";
            box.RequirePairChoice = false;
            pump.RequirePairChoice = false;
            pump.Enabled = false;
            pump.Throttle = 0f;
            return true;
        }

        public bool ValidPair(PumpRecord box)
        {
            var pump = Peer(box);
            return box != null && box.Kind == ControllerId && pump != null && box.Mounted && pump.Mounted &&
                   box.BoatId > 0 && pump.BoatId == box.BoatId;
        }

        public void RemoveFromWall(PumpRecord item)
        {
            item.StopForRemoval();
            var peer = Peer(item);
            if (peer != null) { peer.Enabled = false; peer.Wheel = ""; }
        }

        // Unnailing disables controls without discarding the physical mount or its pair.
        public void StopController(PumpRecord box)
        {
            if (box == null || box.Kind != ControllerId) return;
            box.Wheel = "";
            var pump = Peer(box);
            if (pump != null) pump.Enabled = false;
        }

        public void StopPump(PumpRecord pump)
        {
            if (pump == null || !IsPump(pump.Kind)) return;
            pump.Enabled = false;
            var box = Peer(pump);
            if (box != null) box.Wheel = "";
        }

        public void Install(PumpRecord item, int boatId)
        {
            if (boatId <= 0) return;
            if (item.BoatId != 0 && item.BoatId != boatId)
            {
                var peer = Peer(item);
                if (peer != null) { peer.PeerId = 0; peer.Enabled = false; peer.Wheel = ""; peer.RequirePairChoice = true; }
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
            if (IsPump(item.Kind) && item.Number == 0) item.Number = AvailableNumber(boatId);
        }

        private int AvailableNumber(int boatId)
        {
            var used = new HashSet<int>();
            foreach (var r in Items.Values) if (IsPump(r.Kind) && r.BoatId == boatId && r.Number > 0) used.Add(r.Number);
            int number = 1;
            while (used.Contains(number)) number++;
            return number;
        }

        public void Forget(int id)
        {
            if (!Items.TryGetValue(id, out var item)) return;
            var peer = Peer(item);
            if (peer != null) { peer.PeerId = 0; peer.Enabled = false; peer.Wheel = ""; peer.RequirePairChoice = true; }
            Items.Remove(id);
        }

        public void RetainSavedItems(HashSet<int> savedIds)
        {
            if (!Writable || savedIds == null) return;
            foreach (int id in new List<int>(Items.Keys)) if (!savedIds.Contains(id)) Forget(id);
            if (MigratedNumbers) RebuildNumbers();
        }

        private void RebuildNumbers()
        {
            var pumps = new List<PumpRecord>();
            foreach (var r in Items.Values) if (IsPump(r.Kind)) pumps.Add(r);
            pumps.Sort((a,b) => a.Number != b.Number ? a.Number.CompareTo(b.Number) : a.Id.CompareTo(b.Id));
            var next = new Dictionary<int,int>();
            foreach (var r in pumps)
            {
                if (r.BoatId <= 0) { r.Number = 0; continue; }
                next.TryGetValue(r.BoatId, out int previous);
                r.Number = previous + 1; next[r.BoatId] = r.Number;
            }
        }

        public void SafetyShutdown(int boatId)
        {
            if (!Writable || boatId <= 0) return;
            foreach (var box in Items.Values)
                if (box.Kind == ControllerId && box.BoatId == boatId && box.SafetyEnabled && ValidPair(box))
                    Peer(box).Enabled = false; // Retain throttle, power, pairing and wheel assignment.
        }

        // Control groups. `usable` says whether a controller can be operated now (live: hammered with a working
        // pair; state tests: ValidPair). A panel that can't be used keeps its group but takes no shared change.
        public List<PumpRecord> Members(PumpRecord box, Func<PumpRecord, bool> usable)
        {
            var members = new List<PumpRecord>();
            if (box == null || box.Kind != ControllerId) return members;
            if (box.Group == 0 || !usable(box)) { members.Add(box); return members; }
            foreach (var r in Items.Values)
                if (r.Kind == ControllerId && r.BoatId == box.BoatId && r.Group == box.Group && (r == box || usable(r))) members.Add(r);
            members.Sort((a, b) => a.Id.CompareTo(b.Id));
            return members;
        }

        public void SetEnabled(PumpRecord box, bool on, Func<PumpRecord, bool> usable)
        {
            foreach (var m in Members(box, usable)) if (ValidPair(m)) Peer(m).Enabled = on;
        }
        public void SetImmersion(PumpRecord box, bool required, Func<PumpRecord, bool> usable)
        {
            foreach (var m in Members(box, usable)) if (ValidPair(m)) Peer(m).RequireImmersion = required;
        }
        public void SetPower(PumpRecord box, int power, Func<PumpRecord, bool> usable)
        {
            power = PumpRecord.ClampPower(power);
            foreach (var m in Members(box, usable)) if (ValidPair(m)) Peer(m).Power = power;
        }
        public void SetSafety(PumpRecord box, bool armed, Func<PumpRecord, bool> usable)
        {
            foreach (var m in Members(box, usable)) m.SafetyEnabled = armed;
        }

        // Wheel control: one holder per boat, a whole group or one NONE panel. The link covers every helm.
        public bool SetWheel(PumpRecord box, bool on, Func<PumpRecord, bool> usable)
        {
            if (!ValidPair(box)) return false;
            var members = Members(box, usable);
            foreach (var other in Items.Values)
                if (other.Kind == ControllerId && other.BoatId == box.BoatId) other.Wheel = "";
            if (on) foreach (var m in members) m.Wheel = AllHelms;
            return true;
        }

        // W/S on a linked helm: every linked pump follows the lowest-ID panel's throttle, so a group stays equal.
        public void AdjustThrottle(List<PumpRecord> boxes, bool forward, bool decrease, float seconds)
        {
            PumpRecord lead = null;
            foreach (var box in boxes) if (ValidPair(box) && (lead == null || box.Id < lead.Id)) lead = box;
            if (lead == null) return;
            var leadPump = Peer(lead);
            leadPump.AdjustThrottle(forward, decrease, seconds);
            foreach (var box in boxes) if (ValidPair(box)) Peer(box).Throttle = leadPump.Throttle;
        }

        // The slider steps NONE, 1, 2, 3. Leaving keeps the panel's settings; wheel control stays with the group
        // left behind. Joining copies the group (lowest-ID usable member); the wheel goes with either side.
        public void SetGroup(PumpRecord box, int group, Func<PumpRecord, bool> usable)
        {
            if (box == null || box.Kind != ControllerId) return;
            group = Math.Max(0, Math.Min(GroupCount, group));
            if (group == box.Group) return;
            if (box.Group != 0 && box.Wheel.Length > 0 && Members(box, usable).Count > 1) box.Wheel = "";
            box.Group = group;
            if (group == 0 || !usable(box) || !ValidPair(box)) return;
            var members = Members(box, usable);
            PumpRecord lead = null;
            foreach (var m in members) if (m != box && ValidPair(m)) { lead = m; break; }
            if (lead == null) return;
            var from = Peer(lead);
            var to = Peer(box);
            to.Enabled = from.Enabled; to.RequireImmersion = from.RequireImmersion; to.Power = from.Power; to.Throttle = from.Throttle;
            box.SafetyEnabled = lead.SafetyEnabled;
            bool linked = box.Wheel.Length > 0 || lead.Wheel.Length > 0;
            if (linked) foreach (var m in members) m.Wheel = AllHelms;
        }
        public void CycleGroup(PumpRecord box, Func<PumpRecord, bool> usable) { if (box != null) SetGroup(box, (box.Group + 1) % (GroupCount + 1), usable); }

        public string Encode()
        {
            var text = new StringBuilder("JetPump/7\n");
            var keys = new List<int>(Items.Keys);
            keys.Sort();
            foreach (int key in keys)
            {
                var r = Items[key];
                text.Append(r.Id).Append('|').Append(r.Kind).Append('|').Append(r.PeerId).Append('|').Append(r.BoatId).Append('|')
                    .Append(r.Mounted ? 1 : 0).Append('|').Append(r.Power).Append('|').Append(r.Throttle.ToString("R", Invariant)).Append('|')
                    .Append(r.Enabled ? 1 : 0).Append('|').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(r.Wheel ?? ""))).Append('|')
                    .Append(r.RequirePairChoice ? 1 : 0).Append('|').Append(r.Number).Append('|').Append(r.HasPose ? 1 : 0);
                foreach (float value in new[] { r.X, r.Y, r.Z, r.Qx, r.Qy, r.Qz, r.Qw }) text.Append('|').Append(value.ToString("R", Invariant));
                text.Append('|').Append(r.RequireImmersion ? 1 : 0).Append('|').Append(r.SafetyEnabled ? 1 : 0);
                text.Append('|').Append(r.Group);
                text.Append('\n');
            }
            return text.ToString();
        }

        public static PumpState Decode(string encoded)
        {
            var result = new PumpState();
            if (string.IsNullOrEmpty(encoded)) return result;
            var lines = encoded.Split('\n');
            bool legacy = lines[0].TrimEnd('\r') == "JetPump/1";
            bool previousFormat = lines[0].TrimEnd('\r') == "JetPump/2";
            bool boatNumbers = lines[0].TrimEnd('\r') == "JetPump/3";
            bool groups = lines[0].TrimEnd('\r') == "JetPump/7";
            bool safety = groups || lines[0].TrimEnd('\r') == "JetPump/6";
            bool immersion = safety || lines[0].TrimEnd('\r') == "JetPump/4" || lines[0].TrimEnd('\r') == "JetPump/5";
            if (!legacy && !previousFormat && !boatNumbers && !immersion) { result.Writable = false; return result; }
            result.MigratedNumbers = legacy || previousFormat;
            try
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = lines[i].TrimEnd('\r').Split('|');
                    if (f.Length != (legacy ? 11 : groups ? 22 : safety ? 21 : immersion ? 20 : 19)) throw new FormatException("Record length");
                    var r = new PumpRecord {
                        Id = int.Parse(f[0], Invariant), Kind = int.Parse(f[1], Invariant), PeerId = int.Parse(f[2], Invariant),
                        BoatId = int.Parse(f[3], Invariant), Mounted = f[4] == "1", Power = int.Parse(f[5], Invariant),
                        Throttle = float.Parse(f[6], Invariant), Enabled = f[7] == "1", Wheel = Encoding.UTF8.GetString(Convert.FromBase64String(f[8])), RequirePairChoice = f[9] == "1", Number = int.Parse(f[10], Invariant) };
                    if (!legacy)
                    {
                        r.HasPose = f[11] == "1";
                        r.X = float.Parse(f[12], Invariant); r.Y = float.Parse(f[13], Invariant); r.Z = float.Parse(f[14], Invariant);
                        r.Qx = float.Parse(f[15], Invariant); r.Qy = float.Parse(f[16], Invariant);
                        r.Qz = float.Parse(f[17], Invariant); r.Qw = float.Parse(f[18], Invariant);
                    }
                    if (immersion)
                    {
                        if (f[19] != "0" && f[19] != "1") throw new FormatException("Immersion switch");
                        r.RequireImmersion = f[19] == "1";
                    }
                    if (safety)
                    {
                        if (f[20] != "0" && f[20] != "1") throw new FormatException("Safety switch");
                        r.SafetyEnabled = r.Kind == ControllerId && f[20] == "1";
                    }
                    if (groups)
                    {
                        if (f[21].Length != 1 || f[21][0] < '0' || f[21][0] > '0' + GroupCount) throw new FormatException("Control group");
                        r.Group = f[21][0] - '0';
                    }
                    if (r.Id <= 0 || !IsItem(r.Kind) || result.Items.ContainsKey(r.Id)) throw new FormatException("Record identity");
                    r.Normalize();
                    if (IsPump(r.Kind) && (r.Number < 0 || (r.BoatId > 0 && r.Number == 0))) throw new FormatException("Pump label");
                    result.Items.Add(r.Id, r);
                }
                if (result.MigratedNumbers) result.RebuildNumbers();
                var labels = new HashSet<string>();
                foreach (var r in result.Items.Values)
                    if (IsPump(r.Kind) && r.BoatId > 0 && !labels.Add(r.BoatId + ":" + r.Number)) throw new FormatException("Duplicate boat label");
                var ordered = new List<PumpRecord>(result.Items.Values);
                ordered.Sort((a, b) => a.Id.CompareTo(b.Id));
                var holders = new Dictionary<int, PumpRecord>();
                foreach (var r in ordered)
                {
                    var peer = result.Peer(r);
                    if (peer == null || !peer.Mounted || !r.Mounted || peer.BoatId != r.BoatId) { r.Enabled = false; r.Wheel = ""; }
                    if (r.Wheel.Length == 0) continue;
                    // One wheel-control holder per boat: the lowest-ID linked panel's group (or that panel alone).
                    if (!holders.TryGetValue(r.BoatId, out var holder)) holders.Add(r.BoatId, r);
                    else if (holder.Group == 0 || r.Group != holder.Group) r.Wheel = "";
                }
            }
            catch (Exception)
            {
                // Retain the original payload in GameState.modData; never overwrite a future/corrupt schema.
                result.Items.Clear();
                result.Writable = false;
            }
            return result;
        }
    }
    // Screen-projected diagnostic markers remain visible through hull and water.
    // No physics colliders, extra scene cameras, or SailBalance dependency.
    internal sealed class PumpMarkers : MonoBehaviour
    {
        private struct Marker { internal Vector3 Point, Direction; internal string Label; }
        private readonly List<Marker> markers = new List<Marker>();
        private Camera source;
        private Transform boatRoot;
        private Rigidbody body;
        private Texture2D dot;
        private GUIStyle labelStyle;
        private static readonly Color MassColor = new Color(1f, .84f, .12f);
        private static readonly Color ForceColor = new Color(.15f, .95f, 1f);
        // Enabled only while the markers are shown: an idle OnGUI still costs a layout pass each frame.
        internal static PumpMarkers Instance;
        private void Awake() { Instance = this; enabled = Plugin.ShowMarkers; }
        private void LateUpdate() { Refresh(); }
        internal void Refresh()
        {
            markers.Clear();
            if (!Plugin.Ready || !Plugin.ShowMarkers || !GameState.playing || PumpWorld.Loading || GameState.currentlyLoading || GameState.recovering)
            { boatRoot = null; body = null; return; }
            Transform target = GameState.currentBoat ? GameState.currentBoat.parent : GameState.lastBoat;
            if (target != boatRoot || !body) { boatRoot = target; body = target ? target.GetComponent<Rigidbody>() : null; }
            if (!body) return;
            var save = boatRoot.GetComponent<SaveableObject>();
            if (!save) return;
            Transform model = GameState.currentBoat;
            if (!model || model.parent != boatRoot)
            {
                var refs = boatRoot.GetComponent<BoatRefs>();
                model = refs ? refs.boatModel : null;
            }
            foreach (var record in PumpWorld.State.Items.Values)
            {
                if (!PumpState.IsPump(record.Kind) || !record.Mounted || record.BoatId != save.sceneIndex) continue;
                var item = PumpWorld.Find(record.Id);
                Vector3 point, direction;
                if (item && item.TryThrustGeometry(out point, out direction)) { }
                else if (!TrySavedGeometry(record, model, out point, out direction)) continue;
                markers.Add(new Marker { Point = point, Direction = direction, Label = PumpState.Label(record) });
            }
        }
        internal static bool TrySavedGeometry(PumpRecord record, Transform model, out Vector3 point, out Vector3 direction)
        {
            point = direction = Vector3.zero;
            if (!model || !record.HasPose) return false;
            var prefab = record.Kind == PumpState.HugePumpId ? PumpAssets.HugePump : PumpAssets.Pump;
            if (!prefab) return false;
            Transform anchor = null;
            foreach (var child in prefab.GetComponentsInChildren<Transform>(true)) if (child.name == "ThrustPoint") { anchor = child; break; }
            if (!anchor) return false;
            var rotation = new Quaternion(record.Qx, record.Qy, record.Qz, record.Qw).normalized;
            Vector3 local = new Vector3(record.X, record.Y, record.Z) + rotation * prefab.transform.InverseTransformPoint(anchor.position);
            point = model.TransformPoint(local) + model.up * Plugin.VerticalOffset;
            direction = -model.TransformDirection(rotation * prefab.transform.InverseTransformDirection(anchor.forward)).normalized;
            return true;
        }
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !Plugin.Ready || !Plugin.ShowMarkers || !body || !GameState.playing ||
                PumpWorld.Loading || GameState.currentlyLoading || GameState.recovering) return;
            if (!source || !source.isActiveAndEnabled) source = Camera.main;
            if (!source) return;
            if (!dot) MakeDot();
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            int previousDepth = GUI.depth; Color previousColor = GUI.color;
            GUI.depth = 1000; // Configurator/other IMGUI windows remain in front.
            DrawMarker(body.worldCenterOfMass, Vector3.zero, "Center of mass", MassColor);
            foreach (var marker in markers) DrawMarker(marker.Point, marker.Direction, marker.Label, ForceColor);
            GUI.color = previousColor; GUI.depth = previousDepth;
        }
        private bool Project(Vector3 world, out Vector2 screen)
        {
            Vector3 p = source.WorldToScreenPoint(world);
            screen = new Vector2(p.x, Screen.height - p.y);
            return p.z > source.nearClipPlane && p.x >= 0 && p.x <= Screen.width && p.y >= 0 && p.y <= Screen.height;
        }
        private void DrawMarker(Vector3 point, Vector3 direction, string label, Color color)
        {
            if (!Project(point, out var at)) return;
            if (direction.sqrMagnitude > .01f && Project(point + direction, out var end))
            {
                Vector2 delta = end - at;
                if (delta.sqrMagnitude > 4f)
                {
                    Vector2 tip = at + delta.normalized * 76f;
                    Line(at, tip, color);
                    Vector2 back = -delta.normalized * 16f, side = new Vector2(-back.y, back.x) * .5f;
                    Line(tip, tip + back + side, color); Line(tip, tip + back - side, color);
                }
            }
            GUI.color = Color.black; GUI.DrawTexture(new Rect(at.x - 18, at.y - 18, 36, 36), dot);
            GUI.color = color; GUI.DrawTexture(new Rect(at.x - 12, at.y - 12, 24, 24), dot);
            GUI.color = Color.white;
            labelStyle.normal.textColor = Color.black; GUI.Label(new Rect(at.x + 24, at.y - 20, 300, 48), label, labelStyle);
            labelStyle.normal.textColor = color; GUI.Label(new Rect(at.x + 22, at.y - 22, 300, 48), label, labelStyle);
        }
        private static void Line(Vector2 from, Vector2 to, Color color)
        {
            var matrix = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - 2f, Vector2.Distance(from, to), 4f), Texture2D.whiteTexture);
            GUI.matrix = matrix;
        }
        private void MakeDot()
        {
            dot = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "PropellerMarker", hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f));
                dot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(15.5f - distance)));
            }
            dot.Apply(false, true);
        }
        private void OnDisable() { markers.Clear(); source = null; boatRoot = null; body = null; if (dot) Destroy(dot); dot = null; }
    }

}
