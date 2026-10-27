using BepInEx.Configuration;
using Crest;
using HarmonyLib;
using Object = UnityEngine.Object;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace JetPump;

internal sealed class PumpDrive
{
	private float output;

	internal void Stop()
	{
		output = 0f;
	}

	internal void Apply(Rigidbody body, Vector3 direction, Vector3 point, float command, float dt, PumpTrim trim, float ratedThrust)
	{
		if (command <= 0f)
		{
			Stop();
			return;
		}
		output = Mathf.MoveTowards(output, command, 100f * dt / 1.5f);
		Vector3 vector = direction * (ratedThrust * output / 100f);
		body.AddForceAtPosition(vector, point, ForceMode.Force);
		trim.AddThrust(vector / body.mass, point);
	}
}

internal sealed class PumpWater
{
	private static readonly FieldInfo ResultSegments = AccessTools.Field(typeof(QueryBase), "_resultSegments");

	private const int ProbeCount = 9;

	private const int History = 32;

	private readonly PumpItem owner;

	private readonly Vector3[] localPoints = new Vector3[9];

	private readonly Vector3[] queries = new Vector3[9];

	private readonly Vector3[] displacement = new Vector3[9];

	private readonly Vector3[] normals = new Vector3[9];

	private readonly Vector3[] sampledPoints = new Vector3[9];

	private readonly Vector3[] historyPoints = new Vector3[288];

	private readonly int[] historyFrames = new int[32];

	private readonly float[] historyTimes = new float[32];

	private readonly float[] historyOffsets = new float[32];

	private readonly float[] heights = new float[9];

	private readonly int queryId;

	private ICollProvider provider;

	private float sampledAt;

	private float sampledOffset;

	private bool hasSample;

	internal bool Submerged { get; private set; }

	internal bool SampleValid { get; private set; }

	internal PumpWater(PumpItem item, Transform mouth)
	{
		owner = item;
		queryId = item.GetInstanceID();
		localPoints[0] = item.transform.InverseTransformPoint(mouth.position);
		Transform transform = Array.Find(item.GetComponentsInChildren<Transform>(includeInactive: true), (Transform t) => t.name == "WaterRim");
		float num = Vector3.Distance(mouth.position, transform.position);
		for (int num2 = 1; num2 < 9; num2++)
		{
			float f = (float)(num2 - 1) * (float)Math.PI / 4f;
			localPoints[num2] = item.transform.InverseTransformPoint(mouth.position + num * (mouth.right * Mathf.Cos(f) + mouth.up * Mathf.Sin(f)));
		}
		for (int num3 = 0; num3 < 32; num3++)
		{
			historyFrames[num3] = -1;
		}
		Reset();
	}

	private static bool Finite(Vector3 v)
	{
		if (PumpRecord.Finite(v.x) && PumpRecord.Finite(v.y))
		{
			return PumpRecord.Finite(v.z);
		}
		return false;
	}

	internal Vector3 WorldPoint(int index)
	{
		ShipItem item = owner.Item;
		return PumpItem.ConvertPoint(item.itemRigidbodyC.transform.TransformPoint(localPoints[index]), item.currentWalkCol, item.currentActualBoat) + owner.SpatialOffset;
	}

	internal void Sample()
	{
		OceanRenderer instance = OceanRenderer.Instance;
		ICollProvider collProvider = (instance ? instance.CollisionProvider : null);
		if (provider != collProvider)
		{
			bool flag = (Submerged = false);
			hasSample = flag;
			provider = collProvider;
		}
		if (provider == null)
		{
			bool flag3 = (Submerged = false);
			bool flag = (SampleValid = flag3);
			hasSample = flag;
			return;
		}
		int num = Time.frameCount % 32;
		historyFrames[num] = Time.frameCount;
		historyTimes[num] = Time.unscaledTime;
		historyOffsets[num] = Plugin.VerticalOffset;
		for (int i = 0; i < 9; i++)
		{
			historyPoints[num * 9 + i] = (queries[i] = WorldPoint(i));
		}
		int queryStatus = provider.Query(queryId, 0.2f, queries, displacement, normals, null);
		hasSample = provider.RetrieveSucceeded(queryStatus);
		int num2 = num;
		if (provider is QueryBase)
		{
			Dictionary<int, Vector3Int> dictionary = ((ResultSegments == null) ? null : (ResultSegments.GetValue(provider) as Dictionary<int, Vector3Int>));
			if (dictionary == null || !dictionary.TryGetValue(queryId, out var value) || value.z < 0)
			{
				hasSample = false;
			}
			else
			{
				num2 = value.z % 32;
				if (historyFrames[num2] != value.z)
				{
					hasSample = false;
				}
			}
		}
		sampledAt = historyTimes[num2];
		sampledOffset = historyOffsets[num2];
		for (int j = 0; j < 9; j++)
		{
			sampledPoints[j] = historyPoints[num2 * 9 + j];
			heights[j] = displacement[j].y + instance.SeaLevel;
			hasSample &= PumpRecord.Finite(heights[j]) && Finite(normals[j]) && normals[j].y > 0.1f;
		}
		Evaluate();
	}

