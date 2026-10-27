using HarmonyLib;
using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace JetPump;

public sealed class PumpItem : MonoBehaviour
{
	internal ShipItem Item;

	internal SaveablePrefab Saveable;

	internal PumpRecord Record;

	internal PumpMount Mount;

	internal PumpWater Water;

	internal bool Retiring;

	private int session = -1;

	private int warmup;

	private float reconcileTimer;

	private Transform rotor;

	private Transform needle;

	private Transform lever;

	private Transform immersionLever;

	private Transform safetyLever;

	private Vector3 thrustLocal;

	private Vector3 thrustFacing;

	private Vector3 rotorAxis;

	private Vector3 needleAxis;

	private Quaternion leverBase;

	private Quaternion immersionBase;

	private Quaternion safetyBase;

	private Quaternion needleBase;

	private TextMesh[] powerDigits;

	private TextMesh identityText;

	private PumpPowerDisplay powerDisplay;

	private readonly PumpDrive drive = new PumpDrive();

	private readonly List<PumpControl> controls = new List<PumpControl>();

	private Rigidbody boatBody;

	private PumpTrim trim;

	private Transform bodyOwner;

	internal bool IsPump
	{
		get
		{
			if ((bool)Saveable)
			{
				return PumpState.IsPump(Saveable.prefabIndex);
			}
			return false;
		}
	}

	internal bool Installed
	{
		get
		{
			if (!Retiring && !Mount.Cached && Record != null && Record.Mounted && warmup >= 8 && Item.sold && !Item.held && (bool)Item.currentActualBoat && PumpWorld.BoatId(Item.currentActualBoat) == Record.BoatId && (bool)Item.itemRigidbodyC && Item.itemRigidbodyC.attached && Mount.Verified && Item.itemRigidbodyC.GetCurrentInventorySlot() == null && Item.itemRigidbodyC.GetCurrentBox() == null && Saveable.currentCrateId == 0)
			{
				return base.gameObject.layer != 26;
			}
			return false;
		}
	}

	internal bool CanOperate
	{
		get
		{
			if (!IsPump && Installed)
			{
				return Item.nailed;
			}
			return false;
		}
	}

	internal bool WheelSelected
	{
		get
		{
			PumpItem pump;
			if (CanOperate && Record.Wheel.Length > 0)
			{
				return PumpWorld.PairOperational(this, out pump);
			}
			return false;
		}
	}

	internal Vector3 SpatialOffset
	{
		get
		{
			if (!Item.currentActualBoat)
			{
				return Vector3.zero;
			}
			return Item.currentActualBoat.up * Plugin.VerticalOffset;
		}
	}

	private void Awake()
	{
		Item = GetComponent<ShipItem>();
		Saveable = GetComponent<SaveablePrefab>();
		Mount = new PumpMount(this);
		if (IsPump)
		{
			Transform transform = Require("ThrustPoint");
			thrustLocal = base.transform.InverseTransformPoint(transform.position);
			thrustFacing = base.transform.InverseTransformDirection(transform.forward).normalized;
			rotor = Require("RotorPivot");
			rotorAxis = rotor.InverseTransformDirection(transform.forward).normalized;
			Water = new PumpWater(this, Require("WaterOpening"));
		}
		else
		{
			needle = Require("ThrottleNeedlePivot");
			lever = Require("OnOffLeverPivot");
			needleBase = needle.localRotation;
			needleAxis = needle.parent.InverseTransformDirection(base.transform.forward).normalized;
			leverBase = lever.localRotation;
			powerDigits = new TextMesh[3]
			{
				Require("PowerTextHundreds").GetComponent<TextMesh>(),
				Require("PowerTextTens").GetComponent<TextMesh>(),
				Require("PowerTextOnes").GetComponent<TextMesh>()
			};
			powerDisplay = new PumpPowerDisplay(base.transform, powerDigits);
			AddControl("HIT_OnOff", PumpControl.Action.Toggle, null);
			AddControl("HIT_PowerDecrease", PumpControl.Action.Decrease, Require("PowerDecreasePivot"));
			AddControl("HIT_PowerIncrease", PumpControl.Action.Increase, Require("PowerIncreasePivot"));
			AddControl("HIT_WheelAssign", PumpControl.Action.SelectWheel, Require("WheelAssignPivot"));
			immersionLever = Require("ImmersionLeverPivot");
			immersionBase = immersionLever.localRotation;
			AddControl("HIT_Immersion", PumpControl.Action.Immersion, null);
			safetyLever = Require("SafetyLeverPivot");
			safetyBase = safetyLever.localRotation;
			AddControl("HIT_Safety", PumpControl.Action.Safety, null);
			AddControl("HIT_ControlGroup", PumpControl.Action.Group, Require("GroupSliderPivot"));
		}
		identityText = Require("IdentityText").GetComponent<TextMesh>();
	}

