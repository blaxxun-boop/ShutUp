using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using Mono.Cecil;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace ShutUp
{
	public static class ShutUp
	{
		public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>();
		public static void Patch(AssemblyDefinition assembly) { }
		
		private const string CONFIG_FILE_NAME = "ShutUp.cfg";
		private static readonly ConfigFile Config = new(Path.Combine(Paths.ConfigPath, CONFIG_FILE_NAME), true);
		private static readonly Dictionary<Assembly, ConfigEntry<LogLevel>> logLevel = new();

		[UsedImplicitly]
		public static void Initialize()
		{
			Harmony harmony = new("org.bepinex.patchers.shutup");
			harmony.PatchAll(typeof(Patch_ManualLogSource_Log));
			harmony.PatchAll(typeof(Patch_Preloader_PatchEntrypoint));
		}

		[HarmonyPatch(typeof(ManualLogSource), nameof(ManualLogSource.Log))]
		private static class Patch_ManualLogSource_Log
		{
			private static bool Prefix(LogLevel level)
			{
				if (logLevel.TryGetValue(new StackFrame(3).GetMethod().DeclaringType.Assembly, out ConfigEntry<LogLevel> assemblyLogLevel))
				{
					return (assemblyLogLevel.Value & level) != 0;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(Logger), nameof(Logger.InitializeInternalLoggers))]
		private static class Patch_Preloader_PatchEntrypoint
		{
			private static void Prefix()
			{
				ConfigEntry<LogLevel> valheimLogLevel = Config.Bind("General", "Valheim", LogLevel.Warning);
				foreach (Assembly valheimAssembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => new[] { "assembly_valheim", "assembly_guiutils" }.Contains(a.GetName().Name)))
				{
					logLevel[valheimAssembly] = valheimLogLevel;
				}

				Harmony harmony = new("org.bepinex.patchers.shutup.inner");
				harmony.PatchAll(typeof(Patch_UnityLogSource_OnUnityLogMessageReceived));
				harmony.PatchAll(typeof(Patch_BaseUnityPlugin));
			}

			[HarmonyPatch(typeof(UnityLogSource), nameof(UnityLogSource.OnUnityLogMessageReceived))]
			private static class Patch_UnityLogSource_OnUnityLogMessageReceived
			{
				private static bool Prefix(LogType type)
				{
					LogLevel level;
					switch (type)
					{
						case LogType.Assert:
						case LogType.Error:
						case LogType.Exception:
							level = LogLevel.Error;
							break;
						case LogType.Warning:
							level = LogLevel.Warning;
							break;
						default:
							level = LogLevel.Info;
							break;
					}

					MethodBase methodBase = new StackFrame(8).GetMethod();

					if (methodBase == null || methodBase.DeclaringType == null)
					{
						return true;
					}

					if (logLevel.TryGetValue(methodBase.DeclaringType.Assembly, out ConfigEntry<LogLevel> assemblyLogLevel))
					{
						return (assemblyLogLevel.Value & level) != 0;
					}
					return true;
				}
			}

			[HarmonyPatch(typeof(BaseUnityPlugin), MethodType.Constructor)]
			private static class Patch_BaseUnityPlugin
			{
				private static void Postfix(BaseUnityPlugin __instance)
				{
					logLevel[__instance.GetType().Assembly] = Config.Bind("General", __instance.Info.Metadata.Name, LogLevel.Warning);
				}
			}
		}
	}
}
