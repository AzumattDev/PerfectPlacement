using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using PerfectPlacement;
using PerfectPlacement.Patches;

[HarmonyPatch(typeof(Player))]
public class PlayerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.SetLocalPlayer))]
    public static void AttachGizmoManager(Player __instance)
    {
        if (__instance.gameObject.GetComponent<GizmoManager>() == null)
        {
            __instance.gameObject.AddComponent<GizmoManager>();
        }
    }
}

public class GizmoManager : MonoBehaviour
{
    private const string GizmoTag = "BuildGizmo";
    public static GizmoManager gizmoManager = null!;

    public static Color xAxisGizmoColor = new Color(1, 0, 0, 0.502f);
    public static Color yAxisGizmoColor = new Color(0, 1, 0, 0.502f);
    public static Color zAxisGizmoColor = new Color(0, 0, 1, 0.502f);

    private GameObject _ghost;

    private float ArcRadius = 2f;
    private const float LineLength = 2f;
    public static Vector3 CurrentAxis = Vector3.zero;
    private GameObject _lastGhost;

    public enum Axis
    {
        None,
        X,
        Y,
        Z
    }

    private void Awake()
    {
        gizmoManager = this;
    }

    private void Update()
    {
        // If never show gizmos is on, bail out.
        if (PerfectPlacementPlugin.neverShowGizmos.Value.IsOn())
        {
            ClearGizmos();
            return;
        }

        // If "only show in ABM/AEM" is on, and we are not in either mode, return.
        bool onlyInModes = PerfectPlacementPlugin.onlyShowGizmosInABMOrAEM.Value.IsOn();
        if (onlyInModes && (!ABM.IsInAbmMode() && !AEM.IsInAemMode()))
        {
            ClearGizmos();
            return;
        }

        // If neither arcs nor arrows are shown, no reason to continue.
        bool showArcs = PerfectPlacementPlugin.showArcs.Value.IsOn();
        bool showArrows = PerfectPlacementPlugin.showArrows.Value.IsOn();
        if (!showArcs && !showArrows)
        {
            ClearGizmos();
            return;
        }

        _ghost = Player.m_localPlayer?.m_placementGhost ?? (AEM.IsInAemMode() ? AEM.HitObject : null);


        if (_ghost == null)
        {
            ClearGizmos();
            return;
        }

        if (_ghost != _lastGhost)
        {
            _lastGhost = _ghost;
            CalculateGizmoRadius();
        }

        RenderGizmos();
    }

    private void OnRenderObject()
    {
        if (_ghost != null && PerfectPlacementPlugin.showArrows.Value.IsOn())
        {
            DrawLinesToArrows(_ghost.transform.rotation);
        }
    }

