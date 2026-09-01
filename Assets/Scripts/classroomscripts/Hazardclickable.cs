using UnityEngine;

/// <summary>
/// Alternate hazard-click script — use this on YOUR hazard objects
/// without touching your groupmate's "ClickableHazard.cs" file.
/// Class name is different (HazardClickable) so there's no naming conflict.
///
/// Setup:
/// 1. Attach this to each hazard GameObject (e.g. OctopusWiringHazard, OptomaProjector).
/// 2. Make sure the object has a Collider component (Box Collider is fine).
/// 3. Everything else (foundMarker, targetRenderer) is optional — leave as None if unused.
/// 4. Needs a HazardManager somewhere in the scene (on any GameObject) for the
///    counter to update.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HazardClickable : MonoBehaviour
{
    [Header("State")]
    public bool alreadyFound = false;

    [Header("Optional Visual Feedback")]
    [Tooltip("Object to enable once this hazard is found (checkmark, glow, highlight). Safe to leave empty.")]
    public GameObject foundMarker;

    [Tooltip("Optional: change color when found. Safe to leave empty.")]
    public Renderer targetRenderer;
    public Color foundColor = Color.green;

    private Color originalColor;
    private bool hasOriginalColor = false;

    private void Start()
    {
        if (targetRenderer != null && targetRenderer.material != null)
        {
            originalColor = targetRenderer.material.color;
            hasOriginalColor = true;
        }

        if (foundMarker != null)
        {
            foundMarker.SetActive(false);
        }
    }

    private void OnMouseDown()
    {
        TryIdentify();
    }

    private void TryIdentify()
    {
        if (alreadyFound) return;

        if (HazardManager.Instance == null)
        {
            Debug.LogWarning($"{name}: No HazardManager found in the scene. Add one before hazards can be clicked.");
            return;
        }

        if (!HazardManager.Instance.AreHazardsClickable())
        {
            // Intro dialogue hasn't finished yet — not an error.
            return;
        }

        alreadyFound = true;

        if (foundMarker != null)
            foundMarker.SetActive(true);

        if (targetRenderer != null)
            targetRenderer.material.color = foundColor;

        HazardManager.Instance.RegisterHazardFound();

        Debug.Log($"Hazard found: {name}");
    }

    public void ResetHazard()
    {
        alreadyFound = false;

        if (foundMarker != null)
            foundMarker.SetActive(false);

        if (targetRenderer != null && hasOriginalColor)
            targetRenderer.material.color = originalColor;
    }
}