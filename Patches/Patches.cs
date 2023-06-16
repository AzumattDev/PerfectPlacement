using HarmonyLib;
using PerfectPlacement.Patches.Compatibility.WardIsLove;
using PerfectPlacement;
using UnityEngine;

namespace PerfectPlacement.Patches;

/// <summary>
/// Advanced Editing Mode Game Camera changes
/// </summary>
[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
public static class BlockCameraScrollInAEM
{
    private static void Prefix(GameCamera __instance)
    {
        if (AEM.isActive)
        {
            __instance.m_maxDistance = __instance.m_distance;
            __instance.m_minDistance = __instance.m_distance;
        }
        else
        {
            /*if (Configuration.Current.Camera.IsEnabled)
            {
                if (Configuration.Current.Camera.cameraMaximumZoomDistance >= 1 && Configuration.Current.Camera.cameraMaximumZoomDistance <= 100)
                    __instance.m_maxDistance = Configuration.Current.Camera.cameraMaximumZoomDistance;
                if (Configuration.Current.Camera.cameraBoatMaximumZoomDistance >= 1 && Configuration.Current.Camera.cameraBoatMaximumZoomDistance <= 100)
                    __instance.m_maxDistanceBoat = Configuration.Current.Camera.cameraBoatMaximumZoomDistance;
                if (Configuration.Current.Camera.cameraFOV >= 1 && Configuration.Current.Camera.cameraFOV <= 140)
                    __instance.m_fov = Configuration.Current.Camera.cameraFOV;

                __instance.m_minDistance = 1;
            }
            else
            {*/
            __instance.m_maxDistance = 6;
            //__instance.m_minDistance = 2f;
            /*}*/
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

[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
public static class ModifyPUpdatePlacement
{
    private static void Postfix(Player __instance, bool takeInput, float dt)
    {
        if (PerfectPlacementPlugin.fpmIsEnabled.Value == PerfectPlacementPlugin.Toggle.Off)
            return;

        if (ABM.isActive)
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
        SyncRotationWithTargetInFront(__instance, PerfectPlacementPlugin.fpmcopyRotationParallel.Value,
            false);
        SyncRotationWithTargetInFront(__instance, PerfectPlacementPlugin.fpmcopyRotationPerpendicular.Value,
            true);
    }

    private static void RotateWithWheel(Player __instance)
    {
        var wheel = Input.GetAxis("Mouse ScrollWheel");

        var playerData = PerfectPlacementPlugin.PlayersData[__instance];

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

            Debug.Log("Angle " + playerData.PlaceRotation);
        }
    }

    private static void SyncRotationWithTargetInFront(Player __instance, KeyCode keyCode, bool perpendicular)
    {
        if (__instance.m_placementGhost == null)
            return;

        if (Input.GetKeyUp(keyCode))
        {
            Vector3 point;
            Vector3 normal;
            Piece piece;
            Heightmap heightmap;
            Collider waterSurface;
            if (__instance.PieceRayTest(out point, out normal, out piece, out heightmap, out waterSurface,
                    false) && piece != null)
            {
                var playerData = PerfectPlacementPlugin.PlayersData[__instance];

                var rotation = piece.transform.rotation;
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
                Debug.Log("Sync Angle " + playerData.PlaceRotation);
            }
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
public static class ModifyPlacingRestrictionOfGhost
{
    private static void Postfix(Player __instance, bool flashGuardStone)
    {
        if (PerfectPlacementPlugin.fpmIsEnabled.Value == PerfectPlacementPlugin.Toggle.Off)
            return;

        if (ABM.isActive)
            return;

        UpdatePlacementGhost(__instance, flashGuardStone);
    }

    // almost copy of original UpdatePlacementGhost with modified calculation of Quaternion quaternion = Quaternion.Euler(rotation);
    // need to be re-calculated in Postfix for correct work of auto-attachment of placementGhost after change rotation
    private static void UpdatePlacementGhost(Player __instance, bool flashGuardStone)
    {
        if (__instance.m_placementGhost == null)
        {
            if (!(bool)(Object)__instance.m_placementMarkerInstance)
                return;
            __instance.m_placementMarkerInstance.SetActive(false);
        }
        else
        {
            bool flag = ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace");
            Piece component1 = __instance.m_placementGhost.GetComponent<Piece>();
            bool water = component1.m_waterPiece || component1.m_noInWater;
            Vector3 point;
            Vector3 normal;
            Piece piece;
            Heightmap heightmap;
            Collider waterSurface;
            if (__instance.PieceRayTest(out point, out normal, out piece, out heightmap, out waterSurface, water))
            {
                __instance.m_placementStatus = Player.PlacementStatus.Valid;
                if (__instance.m_placementMarkerInstance == null)
                    __instance.m_placementMarkerInstance =
                        Object.Instantiate(__instance.m_placeMarker, point,
                            Quaternion.identity);
                __instance.m_placementMarkerInstance.SetActive(true);
                __instance.m_placementMarkerInstance.transform.position = point;
                __instance.m_placementMarkerInstance.transform.rotation = Quaternion.LookRotation(normal);
                if (component1.m_groundOnly || component1.m_groundPiece || component1.m_cultivatedGroundOnly)
                    __instance.m_placementMarkerInstance.SetActive(false);
                WearNTear wearNtear = piece != null
                    ? piece.GetComponent<WearNTear>()
                    : null;
                StationExtension component2 = component1.GetComponent<StationExtension>();
                if (component2 != null)
                {
                    CraftingStation closestStationInRange = component2.FindClosestStationInRange(point);
                    if ((bool)(Object)closestStationInRange)
                    {
                        component2.StartConnectionEffect(closestStationInRange);
                    }
                    else
                    {
                        component2.StopConnectionEffect();
                        __instance.m_placementStatus = Player.PlacementStatus.ExtensionMissingStation;
                    }

                    if (component2.OtherExtensionInRange(component1.m_spaceRequirement))
                        __instance.m_placementStatus = Player.PlacementStatus.MoreSpace;
                }

                if (wearNtear && !wearNtear.m_supports)
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_waterPiece && waterSurface == null &&
                    !flag)
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_noInWater && (Object)waterSurface != null)
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_groundPiece && heightmap == null)
                {
                    __instance.m_placementGhost.SetActive(false);
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                    return;
                }

                if (component1.m_groundOnly && heightmap == null)
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_cultivatedGroundOnly &&
                    ((Object)heightmap == null ||
                     !heightmap.IsCultivated(point)))
                    __instance.m_placementStatus = Player.PlacementStatus.NeedCultivated;
                if (component1.m_notOnWood && (bool)(Object)piece &&
                    (bool)(Object)wearNtear &&
                    (wearNtear.m_materialType == WearNTear.MaterialType.Wood ||
                     wearNtear.m_materialType == WearNTear.MaterialType.HardWood))
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_notOnTiltingSurface && normal.y < 0.800000011920929)
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_inCeilingOnly && normal.y > -0.5)
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_notOnFloor && normal.y > 0.100000001490116)
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                if (component1.m_onlyInTeleportArea &&
                    !(bool)(Object)EffectArea.IsPointInsideArea(point, EffectArea.Type.Teleport))
                    __instance.m_placementStatus = Player.PlacementStatus.NoTeleportArea;
                if (!component1.m_allowedInDungeons && __instance.InInterior())
                    __instance.m_placementStatus = Player.PlacementStatus.NotInDungeon;
                if ((bool)(Object)heightmap)
                    normal = Vector3.up;
                __instance.m_placementGhost.SetActive(true);

                var rotation = PerfectPlacementPlugin.PlayersData.ContainsKey(__instance)
                    ? PerfectPlacementPlugin.PlayersData[__instance].PlaceRotation
                    : __instance.m_placeRotation * 22.5f * Vector3.up;

                Quaternion quaternion = Quaternion.Euler(rotation);

                if ((component1.m_groundPiece || component1.m_clipGround) &&
                    (bool)(Object)heightmap || component1.m_clipEverything)
                {
                    if ((bool)(Object)__instance.m_buildPieces.GetSelectedPrefab()
                            .GetComponent<TerrainModifier>() && component1.m_allowAltGroundPlacement &&
                        (component1.m_groundPiece && !ZInput.GetButton("AltPlace")) &&
                        !ZInput.GetButton("JoyAltPlace"))
                    {
                        float groundHeight = ZoneSystem.instance.GetGroundHeight(__instance.transform.position);
                        point.y = groundHeight;
                    }

                    __instance.m_placementGhost.transform.position = point;
                    __instance.m_placementGhost.transform.rotation = quaternion;
                }
                else
                {
                    Collider[] componentsInChildren = __instance.m_placementGhost.GetComponentsInChildren<Collider>();
                    if (componentsInChildren.Length != 0)
                    {
                        __instance.m_placementGhost.transform.position = point + normal * 50f;
                        __instance.m_placementGhost.transform.rotation = quaternion;
                        Vector3 vector3_1 = Vector3.zero;
                        float num1 = 999999f;
                        foreach (Collider collider in componentsInChildren)
                        {
                            if (!collider.isTrigger && collider.enabled)
                            {
                                MeshCollider meshCollider = collider as MeshCollider;
                                if (!((Object)meshCollider != null) ||
                                    meshCollider.convex)
                                {
                                    Vector3 a = collider.ClosestPoint(point);
                                    float num2 = Vector3.Distance(a, point);
                                    if (num2 < (double)num1)
                                    {
                                        vector3_1 = a;
                                        num1 = num2;
                                    }
                                }
                            }
                        }

                        Vector3 vector3_2 = __instance.m_placementGhost.transform.position - vector3_1;
                        if (component1.m_waterPiece)
                            vector3_2.y = 3f;
                        __instance.m_placementGhost.transform.position = point + vector3_2;
                        __instance.m_placementGhost.transform.rotation = quaternion;
                    }
                }

                if (!flag)
                {
                    __instance.m_tempPieces.Clear();
                    Transform a;
                    Transform b;
                    if (__instance.FindClosestSnapPoints(__instance.m_placementGhost.transform, 0.5f, out a, out b,
                            __instance.m_tempPieces))
                    {
                        Vector3 position = b.parent.position;
                        Vector3 p = b.position - (a.position - __instance.m_placementGhost.transform.position);
                        if (!__instance.IsOverlappingOtherPiece(p, __instance.m_placementGhost.transform.rotation,
                                __instance.m_placementGhost.name, __instance.m_tempPieces,
                                component1.m_allowRotatedOverlap))
                            __instance.m_placementGhost.transform.position = p;
                    }
                }

                if (Location.IsInsideNoBuildLocation(__instance.m_placementGhost.transform.position))
                    __instance.m_placementStatus = Player.PlacementStatus.NoBuildZone;
                if (!PrivateArea.CheckAccess(__instance.m_placementGhost.transform.position,
                        (bool)(Object)component1.GetComponent<PrivateArea>()
                            ? component1.GetComponent<PrivateArea>().m_radius
                            : 0.0f, flashGuardStone))
                    __instance.m_placementStatus = Player.PlacementStatus.PrivateZone;
                if (WardIsLovePlugin.IsLoaded())
                {
                    if (WardIsLovePlugin.WardEnabled().Value &&
                        WardMonoscript.CheckInWardMonoscript(__instance.m_placementGhost.transform.position))
                    {
                        var ward = WardMonoscriptExt.GetWardMonoscript(__instance.m_placementGhost.transform.position);
                        if (ward != null)
                        {
                            if (!WardMonoscript.CheckAccess(__instance.m_placementGhost.transform.position, ward.GetWardRadius(), flashGuardStone))
                                __instance.m_placementStatus = Player.PlacementStatus.PrivateZone;
                        }
                    }
                }

                if (__instance.CheckPlacementGhostVSPlayers())
                    __instance.m_placementStatus = Player.PlacementStatus.BlockedbyPlayer;
                if (component1.m_onlyInBiome != Heightmap.Biome.None &&
                    (Heightmap.FindBiome(__instance.m_placementGhost.transform.position) &
                     component1.m_onlyInBiome) == Heightmap.Biome.None)
                    __instance.m_placementStatus = Player.PlacementStatus.WrongBiome;
                if (component1.m_noClipping && __instance.TestGhostClipping(__instance.m_placementGhost, 0.2f))
                    __instance.m_placementStatus = Player.PlacementStatus.Invalid;
            }
            else
            {
                if ((bool)(Object)__instance.m_placementMarkerInstance)
                    __instance.m_placementMarkerInstance.SetActive(false);
                __instance.m_placementGhost.SetActive(false);
                __instance.m_placementStatus = Player.PlacementStatus.Invalid;
            }

            __instance.SetPlacementGhostValid(__instance.m_placementStatus == Player.PlacementStatus.Valid);
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
public static class Player_UpdatePlacementGhost_Patch
{
    private static bool Prefix(ref Player __instance, bool flashGuardStone)
    {
        if (ABM.isActive)
        {
            // Skip the original method
            return false;
        }

        return true;
    }

    private static void Postfix(ref Player __instance)
    {
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
            var points = new System.Collections.Generic.List<Transform>();
            piece.GetSnapPoints(points);
            if (points.Count != 0)
            {
                Vector3 min = Vector3.positiveInfinity;
                Vector3 max = Vector3.negativeInfinity;
                foreach (var point in points)
                {
                    var pos = point.localPosition;
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

            if (ABM.isActive)
                return;

            bool altMode = ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace");

            var piece = player.m_placementGhost.GetComponent<Piece>();

            var newVal = piece.transform.position;
            newVal = Quaternion.Inverse(piece.transform.rotation) * newVal;

            Vector3 alignment;
            Vector3 offset;
            GetAlignment(piece, out alignment, out offset);
            newVal += offset;
            var copy = newVal;
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