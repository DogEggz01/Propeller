using System;
using System.Collections.Generic;
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