    private void RenderGizmos()
    {
        Axis activeAxis = GetActiveAxis();
        Quaternion objectRotation = _ghost.transform.rotation;

        float defaultArcScale = PerfectPlacementPlugin.defaultArcScale.Value;
        float activeArcScale = PerfectPlacementPlugin.activeArcScale.Value;
        float inactiveArcScale = PerfectPlacementPlugin.inactiveArcScale.Value;

        if (PerfectPlacementPlugin.showArcs.Value.IsOn())
        {
            // Keep Y-axis fixed and rotate X/Z based on object rotation
            GizmoMeshCache.DrawArc(_ghost.transform.position, ArcRadius, PerfectPlacementPlugin.xAxisGizmoColor.Value, Vector3.right, objectRotation, activeAxis == Axis.X ? activeArcScale : (activeAxis == Axis.None ? defaultArcScale : inactiveArcScale));
            GizmoMeshCache.DrawArc(_ghost.transform.position, ArcRadius, PerfectPlacementPlugin.yAxisGizmoColor.Value, Vector3.up, Quaternion.identity, activeAxis == Axis.Y ? activeArcScale : (activeAxis == Axis.None ? defaultArcScale : inactiveArcScale));
            GizmoMeshCache.DrawArc(_ghost.transform.position, ArcRadius, PerfectPlacementPlugin.zAxisGizmoColor.Value, Vector3.forward, objectRotation, activeAxis == Axis.Z ? activeArcScale : (activeAxis == Axis.None ? defaultArcScale : inactiveArcScale));
        }

        if (PerfectPlacementPlugin.showArrows.Value.IsOn())
        {
            // Draw arrows for all axes, aligned with the object's rotation
            DrawArrowForAxis(Vector3.right, PerfectPlacementPlugin.xAxisGizmoColor.Value, objectRotation, activeAxis == Axis.X ? activeArcScale : (activeAxis == Axis.None ? defaultArcScale : inactiveArcScale));
            DrawArrowForAxis(Vector3.up, PerfectPlacementPlugin.yAxisGizmoColor.Value, Quaternion.identity, activeAxis == Axis.Y ? activeArcScale : (activeAxis == Axis.None ? defaultArcScale : inactiveArcScale));
            DrawArrowForAxis(Vector3.forward, PerfectPlacementPlugin.zAxisGizmoColor.Value, objectRotation, activeAxis == Axis.Z ? activeArcScale : (activeAxis == Axis.None ? defaultArcScale : inactiveArcScale));


            // Draw lines connecting the gizmo center to the arrows
            DrawLinesToArrows(objectRotation);
        }
    }


    private Axis GetActiveAxis()
    {
        if (CurrentAxis == Vector3.right) return Axis.X;
        if (CurrentAxis == Vector3.up) return Axis.Y;
        return CurrentAxis == Vector3.forward ? Axis.Z : Axis.None;
    }

    public void SetActiveAxis(Axis activeAxis)
    {
        CurrentAxis = activeAxis switch
        {
            Axis.X => Vector3.right,
            Axis.Y => Vector3.up,
            Axis.Z => Vector3.forward,
            _ => Vector3.zero
        };
    }

    private void CalculateGizmoRadius()
    {
        Renderer[] renderers = _ghost.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = new Bounds(_ghost.transform.position, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        // Set the radius to be slightly larger than the bounding sphere
        ArcRadius = bounds.extents.magnitude + PerfectPlacementPlugin.extraRadiusMargin.Value; // Add a margin for visibility
    }


    private void DrawArrowForAxis(Vector3 direction, Color color, Quaternion objectRotation, float scale)
    {
        float lineLength = PerfectPlacementPlugin.arrowLineLength.Value; // Length of the line
        float arrowTipLength = PerfectPlacementPlugin.arrowTipLength.Value; // Length of the arrow tip
        float arrowTipScale = PerfectPlacementPlugin.arrowTipScale.Value; // Width of the arrow tip

        Vector3 origin = _ghost.transform.position;
        Vector3 lineEnd = origin + objectRotation * (direction * lineLength);
        Vector3 position = _ghost.transform.position + objectRotation * (direction * (ArcRadius + 0.5f)); // Position outside the arcs

        // Draw the line along the axis
        GizmoMeshCache.DrawLine(origin, lineEnd, color);

        // Draw the arrow tip at the end of the line
        GizmoMeshCache.DrawArrow(position, objectRotation * direction, arrowTipLength, color, scale);
    }

    private void DrawLinesToArrows(Quaternion objectRotation)
    {
        const float offset = 0.5f;
        Vector3 origin = _ghost.transform.position;

        // Draw lines to each arrow
        GizmoMeshCache.DrawLine(origin, origin + objectRotation * (Vector3.right * (ArcRadius + offset)), PerfectPlacementPlugin.xAxisGizmoColor.Value);
        GizmoMeshCache.DrawLine(origin, origin + Vector3.up * (ArcRadius + offset), PerfectPlacementPlugin.yAxisGizmoColor.Value);
        GizmoMeshCache.DrawLine(origin, origin + objectRotation * (Vector3.forward * (ArcRadius + offset)), PerfectPlacementPlugin.zAxisGizmoColor.Value);
    }

    private void ClearGizmos()
    {
        _ghost = null;
        _lastGhost = null;
        CurrentAxis = Vector3.zero;
    }
}

public static class GizmoMeshCache
{
    private static readonly Dictionary<Tuple<Color, Vector3, float>, Mesh> ArcMeshes = new();
    private static readonly Dictionary<Color, Mesh> ArrowMeshes = new();

