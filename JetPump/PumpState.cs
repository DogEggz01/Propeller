using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JetPump;

public sealed class PumpState
{
	public const int PumpId = 604;

	public const int ControllerId = 605;

	public const int HugePumpId = 606;

	public const string AllHelms = "*";

	public const int GroupCount = 3;

	public readonly Dictionary<int, PumpRecord> Items = new Dictionary<int, PumpRecord>();

	public bool Writable = true;

	public bool MigratedNumbers;

	private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

	public static bool IsPump(int kind)
	{
		if (kind != 604)
		{
			return kind == 606;
		}
		return true;
	}

	public static bool IsItem(int kind)
	{
		if (!IsPump(kind))
		{
			return kind == 605;
		}
		return true;
	}

	public static float RatedThrust(int kind)
	{
		return kind switch
		{
			604 => 30000f, 
			606 => 800000f, 
			_ => 0f, 
		};
	}

	public static string Label(PumpRecord pump)
	{
		return ((pump.Kind == 606) ? "H.PROP " : "PROP ") + pump.Number.ToString("00");
	}

	public PumpRecord GetOrCreate(int id, int kind)
	{
		if (id <= 0 || !IsItem(kind))
		{
			throw new ArgumentException("Invalid item identity.");
		}
		if (!Items.TryGetValue(id, out var value))
		{
			Dictionary<int, PumpRecord> items = Items;
			PumpRecord obj = new PumpRecord
			{
				Id = id,
				Kind = kind,
				SafetyEnabled = (kind == 605)
			};
			value = obj;
			items.Add(id, obj);
		}
		if (value.Kind != kind)
		{
			throw new InvalidOperationException("Saved item identity belongs to a different prefab.");
		}
		return value;
	}

	public PumpRecord Peer(PumpRecord item)
	{
		if (item != null && Items.TryGetValue(item.PeerId, out var value) && value.PeerId == item.Id && ((item.Kind == 605 && IsPump(value.Kind)) || (value.Kind == 605 && IsPump(item.Kind))))
		{
			return value;
		}
		return null;
	}

	public bool Pair(int controllerId, int pumpId, int boatId)
	{
		if (!Items.TryGetValue(controllerId, out var value) || !Items.TryGetValue(pumpId, out var value2) || value.Kind != 605 || !IsPump(value2.Kind) || boatId <= 0 || !value.Mounted || !value2.Mounted || value.BoatId != boatId || value2.BoatId != boatId)
		{
			return false;
		}
		if (value2.PeerId != 0 && value2.PeerId != controllerId)
		{
			PumpRecord pumpRecord = Peer(value2);
			if (pumpRecord != null && pumpRecord.Mounted)
			{
				return false;
			}
			if (pumpRecord != null)
			{
				pumpRecord.PeerId = 0;
				pumpRecord.Wheel = "";
				pumpRecord.RequirePairChoice = true;
			}
		}
		PumpRecord pumpRecord2 = Peer(value);
		if (pumpRecord2 != null && pumpRecord2 != value2)
		{
			pumpRecord2.PeerId = 0;
			pumpRecord2.Enabled = false;
		}
		value.PeerId = value2.Id;
		value2.PeerId = value.Id;
		value.Wheel = "";
		value.RequirePairChoice = false;
		value2.RequirePairChoice = false;
		value2.Enabled = false;
		value2.Throttle = 0f;
		return true;
	}

	public bool ValidPair(PumpRecord box)
	{
		PumpRecord pumpRecord = Peer(box);
		if (box != null && box.Kind == 605 && pumpRecord != null && box.Mounted && pumpRecord.Mounted && box.BoatId > 0)
		{
			return pumpRecord.BoatId == box.BoatId;
		}
		return false;
	}

	public void RemoveFromWall(PumpRecord item)
	{
		item.StopForRemoval();
		PumpRecord pumpRecord = Peer(item);
		if (pumpRecord != null)
		{
			pumpRecord.Enabled = false;
			pumpRecord.Wheel = "";
		}
	}

