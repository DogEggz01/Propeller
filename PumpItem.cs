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
            Item.itemRigidbodyC.attached && Mount.Verified && Item.itemRigidbodyC.GetCurrentInventorySlot() == null &&
            Item.itemRigidbodyC.GetCurrentBox() == null && Saveable.currentCrateId == 0 && gameObject.layer != 26;
        internal bool CanOperate => !IsPump && Installed && Item.nailed;
        internal bool WheelSelected => CanOperate && BoundWheel && PumpWorld.Selected(BoundWheel) == this;
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
        private string cachedWheelKey;
        private Transform cachedWheelBoat;
        private GPButtonSteeringWheel cachedWheel;
        internal GPButtonSteeringWheel BoundWheel
        {
            get
            {
                if (Record == null || !Item.currentActualBoat || string.IsNullOrEmpty(Record.Wheel)) return null;
                if (cachedWheelKey != Record.Wheel || cachedWheelBoat != Item.currentActualBoat || !cachedWheel)
                {
                    cachedWheelKey = Record.Wheel; cachedWheelBoat = Item.currentActualBoat; cachedWheel = null;
                    foreach (var candidate in Item.currentActualBoat.parent.GetComponentsInChildren<GPButtonSteeringWheel>(false))
                        if (PumpWorld.WheelKey(candidate, Item.currentActualBoat.parent) == Record.Wheel) { cachedWheel = candidate; break; }
                }
                return cachedWheel;
            }
        }

        private void Awake()
        {
            Item = GetComponent<ShipItem>();
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
            }
            identityText = Require("IdentityText").GetComponent<TextMesh>();
        }
        private Transform Require(string name)
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
            string id = pump == null || pump.Number <= 0 ? "UNPAIRED" : PumpState.Label(pump);
            if (identityText && identityText.text != id) identityText.text = id;
            if (IsPump)
            {
                if (Installed && Item.nailed && PumpWorld.SimulationAllowed && Record.Enabled && PumpWorld.PairOperational(PumpWorld.Find(Record.PeerId), out _))
                    rotor.Rotate(rotorAxis, 720f * Record.Throttle * Time.deltaTime, Space.Self);
                return;
            }
            float throttle = pump == null ? 0f : pump.Throttle;
            needle.localRotation = Quaternion.AngleAxis(Mathf.Lerp(80f, -80f, Mathf.Clamp01(throttle)), needleAxis) * needleBase;
            int power = pump == null ? -1 : pump.Power;
            powerDisplay.Show(power, Time.deltaTime, !PumpWorld.InputAllowed);
            lever.localRotation = leverBase * Quaternion.AngleAxis(pump != null && pump.Enabled ? -50f : 50f, Vector3.right);
            safetyLever.localRotation = safetyBase * Quaternion.AngleAxis(Record.SafetyEnabled ? -35f : 35f, Vector3.right);
            immersionLever.localRotation = immersionBase * Quaternion.AngleAxis(pump != null && pump.RequireImmersion ? -35f : 35f, Vector3.right);
        }
    }

    public sealed class PumpControl : GoPointerButton
    {
        internal enum Action { Toggle, Decrease, Increase, SelectWheel, Immersion, Safety }
        internal PumpItem Owner;
        private Action action;
        private Transform button;
        private Vector3 rest, pressDirection;
        private Quaternion restRotation;
        private Vector3 rockerAxis;
        private GameObject ivory;
        private Collider hitCollider;
        private float pressUntil;
        internal void Configure(PumpItem owner, Action kind, Transform animatedButton)
        {
            Owner = owner; action = kind; button = animatedButton;
            hitCollider = GetComponent<Collider>();
            hitCollider.enabled = false;
            if (button) { rest = button.localPosition; pressDirection = button.parent.InverseTransformDirection(owner.transform.forward).normalized * .00363f; }
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
            if (action == Action.Safety)
            {
                Owner.Record.SafetyEnabled = !Owner.Record.SafetyEnabled;
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
            switch (action)
            {
                case Action.Toggle: pump.Record.Enabled = !pump.Record.Enabled; if (!pump.Record.Enabled) pump.StopDrive(); break;
                case Action.Immersion: pump.Record.RequireImmersion = !pump.Record.RequireImmersion; break;
                case Action.Decrease: pump.Record.ChangePower(-1); break;
                case Action.Increase: pump.Record.ChangePower(1); break;
                case Action.SelectWheel:
                    var wheel = PumpWorld.FindWheel(Owner);
                    if (wheel) PumpWorld.State.Select(Owner.Record, PumpWorld.WheelKey(wheel, Owner.Item.currentActualBoat.parent));
                    break;
            }
            pressUntil = Time.time + .14f;
        }
        private static void Notify(string message)
        {
            if (NotificationUi.instance) NotificationUi.instance.ShowNotification(message);
        }
        public override void ExtraLateUpdate()
        {
            if (button)
            {
                bool depressed = action == Action.SelectWheel ? Owner && Owner.WheelSelected : Time.time < pressUntil;
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
        private bool previewValid, recoveryChecked;
        private BoatEmbarkCollider bound;
        internal bool Pending;
        internal bool LoadedFromSave;

        internal PumpMount(PumpItem owner) { this.owner = owner; housing = owner.GetComponent<BoxCollider>(); }
        internal static void ResetCache() { boats = null; nextBoatScan = 0f; }
        internal void ResetBinding() { bound = null; recoveryChecked = false; }
        internal void Cancel() { Pending = previewValid = false; contact = null; bound = null; LoadedFromSave = false; }
        internal bool Stored => owner.Saveable.currentCrateId != 0 || owner.gameObject.layer == 26 ||
            (owner.Item.itemRigidbodyC && (owner.Item.itemRigidbodyC.GetCurrentBox() || owner.Item.itemRigidbodyC.GetCurrentInventorySlot()));
        internal bool Cached => owner.Saveable && (owner.Saveable.GetParentObject() == -2 || owner.Saveable.GetParentObject() == -3);
        internal bool ProtectOwnership => Cached || (!owner.Item.held && !Stored && (Pending || (owner.Record != null && owner.Record.Mounted)));
        internal bool Verified => bound && owner.Item.currentActualBoat == bound.transform.parent &&
            owner.Item.currentWalkCol == bound.walkCollider && owner.Item.transform.parent == bound.transform.parent &&
            owner.Item.itemRigidbodyC && owner.Item.itemRigidbodyC.transform.parent == bound.walkCollider &&
            owner.Saveable.GetParentObject() == PumpWorld.BoatId(bound.transform.parent);

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
        internal static BoatEmbarkCollider ResolveSurface(Collider surface, out Transform frame)
        {
            frame = null;
            if (!surface || surface.GetComponentInParent<ShipItem>() || surface.GetComponentInParent<ItemRigidbody>()) return null;
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
            // Only a boat surface can create a powered-item mount. Ordinary world
            // geometry must remain a free drop, not a permanently pending boat claim.
            if (!ResolveSurface(hit.collider, out _)) return false;
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
            if (!owner.Item.itemRigidbodyC.GetBody() || ItemSave.GetValue(owner.Item) == null) return;
            if (Pending)
            {
                if (!contact) { Pending = false; return; }
                var boat = ResolveSurface(contact, out var frame);
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
            else if (!owner.Record.Mounted && LoadedFromSave && !recoveryChecked)
            {
                // Preview 0.1 could save a wall placement without the Mounted record.
                // Recover only a flush, aligned hull contact, never an arbitrary nearby item.
                recoveryChecked = true;
                var inward = owner.transform.forward;
                if (!Raycast(new Ray(owner.transform.position - inward * .05f, inward), .07f, out var hit) ||
                    Vector3.Distance(hit.point, owner.transform.position) > .012f || Vector3.Dot(-hit.normal, inward) < .985f) return;
                var boat = ResolveSurface(hit.collider, out var frame);
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
            if (!Usable(boat) || !proxy || !proxy.GetBody() || ItemSave.GetValue(item) == null) return false;
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

    [HarmonyPatch(typeof(ShipItem), "Update")]
    internal static class PumpPlacementPreview
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ShipItem __instance, Transform ___itemRigidbody, ref bool ___inRangeOfWall, ref Vector3 ___attachPos, ref Quaternion ___attachRot)
        {
            var pump = __instance.GetComponent<PumpItem>();
            if (!pump || !__instance.held) return;
            ___inRangeOfWall = pump.Mount.Preview(___itemRigidbody, out ___attachPos, out ___attachRot);
            if (___inRangeOfWall) pump.Mount.ShowPreview(___attachPos, ___attachRot);
            else pump.Mount.HidePreview();
        }
    }
    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.ExtraFixedUpdate))]
    internal static class PumpKeepMountedBoat
    {
        private static bool Prefix(ShipItem __instance)
        {
            var pump = __instance.GetComponent<PumpItem>();
            return !pump || !pump.Mount.ProtectOwnership;
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
            if (!__result || __instance.GetHeldItem() is ShipItemHammer || !(___stickyClickedButton is GPButtonSteeringWheel wheel)) return;
            var control = ___pointedAtButton as PumpControl;
            if (!control || !control.CanUse || !wheel.transform.IsChildOf(control.Owner.Item.currentActualBoat.parent)) return;
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
        private static bool Prefix(ShipItem item)
        {
            var pump = item ? item.GetComponent<PumpItem>() : null;
            if (!pump) return true;
            // Keep vanilla's hold timer and animation; attached items are stable
            // relative to their hull even when the physics proxy is moving.
            if (PumpWorld.InputAllowed && pump.Installed)
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
            for (int i = 0; i < 3; i++)
            {
                float before = elapsed[i];
                elapsed[i] = Mathf.Min(.25f, elapsed[i] + Mathf.Max(0f, dt));
                if (before < .125f && elapsed[i] >= .125f) { shown[i] = target[i]; digits[i].text = Labels[shown[i]]; }
                if (elapsed[i] >= .25f && shown[i] != target[i]) { elapsed[i] = 0f; }
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
        internal static bool IsOpen => open;

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
        }
        internal static void Close()
        {
            if (!open) return;
            open = false;
            owner = null;
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

    // Capture all native positions together; Start order cannot shift the layout twice.
    [HarmonyPatch(typeof(ShopItemSpawner), "Start")]
    internal static class AcademyStock
    {
        internal const string SceneName = "island 8 A Academy";
        internal const string ExtraName = "JetPump Academy controller left";
        internal const string ThirdName = "Propeller Academy controller far left";
        internal const string HugeName = "Propeller Academy huge";
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
            Vector3 a=first.transform.position, b=second.transform.position, c=last.transform.position;
            Vector3 right=Vector3.ProjectOnPlane(a-c,Vector3.up).normalized;
            if(right.sqrMagnitude<.99f) return;
            Vector3 outward=-Vector3.Cross(right,Vector3.up), shift=right*.18f;
            Quaternion rotation=Quaternion.LookRotation(Vector3.down,-outward);
            float counter=Surface(last);
            Vector3 hugePosition=(a+b+right*.015f)*.5f+shift+outward*.55f-right*.15f;
            
            Place(first,PumpAssets.Pump,a+shift+right*.015f,Surface(first),rotation);
            Place(second,PumpAssets.Pump,b+shift,Surface(second),rotation);
            Place(last,PumpAssets.Controller,c+shift,counter,rotation);
            Extra(root,ExtraName,last,PumpAssets.Controller,c+shift-right*.26f,counter,rotation);
            Extra(root,ThirdName,last,PumpAssets.Controller,c+shift-right*.52f,counter,rotation);
            var huge=Extra(root,HugeName,last,PumpAssets.HugePump,hugePosition,counter,rotation);
            huge.gameObject.AddComponent<AcademyGroundPlacement>().Initialize(root,counter);
            Plugin.Log?.LogInfo("Academy mage: three controllers, two Propellers and one Huge Propeller stocked.");
        }
        private static ShopItemSpawner Slot(Transform root,string name)
        {
            var t=root.Find(name); return t ? t.GetComponent<ShopItemSpawner>() : null;
        }
        private static bool Fish(ShopItemSpawner slot)
        {
            var save=slot && slot.itemPrefab ? slot.itemPrefab.GetComponent<SaveablePrefab>() : null;
            return save && save.prefabIndex==31;
        }
        private static float Surface(ShopItemSpawner slot)
        {
            var mesh=slot.GetComponent<MeshRenderer>(); var filter=slot.GetComponent<MeshFilter>();
            return mesh && filter && filter.sharedMesh ? mesh.bounds.min.y : slot.transform.position.y;
        }
        internal static bool Ground(Vector3 position,float counter,out float surface)
        {
            Physics.SyncTransforms(); surface=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(new Vector3(position.x,counter-.05f,position.z),Vector3.down,5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(hit.normal.y>.5f && hit.point.y<counter-.4f && !hit.collider.GetComponentInParent<ShipItem>()) surface=Mathf.Max(surface,hit.point.y);
            return !float.IsNegativeInfinity(surface);
        }
        private static ShopItemSpawner Extra(Transform root,string name,ShopItemSpawner template,GameObject prefab,Vector3 position,float surface,Quaternion rotation)
        {
            if(root.Find(name)) return root.Find(name).GetComponent<ShopItemSpawner>();
            var go=new GameObject(name); go.SetActive(false); go.transform.SetParent(root,false);
            // Vanilla ItemRigidbody resets child local scale to one every LateUpdate.
            // Match the original shop slots: world scale one, even under Academy scale .5.
            go.transform.localScale=template.transform.localScale;
            var slot=go.AddComponent<ShopItemSpawner>(); slot.availableAtNight=template.availableAtNight; slot.priceMult=template.priceMult;
            Place(slot,prefab,position,surface,rotation); go.SetActive(true); return slot;
        }
        internal static void Place(ShopItemSpawner spawner,GameObject prefab,Vector3 position,float surface,Quaternion rotation)
        {
            var housing=prefab.GetComponent<BoxCollider>(); Vector3 half=housing.size*.5f;
            float bottom=float.PositiveInfinity;
            for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
                bottom=Mathf.Min(bottom,(rotation*(housing.center+Vector3.Scale(half,new Vector3(x,y,z)))).y);
            position.y=surface-bottom+.003f;
            spawner.transform.SetPositionAndRotation(position,rotation); spawner.itemPrefab=prefab;
        }
    }
    // Defer native spawning until terrain is present; never guess a floor height.
    internal sealed class AcademyGroundPlacement : MonoBehaviour
    {
        private Transform island;
        private Vector3 counterPoint;
        internal void Initialize(Transform root,float counter)
        {
            island=root; counterPoint=root.InverseTransformPoint(new Vector3(transform.position.x,counter,transform.position.z));
        }
        internal bool TryPlace(ShopItemSpawner slot)
        {
            if (!island || !AcademyStock.Ground(transform.position,island.TransformPoint(counterPoint).y,out float floor)) return false;
            AcademyStock.Place(slot,slot.itemPrefab,transform.position,floor,transform.rotation);
            return true;
        }
    }
    [HarmonyPatch(typeof(ShopItemSpawner), "SpawnItem")]
    internal static class AcademyGroundSpawn
    {
        private static bool Prefix(ShopItemSpawner __instance)
        {
            var placement=__instance.GetComponent<AcademyGroundPlacement>();
            return !placement || placement.TryPlace(__instance);
        }
    }
}
