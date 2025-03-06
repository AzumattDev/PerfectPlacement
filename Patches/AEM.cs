using System;
using HarmonyLib;
using PerfectPlacement.Patches.Compatibility.WardIsLove;
using PerfectPlacement.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PerfectPlacement.Patches;

internal class AEM
{
    // Status
    public static bool isActive;

    // Player Instance
    public static Player PlayerInstance;

    // Control Flags
    static bool controlFlag;
    static bool shiftFlag;
    static bool altFlag;

    // Hit Object Data
    public static Vector3 HitPoint;
    public static Vector3 HitNormal;
    public static Piece HitPiece;
    public static GameObject HitObject;
    public static Heightmap HitHeightmap;

    private static Quaternion InitialRotation;
    private static Vector3 InitialPosition;

    private static bool isInExistence;

    // Modification Speeds
    const float BASE_TRANSLATION_DISTANCE = (float)0.1; // 1/10th of a 1m pole
    const float BASE_ROTATION_ANGLE_DEGREES = 3;

    static float currentModificationSpeed = 1;
    const float MIN_MODIFICATION_SPEED = 1;
    const float MAX_MODIFICATION_SPEED = 30;

    // Save and Load object rotation
    static Quaternion savedRotation;

    // Executing the raycast to find the object
    public static bool ExecuteRayCast(Player playerInstance)
    {
        int layerMask = playerInstance.m_placeRayMask;

        if (Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, out RaycastHit raycastHit, 50f, layerMask) &&
            raycastHit.collider &&
            !raycastHit.collider.attachedRigidbody &&
            Vector3.Distance(Util.getPlayerCharacter(playerInstance).m_eye.position, raycastHit.point) < playerInstance.m_maxPlaceDistance)
        {
            HitPoint = raycastHit.point;
            HitNormal = raycastHit.normal;
            HitPiece = raycastHit.collider.GetComponentInParent<Piece>();
            HitObject = raycastHit.collider.gameObject;
            HitHeightmap = raycastHit.collider.GetComponent<Heightmap>();
            InitialRotation = HitPiece.transform.rotation;
            InitialPosition = HitPiece.transform.position;

            return isValidRayCastTarget();
        }