	internal Transform Require(string name)
	{
		Transform[] componentsInChildren = GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform.name == name)
			{
				return transform;
			}
		}
		throw new InvalidOperationException("Propeller asset missing " + name);
	}

	private void AddControl(string name, PumpControl.Action action, Transform button)
	{
		PumpControl pumpControl = Require(name).gameObject.AddComponent<PumpControl>();
		pumpControl.Configure(this, action, button);
		controls.Add(pumpControl);
	}

	private void OnEnable()
	{
		if (!PumpWorld.Live.Contains(this))
		{
			PumpWorld.Live.Add(this);
		}
		Font.textureRebuilt += RefreshFont;
		RefreshFont(identityText ? identityText.font : null);
	}

	private void OnDisable()
	{
		StopDrive(resetWater: true);
		powerDisplay?.Reset();
		PumpWorld.Live.Remove(this);
		Font.textureRebuilt -= RefreshFont;
	}

	internal void StopDrive(bool resetWater = false)
	{
		drive.Stop();
		if (resetWater)
		{
			Water?.Reset();
		}
	}

	private void RefreshFont(Font font)
	{
		if ((bool)font && (bool)identityText && identityText.font == font)
		{
			identityText.GetComponent<Renderer>().sharedMaterial.mainTexture = font.material.mainTexture;
		}
		if (!font || powerDigits == null)
		{
			return;
		}
		TextMesh[] array = powerDigits;
		foreach (TextMesh textMesh in array)
		{
			if ((bool)textMesh && textMesh.font == font)
			{
				textMesh.GetComponent<Renderer>().sharedMaterial.mainTexture = font.material.mainTexture;
			}
		}
	}

	internal bool EnsureState()
	{
		if (Retiring || PumpWorld.Loading || !PumpWorld.State.Writable || !Saveable || Saveable.instanceId <= 0)
		{
			return false;
		}
		if (session != PumpWorld.Session || Record == null || Record.Id != Saveable.instanceId)
		{
			Record = PumpWorld.State.GetOrCreate(Saveable.instanceId, Saveable.prefabIndex);
			session = PumpWorld.Session;
			warmup = 0;
			Mount.ResetBinding();
			StopDrive(resetWater: true);
			powerDisplay?.Reset();
		}
		return true;
	}

	internal void Removed()
	{
		Mount.Cancel();
		StopDrive(resetWater: true);
		if (EnsureState())
		{
			PumpWorld.State.RemoveFromWall(Record);
		}
	}

	internal void EnforceNails()
	{
		if (!Retiring && !Mount.Cached && !PumpWorld.Loading && !GameState.currentlyLoading && Record != null && !Item.nailed)
		{
			if (IsPump)
			{
				PumpWorld.State.StopPump(Record);
			}
			else
			{
				PumpWorld.State.StopController(Record);
			}
			StopDrive(resetWater: true);
		}
	}

	private void Update()
	{
		if (!EnsureState())
		{
			StopDrive(resetWater: true);
			powerDisplay?.Reset();
			SetControls(active: false);
			return;
		}
		EnforceNails();
		if (IsPump)
		{
			if (Installed && Item.nailed && PumpWorld.SimulationAllowed)
			{
				if (Record.RequireImmersion)
				{
					Water.Sample();
				}
				else
				{
					Water.Reset();
				}
			}
			else
			{
				StopDrive(resetWater: true);
			}
			if (!Record.Enabled)
			{
				StopDrive();
			}
		}
		SetControls(CanOperate && PumpWorld.SimulationAllowed);
		reconcileTimer -= Time.deltaTime;
		if (reconcileTimer <= 0f && PumpWorld.SimulationAllowed)
		{
			reconcileTimer = 0.25f;
			if (CanOperate)
			{
				PumpWorld.AutoPair(this);
			}
			if (!Installed && warmup >= 8 && Record.Mounted && ((bool)Item.held || Mount.Stored))
			{
				Removed();
			}
		}
		RefreshView();
	}

	private void SetControls(bool active)
	{
		for (int i = 0; i < controls.Count; i++)
		{
			controls[i].SetAvailable(active);
		}
	}

	private void FixedUpdate()
	{
		if (!ApplyThrust())
		{
			StopDrive();
		}
	}

	private bool ApplyThrust()
	{
		if (!EnsureState() || !Item.itemRigidbodyC)
		{
			return false;
		}
		EnforceNails();
		if (!PumpWorld.SimulationAllowed)
		{
			return false;
		}
		Mount.Tick();
		if (Mount.Pending)
		{
			return false;
		}
		if (warmup < 8)
		{
			Item.itemRigidbodyC.attached = Record.Mounted && !Item.held && Saveable.currentCrateId == 0;
			warmup++;
			return false;
		}
		if (!IsPump || !Installed || !Item.nailed || Record.AppliedPower <= 0f)
		{
			return false;
		}
		if (!PumpWorld.PairOperational(PumpWorld.Find(Record.PeerId), out var pump) || pump != this)
		{
			return false;
		}
		if (bodyOwner != Item.currentActualBoat || !boatBody)
		{
			bodyOwner = Item.currentActualBoat;
			boatBody = (bodyOwner.parent ? bodyOwner.parent.GetComponent<Rigidbody>() : null);
			trim = null;
		}
		if (!boatBody || boatBody.isKinematic || !Item.currentWalkCol || Item.itemRigidbodyC.transform.parent != Item.currentWalkCol)
		{
			return false;
		}
		if (Record.RequireImmersion)
		{
			Water.Evaluate();
		}
		float num = Water.Power(Record);
		if (num <= 0f)
		{
			return false;
		}
		if (!TryThrustGeometry(out var point, out var direction))
		{
			return false;
		}
		if (!trim)
		{
			trim = boatBody.GetComponent<PumpTrim>() ?? boatBody.gameObject.AddComponent<PumpTrim>();
		}
		drive.Apply(boatBody, direction, point, num, Time.fixedDeltaTime, trim, PumpState.RatedThrust(Record.Kind));
		return true;
	}

	internal bool TryThrustGeometry(out Vector3 point, out Vector3 direction)
	{
		point = (direction = Vector3.zero);
		if (!IsPump || !Installed || !Item.currentWalkCol)
		{
			return false;
		}
		Transform transform = Item.itemRigidbodyC.transform;
		point = ConvertPoint(transform.TransformPoint(thrustLocal), Item.currentWalkCol, Item.currentActualBoat) + SpatialOffset;
		direction = -ConvertDirection(transform.TransformDirection(thrustFacing), Item.currentWalkCol, Item.currentActualBoat);
		return true;
	}

	public static Vector3 ConvertPoint(Vector3 point, Transform walk, Transform actual)
	{
		return actual.TransformPoint(walk.InverseTransformPoint(point));
	}

	public static Vector3 ConvertDirection(Vector3 direction, Transform walk, Transform actual)
	{
		return actual.TransformDirection(walk.InverseTransformDirection(direction)).normalized;
	}

	private void RefreshView()
	{
		PumpRecord pumpRecord = (IsPump ? Record : PumpWorld.State.Peer(Record));
		string text = ((pumpRecord == null || pumpRecord.Number <= 0) ? "UNPAIRED" : PumpState.Label(pumpRecord));
		if ((bool)identityText && identityText.text != text)
		{
			identityText.text = text;
		}
		if (IsPump)
		{
			if (Installed && Item.nailed && PumpWorld.SimulationAllowed && Record.Enabled && PumpWorld.PairOperational(PumpWorld.Find(Record.PeerId), out var _))
			{
				rotor.Rotate(rotorAxis, 720f * Record.Throttle * Time.deltaTime, Space.Self);
			}
			return;
		}
		float value = pumpRecord?.Throttle ?? 0f;
		needle.localRotation = Quaternion.AngleAxis(Mathf.Lerp(80f, -80f, Mathf.Clamp01(value)), needleAxis) * needleBase;
		int next = pumpRecord?.Power ?? (-1);
		powerDisplay.Show(next, Time.deltaTime, !PumpWorld.InputAllowed);
		lever.localRotation = leverBase * Quaternion.AngleAxis((pumpRecord != null && pumpRecord.Enabled) ? (-50f) : 50f, Vector3.right);
		safetyLever.localRotation = safetyBase * Quaternion.AngleAxis(Record.SafetyEnabled ? (-35f) : 35f, Vector3.right);
		immersionLever.localRotation = immersionBase * Quaternion.AngleAxis((pumpRecord != null && pumpRecord.RequireImmersion) ? (-35f) : 35f, Vector3.right);
	}
}

