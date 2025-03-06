using System;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PerfectPlacement.Patches.Compatibility;

public class FirstPersonModeCompat
{
    public static bool IsFirstPerson = false;

    public static void Init()
    {
        if (!Chainloader.PluginInfos.TryGetValue("Azumatt.FirstPersonMode", out PluginInfo firstPersonModeInfo)) return;
        if (firstPersonModeInfo == null || firstPersonModeInfo.Instance == null) return;
        if (firstPersonModeInfo.Metadata.Version.Major > 1 || (firstPersonModeInfo.Metadata.Version.Major == 1 && firstPersonModeInfo.Metadata.Version.Minor > 3) ||
            (firstPersonModeInfo.Metadata.Version.Major == 1 && firstPersonModeInfo.Metadata.Version.Minor == 3 && firstPersonModeInfo.Metadata.Version.Build >= 8))
        {
            // FirstPersonMode is loaded
            PerfectPlacementPlugin.Instance._harmony.PatchAll(typeof(FirstPersonModeCompat));
        }
    }

    [HarmonyPatch("FirstPersonMode.Util.Functions, FirstPersonMode", "IsInFirstPersonMode"), HarmonyPostfix]
    public static void IsInFirstPersonMode(ref bool __result)
    {
        if (Player.m_localPlayer != null)
        {
            IsFirstPerson = __result;
        }
    }
}