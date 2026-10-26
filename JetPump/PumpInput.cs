using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace JetPump;

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