    public static void DrawArc(Vector3 position, float radius, Color color, Vector3 axis, Quaternion objectRotation, float scale)
    {
        // Keep the Y-axis (green) fixed in world space
        Quaternion rotation = axis == Vector3.up
            ? Quaternion.identity // Y-axis is fixed
            : objectRotation; // Apply object rotation to X and Z axes

        // Get or create the arc mesh
        Tuple<Color, Vector3, float> key = Tuple.Create(color, axis, radius);
        if (!ArcMeshes.TryGetValue(key, out Mesh? mesh))
        {
            mesh = CreateArcMesh(radius, axis);
            ArcMeshes[key] = mesh;
        }

        // Draw the mesh with the specified scale, color, and adjusted rotation
        Material material = GetMaterial(color);
        Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
        Graphics.DrawMesh(mesh, matrix, material, 0);
    }


    private static Mesh CreateArcMesh(float radius, Vector3 axis)
    {
        Mesh mesh = new Mesh();

        const int segments = 64;
        Vector3[] vertices = new Vector3[segments];
        int[] indices = new int[segments * 2];

        Quaternion rotation = Quaternion.LookRotation(axis);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / (segments - 1);
            Vector3 point = new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), 0);
            vertices[i] = rotation * point;

            if (i >= segments - 1) continue;
            indices[i * 2] = i;
            indices[i * 2 + 1] = i + 1;
        }

        mesh.vertices = vertices;
        mesh.SetIndices(indices, MeshTopology.Lines, 0);
        return mesh;
    }

    public static void DrawArrow(Vector3 position, Vector3 direction, float length, Color color, float scale)
    {
        if (!ArrowMeshes.TryGetValue(color, out Mesh? arrowMesh))
        {
            arrowMesh = CreateArrowMesh();
            ArrowMeshes[color] = arrowMesh;
        }

        Quaternion rotation = Quaternion.LookRotation(direction);
        // Create a 90-degree rotation around the X-axis
        Quaternion rightRotation = Quaternion.Euler(90, 0, 0);
        // Apply the 90-degree rotation to the original rotation so they fucking point where I want.
        Quaternion rotatedTheWayIWant = rotation * rightRotation;

        Vector3 scaledDirection = direction.normalized * length;


        // Draw the arrow tip mesh at the position
        Material material = GetMaterial(color);
        Matrix4x4 matrix = Matrix4x4.TRS(position + scaledDirection, rotatedTheWayIWant, Vector3.one * scale);
        Graphics.DrawMesh(arrowMesh, matrix, material, 0);
    }

    private static Mesh CreateArrowMesh()
    {
        Mesh mesh = new Mesh();

        // Create a cone-like arrow tip
        const float radius = 0.1f;
        const float height = 0.2f;
        const int segments = 16;

        List<Vector3> vertices = new List<Vector3> { Vector3.zero }; // Tip of the arrow
        List<int> triangles = new List<int>();

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices.Add(new Vector3(radius * Mathf.Cos(angle), -height, radius * Mathf.Sin(angle)));

            if (i > 0)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();

        return mesh;
    }

    public static void DrawLine(Vector3 start, Vector3 end, Color color)
    {
        Material material = GetMaterial(color);

        GL.PushMatrix();
        material.SetPass(0);
        GL.Begin(GL.LINES);
        GL.Color(color);
        GL.Vertex(start);
        GL.Vertex(end);
        GL.End();
        GL.PopMatrix();
    }


    private static Material GetMaterial(Color color)
    {
        Material material = new Material(Shader.Find("Sprites/Default"))
        {
            color = color
        };
        return material;
    }
}