	public void StopController(PumpRecord box)
	{
		if (box != null && box.Kind == 605)
		{
			box.Wheel = "";
			PumpRecord pumpRecord = Peer(box);
			if (pumpRecord != null)
			{
				pumpRecord.Enabled = false;
			}
		}
	}

	public void StopPump(PumpRecord pump)
	{
		if (pump != null && IsPump(pump.Kind))
		{
			pump.Enabled = false;
			PumpRecord pumpRecord = Peer(pump);
			if (pumpRecord != null)
			{
				pumpRecord.Wheel = "";
			}
		}
	}

	public void Install(PumpRecord item, int boatId)
	{
		if (boatId <= 0)
		{
			return;
		}
		if (item.BoatId != 0 && item.BoatId != boatId)
		{
			PumpRecord pumpRecord = Peer(item);
			if (pumpRecord != null)
			{
				pumpRecord.PeerId = 0;
				pumpRecord.Enabled = false;
				pumpRecord.Wheel = "";
				pumpRecord.RequirePairChoice = true;
			}
			item.PeerId = 0;
			item.RequirePairChoice = true;
			item.Enabled = false;
			item.Throttle = 0f;
			item.Wheel = "";
			item.HasPose = false;
			item.Number = 0;
		}
		item.BoatId = boatId;
		item.Mounted = true;
		if (IsPump(item.Kind) && item.Number == 0)
		{
			item.Number = AvailableNumber(boatId);
		}
	}

	private int AvailableNumber(int boatId)
	{
		HashSet<int> hashSet = new HashSet<int>();
		foreach (PumpRecord value in Items.Values)
		{
			if (IsPump(value.Kind) && value.BoatId == boatId && value.Number > 0)
			{
				hashSet.Add(value.Number);
			}
		}
		int i;
		for (i = 1; hashSet.Contains(i); i++)
		{
		}
		return i;
	}

	public void Forget(int id)
	{
		if (Items.TryGetValue(id, out var value))
		{
			PumpRecord pumpRecord = Peer(value);
			if (pumpRecord != null)
			{
				pumpRecord.PeerId = 0;
				pumpRecord.Enabled = false;
				pumpRecord.Wheel = "";
				pumpRecord.RequirePairChoice = true;
			}
			Items.Remove(id);
		}
	}

	public void RetainSavedItems(HashSet<int> savedIds)
	{
		if (!Writable || savedIds == null)
		{
			return;
		}
		foreach (int item in new List<int>(Items.Keys))
		{
			if (!savedIds.Contains(item))
			{
				Forget(item);
			}
		}
		if (MigratedNumbers)
		{
			RebuildNumbers();
		}
	}

	private void RebuildNumbers()
	{
		List<PumpRecord> list = new List<PumpRecord>();
		foreach (PumpRecord value2 in Items.Values)
		{
			if (IsPump(value2.Kind))
			{
				list.Add(value2);
			}
		}
		list.Sort((PumpRecord a, PumpRecord b) => (a.Number == b.Number) ? a.Id.CompareTo(b.Id) : a.Number.CompareTo(b.Number));
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		foreach (PumpRecord item in list)
		{
			if (item.BoatId <= 0)
			{
				item.Number = 0;
				continue;
			}
			dictionary.TryGetValue(item.BoatId, out var value);
			item.Number = value + 1;
			dictionary[item.BoatId] = item.Number;
		}
	}

	public void SafetyShutdown(int boatId)
	{
		if (!Writable || boatId <= 0)
		{
			return;
		}
		foreach (PumpRecord value in Items.Values)
		{
			if (value.Kind == 605 && value.BoatId == boatId && value.SafetyEnabled && ValidPair(value))
			{
				Peer(value).Enabled = false;
			}
		}
	}

