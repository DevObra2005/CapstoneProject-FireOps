using UnityEngine;

// -------------------------------------------------------
// Optional per-object arrow settings.
//
// Drop this on any object the marker arrow points at when the
// default placement doesn't suit it. MarkerArrow looks for this
// component on its target: found, it uses these values; not found,
// it uses its own defaults.
//
// That means you only add it to the handful of objects that need
// special treatment, and everything else keeps working untouched.
// -------------------------------------------------------
[DisallowMultipleComponent]
public class MarkerArrowAnchor : MonoBehaviour
{
    [Header("Height")]
    [Tooltip("Override the arrow's default gap above this object. Leave unticked to use the arrow's own value.")]
    public bool overrideHeight = false;

    [Tooltip("Gap between the top of this object and the arrow tip, in metres")]
    public float heightOffset = 0.4f;

    [Header("Bounds")]
    [Tooltip("Measure only this object's own renderer, ignoring children. Turn ON when tall children (wall signs, brackets) drag the arrow upward.")]
    public bool ignoreChildRenderers = false;

    [Header("Exact Placement")]
    [Tooltip("Optional. Assign an empty GameObject and the arrow sits directly above it, ignoring renderer bounds entirely. Most precise option.")]
    public Transform anchorPoint;
}