public sealed class PumpControl : GoPointerButton
{
	internal enum Action
	{
		Toggle,
		Decrease,
		Increase,
		SelectWheel,
		Immersion,
		Safety,
		Group
	}

	internal PumpItem Owner;

	private Action action;

	private Transform button;

	private Vector3 rest;

	private Vector3 pressDirection;

	private Quaternion restRotation;

	private Vector3 rockerAxis;

	private GameObject ivory;

	private Collider hitCollider;

	private float pressUntil;

	private Vector3[] stops;

	private bool knobPlaced;

	internal bool CanUse
	{
		get
		{
			if ((bool)Owner && Owner.CanOperate)
			{
				return PumpWorld.InputAllowed;
			}
			return false;
		}
	}

	internal void Configure(PumpItem owner, Action kind, Transform animatedButton)
	{
		Owner = owner;
		action = kind;
		button = animatedButton;
		hitCollider = GetComponent<Collider>();
		hitCollider.enabled = false;
		if ((bool)button)
		{
			rest = button.localPosition;
			pressDirection = button.parent.InverseTransformDirection(owner.transform.forward).normalized * 0.00363f;
		}
		if (kind == Action.Group)
		{
			stops = new Vector3[4];
			for (int i = 0; i < stops.Length; i++)
			{
				Transform transform = owner.Require("GroupStop" + i);
				stops[i] = button.parent.InverseTransformPoint(transform.position);
			}
			button.localPosition = stops[0];
		}
		if (kind == Action.SelectWheel)
		{
			restRotation = button.localRotation;
			rockerAxis = button.parent.InverseTransformDirection(owner.transform.up).normalized;
			Transform[] componentsInChildren = button.GetComponentsInChildren<Transform>(includeInactive: true);
			foreach (Transform transform2 in componentsInChildren)
			{
				if (transform2.name == "WheelIvoryStrip")
				{
					ivory = transform2.gameObject;
				}
			}
			if (!ivory)
			{
				throw new InvalidOperationException("Missing left-edge wheel indicator");
			}
		}
		description = (lookText = "");
		enableRedOutline = false;
	}

	internal void SetAvailable(bool available)
	{
		if (available)
		{
			base.gameObject.layer = 0;
		}
		if ((bool)hitCollider)
		{
			hitCollider.enabled = available;
		}
		unclickable = !available;
	}

	public override void OnActivate(GoPointer pointer)
	{
		if (!pointer || !(pointer.GetHeldItem() is ShipItemHammer))
		{
			Activate();
		}
	}

	public override void OnAltActivate(GoPointer pointer)
	{
		if ((!pointer || !(pointer.GetHeldItem() is ShipItemHammer)) && CanUse && action == Action.SelectWheel)
		{
			PairingMenu.Open(Owner);
		}
	}

	internal void Activate()
	{
		if (!CanUse)
		{
			return;
		}
		PumpState state = PumpWorld.State;
		PumpRecord record = Owner.Record;
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
		PumpRecord pumpRecord = PumpWorld.State.Peer(Owner.Record);
		if (pumpRecord == null)
		{
			if (Owner.Record.PeerId == 0)
			{
				PairingMenu.Open(Owner);
			}
			else
			{
				Notify("Paired propeller is not ready.");
			}
			return;
		}
		PumpItem pump = PumpWorld.Find(pumpRecord.Id);
		if ((bool)pump && !pump.Item.nailed)
		{
			Notify("Propeller is not hammered.\nNail it down before use.");
			return;
		}
		if (!PumpWorld.PairOperational(Owner, out pump))
		{
			Notify("Paired propeller is not ready.");
			return;
		}
		switch (action)
		{
		case Action.Toggle:
		{
			bool flag2 = !pump.Record.Enabled;
			state.SetEnabled(record, flag2, PumpWorld.Usable);
			if (flag2)
			{
				break;
			}
			foreach (PumpRecord item in state.Members(record, PumpWorld.Usable))
			{
				PumpItem pumpItem = PumpWorld.Find(item.PeerId);
				if ((bool)pumpItem)
				{
					pumpItem.StopDrive();
				}
			}
			break;
		}
		case Action.Immersion:
			state.SetImmersion(record, !pump.Record.RequireImmersion, PumpWorld.Usable);
			break;
		case Action.Decrease:
			state.SetPower(record, pump.Record.Power - 5, PumpWorld.Usable);
			break;
		case Action.Increase:
			state.SetPower(record, pump.Record.Power + 5, PumpWorld.Usable);
			break;
		case Action.SelectWheel:
		{
			bool flag = record.Wheel.Length == 0;
			if (flag && !PumpWorld.HasHelm(Owner))
			{
				Notify("No wheel or tiller on this boat.");
				return;
			}
			state.SetWheel(record, flag, PumpWorld.Usable);
			break;
		}
		}
		pressUntil = Time.time + 0.14f;
	}