	internal void Evaluate()
	{
		OceanRenderer instance = OceanRenderer.Instance;
		bool flag = hasSample && sampledOffset == Plugin.VerticalOffset && (bool)instance && instance.CollisionProvider == provider && Time.unscaledTime - sampledAt <= 0.25f;
		float num = float.PositiveInfinity;
		for (int i = 0; i < 9; i++)
		{
			Vector3 vector = WorldPoint(i);
			Vector3 vector2 = vector - sampledPoints[i];
			flag &= vector2.x * vector2.x + vector2.z * vector2.z <= 1f;
			float num2 = heights[i] - (normals[i].x * vector2.x + normals[i].z * vector2.z) / Mathf.Max(0.1f, normals[i].y);
			num = Mathf.Min(num, num2 - vector.y);
		}
		SampleValid = flag;
		Submerged = flag && num > (Submerged ? 0f : 0.02f);
	}

	internal float Power(PumpRecord record)
	{
		if (record.RequireImmersion && !Submerged)
		{
			return 0f;
		}
		return record.AppliedPower;
	}

	internal void Reset()
	{
		bool flag = (SampleValid = false);
		bool flag3 = (Submerged = flag);
		hasSample = flag3;
	}
}

[DefaultExecutionOrder(1000)]
internal sealed class PumpTrim : MonoBehaviour
{
	private static readonly List<PumpTrim> live = new List<PumpTrim>();

	private Rigidbody body;

	private float forwardAcceleration;

	private float noseDownTorque;

	private float filteredPitch;

	private float filteredRate;

	private float blend;

	private bool initialized;

	private Vector3 forward;

	private Vector3 right;

	private void Awake()
	{
		body = GetComponent<Rigidbody>();
		live.Add(this);
	}

	private void OnDestroy()
	{
		live.Remove(this);
	}

	internal static void ResetAll()
	{
		foreach (PumpTrim item in live)
		{
			if ((bool)item)
			{
				item.enabled = false;
				UnityEngine.Object.Destroy(item);
			}
		}
		live.Clear();
	}

	internal void AddThrust(Vector3 acceleration, Vector3 point)
	{
		if (!body)
		{
			body = GetComponent<Rigidbody>();
		}
		right = Vector3.ProjectOnPlane(body.transform.right, Vector3.up).normalized;
		forward = Vector3.Cross(right, Vector3.up);
		float num = Vector3.Dot(acceleration, body.transform.forward);
		forwardAcceleration += num;
		noseDownTorque += Vector3.Dot(Vector3.Cross(point - body.worldCenterOfMass, body.transform.forward * num * body.mass), right);
	}

