using System.Collections.Generic;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Draws moving arrows ON THE FLOOR from the player to a target,
// like the path arrows in a mobile game tutorial.
//
// HOW IT WORKS:
// One LineRenderer lying flat on the floor, with a repeating arrow
// texture. Moving the texture's offset every frame makes the arrows
// "flow" toward the target. The line is rebuilt a few times a second
// from the player's position, so it follows them as they walk.
//
// WAYPOINTS:
// A straight line would cut through desks and walls. Each step can list
// corner points (empty GameObjects on the floor) the path passes through.
// Once the player reaches a waypoint, it is dropped from the path.
//
// PRACTICE ONLY:
// TutorialManager is the only thing that calls Show(). In a real run it
// is switched off, so this line is never drawn.
//
// SETUP:
// 1. Empty GameObject "TutorialFloorPath" (top level) + this script
//    (a LineRenderer is added automatically)
// 2. LineRenderer: assign the arrow material, width about 0.5
// 3. Assign Player (or leave empty to follow the main camera)
// 4. Set Floor Y to the height of the office floor
// -------------------------------------------------------

[RequireComponent(typeof(LineRenderer))]
public class TutorialFloorPath : MonoBehaviour
{
    [Header("Who the arrows start from")]
    [Tooltip("The Player. Leave empty to use the main camera's position.")]
    public Transform player;

    [Header("Floor")]
    [Tooltip("World Y of the floor surface. The line is drawn slightly above it.")]
    public float floorY = 0f;

    [Tooltip("How far above the floor the arrows float, so they never flicker " +
             "into the floor surface.")]
    public float heightAboveFloor = 0.03f;

    [Header("Behaviour")]
    [Tooltip("Hide the arrows once the player is this close to the target (metres).")]
    public float hideWithin = 1.5f;

    [Tooltip("A waypoint counts as reached within this distance (metres).")]
    public float waypointReachedWithin = 1.0f;

    [Tooltip("Seconds between path rebuilds. 0.1 - 0.25 is plenty.")]
    public float refreshInterval = 0.15f;

    [Header("Look")]
    [Tooltip("Length of one arrow on the floor, in metres.")]
    public float arrowLength = 0.6f;

    [Tooltip("How fast the arrows flow toward the target. Make it NEGATIVE " +
             "if they flow the wrong way with your texture.")]
    public float flowSpeed = 1.2f;

    private LineRenderer line;
    private Material lineMaterial;
    private Transform target;
    private readonly List<Transform> remainingWaypoints = new List<Transform>();
    private readonly List<Vector3> points = new List<Vector3>();
    private float nextRefresh;
    private float flowOffset;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Tile;
        line.enabled = false;

        // TransformZ alignment faces the line along this object's Z axis.
        // Pointing Z straight down lays the arrows flat on the floor.
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // An instance, so scrolling the offset never edits the material asset.
        lineMaterial = line.material;
    }

    // -------------------------------------------------------
    // CALLED BY TutorialManager
    // -------------------------------------------------------
    public void Show(Transform newTarget, Transform[] waypoints)
    {
        target = newTarget;

        remainingWaypoints.Clear();
        if (waypoints != null)
            foreach (Transform w in waypoints)
                if (w != null) remainingWaypoints.Add(w);

        nextRefresh = 0f;   // rebuild on the next frame
    }

    public void Hide()
    {
        target = null;
        remainingWaypoints.Clear();
        if (line != null) line.enabled = false;
    }

    private void Update()
    {
        if (target == null) return;

        // Flow the arrows toward the target.
        flowOffset -= flowSpeed * Time.deltaTime;
        if (lineMaterial != null)
            lineMaterial.mainTextureOffset = new Vector2(flowOffset, 0f);

        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + refreshInterval;

        Rebuild();
    }

    private void Rebuild()
    {
        Vector3 start = GetPlayerPosition();

        // Close enough: no arrows needed, the ▼ marker takes over.
        if (FlatDistance(start, target.position) <= hideWithin)
        {
            line.enabled = false;
            return;
        }

        // Drop waypoints the player has already reached.
        while (remainingWaypoints.Count > 0 &&
               FlatDistance(start, remainingWaypoints[0].position) <= waypointReachedWithin)
        {
            remainingWaypoints.RemoveAt(0);
        }

        points.Clear();
        points.Add(OnFloor(start));
        foreach (Transform w in remainingWaypoints)
            points.Add(OnFloor(w.position));
        points.Add(OnFloor(target.position));

        line.positionCount = points.Count;
        line.SetPositions(points.ToArray());

        // One arrow per arrowLength metres along the line.
        line.textureScale = new Vector2(1f / Mathf.Max(0.05f, arrowLength), 1f);

        line.enabled = true;
    }

    private Vector3 GetPlayerPosition()
    {
        if (player != null) return player.position;
        return Camera.main != null ? Camera.main.transform.position : transform.position;
    }

    private Vector3 OnFloor(Vector3 p)
    {
        return new Vector3(p.x, floorY + heightAboveFloor, p.z);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
