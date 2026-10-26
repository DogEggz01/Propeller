using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace JetPump;

[HarmonyPatch]
internal static class PumpSafety
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(PlayerEmbarkerNew), "PlayerDisembark");
		yield return AccessTools.Method(typeof(PlayerEmbarkerNew), "PlayerEmbark");
		yield return AccessTools.Method(typeof(PlayerEmbarkDisembarkTrigger), "ExitBoat");
		yield return AccessTools.Method(typeof(PlayerEmbarkDisembarkTrigger), "EnterBoat");
		yield return AccessTools.Method(typeof(PlayerEmbarkTriggerNew), "ExitBoat");
		yield return AccessTools.Method(typeof(PlayerEmbarkTriggerNew), "EnterBoat");
	}

	private static void Prefix(out Transform __state)
	{
		__state = GameState.currentBoat;
	}

	private static void Postfix(Transform __state)
	{
		BoatChanged(__state, GameState.currentBoat);
	}

	internal static void BoatChanged(Transform before, Transform after)
	{
		if (!Plugin.Ready || !before || before == after || !GameState.playing || PumpWorld.Loading || GameState.currentlyLoading || GameState.loadingBoatLocalItems || GameState.justStarted || GameState.recovering || (bool)GameState.currentShipyard || GameState.sleeping || (bool)GameState.inBed || GameState.justWokeUp)
		{
			return;
		}
		int num = PumpWorld.BoatId(before);
		if ((bool)after && num == PumpWorld.BoatId(after))
		{
			return;
		}
		PumpWorld.State.SafetyShutdown(num);
		foreach (PumpItem item in PumpWorld.Live)
		{
			if ((bool)item && item.IsPump && item.Record != null && item.Record.BoatId == num && !item.Record.Enabled)
			{
				item.StopDrive();
			}
		}
	}
}