	private void FixedUpdate()
	{
		float num = forwardAcceleration;
		float b = noseDownTorque;
		forwardAcceleration = (noseDownTorque = 0f);
		if (!PumpWorld.SimulationAllowed || !body || body.isKinematic || num <= 0.0001f || (Plugin.TrimAngle <= 90f && Vector3.Dot(body.transform.up, Vector3.up) < -0.01f) || Vector3.ProjectOnPlane(body.transform.right, Vector3.up).sqrMagnitude < 0.04f)
		{
			initialized = false;
			blend = 0f;
			return;
		}
		float fixedDeltaTime = Time.fixedDeltaTime;
		float num2 = Mathf.Atan2(Vector3.Dot(body.transform.forward, Vector3.up), Vector3.Dot(body.transform.forward, forward));
		if (Plugin.TrimAngle > 90f && num2 < -(float)Math.PI / 2f)
		{
			num2 += (float)Math.PI * 2f;
		}
		if (!initialized)
		{
			filteredPitch = num2;
			filteredRate = 0f;
			initialized = true;
		}
		float num3 = 1f - Mathf.Exp((0f - fixedDeltaTime) / 2f);
		float num4 = filteredPitch;
		filteredPitch += Mathf.DeltaAngle(filteredPitch * 57.29578f, num2 * 57.29578f) * ((float)Math.PI / 180f) * num3;
		filteredRate = Mathf.Lerp(filteredRate, (filteredPitch - num4) / fixedDeltaTime, num3);
		blend = Mathf.MoveTowards(blend, Mathf.Clamp01(num / 5f), fixedDeltaTime / 2f);
		float magnitude = Vector3.ProjectOnPlane(body.velocity, right).magnitude;
		float num5 = Mathf.Clamp((Plugin.TrimAngle * ((float)Math.PI / 180f) * Mathf.SmoothStep(0f, 1f, magnitude / 3f) - filteredPitch) * 0.8f - filteredRate * 0.9f, -0.1f, 0.1f) * blend;
		Vector3 vector = TorqueForAcceleration(body, -right * num5);
		body.AddTorque(-right * Mathf.Max(0f, b) + vector, ForceMode.Force);
	}

	internal static Vector3 TorqueForAcceleration(Rigidbody body, Vector3 acceleration)
	{
		Quaternion quaternion = body.rotation * body.inertiaTensorRotation;
		return quaternion * Vector3.Scale(body.inertiaTensor, Quaternion.Inverse(quaternion) * acceleration);
	}
}

internal static class PumpInput
{
	private static readonly FieldInfo StickyWheelField = AccessTools.Field(typeof(GoPointer), "stickyClickedButton");

	private static readonly List<PumpRecord> records = new List<PumpRecord>();

	internal static bool HandleWheelInput(GoPointerButton helm, bool increase, bool decrease, float delta)
	{
		if (!PumpWorld.InputAllowed || !helm || (!helm.IsStickyClicked() && !helm.IsCliked()))
		{
			return false;
		}
		records.Clear();
		foreach (PumpItem item in PumpWorld.Linked(helm))
		{
			records.Add(item.Record);
		}
		if (records.Count == 0)
		{
			return false;
		}
		PumpWorld.State.AdjustThrottle(records, increase, decrease, delta);
		return true;
	}

	internal static void PatchCheatspeed(Harmony harmony)
	{
		Type type = PumpWorld.OptionalType("NANDCommand", "NANDCommand.CheatsPatch");
		if (!(type == null))
		{
			MethodInfo methodInfo = AccessTools.Method(type, "Postfix", new Type[1] { typeof(GoPointer) });
			if (methodInfo == null)
			{
				throw new InvalidOperationException("Unrecognized NANDCommand cheatspeed patch; refusing duplicate propulsion.");
			}
			harmony.Patch(methodInfo, new HarmonyMethod(typeof(PumpInput), "AllowCheatspeed"));
		}
	}

	private static bool AllowCheatspeed(GoPointer __0)
	{
		if (!Plugin.Ready || !__0)
		{
			return true;
		}
		GPButtonSteeringWheel gPButtonSteeringWheel = StickyWheelField.GetValue(__0) as GPButtonSteeringWheel;
		if ((bool)gPButtonSteeringWheel)
		{
			return PumpWorld.Linked(gPButtonSteeringWheel).Count == 0;
		}
		return true;
	}
}

[HarmonyPatch(typeof(GPButtonSteeringWheel), "ExtraLateUpdate")]
internal static class PumpWheelThrottle
{
	private static void Postfix(GPButtonSteeringWheel __instance)
	{
		if (PumpWorld.InputAllowed)
		{
			PumpInput.HandleWheelInput(__instance, GameInput.GetKey(InputName.MoveUp), GameInput.GetKey(InputName.MoveDown), Time.deltaTime);
		}
	}
}

