using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace JetPump
{
    // Items, mounting, controls, pairing UI, carrying and Academy stock.

    public sealed class PumpItem : MonoBehaviour
    {
        internal ShipItem Item;
        internal SaveablePrefab Saveable;
        internal PumpRecord Record;
        internal PumpMount Mount;
        internal PumpWater Water;
        internal bool Retiring;
        internal bool IsPump => Saveable && PumpState.IsPump(Saveable.prefabIndex);
        internal bool Installed => !Retiring && !Mount.Cached && Record != null && Record.Mounted && warmup >= 8 && Item.sold && !Item.held &&
            Item.currentActualBoat && PumpWorld.BoatId(Item.currentActualBoat) == Record.BoatId && Item.itemRigidbodyC &&
            Item.itemRigidbodyC.attached && !Mount.Riding && Mount.Verified && Item.itemRigidbodyC.GetCurrentInventorySlot() == null &&
            Item.itemRigidbodyC.GetCurrentBox() == null && Saveable.currentCrateId == 0 && gameObject.layer != 26;
        internal bool CanOperate => !IsPump && Installed && Item.nailed;
        internal bool WheelSelected => CanOperate && Record.Wheel.Length > 0 && PumpWorld.PairOperational(this, out _);
        private int session = -1, warmup;
        private float reconcileTimer;
        private Transform rotor, needle, lever, immersionLever, safetyLever;
        private Vector3 thrustLocal, thrustFacing, rotorAxis, needleAxis;
        private Quaternion leverBase, immersionBase, safetyBase, needleBase;
        private TextMesh[] powerDigits;
        private TextMesh identityText;
        private PumpPowerDisplay powerDisplay;
        private readonly PumpDrive drive = new PumpDrive();
        private readonly List<PumpControl> controls = new List<PumpControl>();
        private Rigidbody boatBody;
        private PumpTrim trim;
        private Transform bodyOwner;
        // The view as last written, so an unchanged frame writes nothing.
        private int shownNumber = -1, shownKind, shownLevers = -1;
        private float shownThrottle = float.NaN;

        private void Awake()
        {
            Item = GetComponent<ShipItem>();
            if ((object)Item != null) PumpWorld.ByItem[Item] = this;
            Saveable = GetComponent<SaveablePrefab>();
            Mount = new PumpMount(this);
            if (IsPump)
            {
                var thrust = Require("ThrustPoint");
                thrustLocal = transform.InverseTransformPoint(thrust.position);
                thrustFacing = transform.InverseTransformDirection(thrust.forward).normalized;
                rotor = Require("RotorPivot");
                rotorAxis = rotor.InverseTransformDirection(thrust.forward).normalized;
                Water = new PumpWater(this, Require("WaterOpening"));
            }
            else
            {
                needle = Require("ThrottleNeedlePivot"); lever = Require("OnOffLeverPivot");
                needleBase = needle.localRotation;
                needleAxis = needle.parent.InverseTransformDirection(transform.forward).normalized;
                // The authoring builder stores a neutral lever rotation in the prefab.
                leverBase = lever.localRotation;
                powerDigits = new[] { Require("PowerTextHundreds").GetComponent<TextMesh>(),
                    Require("PowerTextTens").GetComponent<TextMesh>(), Require("PowerTextOnes").GetComponent<TextMesh>() };
                powerDisplay = new PumpPowerDisplay(transform, powerDigits);
                AddControl("HIT_OnOff", PumpControl.Action.Toggle, null);
                AddControl("HIT_PowerDecrease", PumpControl.Action.Decrease, Require("PowerDecreasePivot"));
                AddControl("HIT_PowerIncrease", PumpControl.Action.Increase, Require("PowerIncreasePivot"));
                AddControl("HIT_WheelAssign", PumpControl.Action.SelectWheel, Require("WheelAssignPivot"));
                immersionLever = Require("ImmersionLeverPivot"); immersionBase = immersionLever.localRotation;
                AddControl("HIT_Immersion", PumpControl.Action.Immersion, null);
                safetyLever = Require("SafetyLeverPivot"); safetyBase = safetyLever.localRotation;
                AddControl("HIT_Safety", PumpControl.Action.Safety, null);
                AddControl("HIT_ControlGroup", PumpControl.Action.Group, Require("GroupSliderPivot"));
            }
            identityText = Require("IdentityText").GetComponent<TextMesh>();
        }
        internal Transform Require(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            throw new InvalidOperationException("Propeller asset missing " + name);
        }
        private void AddControl(string name, PumpControl.Action action, Transform button)
        {
            var t = Require(name);
            var control = t.gameObject.AddComponent<PumpControl>();
            control.Configure(this, action, button);
            controls.Add(control);
        }
        private void OnEnable() { if (!PumpWorld.Live.Contains(this)) PumpWorld.Live.Add(this); Font.textureRebuilt += RefreshFont; RefreshFont(identityText ? identityText.font : null); }
        private void OnDisable() { StopDrive(true); powerDisplay?.Reset(); PumpWorld.Live.Remove(this); Font.textureRebuilt -= RefreshFont; }
        private void OnDestroy() { if ((object)Item != null) PumpWorld.ByItem.Remove(Item); }
        internal void StopDrive(bool resetWater = false) { drive.Stop(); if (resetWater) Water?.Reset(); }
        private void RefreshFont(Font font)
        {
            if (font && identityText && identityText.font == font) identityText.GetComponent<Renderer>().sharedMaterial.mainTexture = font.material.mainTexture;
            if (font && powerDigits != null)
                foreach (var digit in powerDigits)
                    if (digit && digit.font == font) digit.GetComponent<Renderer>().sharedMaterial.mainTexture = font.material.mainTexture;
        }

        internal bool EnsureState()
        {
            if (Retiring || PumpWorld.Loading || !PumpWorld.State.Writable || !Saveable || Saveable.instanceId <= 0) return false;
            if (session != PumpWorld.Session || Record == null || Record.Id != Saveable.instanceId)
            {
                Record = PumpWorld.State.GetOrCreate(Saveable.instanceId, Saveable.prefabIndex);
                session = PumpWorld.Session;
                warmup = 0;
                Mount.ResetBinding();
                StopDrive(true);
                powerDisplay?.Reset();
            }
            return true;
        }
        internal void Removed()
        {
            Mount.Cancel();
            StopDrive(true);
            if (EnsureState()) PumpWorld.State.RemoveFromWall(Record);
        }
        internal void EnforceNails()
        {
            if (Retiring || Mount.Cached || PumpWorld.Loading || GameState.currentlyLoading || Record == null || Item.nailed) return;
            if (IsPump) PumpWorld.State.StopPump(Record);
            else PumpWorld.State.StopController(Record);
            StopDrive(true);
        }
        private void Update()
        {
            if (!EnsureState()) { StopDrive(true); powerDisplay?.Reset(); SetControls(false); return; }
            EnforceNails();
            if (IsPump)
            {
                if (Installed && Item.nailed && PumpWorld.SimulationAllowed)
                {
                    if (Record.RequireImmersion) Water.Sample();
                    else Water.Reset();
                }
                else StopDrive(true);
                if (!Record.Enabled) StopDrive();
            }
            SetControls(CanOperate && PumpWorld.SimulationAllowed);
            reconcileTimer -= Time.deltaTime;
            if (reconcileTimer <= 0f && PumpWorld.SimulationAllowed)
            {
                reconcileTimer = .25f;
                if (CanOperate) PumpWorld.AutoPair(this);
                if (!Installed && warmup >= 8 && Record.Mounted && (Item.held || Mount.Stored)) Removed();
            }
            RefreshView();
        }
        private void SetControls(bool active)
        {
            for (int i = 0; i < controls.Count; i++) controls[i].SetAvailable(active);
        }
        private void FixedUpdate()
        {
            bool producing = ApplyThrust();
            if (!producing) StopDrive();
        }
        private bool ApplyThrust()
        {
            if (!EnsureState() || !Item.itemRigidbodyC) return false;
            EnforceNails();
            if (!PumpWorld.SimulationAllowed) return false;
            Mount.Tick();
            if (Mount.Pending) return false;
            // Vanilla's wall-item initializer sets attached=true even for a loose new spawn.
            // Restore our explicit installation flag before permitting any propulsion.
            if (warmup < 8)
            {
                Item.itemRigidbodyC.attached = Record.Mounted && !Item.held && Saveable.currentCrateId == 0;
                warmup++;
                return false;
            }
            if (!IsPump || !Installed || !Item.nailed || Record.AppliedPower <= 0f) return false;
            var box = PumpWorld.Find(Record.PeerId);
            if (!PumpWorld.PairOperational(box, out var paired) || paired != this) return false;
            if (bodyOwner != Item.currentActualBoat || !boatBody)
            {
                bodyOwner = Item.currentActualBoat;
                boatBody = bodyOwner.parent ? bodyOwner.parent.GetComponent<Rigidbody>() : null;
                trim = null;
            }
            if (!boatBody || boatBody.isKinematic || !Item.currentWalkCol || Item.itemRigidbodyC.transform.parent != Item.currentWalkCol) return false;
            if (Record.RequireImmersion) Water.Evaluate();
            float effectivePower = Water.Power(Record);
            if (effectivePower <= 0f) return false;
            if (!TryThrustGeometry(out var point, out var direction)) return false;
            if (!trim) trim = boatBody.GetComponent<PumpTrim>() ?? boatBody.gameObject.AddComponent<PumpTrim>();
            drive.Apply(boatBody, direction, point, effectivePower, Time.fixedDeltaTime, trim, PumpState.RatedThrust(Record.Kind));
            return true;
        }
        // Used by physics and the marker overlay, even when the engine is OFF.
        internal bool TryThrustGeometry(out Vector3 point, out Vector3 direction)
        {
            point = direction = Vector3.zero;
            if (!IsPump || !Installed || !Item.currentWalkCol) return false;
            var proxy = Item.itemRigidbodyC.transform;
            point = ConvertPoint(proxy.TransformPoint(thrustLocal), Item.currentWalkCol, Item.currentActualBoat) + SpatialOffset;
            direction = -ConvertDirection(proxy.TransformDirection(thrustFacing), Item.currentWalkCol, Item.currentActualBoat);
            return true;
        }
        internal Vector3 SpatialOffset => Item.currentActualBoat ? Item.currentActualBoat.up * Plugin.VerticalOffset : Vector3.zero;

        public static Vector3 ConvertPoint(Vector3 point, Transform walk, Transform actual) => actual.TransformPoint(walk.InverseTransformPoint(point));
        public static Vector3 ConvertDirection(Vector3 direction, Transform walk, Transform actual) => actual.TransformDirection(walk.InverseTransformDirection(direction)).normalized;

        private void RefreshView()
        {
            var pump = IsPump ? Record : PumpWorld.State.Peer(Record);
            int number = pump == null || pump.Number <= 0 ? 0 : pump.Number, kind = number == 0 ? 0 : pump.Kind;
            if (identityText && (number != shownNumber || kind != shownKind))
            {
                identityText.text = number == 0 ? "UNPAIRED" : PumpState.Label(pump);
                shownNumber = number; shownKind = kind;
            }
            if (IsPump)
            {
                if (Record.Enabled && Item.nailed && PumpWorld.SimulationAllowed && Installed && PumpWorld.PairOperational(PumpWorld.Find(Record.PeerId), out _))
                    rotor.Rotate(rotorAxis, 720f * Record.Throttle * Time.deltaTime, Space.Self);
                return;
            }
            float throttle = pump == null ? 0f : pump.Throttle;
            if (throttle != shownThrottle)
            {
                needle.localRotation = Quaternion.AngleAxis(Mathf.Lerp(80f, -80f, Mathf.Clamp01(throttle)), needleAxis) * needleBase;
                shownThrottle = throttle;
            }
            int power = pump == null ? -1 : pump.Power;
            powerDisplay.Show(power, Time.deltaTime, !PumpWorld.InputAllowed);
            bool on = pump != null && pump.Enabled, water = pump != null && pump.RequireImmersion;
            int levers = (on ? 1 : 0) | (Record.SafetyEnabled ? 2 : 0) | (water ? 4 : 0);
            if (levers == shownLevers) return;
            lever.localRotation = leverBase * Quaternion.AngleAxis(on ? -50f : 50f, Vector3.right);
            safetyLever.localRotation = safetyBase * Quaternion.AngleAxis(Record.SafetyEnabled ? -35f : 35f, Vector3.right);
            immersionLever.localRotation = immersionBase * Quaternion.AngleAxis(water ? -35f : 35f, Vector3.right);
            shownLevers = levers;
        }
    }

    public sealed class PumpControl : GoPointerButton
    {
        internal enum Action { Toggle, Decrease, Increase, SelectWheel, Immersion, Safety, Group }
        internal PumpItem Owner;
        private Action action;
        private Transform button;
        private Vector3 rest, pressDirection;
        private Quaternion restRotation;
        private Vector3 rockerAxis;
        private GameObject ivory;
        private Collider hitCollider;
        private float pressUntil;
        private Vector3[] stops;
        internal void Configure(PumpItem owner, Action kind, Transform animatedButton)
        {
            Owner = owner; action = kind; button = animatedButton;
            hitCollider = GetComponent<Collider>();
            hitCollider.enabled = false;
            if (button) { rest = button.localPosition; pressDirection = button.parent.InverseTransformDirection(owner.transform.forward).normalized * .00363f; }
            if (kind == Action.Group)
            {
                // The knob's stops are model empties beside its pivot, so the layout lives in the model.
                stops = new Vector3[PumpState.GroupCount + 1];
                for (int i = 0; i < stops.Length; i++)
                {
                    var stop = owner.Require("GroupStop" + i);
                    stops[i] = button.parent.InverseTransformPoint(stop.position);
                }
                button.localPosition = knobAt = stops[0];
            }
            if (kind == Action.SelectWheel)
            {
                restRotation = button.localRotation;
                rockerAxis = button.parent.InverseTransformDirection(owner.transform.up).normalized;
                foreach (var t in button.GetComponentsInChildren<Transform>(true)) if (t.name == "WheelIvoryStrip") ivory = t.gameObject;
                if (!ivory) throw new InvalidOperationException("Missing left-edge wheel indicator");
            }
            description = lookText = "";
            enableRedOutline = false;
        }
        internal void SetAvailable(bool available)
        {
            // Vanilla inventory exit sets all descendants to Ignore Raycast;
            // DropItem restores only the housing. Restore our hit areas only.
            if (available) gameObject.layer = 0;
            if (hitCollider) hitCollider.enabled = available;
            unclickable = !available;
        }
        internal bool CanUse => Owner && Owner.CanOperate && PumpWorld.InputAllowed;
        public override void OnActivate(GoPointer pointer) { if (!pointer || !(pointer.GetHeldItem() is ShipItemHammer)) Activate(); }
        public override void OnAltActivate(GoPointer pointer)
        {
            if ((!pointer || !(pointer.GetHeldItem() is ShipItemHammer)) && CanUse && action == Action.SelectWheel) PairingMenu.Open(Owner);
        }
        internal void Activate()
        {
            if (!CanUse) return;
            var state = PumpWorld.State;
            var record = Owner.Record;
            if (action == Action.Safety)
            {
                state.SetSafety(record, !record.SafetyEnabled, PumpWorld.Usable);
                return;
            }
            if (action == Action.Group)
            {
                state.CycleGroup(record, PumpWorld.Usable);
                return;
            }
            var pairedRecord = PumpWorld.State.Peer(Owner.Record);
            if (pairedRecord == null)
            {
                if (Owner.Record.PeerId == 0) PairingMenu.Open(Owner);
                else Notify("Paired propeller is not ready.");
                return;
            }
            var pump = PumpWorld.Find(pairedRecord.Id);
            if (pump && !pump.Item.nailed) { Notify("Propeller is not hammered.\nNail it down before use."); return; }
            if (!PumpWorld.PairOperational(Owner, out pump)) { Notify("Paired propeller is not ready."); return; }
            // In a control group every working member takes the value this panel sets.
            switch (action)
            {
                case Action.Toggle:
                    bool on = !pump.Record.Enabled;
                    state.SetEnabled(record, on, PumpWorld.Usable);
                    if (!on)
                        foreach (var member in state.Members(record, PumpWorld.Usable))
                        {
                            var memberPump = PumpWorld.Find(member.PeerId);
                            if (memberPump) memberPump.StopDrive();
                        }
                    break;
                case Action.Immersion: state.SetImmersion(record, !pump.Record.RequireImmersion, PumpWorld.Usable); break;
                case Action.Decrease: state.SetPower(record, pump.Record.Power - 5, PumpWorld.Usable); break;
                case Action.Increase: state.SetPower(record, pump.Record.Power + 5, PumpWorld.Usable); break;
                case Action.SelectWheel:
                    // The rocker links every wheel and tiller of the boat; pressing it again releases them.
                    bool link = record.Wheel.Length == 0;
                    if (link && !PumpWorld.HasHelm(Owner)) { Notify("No wheel or tiller on this boat."); return; }
                    state.SetWheel(record, link, PumpWorld.Usable);
                    break;
            }
            pressUntil = Time.time + .14f;
        }
        private static void Notify(string message)
        {
            if (NotificationUi.instance) NotificationUi.instance.ShowNotification(message);
        }
        private bool knobPlaced;
        private Vector3 knobAt;
        // The button pose as last written (0 up, 1 down), so an unchanged frame writes nothing.
        private int shownPose = -1;
        public override void ExtraLateUpdate()
        {
            if (action == Action.Group)
            {
                if (!button || Owner == null || Owner.Record == null) return;
                var target = stops[Mathf.Clamp(Owner.Record.Group, 0, stops.Length - 1)];
                if (knobPlaced && knobAt.Equals(target)) return;
                // Snap on first show (loaded saves), then slide about one stop per 0.13 s.
                knobAt = knobPlaced ? Vector3.MoveTowards(knobAt, target, .25f * Time.deltaTime) : target;
                button.localPosition = knobAt;
                knobPlaced = true;
                return;
            }
            if (button)
            {
                bool depressed = action == Action.SelectWheel ? Owner && Owner.WheelSelected : Time.time < pressUntil;
                if ((depressed ? 1 : 0) == shownPose) return;
                shownPose = depressed ? 1 : 0;
                if (action == Action.SelectWheel)
                {
                    button.localRotation = Quaternion.AngleAxis(depressed ? -10f : 10f, rockerAxis) * restRotation;
                    ivory.SetActive(depressed);
                }
                else button.localPosition = rest + (depressed ? pressDirection : Vector3.zero);
            }
        }
    }

    // A wall contact identifies the boat even outside its embark trigger. The contact
    // is stored in collider coordinates until vanilla's item/proxy initialization is ready.
    internal sealed class PumpMount
    {
        private static readonly MethodInfo EnterBoat = AccessTools.Method(typeof(ShipItem), "EnterBoat");
        private static readonly MethodInfo Targeter = AccessTools.Method(typeof(ShipItem), "SetUpTargeter");
        private static readonly FieldInfo ItemSave = AccessTools.Field(typeof(ShipItem), "saveable");
        private static readonly FieldInfo TargeterActive = AccessTools.Field(typeof(GoPointerTargeter), "targeterActive");
        private static BoatEmbarkCollider[] boats;
        private static float nextBoatScan;
        private static readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly PumpItem owner;
        private readonly BoxCollider housing;
        private Collider contact;
        private Vector3 contactPosition;
        private Quaternion contactRotation;
        private bool previewValid, recoveryChecked, itemReady;
        private BoatEmbarkCollider bound;
        private int remountTries;
        internal bool Pending;
        internal bool Riding;
        internal bool LoadedFromSave;

        internal PumpMount(PumpItem owner) { this.owner = owner; housing = owner.GetComponent<BoxCollider>(); }
        internal static void ResetCache() { boats = null; nextBoatScan = 0f; }
        internal void ResetBinding() { bound = null; recoveryChecked = false; }
        internal void Cancel() { Pending = previewValid = Riding = false; remountTries = 0; contact = null; bound = null; LoadedFromSave = false; }
        internal bool Stored => owner.Saveable.currentCrateId != 0 || owner.gameObject.layer == 26 ||
            (owner.Item.itemRigidbodyC && (owner.Item.itemRigidbodyC.GetCurrentBox() || owner.Item.itemRigidbodyC.GetCurrentInventorySlot()));
        internal bool Cached => owner.Saveable && (owner.Saveable.GetParentObject() == -2 || owner.Saveable.GetParentObject() == -3);
        internal bool ProtectOwnership => Cached || (!owner.Item.held && !Stored && (Pending || (owner.Record != null && owner.Record.Mounted)));
        internal bool Verified => bound && owner.Item.currentActualBoat == bound.transform.parent &&
            owner.Item.currentWalkCol == bound.walkCollider && owner.Item.transform.parent == bound.transform.parent &&
            owner.Item.itemRigidbodyC && owner.Item.itemRigidbodyC.transform.parent == bound.walkCollider &&
            owner.Saveable.GetParentObject() == PumpWorld.BoatId(bound.transform.parent);

        // ShipItem.Start has run: it sets the item's saveable field once and never clears it.
        private bool ItemReady() => itemReady || (itemReady = ItemSave.GetValue(owner.Item) != null);
        private static BoatEmbarkCollider[] Boats()
        {
            if (boats == null || Time.unscaledTime >= nextBoatScan)
            {
                boats = UnityEngine.Object.FindObjectsOfType<BoatEmbarkCollider>();
                nextBoatScan = Time.unscaledTime + 2f;
            }
            return boats;
        }
        private static bool Usable(BoatEmbarkCollider boat)
        {
            if (!boat || !boat.walkCollider || !boat.transform.parent || !boat.transform.parent.parent || !boat.GetComponent<Collider>()) return false;
            var root = boat.transform.parent.parent;
            return root.GetComponent<Rigidbody>() && root.GetComponent<BoatMass>() && PumpWorld.BoatId(boat.transform.parent) > 0;
        }
        // An item's surface counts only when itemsCount: it is aboard a boat when it sits under that boat's walk copy or model.
        internal static BoatEmbarkCollider ResolveSurface(Collider surface, bool itemsCount, out Transform frame)
        {
            frame = null;
            if (!surface || !itemsCount && (surface.GetComponentInParent<ShipItem>() || surface.GetComponentInParent<ItemRigidbody>())) return null;
            BoatEmbarkCollider result = null;
            foreach (var boat in Boats())
            {
                if (!Usable(boat)) continue;
                Transform candidate = surface.transform.IsChildOf(boat.walkCollider) ? boat.walkCollider :
                    surface.transform.IsChildOf(boat.transform.parent.parent) ? boat.transform.parent : null;
                if (!candidate) continue;
                if (result && result.transform.parent != boat.transform.parent) { frame = null; return null; }
                result = boat; frame = candidate;
            }
            return result;
        }
        private static BoatEmbarkCollider FindSavedBoat(int id)
        {
            if (id <= 0) return null;
            foreach (var boat in Boats()) if (Usable(boat) && PumpWorld.BoatId(boat.transform.parent) == id) return boat;
            return null;
        }
        private bool Raycast(Ray ray, float distance, out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(ray, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A crowded contact should not silently omit the nearest obstruction.
            var buffer = count == hits.Length ? Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : hits;
            if (buffer != hits) count = buffer.Length;
            hit = default(RaycastHit);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var c = buffer[i].collider;
                if (!c || c.transform.IsChildOf(owner.transform) || (owner.Item.itemRigidbodyC && c.transform.IsChildOf(owner.Item.itemRigidbodyC.transform))) continue;
                if (buffer[i].distance < nearest) { nearest = buffer[i].distance; hit = buffer[i]; }
            }
            return hit.collider;
        }
        internal static Quaternion SurfaceRotation(Vector3 normal, Vector3 itemUp)
        {
            // Match vanilla wall alignment, including its floor-facing threshold.
            // Retain a safe tangent for underside hull contacts with parallel axes.
            Vector3 up = Vector3.ProjectOnPlane(normal.y > .8f ? itemUp : Vector3.up, normal);
            if (up.sqrMagnitude < .001f) up = Vector3.ProjectOnPlane(itemUp, normal);
            if (up.sqrMagnitude < .001f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            return Quaternion.LookRotation(-normal, up.normalized);
        }
        internal bool Preview(Transform proxy, out Vector3 position, out Quaternion rotation)
        {
            previewValid = false; position = Vector3.zero; rotation = Quaternion.identity;
            if (!proxy || !owner.Item.held || !PumpWorld.InputAllowed) return false;
            if (!Raycast(new Ray(proxy.position, proxy.forward), 1.3f, out var hit) || hit.distance < .1f) return false;
            // Only a surface aboard a boat (its own or an item's) can create a powered-item mount.
            // Ordinary world geometry must remain a free drop, not a permanently pending boat claim.
            if (!ResolveSurface(hit.collider, true, out _)) return false;
            rotation = SurfaceRotation(hit.normal, owner.transform.up);
            position = ClearancePosition(hit.point, hit.normal, rotation);
            RememberContact(hit.collider, position, rotation);
            return true;
        }
        internal Vector3 ClearancePosition(Vector3 point, Vector3 normal, Quaternion rotation)
        {
            float minimum = 0f;
            if (housing)
            {
                var half = housing.size * .5f;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            minimum = Mathf.Min(minimum, Vector3.Dot(rotation * (housing.center + Vector3.Scale(half, new Vector3(x, y, z))), normal));
            }
            return point + normal * (.001f - minimum);
        }
        internal void RememberContact(Collider surface, Vector3 position, Quaternion rotation)
        {
            contact = surface;
            contactPosition = surface.transform.InverseTransformPoint(position);
            contactRotation = Quaternion.Inverse(surface.transform.rotation) * rotation;
            previewValid = true;
        }
        internal void Drop(bool attached)
        {
            Pending = attached && previewValid && contact;
            bound = null;
        }
        internal void Tick()
        {
            // These are vanilla lifecycle markers, never missing boat references to repair.
            if (Cached || owner.Retiring || owner.Item.held || Stored || !owner.Item.sold || !owner.Item.itemRigidbodyC) return;
            if (!owner.Item.itemRigidbodyC.GetBody() || !ItemReady()) return;
            if (owner.Record.Mounted || owner.Item.nailed)
            {
                // Carried on a plank or pole: its carrier places it, and it is off the wall until set down, then mounted there.
                bool rode = Riding;
                Riding = PumpCarry.Riding(owner.Item);
                if (Riding)
                {
                    if (!rode && owner.Record.Mounted) { if (owner.IsPump) PumpWorld.State.StopPump(owner.Record); else PumpWorld.State.StopController(owner.Record); }
                    return;
                }
                if (rode) remountTries = 1;
                if (remountTries > 0) { Remount(); return; }
            }
            if (Pending)
            {
                if (!contact) { Pending = false; return; }
                var boat = ResolveSurface(contact, true, out var frame);
                if (!boat) return;
                Vector3 local = frame.InverseTransformPoint(contact.transform.TransformPoint(contactPosition));
                Quaternion localRot = Quaternion.Inverse(frame.rotation) * contact.transform.rotation * contactRotation;
                if (Bind(boat, local, localRot)) { PumpWorld.State.Install(owner.Record, PumpWorld.BoatId(boat.transform.parent)); CapturePose(); Pending = false; }
                return;
            }
            if (owner.Record.Mounted && !Verified)
            {
                int savedParent = owner.Saveable.GetParentObject();
                if (savedParent > 0 && savedParent != owner.Record.BoatId) return;
                var boat = FindSavedBoat(owner.Record.BoatId);
                var r = owner.Record;
                if (boat && Bind(boat, r.HasPose ? new Vector3(r.X, r.Y, r.Z) : boat.transform.parent.InverseTransformPoint(owner.transform.position),
                    r.HasPose ? new Quaternion(r.Qx, r.Qy, r.Qz, r.Qw).normalized : Quaternion.Inverse(boat.transform.parent.rotation) * owner.transform.rotation)) CapturePose();
            }
            else if (!owner.IsPump && !owner.Record.Mounted && owner.Item.nailed && owner.Item.currentActualBoat)
            {
                // A controller nailed anywhere aboard is mounted where it sits.
                var frame = owner.Item.currentActualBoat;
                var boat = FindSavedBoat(PumpWorld.BoatId(frame));
                if (boat && Bind(boat, frame.InverseTransformPoint(owner.transform.position), Quaternion.Inverse(frame.rotation) * owner.transform.rotation))
                {
                    PumpWorld.State.Install(owner.Record, PumpWorld.BoatId(frame));
                    CapturePose();
                }
            }
            else if (!owner.Record.Mounted && LoadedFromSave && !recoveryChecked)
            {
                // A loaded item without a Mounted record is mounted again only where it sits flush and
                // aligned against the hull, never onto an arbitrary nearby item.
                recoveryChecked = true;
                var inward = owner.transform.forward;
                if (!Raycast(new Ray(owner.transform.position - inward * .05f, inward), .07f, out var hit) ||
                    Vector3.Distance(hit.point, owner.transform.position) > .012f || Vector3.Dot(-hit.normal, inward) < .985f) return;
                var boat = ResolveSurface(hit.collider, false, out var frame);
                if (!boat || (owner.Saveable.GetParentObject() > 0 && owner.Saveable.GetParentObject() != PumpWorld.BoatId(boat.transform.parent))) return;
                // This ray is in actual world space, not the walking-proxy frame.
                if (frame != boat.transform.parent) return;
                if (Bind(boat, frame.InverseTransformPoint(owner.transform.position), Quaternion.Inverse(frame.rotation) * owner.transform.rotation))
                {
                    PumpWorld.State.Install(owner.Record, PumpWorld.BoatId(boat.transform.parent));
                    CapturePose();
                }
            }
        }
        // Set down after a ride: still nailed and attached on a boat, mounted where it now sits on that boat; let go or aboard
        // none, off the wall. Binding can fail while the carrier's last step settles, so it is tried for 50 ticks.
        private void Remount()
        {
            var frame = owner.Item.currentActualBoat;
            var boat = frame && owner.Item.nailed && owner.Item.itemRigidbodyC.attached ? FindSavedBoat(PumpWorld.BoatId(frame)) : null;
            if (boat && Bind(boat, frame.InverseTransformPoint(owner.transform.position), Quaternion.Inverse(frame.rotation) * owner.transform.rotation))
            {
                PumpWorld.State.Install(owner.Record, PumpWorld.BoatId(frame));
                CapturePose();
                remountTries = 0;
            }
            else if (!boat || ++remountTries > 50) owner.Removed();
        }
        internal void CapturePose()
        {
            if (Cached || owner.Retiring || !Verified || owner.Record == null || !owner.Record.Mounted || owner.Item.held || Stored) return;
            var frame = owner.Item.currentActualBoat;
            var p = frame.InverseTransformPoint(owner.transform.position);
            var q = Quaternion.Inverse(frame.rotation) * owner.transform.rotation;
            var r = owner.Record;
            r.X = p.x; r.Y = p.y; r.Z = p.z; r.Qx = q.x; r.Qy = q.y; r.Qz = q.z; r.Qw = q.w; r.HasPose = true;
        }
        private bool Bind(BoatEmbarkCollider boat, Vector3 local, Quaternion localRot)
        {
            var item = owner.Item;
            var proxy = item.itemRigidbodyC;
            if (!Usable(boat) || !proxy || !proxy.GetBody() || !ItemReady()) return false;
            // Exit the previous mass list before EnterBoat changes currentActualBoat.
            if (item.currentActualBoat && item.currentActualBoat != boat.transform.parent) proxy.ExitBoat();
            item.transform.position = boat.transform.parent.TransformPoint(local);
            item.transform.rotation = boat.transform.parent.rotation * localRot;
            EnterBoat.Invoke(item, new object[] { boat.GetComponent<Collider>() });
            proxy.attached = true;
            proxy.GetBody().velocity = Vector3.zero;
            proxy.GetBody().angularVelocity = Vector3.zero;
            proxy.GetBody().isKinematic = true;
            bound = boat;
            return Verified;
        }
        internal void ShowPreview(Vector3 position, Quaternion rotation)
        {
            Targeter.Invoke(owner.Item, new object[] { position, rotation, owner.GetComponent<MeshFilter>().sharedMesh });
        }
        internal void HidePreview()
        {
            var targeter = owner.Item.held.GetTargeter();
            if (targeter) TargeterActive.SetValue(targeter, false);
        }
    }

    // EconomyOverhaul's and Tent and Plank's carry (PoleCarry.Riding): an item nailed on a plank or pole that is picked up rides with it.
    internal static class PumpCarry
    {
        private static Func<ShipItem, bool>[] riding = new Func<ShipItem, bool>[0];
        internal static void Find()
        {
            var found = new List<Func<ShipItem, bool>>();
            foreach (var mod in new[] { "EconomyOverhaul", "TentandPlank" })
            {
                var type = PumpWorld.OptionalType(mod, mod + ".PoleCarry");
                if (type == null) continue;
                var method = AccessTools.Method(type, "Riding", new[] { typeof(ShipItem) });
                if (method == null || !method.IsStatic || method.ReturnType != typeof(bool))
                {
                    Plugin.Log.LogWarning(mod + "'s PoleCarry.Riding was not found: items on its planks and poles are not carried.");
                    continue;
                }
                found.Add((Func<ShipItem, bool>)Delegate.CreateDelegate(typeof(Func<ShipItem, bool>), method));
            }
            riding = found.ToArray();
        }
        internal static bool Riding(ShipItem item)
        {
            for (int i = 0; i < riding.Length; i++) if (riding[i](item)) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipItem), "Update")]
    internal static class PumpPlacementPreview
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ShipItem __instance, Transform ___itemRigidbody, ref bool ___inRangeOfWall, ref Vector3 ___attachPos, ref Quaternion ___attachRot)
        {
            // Runs for every item in the game each frame: only a held one of ours goes further.
            if (!__instance.held || !PumpWorld.ByItem.TryGetValue(__instance, out var pump)) return;
            ___inRangeOfWall = pump.Mount.Preview(___itemRigidbody, out ___attachPos, out ___attachRot);
            if (___inRangeOfWall) pump.Mount.ShowPreview(___attachPos, ___attachRot);
            else pump.Mount.HidePreview();
        }
    }
    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.ExtraFixedUpdate))]
    internal static class PumpKeepMountedBoat
    {
        // Runs for every item in the game each physics tick.
        private static bool Prefix(ShipItem __instance)
        {
            return !PumpWorld.ByItem.TryGetValue(__instance, out var pump) || !pump.Mount.ProtectOwnership;
        }
    }
    [HarmonyPatch(typeof(SaveablePrefab), nameof(SaveablePrefab.Load))]
    internal static class PumpLoadedPlacement
    {
        private static void Prefix(SaveablePrefab __instance, SavePrefabData data, bool ___loaded)
        {
            var pump = __instance.GetComponent<PumpItem>();
            if (pump && !___loaded) PumpWorld.RetireDuplicate(pump, data.instanceId);
        }
        private static void Postfix(SaveablePrefab __instance)
        {
            var pump = __instance.GetComponent<PumpItem>();
            if (pump) { pump.Mount.LoadedFromSave = true; pump.Mount.ResetBinding(); PumpMount.ResetCache(); }
        }
    }
    [HarmonyPatch(typeof(SaveablePrefab), nameof(SaveablePrefab.PrepareSaveData))]
    internal static class PumpSavePose
    {
        private static void Prefix(SaveablePrefab __instance)
        {
            __instance.GetComponent<PumpItem>()?.Mount.CapturePose();
        }
    }

    [HarmonyPatch(typeof(GoPointer), nameof(GoPointer.MainButtonDown))]
    internal static class PumpClickWhileSteering
    {
        private static int activatedFrame = -1;
        private static void Postfix(GoPointer __instance, GoPointerButton ___stickyClickedButton, GoPointerButton ___pointedAtButton, ref bool __result)
        {
            if (!__result || __instance.GetHeldItem() is ShipItemHammer || !PumpWorld.IsHelm(___stickyClickedButton)) return;
            var control = ___pointedAtButton as PumpControl;
            // Any wheel or tiller of the panel's own boat.
            var body = control && control.CanUse ? PumpWorld.BoatBody(control.Owner) : null;
            if (!body || PumpWorld.HelmBody(___stickyClickedButton) != body) return;
            if (activatedFrame != Time.frameCount) { activatedFrame = Time.frameCount; control.Activate(); }
            __result = false; // Only consume a pump-control click; ordinary clicks still release the wheel.
        }
    }

    [HarmonyPatch(typeof(GoPointer), nameof(GoPointer.GetPointedAtItem))]
    internal static class PumpHammerTarget
    {
        private static void Postfix(GoPointer __instance, GoPointerButton ___pointedAtButton, ref ShipItem __result)
        {
            if (__instance.GetHeldItem() is ShipItemHammer && ___pointedAtButton is PumpControl control && control.Owner)
                __result = control.Owner.Item;
        }
    }
    [HarmonyPatch(typeof(ShipItemHammer), nameof(ShipItemHammer.AllowOnItemClick))]
    internal static class PumpHammerAllowed
    {
        private static bool Prefix(GoPointerButton lookedAtButton, ref bool __result)
        {
            if (!(lookedAtButton is PumpControl control) || !control.Owner) return true;
            __result = ShipItemHammer.CanNail(control.Owner.Item);
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipItemHammer), "NailItem")]
    internal static class PumpHammerMovingMount
    {
        // Low priority: runs after other mods' prefixes (Unlimited Hammer's nails every item), so their nail is seen below.
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(ShipItem item)
        {
            var pump = item ? item.GetComponent<PumpItem>() : null;
            if (!pump) return true;
            // Keep vanilla's hold timer and animation; attached items are stable
            // relative to their hull even when the physics proxy is moving.
            // A controller can also be nailed anywhere aboard; Mount.Tick then binds it where it sits.
            bool controllerAboard = !pump.IsPump && item.sold && !item.held && !pump.Mount.Cached && !pump.Mount.Stored &&
                PumpWorld.BoatId(item.currentActualBoat) > 0;
            // Already nailed here means another mod's prefix did it, with its own sound.
            if (PumpWorld.InputAllowed && (pump.Installed || controllerAboard) && !item.nailed)
            {
                item.nailed = true;
                if (UISoundPlayer.instance) UISoundPlayer.instance.PlayUISound(UISounds.winchUnclick, 1f, .6f);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.OnPickup))]
    internal static class PumpPickup { private static void Prefix(ShipItem __instance) { __instance.GetComponent<PumpItem>()?.Removed(); } }
    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.OnDrop))]
    internal static class PumpDrop
    {
        private static void Postfix(ShipItem __instance, bool ___inRangeOfWall)
        {
            var item = __instance.GetComponent<PumpItem>();
            if (item) item.Mount.Drop(__instance.sold && ___inRangeOfWall && !__instance.forceDisableRedOutline && __instance.itemRigidbodyC && __instance.itemRigidbodyC.attached);
        }
    }
    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.OnEnterInventory))]
    internal static class PumpInventory { private static void Prefix(ShipItem __instance) { __instance.GetComponent<PumpItem>()?.Removed(); } }
    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.DestroyItem))]
    internal static class PumpDestroy
    {
        private static void Prefix(ShipItem __instance)
        {
            var item = __instance.GetComponent<PumpItem>();
            if (!item) return;
            bool temporary = item.Retiring || item.Mount.Cached || PumpWorld.Loading || GameState.currentlyLoading;
            if (!temporary && item.EnsureState())
            {
                item.Removed();
                PumpWorld.State.Forget(item.Saveable.instanceId);
            }
            item.Retiring = true;
            // Temporary cache destruction retains identity; permanent removal
            // leaves its peer requiring an explicit new pairing.
        }
    }

    // One bounded animation per digit. Retargeting never queues old values.
    internal sealed class PumpPowerDisplay
    {
        private static readonly string[] Labels = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "-" };
        private readonly TextMesh[] digits;
        private readonly Transform[] pivots = new Transform[3];
        private readonly Quaternion[] restRotation = new Quaternion[3];
        private readonly Vector3[] restPosition = new Vector3[3], axis = new Vector3[3], outward = new Vector3[3];
        private readonly int[] shown = new int[3], target = new int[3];
        private readonly float[] elapsed = { .25f, .25f, .25f }, direction = new float[3];
        private int value = int.MinValue;
        // Every plate is at rest showing `value`.
        private bool settled;
        internal PumpPowerDisplay(Transform panel, TextMesh[] texts)
        {
            digits = texts;
            for (int i = 0; i < 3; i++)
            {
                pivots[i] = texts[i].transform.parent.parent;
                restRotation[i] = pivots[i].localRotation; restPosition[i] = pivots[i].localPosition;
                axis[i] = pivots[i].parent.InverseTransformDirection(panel.right).normalized;
                outward[i] = pivots[i].parent.InverseTransformDirection(-panel.forward).normalized;
            }
        }
        internal void Reset() { value = int.MinValue; }
        internal void Show(int next, float dt, bool immediate)
        {
            if (next == value && settled) return;
            bool snap = immediate || value == int.MinValue || next < 0 || value < 0;
            if (next != value || snap)
            {
                int divisor = 100;
                for (int i = 0; i < 3; i++, divisor /= 10)
                {
                    int digit = next < 0 ? 10 : next / divisor % 10;
                    if (snap)
                    {
                        shown[i] = target[i] = digit; elapsed[i] = .25f; digits[i].text = Labels[digit];
                    }
                    else if (target[i] != digit)
                    {
                        target[i] = digit;
                        // Keep an ongoing flip continuous; swap its latest target at the edge.
                        if (elapsed[i] >= .25f) { elapsed[i] = 0f; direction[i] = next > value ? 1f : -1f; }
                    }
                }
                value = next;
            }
            settled = true;
            for (int i = 0; i < 3; i++)
            {
                float before = elapsed[i];
                elapsed[i] = Mathf.Min(.25f, elapsed[i] + Mathf.Max(0f, dt));
                if (before < .125f && elapsed[i] >= .125f) { shown[i] = target[i]; digits[i].text = Labels[shown[i]]; }
                if (elapsed[i] >= .25f && shown[i] != target[i]) { elapsed[i] = 0f; }
                if (elapsed[i] < .25f) settled = false;
                float t = elapsed[i] / .25f;
                // Exchanging identical plates edge-on avoids exposing mirrored back text.
                float angle = direction[i] * (t < .5f ? 180f * t : -180f * (1f - t));
                pivots[i].localRotation = Quaternion.AngleAxis(angle, axis[i]) * restRotation[i];
                pivots[i].localPosition = restPosition[i] + outward[i] * (.016f * Mathf.Sin(Mathf.Abs(angle) * Mathf.Deg2Rad));
            }
        }
    }

    public sealed class PairingMenu : MonoBehaviour
    {
        private static PumpItem owner;
        private static bool open;
        private static bool previousCursor, previousMouse, previousControl;
        private static bool previousVisible;
        private static CursorLockMode previousLock;
        private Vector2 scroll;
        // Enabled only while open: an idle OnGUI still costs a layout pass each frame.
        private static PairingMenu instance;
        internal static bool IsOpen => open;
        private void Awake() { instance = this; enabled = false; }

        internal static void Open(PumpItem box)
        {
            if (!box || !box.CanOperate || IsOpen || GameState.inCursorMenu) return;
            owner = box;
            open = true;
            previousCursor = GameState.inCursorMenu;
            previousMouse = MouseLook.MouseLookIsEnabled();
            previousControl = Refs.charController && Refs.charController.enabled;
            previousVisible = Cursor.visible;
            previousLock = Cursor.lockState;
            GameState.inCursorMenu = true;
            MouseLook.ToggleMouseLook(false);
            Refs.SetPlayerControl(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (instance) instance.enabled = true;
        }
        internal static void Close()
        {
            if (!open) return;
            open = false;
            owner = null;
            if (instance) instance.enabled = false;
            GameState.inCursorMenu = previousCursor;
            MouseLook.ToggleMouseLook(previousMouse);
            if (Refs.charController) Refs.SetPlayerControl(previousControl);
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
        }
        private void OnDisable() { Close(); }
        private void Update()
        {
            if (!open) return;
            if (!owner) { Close(); return; }
            if (!owner.CanOperate || !GameState.playing || GameState.currentlyLoading || GameState.recovering ||
                !Camera.main || Vector3.Distance(Camera.main.transform.position, owner.transform.position) > 3f || Input.GetKeyDown(KeyCode.Escape)) Close();
        }
        private void OnGUI()
        {
            if (owner && !owner.CanOperate) { Close(); return; }
            if (!owner) return;
            float width = Mathf.Min(500f, Screen.width - 30f);
            float height = Mathf.Min(380f, Screen.height - 30f);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height), GUI.skin.box);
            GUILayout.Label("PAIR THIS CONTROLLER");
            scroll = GUILayout.BeginScrollView(scroll);
            int count = 0;
            foreach (var pump in PumpWorld.Live)
            {
                if (!pump || !pump.IsPump || !pump.Installed || pump.Item.currentActualBoat != owner.Item.currentActualBoat) continue;
                var oldBox = PumpWorld.State.Peer(pump.Record);
                if (oldBox != null && oldBox.Id != owner.Record.Id && oldBox.Mounted) continue;
                count++;
                Vector3 local = owner.Item.currentActualBoat.InverseTransformPoint(pump.transform.position);
                string side = local.x < -.1f ? "port" : local.x > .1f ? "starboard" : "centre";
                string label = PumpState.Label(pump.Record) + " - " + side + " (fore/aft " + local.z.ToString("F1") + " m)";
                if (GUILayout.Button(label, GUILayout.Height(38f)))
                {
                    // Re-selecting the existing pair is not a request to reset it.
                    bool alreadyPaired = PumpWorld.State.Peer(owner.Record) == pump.Record;
                    if (alreadyPaired || PumpWorld.State.Pair(owner.Record.Id, pump.Record.Id, owner.Record.BoatId)) Close();
                    else if (NotificationUi.instance) NotificationUi.instance.ShowNotification("Propeller is not available for pairing.");
                    break;
                }
            }
            if (count == 0) GUILayout.Label("No available propeller.");
            GUILayout.EndScrollView();
            if (GUILayout.Button("Cancel", GUILayout.Height(32f))) Close();
            GUILayout.EndArea();
        }
    }

    // Shop inspection bypasses holdDistance. Use the same clearance for unpaid Huge stock.
    [HarmonyPatch(typeof(GoPointer), "PositionItemInBuyUI")]
    internal static class PropellerCarry
    {
        private static bool Prefix(GoPointer __instance, PickupableItem item)
        {
            var save = item ? item.GetComponent<SaveablePrefab>() : null;
            if (!Plugin.Ready || !save || save.prefabIndex != 606) return true;
            var pointer = __instance.transform;
            Vector3 target = pointer.position + pointer.forward * item.holdDistance + pointer.up * item.holdHeight;
            item.transform.position = Vector3.Lerp(item.transform.position, target, Time.deltaTime * 9f);
            if (BuyItemUI.instance && BuyItemUI.instance.itemPos)
                item.transform.rotation = BuyItemUI.instance.itemPos.rotation;
            return false;
        }
    }

    // The mage's stock: slots (6) and (7) sell Propellers, (11) a controller, and three spawners are added (two controllers, the
    // Huge Propeller). Each stands at a fixed place in the shop's frame (the slots' parent, scaled 0.5), lying on its back plate:
    // the five on the counter, the Huge Propeller on the ground in front of it.
    [HarmonyPatch(typeof(ShopItemSpawner), "Start")]
    internal static class AcademyStock
    {
        internal const string SceneName = "island 8 A Academy";
        internal const string ExtraName = "JetPump Academy controller left";
        internal const string ThirdName = "Propeller Academy controller far left";
        internal const string HugeName = "Propeller Academy huge";
        private static readonly Quaternion Lying = new Quaternion(.681704f, .187829f, -.187829f, .681704f);
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(ShopItemSpawner __instance)
        {
            if (!Plugin.Ready || __instance.gameObject.scene.name != SceneName) return;
            string name = __instance.name;
            if (name != "shop item (6)" && name != "shop item (7)" && name != "shop item (11)") return;
            Transform root = __instance.transform.parent;
            if (!root || root.Find(HugeName) || !PumpAssets.Pump || !PumpAssets.Controller || !PumpAssets.HugePump) return;
            var mage = root.Find("shopkeeper (mage)");
            var first = Slot(root, "shop item (6)"); var second = Slot(root, "shop item (7)"); var last = Slot(root, "shop item (11)");
            if (!mage || !mage.GetComponent<Shopkeeper>() || !Fish(first) || !Fish(second) || !Fish(last)) return;
            Place(first, PumpAssets.Pump, new Vector3(15.55371f, 3.1187f, -10.93803f));
            Place(second, PumpAssets.Pump, new Vector3(15.08654f, 3.1187f, -10.67266f));
            Place(last, PumpAssets.Controller, new Vector3(14.6102f, 3.1187f, -10.37538f));
            Extra(root, ExtraName, last, PumpAssets.Controller, new Vector3(14.16358f, 3.1187f, -10.10905f));
            Extra(root, ThirdName, last, PumpAssets.Controller, new Vector3(13.71696f, 3.1187f, -9.84272f));
            Extra(root, HugeName, last, PumpAssets.HugePump, new Vector3(14.49907f, 1.29575f, -11.59646f));
            Plugin.Log?.LogInfo("Academy mage: three controllers, two Propellers and one Huge Propeller stocked.");
        }
        private static ShopItemSpawner Slot(Transform root, string name)
        {
            var t = root.Find(name); return t ? t.GetComponent<ShopItemSpawner>() : null;
        }
        private static bool Fish(ShopItemSpawner slot)
        {
            var save = slot && slot.itemPrefab ? slot.itemPrefab.GetComponent<SaveablePrefab>() : null;
            return save && save.prefabIndex == 31;
        }
        private static void Extra(Transform root, string name, ShopItemSpawner template, GameObject prefab, Vector3 local)
        {
            if (root.Find(name)) return;
            var go = new GameObject(name); go.SetActive(false); go.transform.SetParent(root, false);
            // ItemRigidbody resets a child's local scale to one each LateUpdate: the slot's scale keeps the item at world scale one.
            go.transform.localScale = template.transform.localScale;
            var slot = go.AddComponent<ShopItemSpawner>(); slot.availableAtNight = template.availableAtNight; slot.priceMult = template.priceMult;
            Place(slot, prefab, local); go.SetActive(true);
        }
        private static void Place(ShopItemSpawner spawner, GameObject prefab, Vector3 local)
        {
            spawner.transform.localPosition = local; spawner.transform.localRotation = Lying; spawner.itemPrefab = prefab;
        }
    }
}