	public List<PumpRecord> Members(PumpRecord box, Func<PumpRecord, bool> usable)
	{
		List<PumpRecord> list = new List<PumpRecord>();
		if (box == null || box.Kind != 605)
		{
			return list;
		}
		if (box.Group == 0 || !usable(box))
		{
			list.Add(box);
			return list;
		}
		foreach (PumpRecord value in Items.Values)
		{
			if (value.Kind == 605 && value.BoatId == box.BoatId && value.Group == box.Group && (value == box || usable(value)))
			{
				list.Add(value);
			}
		}
		list.Sort((PumpRecord a, PumpRecord b) => a.Id.CompareTo(b.Id));
		return list;
	}

	public void SetEnabled(PumpRecord box, bool on, Func<PumpRecord, bool> usable)
	{
		foreach (PumpRecord item in Members(box, usable))
		{
			if (ValidPair(item))
			{
				Peer(item).Enabled = on;
			}
		}
	}

	public void SetImmersion(PumpRecord box, bool required, Func<PumpRecord, bool> usable)
	{
		foreach (PumpRecord item in Members(box, usable))
		{
			if (ValidPair(item))
			{
				Peer(item).RequireImmersion = required;
			}
		}
	}

	public void SetPower(PumpRecord box, int power, Func<PumpRecord, bool> usable)
	{
		power = PumpRecord.ClampPower(power);
		foreach (PumpRecord item in Members(box, usable))
		{
			if (ValidPair(item))
			{
				Peer(item).Power = power;
			}
		}
	}

	public void SetSafety(PumpRecord box, bool armed, Func<PumpRecord, bool> usable)
	{
		foreach (PumpRecord item in Members(box, usable))
		{
			item.SafetyEnabled = armed;
		}
	}

	public bool SetWheel(PumpRecord box, bool on, Func<PumpRecord, bool> usable)
	{
		if (!ValidPair(box))
		{
			return false;
		}
		List<PumpRecord> list = Members(box, usable);
		foreach (PumpRecord value in Items.Values)
		{
			if (value.Kind == 605 && value.BoatId == box.BoatId)
			{
				value.Wheel = "";
			}
		}
		if (on)
		{
			foreach (PumpRecord item in list)
			{
				item.Wheel = "*";
			}
		}
		return true;
	}

	public void AdjustThrottle(List<PumpRecord> boxes, bool forward, bool decrease, float seconds)
	{
		PumpRecord pumpRecord = null;
		foreach (PumpRecord box in boxes)
		{
			if (ValidPair(box) && (pumpRecord == null || box.Id < pumpRecord.Id))
			{
				pumpRecord = box;
			}
		}
		if (pumpRecord == null)
		{
			return;
		}
		PumpRecord pumpRecord2 = Peer(pumpRecord);
		pumpRecord2.AdjustThrottle(forward, decrease, seconds);
		foreach (PumpRecord box2 in boxes)
		{
			if (ValidPair(box2))
			{
				Peer(box2).Throttle = pumpRecord2.Throttle;
			}
		}
	}

	public void SetGroup(PumpRecord box, int group, Func<PumpRecord, bool> usable)
	{
		if (box == null || box.Kind != 605)
		{
			return;
		}
		group = Math.Max(0, Math.Min(3, group));
		if (group == box.Group)
		{
			return;
		}
		if (box.Group != 0 && box.Wheel.Length > 0 && Members(box, usable).Count > 1)
		{
			box.Wheel = "";
		}
		box.Group = group;
		if (group == 0 || !usable(box) || !ValidPair(box))
		{
			return;
		}
		List<PumpRecord> list = Members(box, usable);
		PumpRecord pumpRecord = null;
		foreach (PumpRecord item in list)
		{
			if (item != box && ValidPair(item))
			{
				pumpRecord = item;
				break;
			}
		}
		if (pumpRecord == null)
		{
			return;
		}
		PumpRecord pumpRecord2 = Peer(pumpRecord);
		PumpRecord pumpRecord3 = Peer(box);
		pumpRecord3.Enabled = pumpRecord2.Enabled;
		pumpRecord3.RequireImmersion = pumpRecord2.RequireImmersion;
		pumpRecord3.Power = pumpRecord2.Power;
		pumpRecord3.Throttle = pumpRecord2.Throttle;
		box.SafetyEnabled = pumpRecord.SafetyEnabled;
		if (box.Wheel.Length <= 0 && pumpRecord.Wheel.Length <= 0)
		{
			return;
		}
		foreach (PumpRecord item2 in list)
		{
			item2.Wheel = "*";
		}
	}