	private static void Notify(string message)
	{
		if ((bool)NotificationUi.instance)
		{
			NotificationUi.instance.ShowNotification(message);
		}
	}

	public override void ExtraLateUpdate()
	{
		if (action == Action.Group)
		{
			if ((bool)button && !(Owner == null) && Owner.Record != null)
			{
				Vector3 vector = stops[Mathf.Clamp(Owner.Record.Group, 0, stops.Length - 1)];
				button.localPosition = (knobPlaced ? Vector3.MoveTowards(button.localPosition, vector, 0.25f * Time.deltaTime) : vector);
				knobPlaced = true;
			}
		}
		else if ((bool)button)
		{
			bool flag = ((action != Action.SelectWheel) ? (Time.time < pressUntil) : ((bool)Owner && Owner.WheelSelected));
			if (action == Action.SelectWheel)
			{
				button.localRotation = Quaternion.AngleAxis(flag ? (-10f) : 10f, rockerAxis) * restRotation;
				ivory.SetActive(flag);
			}
			else
			{
				button.localPosition = rest + (flag ? pressDirection : Vector3.zero);
			}
		}
	}
}

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

	private bool previewValid;

	private bool recoveryChecked;

	private BoatEmbarkCollider bound;

	internal bool Pending;

	internal bool LoadedFromSave;

	internal bool Stored
	{
		get
		{
			if (owner.Saveable.currentCrateId == 0 && owner.gameObject.layer != 26)
			{
				if ((bool)owner.Item.itemRigidbodyC)
				{
					if (!owner.Item.itemRigidbodyC.GetCurrentBox())
					{
						return owner.Item.itemRigidbodyC.GetCurrentInventorySlot();
					}
					return true;
				}
				return false;
			}
			return true;
		}
	}

	internal bool Cached
	{
		get
		{
			if ((bool)owner.Saveable)
			{
				if (owner.Saveable.GetParentObject() != -2)
				{
					return owner.Saveable.GetParentObject() == -3;
				}
				return true;
			}
			return false;
		}
	}

	internal bool ProtectOwnership
	{
		get
		{
			if (!Cached)
			{
				if (!owner.Item.held && !Stored)
				{
					if (!Pending)
					{
						if (owner.Record != null)
						{
							return owner.Record.Mounted;
						}
						return false;
					}
					return true;
				}
				return false;
			}
			return true;
		}
	}

	internal bool Verified
	{
		get
		{
			if ((bool)bound && owner.Item.currentActualBoat == bound.transform.parent && owner.Item.currentWalkCol == bound.walkCollider && owner.Item.transform.parent == bound.transform.parent && (bool)owner.Item.itemRigidbodyC && owner.Item.itemRigidbodyC.transform.parent == bound.walkCollider)
			{
				return owner.Saveable.GetParentObject() == PumpWorld.BoatId(bound.transform.parent);
			}
			return false;
		}
	}

	internal PumpMount(PumpItem owner)
	{
		this.owner = owner;
		housing = owner.GetComponent<BoxCollider>();
	}

	internal static void ResetCache()
	{
		boats = null;
		nextBoatScan = 0f;
	}

	internal void ResetBinding()
	{
		bound = null;
		recoveryChecked = false;
	}

	internal void Cancel()
	{
		Pending = (previewValid = false);
		contact = null;
		bound = null;
		LoadedFromSave = false;
	}

	private static BoatEmbarkCollider[] Boats()
	{
		if (boats == null || Time.unscaledTime >= nextBoatScan)
		{
			boats = Object.FindObjectsOfType<BoatEmbarkCollider>();
			nextBoatScan = Time.unscaledTime + 2f;
		}
		return boats;
	}

	private static bool Usable(BoatEmbarkCollider boat)
	{
		if (!boat || !boat.walkCollider || !boat.transform.parent || !boat.transform.parent.parent || !boat.GetComponent<Collider>())
		{
			return false;
		}
		Transform parent = boat.transform.parent.parent;
		if ((bool)parent.GetComponent<Rigidbody>() && (bool)parent.GetComponent<BoatMass>())
		{
			return PumpWorld.BoatId(boat.transform.parent) > 0;
		}
		return false;
	}

	internal static BoatEmbarkCollider ResolveSurface(Collider surface, out Transform frame)
	{
		frame = null;
		if (!surface || (bool)surface.GetComponentInParent<ShipItem>() || (bool)surface.GetComponentInParent<ItemRigidbody>())
		{
			return null;
		}
		BoatEmbarkCollider boatEmbarkCollider = null;
		BoatEmbarkCollider[] array = Boats();
		foreach (BoatEmbarkCollider boatEmbarkCollider2 in array)
		{
			if (!Usable(boatEmbarkCollider2))
			{
				continue;
			}
			Transform transform = (surface.transform.IsChildOf(boatEmbarkCollider2.walkCollider) ? boatEmbarkCollider2.walkCollider : (surface.transform.IsChildOf(boatEmbarkCollider2.transform.parent.parent) ? boatEmbarkCollider2.transform.parent : null));
			if ((bool)transform)
			{
				if ((bool)boatEmbarkCollider && boatEmbarkCollider.transform.parent != boatEmbarkCollider2.transform.parent)
				{
					frame = null;
					return null;
				}
				boatEmbarkCollider = boatEmbarkCollider2;
				frame = transform;
			}
		}
		return boatEmbarkCollider;
	}

	private static BoatEmbarkCollider FindSavedBoat(int id)
	{
		if (id <= 0)
		{
			return null;
		}
		BoatEmbarkCollider[] array = Boats();
		foreach (BoatEmbarkCollider boatEmbarkCollider in array)
		{
			if (Usable(boatEmbarkCollider) && PumpWorld.BoatId(boatEmbarkCollider.transform.parent) == id)
			{
				return boatEmbarkCollider;
			}
		}
		return null;
	}

	private bool Raycast(Ray ray, float distance, out RaycastHit hit)
	{
		int num = Physics.RaycastNonAlloc(ray, hits, distance, -5, QueryTriggerInteraction.Ignore);
		RaycastHit[] array = ((num == hits.Length) ? Physics.RaycastAll(ray, distance, -5, QueryTriggerInteraction.Ignore) : hits);
		if (array != hits)
		{
			num = array.Length;
		}
		hit = default(RaycastHit);
		float num2 = float.PositiveInfinity;
		for (int i = 0; i < num; i++)
		{
			Collider collider = array[i].collider;
			if ((bool)collider && !collider.transform.IsChildOf(owner.transform) && (!owner.Item.itemRigidbodyC || !collider.transform.IsChildOf(owner.Item.itemRigidbodyC.transform)) && array[i].distance < num2)
			{
				num2 = array[i].distance;
				hit = array[i];
			}
		}
		return hit.collider;
	}

	internal static Quaternion SurfaceRotation(Vector3 normal, Vector3 itemUp)
	{
		Vector3 vector = Vector3.ProjectOnPlane((normal.y > 0.8f) ? itemUp : Vector3.up, normal);
		if (vector.sqrMagnitude < 0.001f)
		{
			vector = Vector3.ProjectOnPlane(itemUp, normal);
		}
		if (vector.sqrMagnitude < 0.001f)
		{
			vector = Vector3.ProjectOnPlane(Vector3.forward, normal);
		}
		return Quaternion.LookRotation(-normal, vector.normalized);
	}

	internal bool Preview(Transform proxy, out Vector3 position, out Quaternion rotation)
	{
		previewValid = false;
		position = Vector3.zero;
		rotation = Quaternion.identity;
		if (!proxy || !owner.Item.held || !PumpWorld.InputAllowed)
		{
			return false;
		}
		if (!Raycast(new Ray(proxy.position, proxy.forward), 1.3f, out var hit) || hit.distance < 0.1f)
		{
			return false;
		}
		if (!ResolveSurface(hit.collider, out var _))
		{
			return false;
		}
		rotation = SurfaceRotation(hit.normal, owner.transform.up);
		position = ClearancePosition(hit.point, hit.normal, rotation);
		RememberContact(hit.collider, position, rotation);
		return true;
	}

	internal Vector3 ClearancePosition(Vector3 point, Vector3 normal, Quaternion rotation)
	{
		float num = 0f;
		if ((bool)housing)
		{
			Vector3 a = housing.size * 0.5f;
			for (int i = -1; i <= 1; i += 2)
			{
				for (int j = -1; j <= 1; j += 2)
				{
					for (int k = -1; k <= 1; k += 2)
					{
						num = Mathf.Min(num, Vector3.Dot(rotation * (housing.center + Vector3.Scale(a, new Vector3(i, j, k))), normal));
					}
				}
			}
		}
		return point + normal * (0.001f - num);
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
		Pending = attached && previewValid && (bool)contact;
		bound = null;
	}

	internal void Tick()
	{
		if (Cached || owner.Retiring || (bool)owner.Item.held || Stored || !owner.Item.sold || !owner.Item.itemRigidbodyC || !owner.Item.itemRigidbodyC.GetBody() || ItemSave.GetValue(owner.Item) == null)
		{
			return;
		}
		if (Pending)
		{
			if (!contact)
			{
				Pending = false;
				return;
			}
			BoatEmbarkCollider boatEmbarkCollider = ResolveSurface(contact, out var frame);
			if ((bool)boatEmbarkCollider)
			{
				Vector3 local = frame.InverseTransformPoint(contact.transform.TransformPoint(contactPosition));
				Quaternion localRot = Quaternion.Inverse(frame.rotation) * contact.transform.rotation * contactRotation;
				if (Bind(boatEmbarkCollider, local, localRot))
				{
					PumpWorld.State.Install(owner.Record, PumpWorld.BoatId(boatEmbarkCollider.transform.parent));
					CapturePose();
					Pending = false;
				}
			}
		}
		else if (owner.Record.Mounted && !Verified)
		{
			int parentObject = owner.Saveable.GetParentObject();
			if (parentObject <= 0 || parentObject == owner.Record.BoatId)
			{
				BoatEmbarkCollider boatEmbarkCollider2 = FindSavedBoat(owner.Record.BoatId);
				PumpRecord record = owner.Record;
				if ((bool)boatEmbarkCollider2 && Bind(boatEmbarkCollider2, record.HasPose ? new Vector3(record.X, record.Y, record.Z) : boatEmbarkCollider2.transform.parent.InverseTransformPoint(owner.transform.position), record.HasPose ? new Quaternion(record.Qx, record.Qy, record.Qz, record.Qw).normalized : (Quaternion.Inverse(boatEmbarkCollider2.transform.parent.rotation) * owner.transform.rotation)))
				{
					CapturePose();
				}
			}
		}
		else if (!owner.IsPump && !owner.Record.Mounted && owner.Item.nailed && owner.Item.currentActualBoat)
		{
			// Vanilla has already assigned this panel to a boat; surface contact is unnecessary.
			Transform actualBoat = owner.Item.currentActualBoat;
			BoatEmbarkCollider boat = FindSavedBoat(PumpWorld.BoatId(actualBoat));
			if (boat && Bind(boat, actualBoat.InverseTransformPoint(owner.transform.position),
				Quaternion.Inverse(actualBoat.rotation) * owner.transform.rotation))
			{
				PumpWorld.State.Install(owner.Record, PumpWorld.BoatId(actualBoat));
				CapturePose();
			}
		}
		else
		{
			if (owner.Record.Mounted || !LoadedFromSave || recoveryChecked)
			{
				return;
			}
			recoveryChecked = true;
			Vector3 forward = owner.transform.forward;
			if (Raycast(new Ray(owner.transform.position - forward * 0.05f, forward), 0.07f, out var hit) && !(Vector3.Distance(hit.point, owner.transform.position) > 0.012f) && !(Vector3.Dot(-hit.normal, forward) < 0.985f))
			{
				BoatEmbarkCollider boatEmbarkCollider3 = ResolveSurface(hit.collider, out var frame2);
				if ((bool)boatEmbarkCollider3 && (owner.Saveable.GetParentObject() <= 0 || owner.Saveable.GetParentObject() == PumpWorld.BoatId(boatEmbarkCollider3.transform.parent)) && !(frame2 != boatEmbarkCollider3.transform.parent) && Bind(boatEmbarkCollider3, frame2.InverseTransformPoint(owner.transform.position), Quaternion.Inverse(frame2.rotation) * owner.transform.rotation))
				{
					PumpWorld.State.Install(owner.Record, PumpWorld.BoatId(boatEmbarkCollider3.transform.parent));
					CapturePose();
				}
			}
		}
	}

	internal void CapturePose()
	{
		if (!Cached && !owner.Retiring && Verified && owner.Record != null && owner.Record.Mounted && !owner.Item.held && !Stored)
		{
			Transform currentActualBoat = owner.Item.currentActualBoat;
			Vector3 vector = currentActualBoat.InverseTransformPoint(owner.transform.position);
			Quaternion quaternion = Quaternion.Inverse(currentActualBoat.rotation) * owner.transform.rotation;
			PumpRecord record = owner.Record;
			record.X = vector.x;
			record.Y = vector.y;
			record.Z = vector.z;
			record.Qx = quaternion.x;
			record.Qy = quaternion.y;
			record.Qz = quaternion.z;
			record.Qw = quaternion.w;
			record.HasPose = true;
		}
	}

	private bool Bind(BoatEmbarkCollider boat, Vector3 local, Quaternion localRot)
	{
		ShipItem item = owner.Item;
		ItemRigidbody itemRigidbodyC = item.itemRigidbodyC;
		if (!Usable(boat) || !itemRigidbodyC || !itemRigidbodyC.GetBody() || ItemSave.GetValue(item) == null)
		{
			return false;
		}
		if ((bool)item.currentActualBoat && item.currentActualBoat != boat.transform.parent)
		{
			itemRigidbodyC.ExitBoat();
		}
		item.transform.position = boat.transform.parent.TransformPoint(local);
		item.transform.rotation = boat.transform.parent.rotation * localRot;
		EnterBoat.Invoke(item, new object[1] { boat.GetComponent<Collider>() });
		itemRigidbodyC.attached = true;
		itemRigidbodyC.GetBody().velocity = Vector3.zero;
		itemRigidbodyC.GetBody().angularVelocity = Vector3.zero;
		itemRigidbodyC.GetBody().isKinematic = true;
		bound = boat;
		return Verified;
	}

	internal void ShowPreview(Vector3 position, Quaternion rotation)
	{
		Targeter.Invoke(owner.Item, new object[3]
		{
			position,
			rotation,
			owner.GetComponent<MeshFilter>().sharedMesh
		});
	}

	internal void HidePreview()
	{
		GoPointerTargeter targeter = owner.Item.held.GetTargeter();
		if ((bool)targeter)
		{
			TargeterActive.SetValue(targeter, false);
		}
	}
}

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

