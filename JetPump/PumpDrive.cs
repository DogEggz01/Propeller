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
