using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace ConversionSizeSpeed;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class ConversionSizeSpeed : BaseUnityPlugin
{
	private const string ModName = "Conversion Size & Speed";
	private const string ModVersion = "1.0.0";
	private const string ModGUID = "org.bepinex.plugins.conversionsizespeed";

	private readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static readonly Dictionary<string, ConfigEntry<int>> storageSpace = new();
	private static readonly Dictionary<string, ConfigEntry<int>> fuelSpace = new();
	private static readonly Dictionary<string, ConfigEntry<int>> storageSpaceIncreasePerBoss = new();
	private static readonly Dictionary<string, ConfigEntry<int>> fuelSpaceIncreasePerBoss = new();
	private static readonly Dictionary<string, ConfigEntry<int>> conversionSpeed = new();

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public string? Category;
		[UsedImplicitly] public int? Order;
		[UsedImplicitly] public bool? ShowRangeAsPercent;
	}

	private enum Toggle
	{
		On = 1,
		Off = 0
	}

	private static ConversionSizeSpeed mod = null!;

	public void Awake()
	{
		mod = this;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, new ConfigDescription("If on, only admins can change the configuration on a server."));
		configSync.AddLockingConfigEntry(serverConfigLocked);
	}

	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
	private class FetchSmelterPiecesObjectDB
	{
		private static void Postfix(ObjectDB __instance)
		{
			if (__instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces is { } pieces)
			{
				FetchSmelterPieces(pieces.m_pieces);
			}
		}
	}

	[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
	private class FetchSmelterPiecesZNetScene
	{
		private static void Postfix(ZNetScene __instance)
		{
			FetchSmelterPieces(__instance.m_prefabs);
		}
	}

	private static void FetchSmelterPieces(IEnumerable<GameObject> prefabs)
	{
		Localization english = new();
		english.SetupLanguage("English");

		Regex regex = new("['[\"\\]]");

		foreach (Smelter smelter in prefabs.Select(p => p.GetComponent<Smelter>()).Where(s => s != null))
		{
			int order = 0;

			string pieceName = smelter.GetComponent<Piece>().m_name;

			if (conversionSpeed.ContainsKey(pieceName))
			{
				return;
			}

			int i = conversionSpeed.Count + 2;

			if (smelter.m_addOreSwitch)
			{
				storageSpace[pieceName] = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Storage space", smelter.m_maxOre, new ConfigDescription($"Sets the maximum number of items that a {english.Localize(pieceName)} can hold.", new AcceptableValueRange<int>(1, 1000), new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order }));
				storageSpace[pieceName].SettingChanged += OnSizeChanged;
				storageSpaceIncreasePerBoss[pieceName] = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Storage space increase per boss", 0, new ConfigDescription($"Increases the maximum number of items that a {english.Localize(pieceName)} can hold for each boss killed.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order, ShowRangeAsPercent = false }));
				storageSpaceIncreasePerBoss[pieceName].SettingChanged += OnSizeChanged;
			}
			if (smelter.m_addWoodSwitch)
			{
				fuelSpace[pieceName] = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Fuel space", smelter.m_maxFuel, new ConfigDescription($"Sets the maximum number of fuel that a {english.Localize(pieceName)} can hold.", new AcceptableValueRange<int>(1, 1000), new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order }));
				fuelSpace[pieceName].SettingChanged += OnSizeChanged;
				fuelSpaceIncreasePerBoss[pieceName] = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Fuel space increase per boss", 0, new ConfigDescription($"Increases the maximum number of fuel that a {english.Localize(pieceName)} can hold for each boss killed.", new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order, ShowRangeAsPercent = false }));
				fuelSpaceIncreasePerBoss[pieceName].SettingChanged += OnSizeChanged;
			}

			conversionSpeed[pieceName] = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Conversion time", (int)smelter.m_secPerProduct, new ConfigDescription($"Time in seconds that a {english.Localize(pieceName)} needs for one conversion.", new AcceptableValueRange<int>(1, pieceName == "$piece_bathtub" ? 10000 : 1000), new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order }));
			conversionSpeed[pieceName].SettingChanged += OnSpeedChanged;
		}
	}

	private static int BossesDead() => new[] { "eikthyr", "gdking", "bonemass", "dragon", "goblinking" }.Count(boss => ZoneSystem.instance.GetGlobalKey("defeated_" + boss));

	private static void OnSizeChanged(object o, EventArgs e) => RecalculateAllSizes();
	private static void OnSpeedChanged(object o, EventArgs e) => UpdateConversionSpeed();

	private static void RecalculateAllSizes()
	{
		foreach (Smelter smelter in FindObjectsOfType<Smelter>())
		{
			string pieceName = smelter.GetComponent<Piece>().m_name;
			if (smelter.m_addOreSwitch)
			{
				smelter.m_maxOre = storageSpace[pieceName].Value + storageSpaceIncreasePerBoss[pieceName].Value * BossesDead();
			}

			if (smelter.m_addWoodSwitch)
			{
				smelter.m_maxFuel = fuelSpace[pieceName].Value + fuelSpaceIncreasePerBoss[pieceName].Value * BossesDead();
			}
		}
	}

	private static void UpdateConversionSpeed()
	{
		foreach (Smelter smelter in FindObjectsOfType<Smelter>())
		{
			string pieceName = smelter.GetComponent<Piece>().m_name;
			smelter.m_secPerProduct = conversionSpeed[pieceName].Value;
		}
	}

	[HarmonyPatch(typeof(Smelter), nameof(Smelter.Awake))]
	private class ChangeSmelterValues
	{
		[UsedImplicitly]
		private static void Postfix(Smelter __instance)
		{
			string pieceName = __instance.GetComponent<Piece>().m_name;

			if (__instance.m_addOreSwitch)
			{
				__instance.m_maxOre = storageSpace[pieceName].Value;
			}

			if (__instance.m_addWoodSwitch)
			{
				__instance.m_maxFuel = fuelSpace[pieceName].Value;
			}

			__instance.m_secPerProduct = conversionSpeed[pieceName].Value;
		}
	}

	[HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
	private class PatchBossDeath
	{
		[UsedImplicitly]
		private static void Postfix(Character __instance)
		{
			if (__instance.IsBoss())
			{
				RecalculateAllSizes();
			}
		}
	}
}