[HarmonyPatch(typeof(ShipItem), "ExtraFixedUpdate")]
internal static class PumpKeepMountedBoat
{
	private static bool Prefix(ShipItem __instance)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			return !component.Mount.ProtectOwnership;
		}
		return true;
	}
}

[HarmonyPatch(typeof(SaveablePrefab), "Load")]
internal static class PumpLoadedPlacement
{
	private static void Prefix(SaveablePrefab __instance, SavePrefabData data, bool ___loaded)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component && !___loaded)
		{
			PumpWorld.RetireDuplicate(component, data.instanceId);
		}
	}

	private static void Postfix(SaveablePrefab __instance)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			component.Mount.LoadedFromSave = true;
			component.Mount.ResetBinding();
			PumpMount.ResetCache();
		}
	}
}

[HarmonyPatch(typeof(SaveablePrefab), "PrepareSaveData")]
internal static class PumpSavePose
{
	private static void Prefix(SaveablePrefab __instance)
	{
		__instance.GetComponent<PumpItem>()?.Mount.CapturePose();
	}
}

[HarmonyPatch(typeof(GoPointer), "MainButtonDown")]
internal static class PumpClickWhileSteering
{
	private static int activatedFrame = -1;

	private static void Postfix(GoPointer __instance, GoPointerButton ___stickyClickedButton, GoPointerButton ___pointedAtButton, ref bool __result)
	{
		if (!__result || __instance.GetHeldItem() is ShipItemHammer || !PumpWorld.IsHelm(___stickyClickedButton))
		{
			return;
		}
		PumpControl pumpControl = ___pointedAtButton as PumpControl;
		Rigidbody rigidbody = (((bool)pumpControl && pumpControl.CanUse) ? PumpWorld.BoatBody(pumpControl.Owner) : null);
		if ((bool)rigidbody && !(PumpWorld.HelmBody(___stickyClickedButton) != rigidbody))
		{
			if (activatedFrame != Time.frameCount)
			{
				activatedFrame = Time.frameCount;
				pumpControl.Activate();
			}
			__result = false;
		}
	}
}

