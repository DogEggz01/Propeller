using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace JetPump;

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