	public void CycleGroup(PumpRecord box, Func<PumpRecord, bool> usable)
	{
		if (box != null)
		{
			SetGroup(box, (box.Group + 1) % 4, usable);
		}
	}

	public string Encode()
	{
		StringBuilder stringBuilder = new StringBuilder("JetPump/7\n");
		List<int> list = new List<int>(Items.Keys);
		list.Sort();
		foreach (int item in list)
		{
			PumpRecord pumpRecord = Items[item];
			stringBuilder.Append(pumpRecord.Id).Append('|').Append(pumpRecord.Kind)
				.Append('|')
				.Append(pumpRecord.PeerId)
				.Append('|')
				.Append(pumpRecord.BoatId)
				.Append('|')
				.Append(pumpRecord.Mounted ? 1 : 0)
				.Append('|')
				.Append(pumpRecord.Power)
				.Append('|')
				.Append(pumpRecord.Throttle.ToString("R", Invariant))
				.Append('|')
				.Append(pumpRecord.Enabled ? 1 : 0)
				.Append('|')
				.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pumpRecord.Wheel ?? "")))
				.Append('|')
				.Append(pumpRecord.RequirePairChoice ? 1 : 0)
				.Append('|')
				.Append(pumpRecord.Number)
				.Append('|')
				.Append(pumpRecord.HasPose ? 1 : 0);
			float[] array = new float[7] { pumpRecord.X, pumpRecord.Y, pumpRecord.Z, pumpRecord.Qx, pumpRecord.Qy, pumpRecord.Qz, pumpRecord.Qw };
			foreach (float num in array)
			{
				stringBuilder.Append('|').Append(num.ToString("R", Invariant));
			}
			stringBuilder.Append('|').Append(pumpRecord.RequireImmersion ? 1 : 0).Append('|')
				.Append(pumpRecord.SafetyEnabled ? 1 : 0);
			stringBuilder.Append('|').Append(pumpRecord.Group);
			stringBuilder.Append('\n');
		}
		return stringBuilder.ToString();
	}

	public static PumpState Decode(string encoded)
	{
		PumpState pumpState = new PumpState();
		if (string.IsNullOrEmpty(encoded))
		{
			return pumpState;
		}
		string[] array = encoded.Split('\n');
		bool flag = array[0].TrimEnd('\r') == "JetPump/1";
		bool flag2 = array[0].TrimEnd('\r') == "JetPump/2";
		bool flag3 = array[0].TrimEnd('\r') == "JetPump/3";
		bool flag4 = array[0].TrimEnd('\r') == "JetPump/7";
		bool flag5 = flag4 || array[0].TrimEnd('\r') == "JetPump/6";
		bool flag6 = flag5 || array[0].TrimEnd('\r') == "JetPump/4" || array[0].TrimEnd('\r') == "JetPump/5";
		if (!flag && !flag2 && !flag3 && !flag6)
		{
			pumpState.Writable = false;
			return pumpState;
		}
		pumpState.MigratedNumbers = flag | flag2;
		try
		{
			for (int i = 1; i < array.Length; i++)
			{
				if (string.IsNullOrWhiteSpace(array[i]))
				{
					continue;
				}
				string[] array2 = array[i].TrimEnd('\r').Split('|');
				if (array2.Length != (flag ? 11 : (flag4 ? 22 : (flag5 ? 21 : (flag6 ? 20 : 19)))))
				{
					throw new FormatException("Record length");
				}
				PumpRecord pumpRecord = new PumpRecord
				{
					Id = int.Parse(array2[0], Invariant),
					Kind = int.Parse(array2[1], Invariant),
					PeerId = int.Parse(array2[2], Invariant),
					BoatId = int.Parse(array2[3], Invariant),
					Mounted = (array2[4] == "1"),
					Power = int.Parse(array2[5], Invariant),
					Throttle = float.Parse(array2[6], Invariant),
					Enabled = (array2[7] == "1"),
					Wheel = Encoding.UTF8.GetString(Convert.FromBase64String(array2[8])),
					RequirePairChoice = (array2[9] == "1"),
					Number = int.Parse(array2[10], Invariant)
				};
				if (!flag)
				{
					pumpRecord.HasPose = array2[11] == "1";
					pumpRecord.X = float.Parse(array2[12], Invariant);
					pumpRecord.Y = float.Parse(array2[13], Invariant);
					pumpRecord.Z = float.Parse(array2[14], Invariant);
					pumpRecord.Qx = float.Parse(array2[15], Invariant);
					pumpRecord.Qy = float.Parse(array2[16], Invariant);
					pumpRecord.Qz = float.Parse(array2[17], Invariant);
					pumpRecord.Qw = float.Parse(array2[18], Invariant);
				}
				if (flag6)
				{
					if (array2[19] != "0" && array2[19] != "1")
					{
						throw new FormatException("Immersion switch");
					}
					pumpRecord.RequireImmersion = array2[19] == "1";
				}
				if (flag5)
				{
					if (array2[20] != "0" && array2[20] != "1")
					{
						throw new FormatException("Safety switch");
					}
					pumpRecord.SafetyEnabled = pumpRecord.Kind == 605 && array2[20] == "1";
				}
				if (flag4)
				{
					if (array2[21].Length != 1 || array2[21][0] < '0' || array2[21][0] > '3')
					{
						throw new FormatException("Control group");
					}
					pumpRecord.Group = array2[21][0] - 48;
				}
				if (pumpRecord.Id <= 0 || !IsItem(pumpRecord.Kind) || pumpState.Items.ContainsKey(pumpRecord.Id))
				{
					throw new FormatException("Record identity");
				}
				pumpRecord.Normalize();
				if (IsPump(pumpRecord.Kind) && (pumpRecord.Number < 0 || (pumpRecord.BoatId > 0 && pumpRecord.Number == 0)))
				{
					throw new FormatException("Pump label");
				}
				pumpState.Items.Add(pumpRecord.Id, pumpRecord);
			}
			if (pumpState.MigratedNumbers)
			{
				pumpState.RebuildNumbers();
			}
			HashSet<string> hashSet = new HashSet<string>();
			foreach (PumpRecord value2 in pumpState.Items.Values)
			{
				if (IsPump(value2.Kind) && value2.BoatId > 0 && !hashSet.Add(value2.BoatId + ":" + value2.Number))
				{
					throw new FormatException("Duplicate boat label");
				}
			}
			List<PumpRecord> list = new List<PumpRecord>(pumpState.Items.Values);
			list.Sort((PumpRecord a, PumpRecord b) => a.Id.CompareTo(b.Id));
			Dictionary<int, PumpRecord> dictionary = new Dictionary<int, PumpRecord>();
			foreach (PumpRecord item in list)
			{
				PumpRecord pumpRecord2 = pumpState.Peer(item);
				if (pumpRecord2 == null || !pumpRecord2.Mounted || !item.Mounted || pumpRecord2.BoatId != item.BoatId)
				{
					item.Enabled = false;
					item.Wheel = "";
				}
				if (item.Wheel.Length != 0)
				{
					if (!dictionary.TryGetValue(item.BoatId, out var value))
					{
						dictionary.Add(item.BoatId, item);
					}
					else if (value.Group == 0 || item.Group != value.Group)
					{
						item.Wheel = "";
					}
				}
			}
		}
		catch (Exception)
		{
			pumpState.Items.Clear();
			pumpState.Writable = false;
		}
		return pumpState;
	}
}