[HarmonyPatch(typeof(GoPointer), "GetPointedAtItem")]
internal static class PumpHammerTarget
{
	private static void Postfix(GoPointer __instance, GoPointerButton ___pointedAtButton, ref ShipItem __result)
	{
		if (__instance.GetHeldItem() is ShipItemHammer && ___pointedAtButton is PumpControl pumpControl && (bool)pumpControl.Owner)
		{
			__result = pumpControl.Owner.Item;
		}
	}
}

[HarmonyPatch(typeof(ShipItemHammer), "AllowOnItemClick")]
internal static class PumpHammerAllowed
{
	private static bool Prefix(GoPointerButton lookedAtButton, ref bool __result)
	{
		if (!(lookedAtButton is PumpControl pumpControl) || !pumpControl.Owner)
		{
			return true;
		}
		__result = ShipItemHammer.CanNail(pumpControl.Owner.Item);
		return false;
	}
}

[HarmonyPatch(typeof(ShipItemHammer), "NailItem")]
internal static class PumpHammerMovingMount
{
	private static bool Prefix(ShipItem item)
	{
		PumpItem pumpItem = (item ? item.GetComponent<PumpItem>() : null);
		if (!pumpItem)
		{
			return true;
		}
		bool onboardPanel = !pumpItem.IsPump && item.sold && !item.held
			&& !pumpItem.Mount.Cached && !pumpItem.Mount.Stored
			&& PumpWorld.BoatId(item.currentActualBoat) > 0;
		if (PumpWorld.InputAllowed && (pumpItem.Installed || onboardPanel))
		{
			item.nailed = true;
			if ((bool)UISoundPlayer.instance)
			{
				UISoundPlayer.instance.PlayUISound(UISounds.winchUnclick, 1f, 0.6f);
			}
		}
		return false;
	}
}

