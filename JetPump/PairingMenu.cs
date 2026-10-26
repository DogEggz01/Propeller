using UnityEngine;

namespace JetPump;

public sealed class PairingMenu : MonoBehaviour
{
	private static PumpItem owner;

	private static bool open;

	private static bool previousCursor;

	private static bool previousMouse;

	private static bool previousControl;

	private static bool previousVisible;

	private static CursorLockMode previousLock;

	private Vector2 scroll;

	internal static bool IsOpen => open;

	internal static void Open(PumpItem box)
	{
		if ((bool)box && box.CanOperate && !IsOpen && !GameState.inCursorMenu)
		{
			owner = box;
			open = true;
			previousCursor = GameState.inCursorMenu;
			previousMouse = MouseLook.MouseLookIsEnabled();
			previousControl = (bool)Refs.charController && Refs.charController.enabled;
			previousVisible = Cursor.visible;
			previousLock = Cursor.lockState;
			GameState.inCursorMenu = true;
			MouseLook.ToggleMouseLook(newState: false);
			Refs.SetPlayerControl(state: false);
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}
	}

	internal static void Close()
	{
		if (open)
		{
			open = false;
			owner = null;
			GameState.inCursorMenu = previousCursor;
			MouseLook.ToggleMouseLook(previousMouse);
			if ((bool)Refs.charController)
			{
				Refs.SetPlayerControl(previousControl);
			}
			Cursor.lockState = previousLock;
			Cursor.visible = previousVisible;
		}
	}

	private void OnDisable()
	{
		Close();
	}

	private void Update()
	{
		if (open)
		{
			if (!owner)
			{
				Close();
			}
			else if (!owner.CanOperate || !GameState.playing || GameState.currentlyLoading || GameState.recovering || !Camera.main || Vector3.Distance(Camera.main.transform.position, owner.transform.position) > 3f || Input.GetKeyDown(KeyCode.Escape))
			{
				Close();
			}
		}
	}

	private void OnGUI()
	{
		if ((bool)owner && !owner.CanOperate)
		{
			Close();
		}
		else
		{
			if (!owner)
			{
				return;
			}
			float num = Mathf.Min(500f, (float)Screen.width - 30f);
			float num2 = Mathf.Min(380f, (float)Screen.height - 30f);
			GUILayout.BeginArea(new Rect(((float)Screen.width - num) / 2f, ((float)Screen.height - num2) / 2f, num, num2), GUI.skin.box);
			GUILayout.Label("PAIR THIS CONTROLLER");
			scroll = GUILayout.BeginScrollView(scroll);
			int num3 = 0;
			foreach (PumpItem item in PumpWorld.Live)
			{
				if (!item || !item.IsPump || !item.Installed || item.Item.currentActualBoat != owner.Item.currentActualBoat)
				{
					continue;
				}
				PumpRecord pumpRecord = PumpWorld.State.Peer(item.Record);
				if (pumpRecord != null && pumpRecord.Id != owner.Record.Id && pumpRecord.Mounted)
				{
					continue;
				}
				num3++;
				Vector3 vector = owner.Item.currentActualBoat.InverseTransformPoint(item.transform.position);
				string text = ((vector.x < -0.1f) ? "port" : ((vector.x > 0.1f) ? "starboard" : "centre"));
				if (GUILayout.Button(PumpState.Label(item.Record) + " - " + text + " (fore/aft " + vector.z.ToString("F1") + " m)", GUILayout.Height(38f)))
				{
					if (PumpWorld.State.Peer(owner.Record) == item.Record || PumpWorld.State.Pair(owner.Record.Id, item.Record.Id, owner.Record.BoatId))
					{
						Close();
					}
					else if ((bool)NotificationUi.instance)
					{
						NotificationUi.instance.ShowNotification("Propeller is not available for pairing.");
					}
					break;
				}
			}
			if (num3 == 0)
			{
				GUILayout.Label("No available propeller.");
			}
			GUILayout.EndScrollView();
			if (GUILayout.Button("Cancel", GUILayout.Height(32f)))
			{
				Close();
			}
			GUILayout.EndArea();
		}
	}
}