        resetObjectInfo();
        return false;
    }

    // Exiting variables
    public static bool forceExitNextIteration;


    // Initializing class
    public static bool checkForObject()
    {
        if (PlayerInstance == null)
        {
            return false;
        }

        if (!ExecuteRayCast(PlayerInstance))
        {
            return false;
        }

        return true;
    }

    public static void run()
    {
        // ADD ZNET ERROR HANDLING AND REMOVE OBJECT IF

        // force exit
        if (forceExitNextIteration)
        {
            forceExitNextIteration = false;
            resetObjectInfo();
            isActive = false;
            return;
        }


        // CHECK FOR BUILD MODE
        if (isInBuildMode())
        {
            if (IsInAemMode())
            {
                exitMode();
                resetObjectTransform();
            }

            return;
        }

        // CHECK FOR ABM
        if (ABM.IsInAbmMode())
        {
            if (IsInAemMode())
            {
                exitMode();
                resetObjectTransform();
            }

            return;
        }

        if (!IsInAemMode())
        {
            if (Input.GetKeyDown(PerfectPlacementPlugin.aementerAdvancedEditingMode.Value))
            {
                if (checkForObject())
                    startMode();
                return;
            }
        }

        if (Input.GetKeyDown(PerfectPlacementPlugin.aemabortAndExitAdvancedEditingMode.Value))
        {
            exitMode();
            resetObjectTransform();
        }

        if (IsInAemMode())
        {
            // If object is not in existence anymore
            if (hitPieceStillExists())
            {
                // Try to prevent znet error, relatively untested yet if this is any solution.
                // ghetto solution, will be improved in future version if it proofs to be effective.
                try
                {
                    ZNetView component1 = HitPiece.GetComponent<ZNetView>();
                    if (component1 == null)
                    {
                        PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("AEM: Error, network object empty. Code: 2.");
                        exitMode();
                        return;
                    }
                }
                catch
                {
                    PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("AEM: Error, network object empty. Code: 3.");
                    exitMode();
                }

                if (PerfectPlacementPlugin.cachedModWorldText != null)
                {
                    PerfectPlacementPlugin.cachedModWorldText.m_timer = 5f;
                }

                listenToHotKeysAndDoWork();
            }
            else
            {
                exitMode();
            }
        }
    }

    private static void listenToHotKeysAndDoWork()
    {
        float rX = 0;
        float rZ = 0;
        float rY = 0;

        PerfectPlacementPlugin.UpdateKeyBindings();

        if (Input.GetKeyDown(PerfectPlacementPlugin.aemresetAdvancedEditingMode.Value))
        {
            resetObjectTransform();
        }

        if (Input.GetKeyDown(PerfectPlacementPlugin.aemconfirmPlacementOfAdvancedEditingMode.Value))
        {
            // Instantiate the new game object based on HitPiece
            GameObject gameObject2 = Object.Instantiate(HitPiece.gameObject, HitPiece.transform.position, HitPiece.transform.rotation);
            gameObject2.name = HitPiece.gameObject.name;
            HitPiece.m_placeEffect.Create(HitPiece.transform.position, HitPiece.transform.rotation, gameObject2.transform, 1f);

            // Retrieve the old ZDO and its UID
            ZDO oldZdo = HitPiece.m_nview.GetZDO();
            if (oldZdo == null)
            {
                PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("AEM: Error, network object empty. Old zdo was null.");
                resetObjectTransform();
                exitMode();
                return;
            }

            ZDOID oldZdoID = oldZdo.m_uid;

            // Get the new ZDO from the newly instantiated object
            ZNetView newZNetView = gameObject2.GetComponent<ZNetView>();
            if (newZNetView == null)
            {
                PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("AEM: Error, network object empty. New ZNetView component was null.");
                resetObjectTransform();
                exitMode();
                return;
            }

            ZDO newZdo = newZNetView.GetZDO();
            if (newZdo == null)
            {
                PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("AEM: Error, network object empty. New zdo was null.");
                resetObjectTransform();
                exitMode();
                return;
            }

            // Deep clone each of the ZDOExtraData dictionaries if they exist for the old ZDOID.
            if (ZDOExtraData.s_floats.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_floats[newZdo.m_uid] = DeepCloneDictionary(ZDOExtraData.s_floats[oldZdoID]);
            }

            if (ZDOExtraData.s_vec3.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_vec3[newZdo.m_uid] = DeepCloneDictionary(ZDOExtraData.s_vec3[oldZdoID]);
            }

            if (ZDOExtraData.s_quats.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_quats[newZdo.m_uid] = DeepCloneDictionary(ZDOExtraData.s_quats[oldZdoID]);
            }

            if (ZDOExtraData.s_ints.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_ints[newZdo.m_uid] = DeepCloneDictionary(ZDOExtraData.s_ints[oldZdoID]);
            }

            if (ZDOExtraData.s_longs.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_longs[newZdo.m_uid] = DeepCloneDictionary(ZDOExtraData.s_longs[oldZdoID]);
            }

            if (ZDOExtraData.s_strings.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_strings[newZdo.m_uid] = DeepCloneDictionary(ZDOExtraData.s_strings[oldZdoID]);
            }

            if (ZDOExtraData.s_byteArrays.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_byteArrays[newZdo.m_uid] = DeepCloneDictionary(ZDOExtraData.s_byteArrays[oldZdoID]);
            }

            // For connection-related data, shallow copies might be acceptable if they are immutable or if a deep clone isn’t necessary.
            if (ZDOExtraData.s_connections.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_connections[newZdo.m_uid] = ZDOExtraData.s_connections[oldZdoID];
            }

            if (ZDOExtraData.s_connectionsHashData.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_connectionsHashData[newZdo.m_uid] = ZDOExtraData.s_connectionsHashData[oldZdoID];
            }

            if (ZDOExtraData.s_owner.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_owner[newZdo.m_uid] = ZDOExtraData.s_owner[oldZdoID];
            }

            if (ZDOExtraData.s_tempTimeCreated.ContainsKey(oldZdoID))
            {
                ZDOExtraData.s_tempTimeCreated[newZdo.m_uid] = ZDOExtraData.s_tempTimeCreated[oldZdoID];
            }

            // Remove the old object from the network scene.
            ZNetView oldNetView = HitPiece.GetComponent<ZNetView>();
            if (oldNetView == null)
            {
                PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("AEM: Error, network object empty.");
                resetObjectTransform();
                exitMode();
                return;
            }

            oldNetView.ClaimOwnership();
            ZNetScene.instance.Destroy(HitPiece.gameObject);

            PerfectPlacementPlugin.PerfectPlacementLogger.LogInfo("AEM: Executed.");
            exitMode();
            return;
        }

        // CONTROL PRESSED
        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            controlFlag = true;
        }

        if (Input.GetKeyUp(KeyCode.LeftControl))
        {
            controlFlag = false;
        }

        // SHIFT PRESSED
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            shiftFlag = true;
        }

        if (Input.GetKeyUp(KeyCode.LeftShift))
        {
            shiftFlag = false;
        }

        // LEFT ALT PRESSED
        if (Input.GetKeyDown(KeyCode.LeftAlt))
        {
            altFlag = true;
        }

        if (Input.GetKeyUp(KeyCode.LeftAlt))
        {
            altFlag = false;
        }

        changeModificationSpeed();

        if (Input.GetKeyUp(PerfectPlacementPlugin.aemcopyObjectRotation.Value))
        {
            savedRotation = HitPiece.transform.rotation;
        }

        if (Input.GetKeyUp(PerfectPlacementPlugin.aempasteObjectRotation.Value))
        {
            HitPiece.transform.rotation = savedRotation;
        }

        // Maximum distance between player and placed piece
        if (Vector3.Distance(PlayerInstance.transform.position, HitPiece.transform.position) > PlayerInstance.m_maxPlaceDistance)
        {
            resetObjectTransform();
            exitMode();
        }

        float currentRotationAngleDegrees = BASE_ROTATION_ANGLE_DEGREES * currentModificationSpeed;
        if (Input.GetAxis("Mouse ScrollWheel") > 0f)
        {
            Quaternion rotation;
            if (controlFlag)
            {
                GizmoManager.CurrentAxis = Vector3.right;
                rX++;
                rotation = Quaternion.Euler(HitPiece.transform.eulerAngles.x + (currentRotationAngleDegrees * rX),
                    HitPiece.transform.eulerAngles.y, HitPiece.transform.eulerAngles.z); // forward to backwards
            }
            else if (altFlag)
            {
                GizmoManager.CurrentAxis = Vector3.forward;
                rZ++;
                rotation = Quaternion.Euler(HitPiece.transform.eulerAngles.x, HitPiece.transform.eulerAngles.y, HitPiece.transform.eulerAngles.z + (currentRotationAngleDegrees * rZ)); // diagonal
            }
            else
            {
                GizmoManager.CurrentAxis = Vector3.up;
                rY++;
                rotation = Quaternion.Euler(HitPiece.transform.eulerAngles.x, HitPiece.transform.eulerAngles.y + (currentRotationAngleDegrees * rY), HitPiece.transform.eulerAngles.z); // left<->right
            }

            HitPiece.transform.rotation = rotation;
        }

        if (Input.GetAxis("Mouse ScrollWheel") < 0f)
        {
            Quaternion rotation;
            if (controlFlag)
            {
                GizmoManager.CurrentAxis = Vector3.right;
                rX--;
                rotation = Quaternion.Euler(HitPiece.transform.eulerAngles.x + (currentRotationAngleDegrees * rX), HitPiece.transform.eulerAngles.y, HitPiece.transform.eulerAngles.z); // forward to backwards
            }
            else if (altFlag)
            {
                GizmoManager.CurrentAxis = Vector3.forward;
                rZ--;
                rotation = Quaternion.Euler(HitPiece.transform.eulerAngles.x, HitPiece.transform.eulerAngles.y, HitPiece.transform.eulerAngles.z + (currentRotationAngleDegrees * rZ)); // diagonal
            }
            else
            {
                GizmoManager.CurrentAxis = Vector3.up;
                rY--;
                rotation = Quaternion.Euler(HitPiece.transform.eulerAngles.x, HitPiece.transform.eulerAngles.y + (currentRotationAngleDegrees * rY), HitPiece.transform.eulerAngles.z); // left<->right
            }

            HitPiece.transform.rotation = rotation;
        }

        float currentTranslationDistance = BASE_TRANSLATION_DISTANCE * currentModificationSpeed;
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            if (controlFlag)
            {
                HitPiece.transform.Translate(Vector3.up * currentTranslationDistance);
            }
            else
            {
                HitPiece.transform.Translate(Vector3.forward * currentTranslationDistance);
            }
        }

        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            if (controlFlag)
            {
                HitPiece.transform.Translate(Vector3.down * currentTranslationDistance);
            }
            else
            {
                HitPiece.transform.Translate(Vector3.back * currentTranslationDistance);
            }
        }

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            HitPiece.transform.Translate(Vector3.left * currentTranslationDistance);
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            HitPiece.transform.Translate(Vector3.right * currentTranslationDistance);
        }
    }

    /// <summary>
    /// Creates a deep clone of a BinarySearchDictionary by allocating new arrays for the keys and values.
    /// </summary>
    private static BinarySearchDictionary<TKey, TValue> DeepCloneDictionary<TKey, TValue>(BinarySearchDictionary<TKey, TValue> original)
        where TKey : IComparable<TKey>
    {
        BinarySearchDictionary<TKey, TValue> clone = new BinarySearchDictionary<TKey, TValue>
        {
            Capacity = original.Capacity,
            m_length = original.m_length,
            m_keys = (TKey[])original.m_keys.Clone(),
            m_values = (TValue[])original.m_values.Clone()
        };
        return clone;
    }

    // Hit Piece still is a valid target
    private static bool hitPieceStillExists()
    {
        try
        {
            // check to see if the hit object still exists
            if (IsInAemMode())
            {
                isInExistence = true;
            }
        }
        catch
        {
            isInExistence = false;
        }

        return isInExistence;
    }

    // Check for access to object
    private static bool isValidRayCastTarget()
    {
        bool hitValid = true;

        if (HitPiece.m_onlyInTeleportArea && !EffectArea.IsPointInsideArea(HitPiece.transform.position, EffectArea.Type.Teleport, 0f))
        {
            // Not in Teleport Area
            hitValid = false;
        }

        if (!HitPiece.m_allowedInDungeons && (HitPiece.transform.position.y > 3000f))
        {
            // Not in dungeon
            hitValid = false;
        }

        if (Location.IsInsideNoBuildLocation(HitPiece.transform.position))
        {
            // No build zone
            hitValid = false;
        }

        float radius = HitPiece.GetComponent<PrivateArea>() ? HitPiece.GetComponent<PrivateArea>().m_radius : 0f;
        if (!PrivateArea.CheckAccess(HitPiece.transform.position, radius, true))
        {
            // private zone
            hitValid = false;
        }

        if (WardIsLovePlugin.IsLoaded())
        {
            if (WardIsLovePlugin.WardEnabled().Value &&
                WardMonoscript.CheckInWardMonoscript(HitPiece.transform.position))
            {
                if (!WardMonoscript.CheckAccess(HitPiece.transform.position, radius, true))
                {
                    // private zone
                    hitValid = false;
                }
            }
        }

        return hitValid;
    }

    // Check if user is in build mode
    private static bool isInBuildMode()
    {
        return PlayerInstance.InPlaceMode();
    }

    public static bool IsInAemMode()
    {
        return isActive;
    }

    private static void resetObjectTransform()
    {
        if (HitPiece == null) return;
        notifyUser("Object has been reset to initial position & rotation.");
        HitPiece.transform.position = InitialPosition;
        HitPiece.transform.rotation = InitialRotation;
    }

    private static void resetObjectInfo()
    {
        HitPoint = Vector3.zero;
        HitNormal = Vector3.zero;
        HitObject = null;
        HitPiece = null;
        HitHeightmap = null;
        InitialRotation = new Quaternion();
        InitialPosition = new Vector3();
    }

    private static void startMode()
    {
        notifyUser("Entering AEM");
        isActive = true;
        KeyBindingOverlay.ToggleOverlay(true);
        Chat.instance.AddInworldText(
            PlayerInstance.gameObject,
            (long)PerfectPlacementPlugin.ModName.GetStableHashCode(),
            PlayerInstance.GetHeadPoint(),
            Talker.Type.Normal,
            UserInfo.GetLocalUser(),
            Localization.instance.Localize("<color=yellow>AEM is active</color>")
        );

        // Immediately find the world text we just created and cache it
        Chat.WorldTextInstance? abmText = Chat.instance.FindExistingWorldText((long)PerfectPlacementPlugin.ModName.GetStableHashCode());
        if (abmText != null)
        {
            PerfectPlacementPlugin.cachedModWorldText = abmText;
            PerfectPlacementPlugin.cachedModWorldText.m_timer = 0.0f;
        }
    }

    private static void exitMode()
    {
        notifyUser("Exiting AEM");
        forceExitNextIteration = true;
        KeyBindingOverlay.ToggleOverlay(false);
        if (PerfectPlacementPlugin.cachedModWorldText != null)
        {
            PerfectPlacementPlugin.cachedModWorldText.m_timer = 5f;
            PerfectPlacementPlugin.cachedModWorldText = null;
        }
    }

    private static void notifyUser(string Message, MessageHud.MessageType position = MessageHud.MessageType.TopLeft)
    {
        MessageHud.instance.ShowMessage(position, "AEM: " + Message);
    }


    private static bool isContainer()
    {
        Container ContainerInstance = HitPiece.GetComponent<Container>();

        return ContainerInstance != null;
    }

    private static void dropContainerContents()
    {
        Container ContainerInstance = HitPiece.GetComponent<Container>();
        ContainerInstance.DropAllItems();
    }

    private static void changeModificationSpeed()
    {
        float speedDelta = 1;
        if (shiftFlag)
        {
            speedDelta = 10;
        }

        if (Input.GetKeyDown(PerfectPlacementPlugin.aemincreaseScrollSpeed.Value))
        {
            currentModificationSpeed = Mathf.Clamp(currentModificationSpeed + speedDelta, MIN_MODIFICATION_SPEED, MAX_MODIFICATION_SPEED);
            notifyUser("Modification Speed: " + currentModificationSpeed);
        }

        if (Input.GetKeyDown(PerfectPlacementPlugin.aemdecreaseScrollSpeed.Value))
        {
            currentModificationSpeed = Mathf.Clamp(currentModificationSpeed - speedDelta, MIN_MODIFICATION_SPEED, MAX_MODIFICATION_SPEED);
            notifyUser("Modification Speed: " + currentModificationSpeed);
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.CanRotatePiece))]
static class OverridePlayerInPlaceModePatch
{
    static void Postfix(Player __instance, ref bool __result)
    {
        if (AEM.IsInAemMode())
        {
            __result = true;
        }
    }
}