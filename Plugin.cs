﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using PerfectPlacement.Patches;
using PerfectPlacement.Patches.Compatibility;
using PerfectPlacement.UI;
using ServerSync;
using UnityEngine;

namespace PerfectPlacement;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class PerfectPlacementPlugin : BaseUnityPlugin
{
    /*
     * This code is almost a direct copy from ValheimPlus. The modifications are ServerSync additions and WardIsLove compatibility, author credits and some minor changes.
     *
     */
    internal const string ModName = "PerfectPlacement";
    internal const string ModVersion = "1.2.2";
    internal const string Author = "Azumatt_and_ValheimPlusDevs";
    private const string ModGUID = Author + "." + ModName;
    private static string ConfigFileName = ModGUID + ".cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    internal static string ConnectionError = "";
    internal readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource PerfectPlacementLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
    internal static PerfectPlacementPlugin Instance;
    internal static Chat.WorldTextInstance? cachedModWorldText;

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    internal class PlayerData
    {
        public Vector3 PlaceRotation = Vector3.zero;
        public bool Opposite;
        public Piece LastPiece;
        public KeyCode LastKeyCode;
    }

    internal static readonly Dictionary<Player, PlayerData> PlayersData = new();

    public void Awake()
    {
        Instance = this;
        _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, new ConfigDescription("If on, the configuration is locked and can be changed by server admins only.", null, new ConfigurationManagerAttributes { Order = 5 }));
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);


        /* FPM Configs */
        fpmIsEnabled = config("1 - General", "Enable Free Placement Rotation", Toggle.On, new ConfigDescription("If on, Free Placement Rotation is enabled. Everything in section 2 will be affected.", null, new ConfigurationManagerAttributes { Order = 1 }));
        fpmrotateY = config("2 - Free Placement Rotation", "Rotate Y", KeyCode.LeftAlt, "The key to rotate the object you are placing on the Y axis, Rotates placement marker by 1 degree with keep ability to attach to nearly pieces.", false);
        fpmrotateX = config("2 - Free Placement Rotation", "Rotate X", KeyCode.C, "The key to rotate the object you are placing on the X axis, Rotates placement marker by 1 degree with keep ability to attach to nearly pieces.", false);
        fpmrotateZ = config("2 - Free Placement Rotation", "Rotate Z", KeyCode.V, "The key to rotate the object you are placing on the Z axis, Rotates placement marker by 1 degree with keep ability to attach to nearly pieces.", false);
        fpmcopyRotationParallel = config("2 - Free Placement Rotation", "Copy Rotation Parallel", KeyCode.F, "Copy rotation of placement marker from target piece in front of you.", false);
        fpmcopyRotationPerpendicular = config("2 - Free Placement Rotation", "Copy Rotation Perpendicular", KeyCode.G, "Set rotation to be perpendicular to piece in front of you.", false);

        /* ABM Configs*/
        abmIsEnabled = config("1 - General", "Enable Advanced Building Mode", Toggle.On, new ConfigDescription("If on, Advanced Building Mode is enabled. Everything in section 3 will be affected.", null, new ConfigurationManagerAttributes { Order = 2 }));
        abmenterAdvancedBuildingMode = config("3 - Advanced Building Mode", "Enter Advanced Building Mode", KeyCode.F1, "The key to enter Advanced Building Mode when building", false);
        abmresetAdvancedBuildingMode = config("3 - Advanced Building Mode", "Reset Advanced Building Mode", KeyCode.F7, "The key to reset the object to its original position and rotation", false);
        abmexitAdvancedBuildingMode = config("3 - Advanced Building Mode", "Exit Advanced Building Mode", KeyCode.F3, "The key to exit Advanced Building Mode when building", false);
        abmcopyObjectRotation = config("3 - Advanced Building Mode", "Copy Object Rotation", KeyCode.Keypad7, "Copy the object rotation of the currently selected object in ABM", false);
        abmpasteObjectRotation = config("3 - Advanced Building Mode", "Paste Object Rotation", KeyCode.Keypad8, "Apply the copied object rotation to the currently selected object in ABM", false);
        abmincreaseScrollSpeed = config("3 - Advanced Building Mode", "Increase Scroll Speed", KeyCode.KeypadPlus, "Increases the amount an object rotates and moves. Holding Shift will increase in increments of 10 instead of 1.", false);
        abmdecreaseScrollSpeed = config("3 - Advanced Building Mode", "Decrease Scroll Speed", KeyCode.KeypadMinus, "Decreases the amount an object rotates and moves. Holding Shift will decrease in increments of 10 instead of 1.", false);


        /* AEM Configs */
        aemIsEnabled = config("1 - General", "Enable Advanced Editing Mode", Toggle.On, new ConfigDescription("If on, Advanced Editing Mode is enabled. Everything in section 4 will be affected.", null, new ConfigurationManagerAttributes { Order = 3 }));
        aementerAdvancedEditingMode = config("4 - Advanced Editing Mode", "Enter Advanced Editing Mode", KeyCode.Keypad0, "The key to enter Advanced Editing Mode", false);
        aemresetAdvancedEditingMode = config("4 - Advanced Editing Mode", "Reset Advanced Editing Mode", KeyCode.F7, "The key to reset the object to its original position and rotation", false);
        aemabortAndExitAdvancedEditingMode = config("4 - Advanced Editing Mode", "Abort and Exit Advanced Editing Mode", KeyCode.F8, "The key to abort and exit Advanced Editing Mode and reset the object", false);
        aemconfirmPlacementOfAdvancedEditingMode = config("4 - Advanced Editing Mode", "Confirm Placement of Advanced Editing Mode", KeyCode.KeypadEnter, "The key to confirm the placement of the object and place it", false);
        aemcopyObjectRotation = config("4 - Advanced Editing Mode", "Copy Object Rotation", KeyCode.Keypad7, "The key to copy the object rotation of the currently selected object in AEM", false);
        aempasteObjectRotation = config("4 - Advanced Editing Mode", "Paste Object Rotation", KeyCode.Keypad8, "The key to apply the copied object rotation to the currently selected object in AEM", false);
        aemincreaseScrollSpeed = config("4 - Advanced Editing Mode", "Increase Scroll Speed", KeyCode.KeypadPlus, "The key to increase the scroll speed. Increases the amount an object rotates and moves. Holding Shift will increase in increments of 10 instead of 1.", false);
        aemdecreaseScrollSpeed = config("4 - Advanced Editing Mode", "Decrease Scroll Speed", KeyCode.KeypadMinus, "The key to decrease the scroll speed. Decreases the amount an object rotates and moves. Holding Shift will increase in increments of 10 instead of 1.", false);

        /* Grid Configs */
        gridAlignmentEnabled = config("1 - General", "Enable Grid Alignment", Toggle.Off, new ConfigDescription("If off, Grid Alignment is disabled overall, all code for it will be skipped. Everything in section 5 will be affected.", null, new ConfigurationManagerAttributes { Order = 4 }));
        alignToGrid = config("5 - Grid Alignment", "Align to Grid", KeyCode.LeftAlt, "The key to enable grid alignment while building", false);
        alignToggle = config("5 - Grid Alignment", "Align Toggle", KeyCode.F7, "The key to toggle grid alignment while building", false);
        changeDefaultAlignment = config("5 - Grid Alignment", "Change Default Alignment", KeyCode.F6, "The key to change the default alignment", false);

        /* Gizmo Configs */
        onlyShowGizmosInABMOrAEM = config("6 - Gizmos", "Show Gizmos only in AEM or ABM", Toggle.On, new ConfigDescription("If off, anytime you're about to build something, the gizmo's will show. If on, the gizmos will only show when in ABM or AEM mode.", null, new ConfigurationManagerAttributes { Order = 15 }), false);
        neverShowGizmos = config("6 - Gizmos", "Never Show Gizmos", Toggle.Off, new ConfigDescription("If off, the gizmos will show when you have specified based on configurations. If on, this overrides all configurations about showing, and makes the gizmos never show at all.", null, new ConfigurationManagerAttributes { Order = 14 }), false);
        showArcs = config("6 - Gizmos", "Show Rotation Arcs", Toggle.On, new ConfigDescription("If enabled, rotation arcs around the gizmo will be displayed.", null, new ConfigurationManagerAttributes { Order = 13 }), false);
        showArrows = config("6 - Gizmos", "Show Axis Arrows", Toggle.On, new ConfigDescription("If enabled, arrows pointing along each axis will be displayed.", null, new ConfigurationManagerAttributes { Order = 12 }), false);
        defaultArcScale = config("6 - Gizmos", "Default Arc Scale", 1f, new ConfigDescription("Scale factor for arcs by default.", null, new ConfigurationManagerAttributes { Order = 11 }), false);
        activeArcScale = config("6 - Gizmos", "Active Arc Scale", 1.2f, new ConfigDescription("Scale factor for arcs when an axis is active.", null, new ConfigurationManagerAttributes { Order = 10 }), false);
        inactiveArcScale = config("6 - Gizmos", "Inactive Arc Scale", 0.8f, new ConfigDescription("Scale factor for arcs when an axis is inactive.", null, new ConfigurationManagerAttributes { Order = 9 }), false);
        extraRadiusMargin = config("6 - Gizmos", "Arc Radius Margin", 0.5f, new ConfigDescription("Additional margin added to the calculated arc radius for better visibility.", null, new ConfigurationManagerAttributes { Order = 8 }), false);
        arrowLineLength = config("6 - Gizmos", "Arrow Line Length", 2f, new ConfigDescription("Length of the line before the arrow tip.", null, new ConfigurationManagerAttributes { Order = 7 }), false);
        arrowTipLength = config("6 - Gizmos", "Arrow Tip Length", 0.85f, new ConfigDescription("Length of the arrow tip portion.", null, new ConfigurationManagerAttributes { Order = 6 }), false);
        arrowTipScale = config("6 - Gizmos", "Arrow Tip Scale", 0.2f, new ConfigDescription("Scale (width) factor of the arrow tip.", null, new ConfigurationManagerAttributes { Order = 5 }), false);
        xAxisGizmoColor = config("6 - Gizmos", "Gizmo X Axis Color", new Color(1f, 0f, 0f, 0.502f), new ConfigDescription("Color for the X Axis gizmos.", null, new ConfigurationManagerAttributes { Order = 3 }), false);
        yAxisGizmoColor = config("6 - Gizmos", "Gizmo Y Axis Color", new Color(0f, 1f, 0f, 0.502f), new ConfigDescription("Color for the Y Axis gizmos.", null, new ConfigurationManagerAttributes { Order = 2 }), false);
        zAxisGizmoColor = config("6 - Gizmos", "Gizmo Z Axis Color", new Color(0f, 0f, 1f, 0.502f), new ConfigDescription("Color for the Z Axis gizmos.", null, new ConfigurationManagerAttributes { Order = 1 }), false);

        Assembly assembly = Assembly.GetExecutingAssembly();
        _harmony.PatchAll(assembly);
        SetupWatcher();
    }

    private void Start()
    {
        AutoDoc();
        FirstPersonModeCompat.Init();
    }

    internal void AutoDoc()
    {
#if DEBUG
            // Store Regex to get all characters after a [
            Regex regex = new(@"\[(.*?)\]");

            // Strip using the regex above from Config[x].Description.Description
            string Strip(string x) => regex.Match(x).Groups[1].Value;
            StringBuilder sb = new();
            string lastSection = "";
            foreach (ConfigDefinition x in Config.Keys)
            {
                // skip first line
                if (x.Section != lastSection)
                {
                    lastSection = x.Section;
                    sb.Append($"{Environment.NewLine}`{x.Section}`{Environment.NewLine}");
                }

                sb.Append($"\n{x.Key} [{Strip(Config[x].Description.Description)}]" +
                          $"{Environment.NewLine}   * {Config[x].Description.Description.Replace("[Synced with Server]", "").Replace("[Not Synced with Server]", "")}" +
                          $"{Environment.NewLine}     * Default Value: {Config[x].GetSerializedValue()}{Environment.NewLine}");
            }

            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                    $"{ModName}_AutoDoc.md"), sb.ToString());
