﻿using System;
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
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class ConversionSizeSpeed : BaseUnityPlugin
{
	private const string ModName = "Conversion Size & Speed";
	private const string ModVersion = "1.0.16";
	private const string ModGUID = "org.bepinex.plugins.conversionsizespeed";

	private readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<KeyboardShortcut> fillModifierKey = null!;
	private static readonly Dictionary<string, ConfigEntry<int>> storageSpace = new();
	private static readonly Dictionary<string, ConfigEntry<int>> fuelSpace = new();
	private static readonly Dictionary<string, ConfigEntry<int>> fuelPerProduct = new();
	private static readonly Dictionary<string, ConfigEntry<int>> storageSpaceIncreasePerBoss = new();
	private static readonly Dictionary<string, ConfigEntry<int>> fuelSpaceIncreasePerBoss = new();
	private static readonly Dictionary<string, ConfigEntry<int>> conversionSpeed = new();
	private static ConfigEntry<Toggle> ignoreWindspeed = null!;
	
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
		Off = 0,
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
		fillModifierKey = config("1 - General", "Fill up modifier key", new KeyboardShortcut(KeyCode.LeftShift), new ConfigDescription("Modifier key to hold, to fill all possible items at once. Clear value to disable this."), false);
	}

	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
	private class FetchSmelterPiecesObjectDB
	{
		[HarmonyPriority(Priority.Last)]
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
		[HarmonyPriority(Priority.Last)]
		private static void Postfix(ZNetScene __instance)
		{
			FetchSmelterPieces(__instance.m_prefabs);
		}
	}

	private static void FetchSmelterPieces(IEnumerable<GameObject> prefabs)
	{
		Localization english = new();
		english.SetupLanguage("English");

		Regex regex = new("""['\["\]]""");

		foreach (Smelter smelter in prefabs.Select(p => p.GetComponentInChildren<Smelter>()).Where(s => s?.GetComponentInParent<Piece>() != null))
		{
			int order = 0;

			string pieceName = smelter.GetComponentInParent<Piece>().m_name;

			if (conversionSpeed.ContainsKey(pieceName))
			{
				continue;
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
				if (pieceName != "$piece_bathtub")
				{
					fuelPerProduct[pieceName] = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Fuel per product", smelter.m_fuelPerProduct, new ConfigDescription($"Sets how much fuel a {english.Localize(pieceName)} needs per produced product.", new AcceptableValueRange<int>(1, 20), new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order }));
					fuelPerProduct[pieceName].SettingChanged += OnFuelPerProductChanged;
				}
			}
			if (smelter.m_windmill is not null)
			{
				ignoreWindspeed = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Ignore wind intensity", Toggle.Off, new ConfigDescription($"If on, {english.Localize(pieceName)} always produces at average speed, regardless of wind.", null, new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order }));
			}

			conversionSpeed[pieceName] = mod.config($"{i} - {regex.Replace(english.Localize(pieceName), "")}", "Conversion time", (int)smelter.m_secPerProduct, new ConfigDescription($"Time in seconds that a {english.Localize(pieceName)} needs for one conversion.", new AcceptableValueRange<int>(1, pieceName == "$piece_bathtub" ? 10000 : 1000), new ConfigurationManagerAttributes { Category = $"{i} - {Localization.instance.Localize(pieceName)}", Order = --order }));
			conversionSpeed[pieceName].SettingChanged += OnSpeedChanged;
		}
	}

	private static int BossesDead() => new[] { "eikthyr", "gdking", "bonemass", "dragon", "goblinking", "queen", "fader" }.Count(boss => ZoneSystem.instance.GetGlobalKey("defeated_" + boss));

	private static void OnSizeChanged(object o, EventArgs e) => RecalculateAllSizes();
	private static void OnSpeedChanged(object o, EventArgs e) => UpdateConversionSpeed();
	private static void OnFuelPerProductChanged(object o, EventArgs e) => UpdateFuelPerProduct();

	private static void RecalculateAllSizes()
	{
		foreach (Smelter smelter in FindObjectsOfType<Smelter>())
		{
			string pieceName = smelter.GetComponentInParent<Piece>().m_name;
			if (smelter.m_addOreSwitch && storageSpace.TryGetValue(pieceName, out ConfigEntry<int> oreValue))
			{
				smelter.m_maxOre = oreValue.Value + storageSpaceIncreasePerBoss[pieceName].Value * BossesDead();
			}

			if (smelter.m_addWoodSwitch && fuelSpace.TryGetValue(pieceName, out ConfigEntry<int> fuelValue))
			{
				smelter.m_maxFuel = fuelValue.Value + fuelSpaceIncreasePerBoss[pieceName].Value * BossesDead();
			}
		}
	}

	private static void UpdateConversionSpeed()
	{
		foreach (Smelter smelter in FindObjectsOfType<Smelter>())
		{
			string pieceName = smelter.GetComponentInParent<Piece>().m_name;
			if (conversionSpeed.TryGetValue(pieceName, out ConfigEntry<int> speedValue))
			{
				smelter.m_secPerProduct = speedValue.Value;
			}
		}
	}

	private static void UpdateFuelPerProduct()
	{
		foreach (Smelter smelter in FindObjectsOfType<Smelter>())
		{
			string pieceName = smelter.GetComponentInParent<Piece>().m_name;
			if (fuelPerProduct.ContainsKey(pieceName) && smelter.m_addWoodSwitch)
			{
				smelter.m_fuelPerProduct = fuelPerProduct[pieceName].Value;
			}
		}
	}

	[HarmonyPatch(typeof(Smelter), nameof(Smelter.Awake))]
	private class ChangeSmelterValues
	{
		[UsedImplicitly]
		private static void Postfix(Smelter __instance)
		{
			string pieceName = __instance.GetComponentInParent<Piece>().m_name;

			if (__instance.m_addOreSwitch && storageSpace.TryGetValue(pieceName, out ConfigEntry<int> oreValue))
			{
				__instance.m_maxOre = oreValue.Value + storageSpaceIncreasePerBoss[pieceName].Value * BossesDead();
			}

			if (__instance.m_addWoodSwitch && fuelSpace.TryGetValue(pieceName, out ConfigEntry<int> fuelValue))
			{
				__instance.m_maxFuel = fuelValue.Value + fuelSpaceIncreasePerBoss[pieceName].Value * BossesDead();
			}

			if (conversionSpeed.TryGetValue(pieceName, out ConfigEntry<int> speedValue))
			{
				__instance.m_secPerProduct = speedValue.Value;
			}

			if (fuelPerProduct.TryGetValue(pieceName, out ConfigEntry<int> fuelUsageValue))
			{
				__instance.m_fuelPerProduct = fuelUsageValue.Value;
			}
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

	[HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddOre))]
	private class FillAllOres
	{
		[HarmonyPriority(Priority.High)]
		public static void Prefix(Smelter __instance, ItemDrop.ItemData? item, out KeyValuePair<ItemDrop.ItemData?, int> __state)
		{
			int ore = __instance.m_nview.GetZDO().GetInt("queued");
			__state = new KeyValuePair<ItemDrop.ItemData?, int>(item, ore);
		}

		public static void Postfix(Smelter __instance, Switch sw, Humanoid user, KeyValuePair<ItemDrop.ItemData?, int> __state, bool __result)
		{
			if (Input.GetKey(fillModifierKey.Value.MainKey) && fillModifierKey.Value.Modifiers.All(Input.GetKey) && __result && __state.Key is null)
			{
				if (!__instance.m_nview.IsOwner())
				{
					ZDOID zdoid = __instance.m_nview.GetZDO().m_uid;
					if (!ZDOExtraData.s_ints.TryGetValue(zdoid, out BinarySearchDictionary<int, int> ints))
					{
						ZDOExtraData.s_ints[zdoid] = ints = new BinarySearchDictionary<int, int>();
					}
					ints.TryGetValue("queued".GetStableHashCode(), out int ore);
					// ReSharper disable once CompareOfFloatsByEqualityOperator
					if (ore == __state.Value)
					{
						ints["queued".GetStableHashCode()] = ore + 1;
					}
				}

				MessageHud originalMessageHud = MessageHud.m_instance;
				MessageHud.m_instance = null;
				try
				{
					__instance.OnAddOre(sw, user, null);
				}
				finally
				{
					MessageHud.m_instance = originalMessageHud;
				}
			}
		}
	}

	[HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddFuel))]
	private class FillAllFuel
	{
		[HarmonyPriority(Priority.High)]
		public static void Prefix(Smelter __instance, ItemDrop.ItemData? item, out KeyValuePair<ItemDrop.ItemData?, float> __state)
		{
			float fuel = __instance.m_nview.GetZDO().GetFloat("fuel".GetStableHashCode());
			__state = new KeyValuePair<ItemDrop.ItemData?, float>(item, fuel);
		}

		public static void Postfix(Smelter __instance, Switch sw, Humanoid user, KeyValuePair<ItemDrop.ItemData?, float> __state, bool __result)
		{
			if (Input.GetKey(fillModifierKey.Value.MainKey) && fillModifierKey.Value.Modifiers.All(Input.GetKey) && __result && __state.Key is null)
			{
				if (!__instance.m_nview.IsOwner())
				{
					ZDOID zdoid = __instance.m_nview.GetZDO().m_uid;
					if (!ZDOExtraData.s_floats.TryGetValue(zdoid, out BinarySearchDictionary<int, float> floats))
					{
						ZDOExtraData.s_floats[zdoid] = floats = new BinarySearchDictionary<int, float>();
					}
					floats.TryGetValue("fuel".GetStableHashCode(), out float fuel);
					// ReSharper disable once CompareOfFloatsByEqualityOperator
					if (fuel == __state.Value)
					{
						floats["fuel".GetStableHashCode()] = fuel + 1;
					}
				}

				MessageHud originalMessageHud = MessageHud.m_instance;
				MessageHud.m_instance = null;
				try
				{
					__instance.OnAddFuel(sw, user, null);
				}
				finally
				{
					MessageHud.m_instance = originalMessageHud;
				}
			}
		}
	}

	[HarmonyPatch(typeof(Smelter), nameof(Smelter.RPC_AddOre))]
	private class ThrottleOreEffects
	{
		private static float lastFill;

		public static void Prefix(Smelter __instance, out EffectList? __state)
		{
			if (Math.Abs(lastFill - Time.fixedTime) > 0)
			{
				__state = null;
				lastFill = Time.fixedTime;
			}
			else
			{
				__state = __instance.m_oreAddedEffects;
				__instance.m_oreAddedEffects = new EffectList();
			}
		}

		public static void Finalizer(Smelter __instance, EffectList? __state)
		{
			if (__state is not null)
			{
				__instance.m_oreAddedEffects = __state;
			}
		}
	}

	[HarmonyPatch(typeof(Smelter), nameof(Smelter.RPC_AddFuel))]
	private class ThrottleFuelEffects
	{
		private static float lastFill;

		public static void Prefix(Smelter __instance, out EffectList? __state)
		{
			if (Math.Abs(lastFill - Time.fixedTime) > 0)
			{
				__state = null;
				lastFill = Time.fixedTime;
			}
			else
			{
				__state = __instance.m_fuelAddedEffects;
				__instance.m_fuelAddedEffects = new EffectList();
			}
		}

		public static void Finalizer(Smelter __instance, EffectList? __state)
		{
			if (__state is not null)
			{
				__instance.m_fuelAddedEffects = __state;
			}
		}
	}

	[HarmonyPatch(typeof(Smelter), nameof(Smelter.OnHoverAddFuel))]
	private class OverrideFuelHoverText
	{
		public static void Postfix(Smelter __instance, ref string __result)
		{
			if (fillModifierKey.Value.MainKey is not KeyCode.None)
			{
				int amount = Math.Min(__instance.m_maxFuel - Mathf.CeilToInt(__instance.GetFuel()), Player.m_localPlayer?.m_inventory.CountItems(__instance.m_fuelItem.m_itemData.m_shared.m_name) ?? 0);
				if (amount > 0)
				{
					__result += Localization.instance.Localize($"\n[<b><color=yellow>{fillModifierKey.Value}</color> + <color=yellow>$KEY_Use</color></b>] $piece_smelter_add {__instance.m_fuelItem.m_itemData.m_shared.m_name} ({amount})");
				}
			}
		}
	}

	[HarmonyPatch]
	private class OverrideOreHoverText
	{
		public static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(Smelter), nameof(Smelter.OnHoverAddOre)),
			AccessTools.DeclaredMethod(typeof(Smelter), nameof(Smelter.OnHoverEmptyOre)),
		};

		public static void Postfix(Smelter __instance, ref string __result)
		{
			if (fillModifierKey.Value.MainKey is not KeyCode.None)
			{
				int free = __instance.m_maxOre - __instance.GetQueueSize();
				List<string> items = new();
				foreach (Smelter.ItemConversion conversion in __instance.m_conversion)
				{
					if (free <= 0)
					{
						break;
					}

					int inInv = Player.m_localPlayer?.m_inventory.CountItems(conversion.m_from.m_itemData.m_shared.m_name) ?? 0;
					if (inInv > 0)
					{
						items.Add($"{Math.Min(free, inInv)} {conversion.m_from.m_itemData.m_shared.m_name}");
					}

					free -= inInv;
				}
				if (items.Count > 0)
				{
					__result += Localization.instance.Localize($"\n[<b><color=yellow>{fillModifierKey.Value}</color> + <color=yellow>$KEY_Use</color></b>] {__instance.m_addOreTooltip} ({string.Join(", ", items)})");
				}
			}
		}
	}

	[HarmonyPatch(typeof(Windmill), nameof(Windmill.GetPowerOutput))]
	private static class MakeWindmillIgnoreWind
	{
		private static bool Prefix(ref float __result)
		{
			if (ignoreWindspeed.Value == Toggle.On)
			{
				__result = 0.5f;
				return false;
			}

			return true;
		}
	}
}
