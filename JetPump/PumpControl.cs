using System;
using UnityEngine;

namespace JetPump;

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