[HarmonyPatch]
internal static class PumpSafety
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(PlayerEmbarkerNew), "PlayerDisembark");
		yield return AccessTools.Method(typeof(PlayerEmbarkerNew), "PlayerEmbark");
		yield return AccessTools.Method(typeof(PlayerEmbarkDisembarkTrigger), "ExitBoat");
		yield return AccessTools.Method(typeof(PlayerEmbarkDisembarkTrigger), "EnterBoat");
		yield return AccessTools.Method(typeof(PlayerEmbarkTriggerNew), "ExitBoat");
		yield return AccessTools.Method(typeof(PlayerEmbarkTriggerNew), "EnterBoat");
	}

	private static void Prefix(out Transform __state)
	{
		__state = GameState.currentBoat;
	}

	private static void Postfix(Transform __state)
	{
		BoatChanged(__state, GameState.currentBoat);
	}

	internal static void BoatChanged(Transform before, Transform after)
	{
		if (!Plugin.Ready || !before || before == after || !GameState.playing || PumpWorld.Loading || GameState.currentlyLoading || GameState.loadingBoatLocalItems || GameState.justStarted || GameState.recovering || (bool)GameState.currentShipyard || GameState.sleeping || (bool)GameState.inBed || GameState.justWokeUp)
		{
			return;
		}
		int num = PumpWorld.BoatId(before);
		if ((bool)after && num == PumpWorld.BoatId(after))
		{
			return;
		}
		PumpWorld.State.SafetyShutdown(num);
		foreach (PumpItem item in PumpWorld.Live)
		{
			if ((bool)item && item.IsPump && item.Record != null && item.Record.BoatId == num && !item.Record.Enabled)
			{
				item.StopDrive();
			}
		}
	}
}

internal static class PumpTiller
{
	private static FieldInfo input;

	private static FieldInfo hinge;

	private static FieldInfo locked;

	private static FieldInfo sticky;

	private static ConfigEntry<bool> inverted;

	internal static void Patch(Harmony harmony)
	{
		Type type = PumpWorld.OptionalType("Dinghies", "Dinghies.TillerRudder");
		if (type == null)
		{
			return;
		}
		try
		{
			MethodInfo methodInfo = AccessTools.Method(type, "ExtraLateUpdate");
			FieldInfo fieldInfo = AccessTools.Field(type, "rudder");
			input = AccessTools.Field(type, "input");
			hinge = AccessTools.Field(type, "hingeJoint");
			locked = AccessTools.Field(type, "locked");
			sticky = AccessTools.Field(typeof(GoPointerButton), "stickyClickedBy");
			if (!typeof(GoPointerButton).IsAssignableFrom(type) || methodInfo == null || fieldInfo?.FieldType != typeof(Rudder) || input?.FieldType != typeof(float) || hinge?.FieldType != typeof(HingeJoint) || locked?.FieldType != typeof(bool) || sticky == null)
			{
				throw new MissingMemberException("Dinghies.TillerRudder members changed");
			}
			Type type2 = PumpWorld.OptionalType("Dinghies", "Dinghies.DinghiesMain");
			inverted = ((type2 == null) ? null : (AccessTools.Field(type2, "invertedTillerConfig")?.GetValue(null) as ConfigEntry<bool>));
			harmony.Patch(methodInfo, new HarmonyMethod(typeof(PumpTiller), "Prefix"), new HarmonyMethod(typeof(PumpTiller), "Postfix"));
			PumpWorld.TillerType = type;
			PumpWorld.TillerRudder = fieldInfo;
		}
		catch (Exception ex)
		{
			PumpWorld.TillerType = null;
			Plugin.Log.LogWarning("Dinghies tiller not supported by this Propeller version; wheel control works without it. " + ex.Message);
		}
	}

	private static void Prefix(GoPointerButton __instance, out float __state)
	{
		__state = (float)input.GetValue(__instance);
	}

	private static void Postfix(GoPointerButton __instance, float __state)
	{
		if (!Plugin.Ready || !PumpInput.HandleWheelInput(__instance, GameInput.GetKey(InputName.MoveUp), GameInput.GetKey(InputName.MoveDown), Time.deltaTime))
		{
			return;
		}
		GoPointer goPointer = sticky.GetValue(__instance) as GoPointer;
		HingeJoint hingeJoint = hinge.GetValue(__instance) as HingeJoint;
		if ((bool)goPointer && (bool)goPointer.movement && (bool)hingeJoint && !(bool)locked.GetValue(__instance))
		{
			Vector3 keyboardDelta = goPointer.movement.GetKeyboardDelta();
			if (keyboardDelta.y != 0f)
			{
				float max = hingeJoint.limits.max;
				float num = Mathf.Clamp(__state + keyboardDelta.x * 0.025f * ((inverted != null && inverted.Value) ? (-1f) : 1f), 0f - max, max);
				input.SetValue(__instance, num);
				JointSpring spring = hingeJoint.spring;
				spring.targetPosition = num;
				hingeJoint.spring = spring;
			}
		}
	}
}
