using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using HarmonyLib;
using PerfectPlacement.Patches.Compatibility;
using UnityEngine;

namespace PerfectPlacement.Patches;

[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.Awake))]
static class CachedCameraDistance
{
    internal static float cachedCameraDistanceMax;
    internal static float cachedCameraDistanceMin;

    static void Prefix(GameCamera __instance)
    {
        cachedCameraDistanceMax = __instance.m_maxDistance;
        cachedCameraDistanceMin = __instance.m_minDistance;
    }
}

/// <summary>
/// Advanced Editing Mode Game Camera changes
/// </summary>
[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
public static class BlockCameraScrollInAEM
{
    private static void Prefix(GameCamera __instance)
    {
        if (AEM.IsInAemMode())
        {
            __instance.m_maxDistance = __instance.m_distance;
            __instance.m_minDistance = __instance.m_distance;
        }
        else
        {
            if (!FirstPersonModeCompat.IsFirstPerson)
            {
                __instance.m_maxDistance = CachedCameraDistance.cachedCameraDistanceMax;
                __instance.m_minDistance = CachedCameraDistance.cachedCameraDistanceMin;
            }
        }
    }
}

/// <summary>
/// Hooks for ABM and AEM
/// </summary>
/// 
[HarmonyPatch(typeof(Player), nameof(Player.Update))]
public static class Player_Update_Patch
{
    private static GameObject timeObj = null;
    private static double savedEnvMinutes = -1;

    private static void Postfix(ref Player __instance, ref Vector3 ___m_moveDir, ref Vector3 ___m_lookDir,
        ref GameObject ___m_placementGhost, Transform ___m_eye)
    {
        if (!__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;

        if (PerfectPlacementPlugin.aemIsEnabled.Value == PerfectPlacementPlugin.Toggle.On)
        {
            AEM.PlayerInstance = __instance;
            AEM.run();
        }

        if (PerfectPlacementPlugin.abmIsEnabled.Value == PerfectPlacementPlugin.Toggle.On)
        {
            ABM.Run(ref __instance);
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
public static class Player_UpdatePlacementGhost_Transpile
{
    private static MethodInfo method_Quaternion_Euler = AccessTools.Method(typeof(Quaternion), nameof(Quaternion.Euler), new Type[] { typeof(float), typeof(float), typeof(float) });
    private static MethodInfo method_GetRotation = AccessTools.Method(typeof(Player_UpdatePlacementGhost_Transpile), nameof(Player_UpdatePlacementGhost_Transpile.GetRotation));

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        if (PerfectPlacementPlugin.fpmIsEnabled.Value == PerfectPlacementPlugin.Toggle.Off) return instructions;

        List<CodeInstruction> il = instructions.ToList();

        for (int i = 0; i < il.Count; ++i)
        {
            if (il[i].Calls(method_Quaternion_Euler))
            {
                // remove direct call to Quaternion.Euler and replace with function call to switch
                il[i - 1] = new CodeInstruction(OpCodes.Ldarg_0);
                il[i] = new CodeInstruction(OpCodes.Call, method_GetRotation);
                il.RemoveRange(i - 8, 7);

                break;
            }
        }

        return il.AsEnumerable();
    }

    public static Quaternion GetRotation(Player __instance)
    {
        if (ABM.IsInAbmMode())
        {
            return Quaternion.Euler(0f, __instance.m_placeRotationDegrees * __instance.m_placeRotation, 0f);
        }

        Vector3 rotation = PerfectPlacementPlugin.PlayersData.TryGetValue(__instance, out PerfectPlacementPlugin.PlayerData? value)
            ? value.PlaceRotation
            : __instance.m_placeRotation * 22.5f * Vector3.up;

        return Quaternion.Euler(rotation);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
public static class ModifyPUpdatePlacement
{
    private static void Postfix(Player __instance, bool takeInput, float dt)
    {
        if (PerfectPlacementPlugin.fpmIsEnabled.Value == PerfectPlacementPlugin.Toggle.Off)
            return;

        if (ABM.IsInAbmMode())
            return;

        if (!__instance.InPlaceMode())
            return;

        if (!takeInput)
            return;

        if (Hud.IsPieceSelectionVisible())
            return;

        if (!PerfectPlacementPlugin.PlayersData.ContainsKey(__instance))
            PerfectPlacementPlugin.PlayersData[__instance] = new PerfectPlacementPlugin.PlayerData();

        RotateWithWheel(__instance);
        SyncRotationWithTargetInFront(__instance, PerfectPlacementPlugin.fpmcopyRotationParallel.Value, perpendicular: false);
        SyncRotationWithTargetInFront(__instance, PerfectPlacementPlugin.fpmcopyRotationPerpendicular.Value, perpendicular: true);
    }

    private static void RotateWithWheel(Player __instance)
    {
        float wheel = Input.GetAxis("Mouse ScrollWheel");

        PerfectPlacementPlugin.PlayerData? playerData = PerfectPlacementPlugin.PlayersData[__instance];

        if (!wheel.Equals(0f) || ZInput.GetButton("JoyRotate"))
        {
            if (Input.GetKey(PerfectPlacementPlugin.fpmrotateY.Value))
            {
                playerData.PlaceRotation += Vector3.up * Mathf.Sign(wheel);
                __instance.m_placeRotation = (int)(playerData.PlaceRotation.y / 22.5f);
            }
            else if (Input.GetKey(PerfectPlacementPlugin.fpmrotateX.Value))
            {
                playerData.PlaceRotation += Vector3.right * Mathf.Sign(wheel);
            }
            else if (Input.GetKey(PerfectPlacementPlugin.fpmrotateZ.Value))
            {
                playerData.PlaceRotation += Vector3.forward * Mathf.Sign(wheel);
            }
            else
            {
                __instance.m_placeRotation = Util.ClampPlaceRotation(__instance.m_placeRotation);
                playerData.PlaceRotation = new Vector3(0, __instance.m_placeRotation * 22.5f, 0);
            }

            playerData.PlaceRotation = Util.ClampAngles(playerData.PlaceRotation);

            //PerfectPlacement.PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("Angle " + playerData.PlaceRotation);
        }
    }

    private static void SyncRotationWithTargetInFront(Player __instance, KeyCode keyCode, bool perpendicular)
    {
        if (__instance.m_placementGhost == null)
            return;

        if (!Input.GetKeyUp(keyCode)) return;
        Piece piece;
        if (!__instance.PieceRayTest(out Vector3 _, out Vector3 _, out piece, out Heightmap _, out Collider _, false) || piece == null) return;
        PerfectPlacementPlugin.PlayerData? playerData = PerfectPlacementPlugin.PlayersData[__instance];

        Quaternion rotation = piece.transform.rotation;
        if (perpendicular)
            rotation *= Quaternion.Euler(0, 90, 0);

        if (playerData.LastKeyCode != keyCode || playerData.LastPiece != piece)
            playerData.Opposite = false;

        playerData.LastKeyCode = keyCode;
        playerData.LastPiece = piece;

        if (playerData.Opposite)
            rotation *= Quaternion.Euler(0, 180, 0);

        playerData.Opposite = !playerData.Opposite;

        playerData.PlaceRotation = rotation.eulerAngles;
        PerfectPlacement.PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("Sync Angle " + playerData.PlaceRotation);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
public static class Player_UpdatePlacementGhost_Patch
{
    private static Vector3? ghostPosition = null;
    private static Quaternion? ghostRotation = null;
    private static Vector3? markerPosition = null;
    private static Quaternion? markerRotation = null;

    private static void Prefix(ref Player __instance, bool flashGuardStone)
    {
        if (ABM.IsInAbmMode())
        {
            // ABM controls the ghost/marker position, so undo any ghost/marker changes the patched method
            // does by storing the transforms in the prefix and then applying them in the postfix.
            if (__instance.m_placementGhost)
            {
                ghostPosition = __instance.m_placementGhost.transform.position + Vector3.zero;
                ghostRotation = __instance.m_placementGhost.transform.rotation * Quaternion.identity;
            }

            if (__instance.m_placementMarkerInstance)
            {
                markerPosition = __instance.m_placementMarkerInstance.transform.position + Vector3.zero;
                markerRotation = __instance.m_placementMarkerInstance.transform.rotation * Quaternion.identity;
            }
        }
    }

    private static void Postfix(ref Player __instance)
    {
        if (ABM.IsInAbmMode())
        {
            __instance.m_placementGhost.transform.position = (Vector3)ghostPosition;
            __instance.m_placementGhost.transform.rotation = (Quaternion)ghostRotation;
            ghostPosition = null;
            ghostRotation = null;
            __instance.m_placementMarkerInstance.transform.position = (Vector3)markerPosition;
            __instance.m_placementMarkerInstance.transform.rotation = (Quaternion)markerRotation;
            markerPosition = null;
            markerRotation = null;

            if (__instance.m_placementMarkerInstance)
            {
                __instance.m_placementMarkerInstance.SetActive(false);
            }
        }


        if (ABM.exitOnNextIteration)
        {
            try
            {
                if (__instance.m_placementMarkerInstance)
                {
                    __instance.m_placementMarkerInstance.SetActive(false);
                }
            }
            catch
            {
                // ignored
            }
        }

        if (PerfectPlacementPlugin.gridAlignmentEnabled.Value == PerfectPlacementPlugin.Toggle.On)
        {
            if (GridAlignment.AlignPressed ^ GridAlignment.AlignToggled)
                GridAlignment.UpdatePlacementGhost(__instance);
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.Update))]
public static class GridAlignment
{
    public static int DefaultAlignment = 100;
    public static bool AlignPressed = false;
    public static bool AlignToggled = false;

    private static void Postfix(ref Player __instance)
    {
        if (!__instance.IsPlayer())
            return;

        if (PerfectPlacementPlugin.gridAlignmentEnabled.Value == PerfectPlacementPlugin.Toggle.Off)
            return;

        if (Input.GetKeyDown(PerfectPlacementPlugin.alignToGrid.Value))
            AlignPressed = true;
        if (Input.GetKeyUp(PerfectPlacementPlugin.alignToGrid.Value))
            AlignPressed = false;

        if (Input.GetKeyDown(PerfectPlacementPlugin.changeDefaultAlignment.Value))
        {
            if (DefaultAlignment == 50)
                DefaultAlignment = 100;
            else if (DefaultAlignment == 100)
                DefaultAlignment = 200;
            else if (DefaultAlignment == 200)
                DefaultAlignment = 400;
            else
                DefaultAlignment = 50;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Default grid alignment set to " + (DefaultAlignment / 100f));
        }

        if (Input.GetKeyDown(PerfectPlacementPlugin.alignToggle.Value))
        {
            AlignToggled ^= true;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Grid alignment by default " + (AlignToggled ? "enabled" : "disabled"));
        }
    }

    static float FixAlignment(float f)
    {
        int i = (int)Mathf.Round(f * 100f);
        if (i <= 0)
            return DefaultAlignment / 100f;
        if (i <= 50)
            return 0.5f;
        if (i <= 100)
            return 1f;
        if (i <= 200)
            return 2f;
        return 4f;
    }

    public static void GetAlignment(Piece piece, out Vector3 alignment, out Vector3 offset)
    {
        List<Transform>? points = new System.Collections.Generic.List<Transform>();
        piece.GetSnapPoints(points);
        if (points.Count != 0)
        {
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            foreach (Transform? point in points)
            {
                Vector3 pos = point.localPosition;
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }

            alignment = max - min;
            alignment.x = FixAlignment(alignment.x);
            alignment.y = FixAlignment(alignment.y);
            alignment.z = FixAlignment(alignment.z);
            // Align at top
            offset = max;
            if (piece.name is "iron_grate" or "wood_gate")
            {
                // Align at bottom, not top
                offset.y = min.y;
            }

            if (piece.name == "wood_gate")
            {
                alignment.x = 4;
            }
        }
        else
        {
            if (piece.m_notOnFloor || piece.name == "sign" || piece.name == "itemstand")
            {
                alignment = new Vector3(0.5f, 0.5f, 0);
                offset = new Vector3(0, 0, 0);
                if (piece.name == "sign")
                    alignment.y = 0.25f;
            }
            else if (piece.name == "piece_walltorch")
            {
                alignment = new Vector3(0, 0.5f, 0.5f);
                offset = new Vector3(0, 0, 0);
            }
            else
            {
                alignment = new Vector3(0.5f, 0, 0.5f);
                offset = new Vector3(0, 0, 0);
            }
        }
    }

    public static float Align(float value, out float alpha)
    {
        float result = Mathf.Round(value);
        alpha = value - result;
        return result;
    }


    public static void UpdatePlacementGhost(Player player)
    {
        if (player.m_placementGhost == null || !player.IsPlayer())
            return;

        if (ABM.IsInAbmMode())
            return;

        bool altMode = ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace");

        Piece? piece = player.m_placementGhost.GetComponent<Piece>();

        Vector3 newVal = piece.transform.position;
        newVal = Quaternion.Inverse(piece.transform.rotation) * newVal;

        Vector3 alignment;
        Vector3 offset;
        GetAlignment(piece, out alignment, out offset);
        newVal += offset;
        Vector3 copy = newVal;
        newVal = new Vector3(newVal.x / alignment.x, newVal.y / alignment.y, newVal.z / alignment.z);
        float alphaX, alphaY, alphaZ;
        newVal = new UnityEngine.Vector3(Align(newVal.x, out alphaX), Align(newVal.y, out alphaY), Align(newVal.z, out alphaZ));
        if (altMode)
        {
            float alphaMin = 0.2f;
            if (Mathf.Abs(alphaX) >= alphaMin && Mathf.Abs(alphaX) >= Mathf.Abs(alphaY) && Mathf.Abs(alphaX) >= Mathf.Abs(alphaZ))
                newVal.x += Mathf.Sign(alphaX);
            else if (Mathf.Abs(alphaY) >= alphaMin && Mathf.Abs(alphaY) >= Mathf.Abs(alphaZ))
                newVal.y += Mathf.Sign(alphaY);
            else if (Mathf.Abs(alphaZ) >= alphaMin)
                newVal.z += Mathf.Sign(alphaZ);
        }

        newVal = new Vector3(newVal.x * alignment.x, newVal.y * alignment.y, newVal.z * alignment.z);
        if (alignment.x <= 0)
            newVal.x = copy.x;
        if (alignment.y <= 0)
            newVal.y = copy.y;
        if (alignment.z <= 0)
            newVal.z = copy.z;
        newVal -= offset;

        newVal = piece.transform.rotation * newVal;
        piece.transform.position = newVal;
    }
}