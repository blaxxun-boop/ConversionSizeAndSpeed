using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ConversionSizeSpeed;

public static class ZDOStuff
{
	[HarmonyPatch(typeof(ZDO), nameof(ZDO.Save))]
	private static class StoreZDO
	{
		[HarmonyPriority(Priority.Low)]
		private static void Prefix(ZDO __instance)
		{
			if (ConversionSizeSpeed.smelterHashes.Contains(__instance.m_prefab))
			{
				if (ZDOExtraData.s_saveInts.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, int> smelterInts))
				{
					if (smelterInts.TryGetValue(ZDOVars.s_queued, out int queued) && queued > 100)
					{
						smelterInts[ZDOVars.s_queued] = 100;
						smelterInts["ConversionSizeSpeed queued".GetStableHashCode()] = queued;
					}
				}
			}

			if (ZDOExtraData.s_saveStrings.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, string> cache) && cache.Count > 0x7f)
			{
				List<KeyValuePair<int, string>> overflow = cache.Skip(0x7f).ToList();

				foreach (KeyValuePair<int, string> entry in overflow)
				{
					cache.Remove(entry.Key);
				}

				ZPackage package = new();
				package.Write(overflow.Count);
				foreach (KeyValuePair<int, string> keyValuePair in overflow)
				{
					package.Write(keyValuePair.Key);
					package.Write(keyValuePair.Value);
				}

				if (!ZDOExtraData.s_saveByteArrays.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, byte[]> value))
				{
					ZDOExtraData.s_saveByteArrays.Add(__instance.m_uid, value = new BinarySearchDictionary<int, byte[]>());
				}
				value.Add("zdo overflow strings".GetStableHashCode(), package.GetArray());
			}
		}
	}

	[HarmonyPatch(typeof(ZDO), nameof(ZDO.Serialize))]
	private static class SerializeZDO
	{
		[HarmonyPriority(Priority.Low)]
		private static void Prefix(ZDO __instance)
		{
			if (ConversionSizeSpeed.smelterHashes.Contains(__instance.m_prefab))
			{
				if (ZDOExtraData.s_ints.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, int> smelterInts))
				{
					if (smelterInts.TryGetValue(ZDOVars.s_queued, out int queued) && queued > 100)
					{
						smelterInts[ZDOVars.s_queued] = 100;
						smelterInts["ConversionSizeSpeed queued".GetStableHashCode()] = queued;
					}
				}
			}

			if (ZDOExtraData.s_strings.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, string> cache) && cache.Count > 0x7f)
			{
				List<KeyValuePair<int, string>> overflow = cache.Skip(0x7f).ToList();

				foreach (KeyValuePair<int, string> entry in overflow)
				{
					cache.Remove(entry.Key);
				}

				ZPackage package = new();
				package.Write(overflow.Count);
				foreach (KeyValuePair<int, string> keyValuePair in overflow)
				{
					package.Write(keyValuePair.Key);
					package.Write(keyValuePair.Value);
				}

				if (!ZDOExtraData.s_byteArrays.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, byte[]> value))
				{
					ZDOExtraData.s_byteArrays.Add(__instance.m_uid, value = new BinarySearchDictionary<int, byte[]>());
				}
				value.Add("zdo overflow strings".GetStableHashCode(), package.GetArray());
			}
		}

		private static void Postfix(ZDO __instance) => LoadZDO.Postfix(__instance);
	}

	[HarmonyPatch]
	private static class LoadZDO
	{
		private static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(ZDO), nameof(ZDO.Load)),
			AccessTools.DeclaredMethod(typeof(ZDO), nameof(ZDO.Deserialize)),
		};

		[HarmonyPriority(Priority.High)]
		public static void Postfix(ZDO __instance)
		{
			if (ZDOExtraData.s_byteArrays.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, byte[]> cache))
			{
				if (cache.TryGetValue("zdo overflow strings".GetStableHashCode(), out byte[] byteArray))
				{
					cache.Remove("zdo overflow strings".GetStableHashCode());

					ZPackage pkg = new(byteArray);
					int size = pkg.ReadInt();
					for (int index = 0; index < size; ++index)
					{
						int key = pkg.ReadInt();
						string data = pkg.ReadString();
						if (!ZDO.Strip(key, data))
						{
							ZDOExtraData.Set(__instance.m_uid, key, data);
						}
					}
				}
			}

			if (ConversionSizeSpeed.smelterHashes.Contains(__instance.m_prefab))
			{
				if (ZDOExtraData.s_ints.TryGetValue(__instance.m_uid, out BinarySearchDictionary<int, int> smelterInts))
				{
					if (smelterInts.TryGetValue("ConversionSizeSpeed queued".GetStableHashCode(), out int queued))
					{
						smelterInts[ZDOVars.s_queued] = queued;
						smelterInts.Remove("ConversionSizeSpeed queued".GetStableHashCode());
					}
				}
			}
		}
	}
}