[HarmonyPatch(typeof(ShipItem), "OnPickup")]
internal static class PumpPickup
{
	private static void Prefix(ShipItem __instance)
	{
		__instance.GetComponent<PumpItem>()?.Removed();
	}
}

[HarmonyPatch(typeof(ShipItem), "OnDrop")]
internal static class PumpDrop
{
	private static void Postfix(ShipItem __instance, bool ___inRangeOfWall)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			component.Mount.Drop((__instance.sold & ___inRangeOfWall) && !__instance.forceDisableRedOutline && (bool)__instance.itemRigidbodyC && __instance.itemRigidbodyC.attached);
		}
	}
}

[HarmonyPatch(typeof(ShipItem), "OnEnterInventory")]
internal static class PumpInventory
{
	private static void Prefix(ShipItem __instance)
	{
		__instance.GetComponent<PumpItem>()?.Removed();
	}
}

[HarmonyPatch(typeof(ShipItem), "DestroyItem")]
internal static class PumpDestroy
{
	private static void Prefix(ShipItem __instance)
	{
		PumpItem component = __instance.GetComponent<PumpItem>();
		if ((bool)component)
		{
			if (!component.Retiring && !component.Mount.Cached && !PumpWorld.Loading && !GameState.currentlyLoading && component.EnsureState())
			{
				component.Removed();
				PumpWorld.State.Forget(component.Saveable.instanceId);
			}
			component.Retiring = true;
		}
	}
}

internal sealed class PumpPowerDisplay
{
	private static readonly string[] Labels = new string[11]
	{
		"0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
		"-"
	};

	private readonly TextMesh[] digits;

	private readonly Transform[] pivots = new Transform[3];

	private readonly Quaternion[] restRotation = new Quaternion[3];

	private readonly Vector3[] restPosition = new Vector3[3];

	private readonly Vector3[] axis = new Vector3[3];

	private readonly Vector3[] outward = new Vector3[3];

	private readonly int[] shown = new int[3];

	private readonly int[] target = new int[3];

	private readonly float[] elapsed = new float[3] { 0.25f, 0.25f, 0.25f };

	private readonly float[] direction = new float[3];

	private int value = int.MinValue;

	internal PumpPowerDisplay(Transform panel, TextMesh[] texts)
	{
		digits = texts;
		for (int i = 0; i < 3; i++)
		{
			pivots[i] = texts[i].transform.parent.parent;
			restRotation[i] = pivots[i].localRotation;
			restPosition[i] = pivots[i].localPosition;
			axis[i] = pivots[i].parent.InverseTransformDirection(panel.right).normalized;
			outward[i] = pivots[i].parent.InverseTransformDirection(-panel.forward).normalized;
		}
	}

	internal void Reset()
	{
		value = int.MinValue;
	}

