using System.Collections.Generic;
using UnityEngine;

namespace JetPump;

internal sealed class PumpMarkers : MonoBehaviour
{
	private struct Marker
	{
		internal Vector3 Point;

		internal Vector3 Direction;

		internal string Label;
	}

	private readonly List<Marker> markers = new List<Marker>();

	private Camera source;

	private Transform boatRoot;

	private Rigidbody body;

	private Texture2D dot;

	private GUIStyle labelStyle;

	private static readonly Color MassColor = new Color(1f, 0.84f, 0.12f);

	private static readonly Color ForceColor = new Color(0.15f, 0.95f, 1f);

	private void LateUpdate()
	{
		Refresh();
	}

	internal void Refresh()
	{
		markers.Clear();
		if (!Plugin.Ready || !Plugin.ShowMarkers || !GameState.playing || PumpWorld.Loading || GameState.currentlyLoading || GameState.recovering)
		{
			boatRoot = null;
			body = null;
			return;
		}
		Transform transform = (GameState.currentBoat ? GameState.currentBoat.parent : GameState.lastBoat);
		if (transform != boatRoot || !body)
		{
			boatRoot = transform;
			body = (transform ? transform.GetComponent<Rigidbody>() : null);
		}
		if (!body)
		{
			return;
		}
		SaveableObject component = boatRoot.GetComponent<SaveableObject>();
		if (!component)
		{
			return;
		}
		Transform transform2 = GameState.currentBoat;
		if (!transform2 || transform2.parent != boatRoot)
		{
			BoatRefs component2 = boatRoot.GetComponent<BoatRefs>();
			transform2 = (component2 ? component2.boatModel : null);
		}
		foreach (PumpRecord value in PumpWorld.State.Items.Values)
		{
			if (PumpState.IsPump(value.Kind) && value.Mounted && value.BoatId == component.sceneIndex)
			{
				PumpItem pumpItem = PumpWorld.Find(value.Id);
				if (((bool)pumpItem && pumpItem.TryThrustGeometry(out var point, out var direction)) || TrySavedGeometry(value, transform2, out point, out direction))
				{
					markers.Add(new Marker
					{
						Point = point,
						Direction = direction,
						Label = PumpState.Label(value)
					});
				}
			}
		}
	}

	internal static bool TrySavedGeometry(PumpRecord record, Transform model, out Vector3 point, out Vector3 direction)
	{
		point = (direction = Vector3.zero);
		if (!model || !record.HasPose)
		{
			return false;
		}
		GameObject gameObject = ((record.Kind == 606) ? PumpAssets.HugePump : PumpAssets.Pump);
		if (!gameObject)
		{
			return false;
		}
		Transform transform = null;
		Transform[] componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform2 in componentsInChildren)
		{
			if (transform2.name == "ThrustPoint")
			{
				transform = transform2;
				break;
			}
		}
		if (!transform)
		{
			return false;
		}
		Quaternion normalized = new Quaternion(record.Qx, record.Qy, record.Qz, record.Qw).normalized;
		Vector3 position = new Vector3(record.X, record.Y, record.Z) + normalized * gameObject.transform.InverseTransformPoint(transform.position);
		point = model.TransformPoint(position) + model.up * Plugin.VerticalOffset;
		direction = -model.TransformDirection(normalized * gameObject.transform.InverseTransformDirection(transform.forward)).normalized;
		return true;
	}

	private void OnGUI()
	{
		if (Event.current.type != EventType.Repaint || !Plugin.Ready || !Plugin.ShowMarkers || !body || !GameState.playing || PumpWorld.Loading || GameState.currentlyLoading || GameState.recovering)
		{
			return;
		}
		if (!source || !source.isActiveAndEnabled)
		{
			source = Camera.main;
		}
		if (!source)
		{
			return;
		}
		if (!dot)
		{
			MakeDot();
		}
		if (labelStyle == null)
		{
			labelStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 28,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleLeft
			};
		}
		int depth = GUI.depth;
		Color color = GUI.color;
		GUI.depth = 1000;
		DrawMarker(body.worldCenterOfMass, Vector3.zero, "Center of mass", MassColor);
		foreach (Marker marker in markers)
		{
			DrawMarker(marker.Point, marker.Direction, marker.Label, ForceColor);
		}
		GUI.color = color;
		GUI.depth = depth;
	}

	private bool Project(Vector3 world, out Vector2 screen)
	{
		Vector3 vector = source.WorldToScreenPoint(world);
		screen = new Vector2(vector.x, (float)Screen.height - vector.y);
		if (vector.z > source.nearClipPlane && vector.x >= 0f && vector.x <= (float)Screen.width && vector.y >= 0f)
		{
			return vector.y <= (float)Screen.height;
		}
		return false;
	}

	private void DrawMarker(Vector3 point, Vector3 direction, string label, Color color)
	{
		if (!Project(point, out var screen))
		{
			return;
		}
		if (direction.sqrMagnitude > 0.01f && Project(point + direction, out var screen2))
		{
			Vector2 vector = screen2 - screen;
			if (vector.sqrMagnitude > 4f)
			{
				Vector2 vector2 = screen + vector.normalized * 76f;
				Line(screen, vector2, color);
				Vector2 vector3 = -vector.normalized * 16f;
				Vector2 vector4 = new Vector2(0f - vector3.y, vector3.x) * 0.5f;
				Line(vector2, vector2 + vector3 + vector4, color);
				Line(vector2, vector2 + vector3 - vector4, color);
			}
		}
		GUI.color = Color.black;
		GUI.DrawTexture(new Rect(screen.x - 18f, screen.y - 18f, 36f, 36f), dot);
		GUI.color = color;
		GUI.DrawTexture(new Rect(screen.x - 12f, screen.y - 12f, 24f, 24f), dot);
		GUI.color = Color.white;
		labelStyle.normal.textColor = Color.black;
		GUI.Label(new Rect(screen.x + 24f, screen.y - 20f, 300f, 48f), label, labelStyle);
		labelStyle.normal.textColor = color;
		GUI.Label(new Rect(screen.x + 22f, screen.y - 22f, 300f, 48f), label, labelStyle);
	}

	private static void Line(Vector2 from, Vector2 to, Color color)
	{
		Matrix4x4 matrix = GUI.matrix;
		GUI.color = color;
		GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y - from.y, to.x - from.x) * 57.29578f, from);
		GUI.DrawTexture(new Rect(from.x, from.y - 2f, Vector2.Distance(from, to), 4f), Texture2D.whiteTexture);
		GUI.matrix = matrix;
	}

	private void MakeDot()
	{
		dot = new Texture2D(32, 32, TextureFormat.RGBA32, mipChain: false)
		{
			name = "PropellerMarker",
			hideFlags = HideFlags.HideAndDontSave
		};
		for (int i = 0; i < 32; i++)
		{
			for (int j = 0; j < 32; j++)
			{
				float num = Vector2.Distance(new Vector2(j, i), new Vector2(15.5f, 15.5f));
				dot.SetPixel(j, i, new Color(1f, 1f, 1f, Mathf.Clamp01(15.5f - num)));
			}
		}
		dot.Apply(updateMipmaps: false, makeNoLongerReadable: true);
	}

	private void OnDisable()
	{
		markers.Clear();
		source = null;
		boatRoot = null;
		body = null;
		if ((bool)dot)
		{
			Object.Destroy(dot);
		}
		dot = null;
	}
}
