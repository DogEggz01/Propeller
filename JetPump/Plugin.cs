using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace JetPump;

[BepInPlugin("DogEggz.JetPump", "Propeller", "1.2.2")]
[BepInDependency("com.nandbrew.nandcommand", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("pr0skynesis.dinghies", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
	public const string Guid = "DogEggz.JetPump";

	internal static ManualLogSource Log;

	internal static bool Ready;

	private static ConfigEntry<float> waterOffset;

	private static ConfigEntry<float> trimAngle;

	private static ConfigEntry<bool> showMarkers;

	private Harmony harmony;

	internal static bool ShowMarkers
	{
		get
		{
			if (showMarkers != null)
			{
				return showMarkers.Value;
			}
			return false;
		}
	}

	internal static float VerticalOffset => Mathf.Round(Finite(waterOffset, 0f, -2f, 2f) * 100f) / 100f;

	internal static float TrimAngle => Finite(trimAngle, 80f, 0f, 180f);

	private static float Finite(ConfigEntry<float> entry, float fallback, float min, float max)
	{
		float value = entry?.Value ?? fallback;
		if (!PumpRecord.Finite(value))
		{
			return fallback;
		}
		return Mathf.Clamp(value, min, max);
	}

	internal static void BindSettings(ConfigFile config)
	{
		if (waterOffset != null)
		{
			waterOffset.SettingChanged -= SnapOffset;
		}
		waterOffset = config.Bind("Propulsion", "Vertical offset (metres)", 0f, new ConfigDescription("Moves thrust and all water-detection points together along the boat's vertical axis. Positive raises, negative lowers. Steps of 0.01 m; model position stays unchanged.", new AcceptableValueRange<float>(-2f, 2f), new ConfigurationManagerAttributes
		{
			ShowRangeAsPercent = false,
			CustomDrawer = DrawOffset
		}));
		waterOffset.SettingChanged += SnapOffset;
		SnapOffset(waterOffset, EventArgs.Empty);
		showMarkers = config.Bind("Display", "Show propulsion markers", defaultValue: false, "Show the current/last boat's live physics centre of mass and installed propellers' thrust points and directions, through the hull, inside or outside the shipyard.");
		trimAngle = config.Bind("Trim", "Bow-up angle (degrees)", 80f, new ConfigDescription("Average bow-up target during forward propulsion, shared by all propellers on a boat. Zero targets level with pitch compensation, 90 vertical and 180 inverted. Values above 90 permit intentional inversion.", new AcceptableValueRange<float>(0f, 180f), new ConfigurationManagerAttributes
		{
			ShowRangeAsPercent = false
		}));
	}

	private static void SnapOffset(object sender, EventArgs args)
	{
		ConfigEntry<float> configEntry = (ConfigEntry<float>)sender;
		float num = Mathf.Round(Finite(configEntry, 0f, -2f, 2f) * 100f) / 100f;
		if (configEntry.Value != num)
		{
			configEntry.Value = num;
		}
	}

	private static void DrawOffset(ConfigEntryBase entry)
	{
		ConfigEntry<float> configEntry = (ConfigEntry<float>)entry;
		GUILayout.BeginHorizontal();
		float num = GUILayout.HorizontalSlider(configEntry.Value, -2f, 2f, GUILayout.MinWidth(130f));
		if (GUILayout.Button("−", GUILayout.Width(25f)))
		{
			num -= 0.01f;
		}
		if (GUILayout.Button("+", GUILayout.Width(25f)))
		{
			num += 0.01f;
		}
		num = Mathf.Clamp(Mathf.Round(num * 100f) / 100f, -2f, 2f);
		GUILayout.Label(num.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " m", GUILayout.Width(70f));
		if (num != configEntry.Value)
		{
			configEntry.Value = num;
		}
		GUILayout.EndHorizontal();
	}

	private void Awake()
	{
		Log = base.Logger;
		BindSettings(base.Config);
		try
		{
			PumpAssets.Load(Path.GetDirectoryName(base.Info.Location));
			harmony = new Harmony("DogEggz.JetPump");
			harmony.PatchAll(typeof(Plugin).Assembly);
			PumpInput.PatchCheatspeed(harmony);
			PumpTiller.Patch(harmony);
			base.gameObject.AddComponent<PairingMenu>();
			base.gameObject.AddComponent<PumpMarkers>();
			Ready = true;
			base.Logger.LogInfo("Propeller 1.2.2 loaded: Propeller 604 (30,000 N), controller 605, Huge Propeller 606 (800,000 N).");
		}
		catch (Exception ex)
		{
			Ready = false;
			harmony?.UnpatchSelf();
			PumpAssets.Unload();
			base.Logger.LogError("Propeller initialization aborted: " + ex);
		}
	}

	private void OnDestroy()
	{
		Ready = false;
		if (waterOffset != null)
		{
			waterOffset.SettingChanged -= SnapOffset;
		}
		PairingMenu.Close();
		harmony?.UnpatchSelf();
		PumpWorld.Reset();
		PumpAssets.Unload();
	}
}