	internal void Show(int next, float dt, bool immediate)
	{
		bool flag = immediate || value == int.MinValue || next < 0 || value < 0;
		if ((next != value) | flag)
		{
			int num = 100;
			int num2 = 0;
			while (num2 < 3)
			{
				int num3 = ((next < 0) ? 10 : (next / num % 10));
				if (flag)
				{
					shown[num2] = (target[num2] = num3);
					elapsed[num2] = 0.25f;
					digits[num2].text = Labels[num3];
				}
				else if (target[num2] != num3)
				{
					target[num2] = num3;
					if (elapsed[num2] >= 0.25f)
					{
						elapsed[num2] = 0f;
						direction[num2] = ((next > value) ? 1f : (-1f));
					}
				}
				num2++;
				num /= 10;
			}
			value = next;
		}
		for (int i = 0; i < 3; i++)
		{
			float num4 = elapsed[i];
			elapsed[i] = Mathf.Min(0.25f, elapsed[i] + Mathf.Max(0f, dt));
			if (num4 < 0.125f && elapsed[i] >= 0.125f)
			{
				shown[i] = target[i];
				digits[i].text = Labels[shown[i]];
			}
			if (elapsed[i] >= 0.25f && shown[i] != target[i])
			{
				elapsed[i] = 0f;
			}
			float num5 = elapsed[i] / 0.25f;
			float num6 = direction[i] * ((num5 < 0.5f) ? (180f * num5) : (-180f * (1f - num5)));
			pivots[i].localRotation = Quaternion.AngleAxis(num6, axis[i]) * restRotation[i];
			pivots[i].localPosition = restPosition[i] + outward[i] * (0.016f * Mathf.Sin(Mathf.Abs(num6) * ((float)Math.PI / 180f)));
		}
	}
}

public sealed class PairingMenu : MonoBehaviour
{
	private static PumpItem owner;

	private static bool open;

	private static bool previousCursor;

	private static bool previousMouse;

	private static bool previousControl;

	private static bool previousVisible;

	private static CursorLockMode previousLock;

	private Vector2 scroll;

	internal static bool IsOpen => open;

	internal static void Open(PumpItem box)
	{
		if ((bool)box && box.CanOperate && !IsOpen && !GameState.inCursorMenu)
		{
			owner = box;
			open = true;
			previousCursor = GameState.inCursorMenu;
			previousMouse = MouseLook.MouseLookIsEnabled();
			previousControl = (bool)Refs.charController && Refs.charController.enabled;
			previousVisible = Cursor.visible;
			previousLock = Cursor.lockState;
			GameState.inCursorMenu = true;
			MouseLook.ToggleMouseLook(newState: false);
			Refs.SetPlayerControl(state: false);
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}
	}

	internal static void Close()
	{
		if (open)
		{
			open = false;
			owner = null;
			GameState.inCursorMenu = previousCursor;
			MouseLook.ToggleMouseLook(previousMouse);
			if ((bool)Refs.charController)
			{
				Refs.SetPlayerControl(previousControl);
			}
			Cursor.lockState = previousLock;
			Cursor.visible = previousVisible;
		}
	}

	private void OnDisable()
	{
		Close();
	}

	private void Update()
	{
		if (open)
		{
			if (!owner)
			{
				Close();
			}
			else if (!owner.CanOperate || !GameState.playing || GameState.currentlyLoading || GameState.recovering || !Camera.main || Vector3.Distance(Camera.main.transform.position, owner.transform.position) > 3f || Input.GetKeyDown(KeyCode.Escape))
			{
				Close();
			}
		}
	}

	private void OnGUI()
	{
		if ((bool)owner && !owner.CanOperate)
		{
			Close();
		}
		else
		{
			if (!owner)
			{
				return;
			}
			float num = Mathf.Min(500f, (float)Screen.width - 30f);
			float num2 = Mathf.Min(380f, (float)Screen.height - 30f);
			GUILayout.BeginArea(new Rect(((float)Screen.width - num) / 2f, ((float)Screen.height - num2) / 2f, num, num2), GUI.skin.box);
			GUILayout.Label("PAIR THIS CONTROLLER");
			scroll = GUILayout.BeginScrollView(scroll);
			int num3 = 0;
			foreach (PumpItem item in PumpWorld.Live)
			{
				if (!item || !item.IsPump || !item.Installed || item.Item.currentActualBoat != owner.Item.currentActualBoat)
				{
					continue;
				}
				PumpRecord pumpRecord = PumpWorld.State.Peer(item.Record);
				if (pumpRecord != null && pumpRecord.Id != owner.Record.Id && pumpRecord.Mounted)
				{
					continue;
				}
				num3++;
				Vector3 vector = owner.Item.currentActualBoat.InverseTransformPoint(item.transform.position);
				string text = ((vector.x < -0.1f) ? "port" : ((vector.x > 0.1f) ? "starboard" : "centre"));
				if (GUILayout.Button(PumpState.Label(item.Record) + " - " + text + " (fore/aft " + vector.z.ToString("F1") + " m)", GUILayout.Height(38f)))
				{
					if (PumpWorld.State.Peer(owner.Record) == item.Record || PumpWorld.State.Pair(owner.Record.Id, item.Record.Id, owner.Record.BoatId))
					{
						Close();
					}
					else if ((bool)NotificationUi.instance)
					{
						NotificationUi.instance.ShowNotification("Propeller is not available for pairing.");
					}
					break;
				}
			}
			if (num3 == 0)
			{
				GUILayout.Label("No available propeller.");
			}
			GUILayout.EndScrollView();
			if (GUILayout.Button("Cancel", GUILayout.Height(32f)))
			{
				Close();
			}
			GUILayout.EndArea();
		}
	}
}

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

[HarmonyPatch(typeof(ShopItemSpawner), "SpawnItem")]
internal static class AcademyGroundSpawn
{
	private static bool Prefix(ShopItemSpawner __instance)
	{
		AcademyGroundPlacement component = __instance.GetComponent<AcademyGroundPlacement>();
		if ((bool)component)
		{
			return component.TryPlace(__instance);
		}
		return true;
	}
}
