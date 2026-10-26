using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace JetPump;

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
