using System;
using BepInEx.Configuration;

namespace JetPump;

internal sealed class ConfigurationManagerAttributes
{
	public bool? ShowRangeAsPercent;

	public Action<ConfigEntryBase> CustomDrawer;
}
