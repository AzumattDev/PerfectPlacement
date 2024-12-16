using UnityEngine;

namespace PerfectPlacement.Patches;

public class Util
{
    internal static Vector3 ClampAngles(Vector3 angles)
    {
        return new Vector3(ClampAngle(angles.x), ClampAngle(angles.y), ClampAngle(angles.z));
    }

    internal static int ClampPlaceRotation(int index)
    {
        const int MaxIndex = 16; // 360/22.5f
            
        if (index < 0)
            index = MaxIndex + index;
        else if (index >= MaxIndex)
            index -= MaxIndex;
        return index;
    }
        
    private static float ClampAngle(float angle)
    {
        if (angle < 0)
            angle = 360 + angle;
        else if (angle >= 360)
            angle -= 360;
        return angle;
    }
    
    public static Character getPlayerCharacter(Player __instance)
    {
        return __instance;
    }
}