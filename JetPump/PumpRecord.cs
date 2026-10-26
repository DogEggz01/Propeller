using System;

namespace JetPump;

public sealed class PumpRecord
{
	public int Id;

	public int Kind;

	public int PeerId;

	public int BoatId;

	public bool Mounted;

	public int Power = 5;

	public float Throttle;

	public bool Enabled;

	public bool RequireImmersion;

	public bool RequirePairChoice;

	public bool SafetyEnabled;

	public int Number;

	public string Wheel = "";

	public int Group;

	public bool HasPose;

	public float X;

	public float Y;

	public float Z;

	public float Qx;

	public float Qy;

	public float Qz;

	public float Qw = 1f;

	public float AppliedPower
	{
		get
		{
			if (!Enabled)
			{
				return 0f;
			}
			return (float)Power * Throttle;
		}
	}

	public void Normalize()
	{
		Power = ClampPower(Power);
		Throttle = (Finite(Throttle) ? Math.Max(0f, Math.Min(1f, Throttle)) : 0f);
		if (PeerId < 0)
		{
			PeerId = 0;
		}
		if (BoatId <= 0)
		{
			BoatId = 0;
			Mounted = false;
		}
		Wheel = Wheel ?? "";
		if (Wheel.Length > 0)
		{
			Wheel = "*";
		}
		Group = ((Kind == 605) ? Math.Max(0, Math.Min(3, Group)) : 0);
		if (!Mounted)
		{
			Enabled = false;
			Wheel = "";
		}
		if (!Mounted || !Finite(X) || !Finite(Y) || !Finite(Z) || !Finite(Qx) || !Finite(Qy) || !Finite(Qz) || !Finite(Qw) || Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw < 0.01f)
		{
			HasPose = false;
		}
	}

	internal static bool Finite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	public static int ClampPower(int power)
	{
		return Math.Max(5, Math.Min(100, (int)Math.Round((double)power / 5.0) * 5));
	}

	public void AdjustThrottle(bool forward, bool decrease, float seconds)
	{
		if (forward != decrease && !(seconds <= 0f) && Finite(seconds))
		{
			Throttle = Math.Max(0f, Math.Min(1f, Throttle + (forward ? 1f : (-1f)) * seconds * 0.2f));
		}
	}

	public void StopForRemoval()
	{
		Enabled = false;
		Mounted = false;
		Wheel = "";
		HasPose = false;
	}
}
