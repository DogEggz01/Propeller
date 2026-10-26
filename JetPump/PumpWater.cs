using System;
using System.Collections.Generic;
using System.Reflection;
using Crest;
using HarmonyLib;
using UnityEngine;

namespace JetPump;

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
