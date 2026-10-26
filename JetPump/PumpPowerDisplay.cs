using System;
using UnityEngine;

namespace JetPump;

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
