using System;
using System.Collections.Generic;
using UnityEngine;

namespace JetPump;

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