#endif
    }

    private void OnDestroy()
    {
        Config.Save();
    }

    private void SetupWatcher()
    {
        FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
        watcher.Changed += ReadConfigValues;
        watcher.Created += ReadConfigValues;
        watcher.Renamed += ReadConfigValues;
        watcher.IncludeSubdirectories = true;
        watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        watcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        if (!File.Exists(ConfigFileFullPath)) return;
        try
        {
            PerfectPlacementLogger.LogDebug("ReadConfigValues called");
            Config.Reload();
        }
        catch
        {
            PerfectPlacementLogger.LogError($"There was an issue loading your {ConfigFileName}");
            PerfectPlacementLogger.LogError("Please check your config entries for spelling and format!");
        }
    }

    internal static void UpdateKeyBindings()
    {
        if (ABM.IsInAbmMode())
        {
            KeyBindingOverlay.UpdateBindings(KeyBindingOverlay.FormatBinding("orange", "Advanced Building Mode"), new Dictionary<string, string>
            {
                { "Left Alt", KeyBindingOverlay.FormatBinding("blue", "Rotate on Blue") },
                { "ScrollWheel", KeyBindingOverlay.FormatBinding("green", "Rotate on Green") },
                { "Left Ctrl", KeyBindingOverlay.FormatBinding("red", "Rotate on Red") },
                { "Enter Mode", abmenterAdvancedBuildingMode.Value.ToString() },
                { "Exit Mode", abmexitAdvancedBuildingMode.Value.ToString() },
                { "Reset Position/Rotation", abmresetAdvancedBuildingMode.Value.ToString() },
                { "Copy Rotation", abmcopyObjectRotation.Value.ToString() },
                { "Paste Rotation", abmpasteObjectRotation.Value.ToString() },
                { "Increase Speed", abmincreaseScrollSpeed.Value.ToString() },
                { "Decrease Speed", abmdecreaseScrollSpeed.Value.ToString() },
                { $"Move Up [{KeyBindingOverlay.FormatBinding("green", "Green Arrow")}]", "Ctrl + ↑" },
                { $"Move Down [{KeyBindingOverlay.FormatBinding("green", "Green Arrow")}]", "Ctrl + ↓" },
                { $"Move Forward [{KeyBindingOverlay.FormatBinding("blue", "Blue Arrow")}]", "↑" },
                { $"Move Backward [{KeyBindingOverlay.FormatBinding("blue", "Blue Arrow")}]", "↓" },
                { $"Move Left [{KeyBindingOverlay.FormatBinding("red", "Red Arrow")}]", "←" },
                { $"Move Right [{KeyBindingOverlay.FormatBinding("red", "Red Arrow")}]", "→" }
            });
        }
        else if (AEM.IsInAemMode())
        {
            KeyBindingOverlay.UpdateBindings(KeyBindingOverlay.FormatBinding("orange", "Advanced Editing Mode"), new Dictionary<string, string>
            {
                { "Left Alt", KeyBindingOverlay.FormatBinding("blue", "Rotate on Blue") },
                { "ScrollWheel", KeyBindingOverlay.FormatBinding("green", "Rotate on Green") },
                { "Left Ctrl", KeyBindingOverlay.FormatBinding("red", "Rotate on Red") },
                { "Enter Mode", aementerAdvancedEditingMode.Value.ToString() },
                { "Exit Mode", aemabortAndExitAdvancedEditingMode.Value.ToString() },
                { "Confirm Placement", aemconfirmPlacementOfAdvancedEditingMode.Value.ToString() },
                { "Copy Rotation", aemcopyObjectRotation.Value.ToString() },
                { "Paste Rotation", aempasteObjectRotation.Value.ToString() },
                { "Increase Speed", aemincreaseScrollSpeed.Value.ToString() },
                { "Decrease Speed", aemdecreaseScrollSpeed.Value.ToString() },
                { $"Move Up [{KeyBindingOverlay.FormatBinding("green", "Green Arrow")}]", "Ctrl + ↑" },
                { $"Move Down [{KeyBindingOverlay.FormatBinding("green", "Green Arrow")}]", "Ctrl + ↓" },
                { $"Move Forward [{KeyBindingOverlay.FormatBinding("blue", "Blue Arrow")}]", "↑" },
                { $"Move Backward [{KeyBindingOverlay.FormatBinding("blue", "Blue Arrow")}]", "↓" },
                { $"Move Left [{KeyBindingOverlay.FormatBinding("red", "Red Arrow")}]", "←" },
                { $"Move Right [{KeyBindingOverlay.FormatBinding("red", "Red Arrow")}]", "→" }
            });
        }
        else
        {
            KeyBindingOverlay.UpdateBindings(KeyBindingOverlay.FormatBinding("orange", "Enter Modes"), new Dictionary<string, string>
            {
                { "Enter ABM", abmenterAdvancedBuildingMode.Value.ToString() },
                { "Enter AEM \n\t[Without Hammer equipped and looking at existing build piece!]", aementerAdvancedEditingMode.Value.ToString() },
            });
        }
    }

    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;

    /* FPM Configs */
    internal static ConfigEntry<Toggle> fpmIsEnabled = null!;
    internal static ConfigEntry<KeyCode> fpmrotateY = null!;
    internal static ConfigEntry<KeyCode> fpmrotateX = null!;
    internal static ConfigEntry<KeyCode> fpmrotateZ = null!;
    internal static ConfigEntry<KeyCode> fpmcopyRotationParallel = null!;
    internal static ConfigEntry<KeyCode> fpmcopyRotationPerpendicular = null!;

    /* ABM Configs */
    internal static ConfigEntry<Toggle> abmIsEnabled = null!;
    internal static ConfigEntry<KeyCode> abmenterAdvancedBuildingMode = null!;
    internal static ConfigEntry<KeyCode> abmresetAdvancedBuildingMode = null!;
    internal static ConfigEntry<KeyCode> abmexitAdvancedBuildingMode = null!;
    internal static ConfigEntry<KeyCode> abmcopyObjectRotation = null!;
    internal static ConfigEntry<KeyCode> abmpasteObjectRotation = null!;
    internal static ConfigEntry<KeyCode> abmincreaseScrollSpeed = null!;
    internal static ConfigEntry<KeyCode> abmdecreaseScrollSpeed = null!;

    /* AEM Configs */
    internal static ConfigEntry<Toggle> aemIsEnabled = null!;
    internal static ConfigEntry<KeyCode> aementerAdvancedEditingMode = null!;
    internal static ConfigEntry<KeyCode> aemresetAdvancedEditingMode = null!;
    internal static ConfigEntry<KeyCode> aemabortAndExitAdvancedEditingMode = null!;
    internal static ConfigEntry<KeyCode> aemconfirmPlacementOfAdvancedEditingMode = null!;
    internal static ConfigEntry<KeyCode> aemcopyObjectRotation = null!;
    internal static ConfigEntry<KeyCode> aempasteObjectRotation = null!;
    internal static ConfigEntry<KeyCode> aemincreaseScrollSpeed = null!;
    internal static ConfigEntry<KeyCode> aemdecreaseScrollSpeed = null!;

    /* Grid Configs */
    internal static ConfigEntry<Toggle> gridAlignmentEnabled = null!;
    internal static ConfigEntry<KeyCode> alignToGrid = null!;
    internal static ConfigEntry<KeyCode> alignToggle = null!;
    internal static ConfigEntry<KeyCode> changeDefaultAlignment = null!;

    // Gizmo configuration entries
    public static ConfigEntry<Toggle> onlyShowGizmosInABMOrAEM = null!;
    public static ConfigEntry<Toggle> neverShowGizmos = null!;
    public static ConfigEntry<float> defaultArcScale = null!;
    public static ConfigEntry<float> activeArcScale = null!;
    public static ConfigEntry<float> inactiveArcScale = null!;
    public static ConfigEntry<float> extraRadiusMargin = null!;
    public static ConfigEntry<float> arrowLineLength = null!;
    public static ConfigEntry<float> arrowTipLength = null!;
    public static ConfigEntry<float> arrowTipScale = null!;
    public static ConfigEntry<Color> xAxisGizmoColor = null!;
    public static ConfigEntry<Color> yAxisGizmoColor = null!;
    public static ConfigEntry<Color> zAxisGizmoColor = null!;
    public static ConfigEntry<Toggle> showArrows = null!;
    public static ConfigEntry<Toggle> showArcs = null!;

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        //var configEntry = Config.Bind(group, name, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order;
        [UsedImplicitly] public bool? Browsable;
        [UsedImplicitly] public string? Category;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
    }

    #endregion
}

public static class ToggleExtentions
{
    public static bool IsOn(this PerfectPlacementPlugin.Toggle value)
    {
        return value == PerfectPlacementPlugin.Toggle.On;
    }

    public static bool IsOff(this PerfectPlacementPlugin.Toggle value)
    {
        return value == PerfectPlacementPlugin.Toggle.Off;
    }
}