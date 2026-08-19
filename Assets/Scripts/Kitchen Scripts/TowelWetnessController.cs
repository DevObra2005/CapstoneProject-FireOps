using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// TowelWetnessController - owns ONE piece of state: is the towel wet?
//
// This is deliberately small. Right now its only job is to give
// WCTLButtonManager something truthful to check before allowing Cover, and to
// darken the towel so the player can SEE the difference.
//
// The dip animation, the wring motion and the faucet VFX are NOT here. Those
// belong to whatever owns the Wet step's choreography - same one-owner-per-
// behaviour rule that stopped the Pull -> Aim bug in Office. This file just
// holds the flag and the look.
//
// -------------------------------------------------------
// WHY THE MATERIAL IS LERPED AND NOT SWAPPED
//
// Swapping to a second material would work, but it also throws away anything
// else on the original - texture, tiling, normal map - the moment someone
// changes the dry material and forgets to change the wet one.
//
// So we CACHE the original colour and smoothness per renderer at Start, then
// lerp from those cached values. Same lesson as the emission glow in
// ClickableHazard: add to what was there, never overwrite blind.
//
// GetComponentsInChildren, plural. A towel exported from Blender can arrive
// as a parent with the mesh on a child, and a single GetComponent would
// silently find nothing.
// -------------------------------------------------------

public class TowelWetnessController : MonoBehaviour
{
    [Header("State")]
    [Tooltip("Read by WCTLButtonManager before allowing the Cover step. " +
             "Starts false - the towel is dry until the player wets it.")]
    [SerializeField] private bool isWet = false;

    [Header("Wet Look")]
    [Tooltip("How much darker the towel gets when wet. 0.55 means it keeps " +
             "55% of its dry brightness - wet fabric absorbs light rather " +
             "than changing hue, so darkening reads better than tinting.")]
    [Range(0.2f, 1f)]
    [SerializeField] private float wetDarkening = 0.55f;

    [Tooltip("Smoothness when soaked. Dry cloth is near 0; wet cloth catches " +
             "a soft sheen. Do not push this near 1 - that reads as plastic, " +
             "not damp fabric.")]
    [Range(0f, 1f)]
    [SerializeField] private float wetSmoothness = 0.45f;

    [Tooltip("Seconds for the colour change. Around 1s reads as water soaking " +
             "in; instant reads as a bug.")]
    [SerializeField] private float soakDuration = 1f;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int SmoothnessID = Shader.PropertyToID("_Smoothness");

    private Renderer[] towelRenderers;
    private Color[] dryColors;
    private float[] drySmoothness;

    private Coroutine soakRoutine;

    /// <summary>
    /// TRUE once the towel has been wetted at the sink. WCTLButtonManager
    /// blocks the Cover step while this is false - covering a gas flame with
    /// a dry cloth sets the cloth alight.
    /// </summary>
    public bool IsWet => isWet;

    private void Start()
    {
        CacheDryValues();
    }

    // Store each renderer's starting colour and smoothness so we can lerp
    // from them, and restore them exactly on a replay.
    private void CacheDryValues()
    {
        towelRenderers = GetComponentsInChildren<Renderer>();
        dryColors = new Color[towelRenderers.Length];
        drySmoothness = new float[towelRenderers.Length];

        for (int i = 0; i < towelRenderers.Length; i++)
        {
            Material mat = towelRenderers[i].material;

            dryColors[i] = mat.HasProperty(BaseColorID)
                ? mat.GetColor(BaseColorID)
                : Color.white;

            drySmoothness[i] = mat.HasProperty(SmoothnessID)
                ? mat.GetFloat(SmoothnessID)
                : 0f;
        }
    }

    // -------------------------------------------------------
    // Called when the Wet step completes. Safe to call twice.
    // -------------------------------------------------------
    public void SetWet()
    {
        if (isWet) return;

        isWet = true;

        if (soakRoutine != null) StopCoroutine(soakRoutine);
        soakRoutine = StartCoroutine(SoakRoutine());

        Debug.Log("[TowelWetness] Towel is now wet.");
    }

    private IEnumerator SoakRoutine()
    {
        if (towelRenderers == null || towelRenderers.Length == 0)
        {
            soakRoutine = null;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < soakDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / soakDuration);

            ApplyWetness(t);
            yield return null;
        }

        // Land exactly on the target - a lerp can finish a hair short.
        ApplyWetness(1f);
        soakRoutine = null;
    }

    // t = 0 is fully dry, t = 1 is fully soaked.
    private void ApplyWetness(float t)
    {
        for (int i = 0; i < towelRenderers.Length; i++)
        {
            if (towelRenderers[i] == null) continue;

            Material mat = towelRenderers[i].material;

            if (mat.HasProperty(BaseColorID))
            {
                Color wet = dryColors[i] * wetDarkening;
                wet.a = dryColors[i].a;          // never touch alpha
                mat.SetColor(BaseColorID, Color.Lerp(dryColors[i], wet, t));
            }

            if (mat.HasProperty(SmoothnessID))
            {
                mat.SetFloat(SmoothnessID,
                    Mathf.Lerp(drySmoothness[i], wetSmoothness, t));
            }
        }
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // Call this from wherever the Kitchen run resets. Without it, a second
    // attempt starts with an already-wet towel and the Cover guard never
    // fires - the lesson quietly stops being taught.
    // -------------------------------------------------------
    public void ResetToDry()
    {
        if (soakRoutine != null)
        {
            StopCoroutine(soakRoutine);
            soakRoutine = null;
        }

        isWet = false;

        if (towelRenderers != null)
            ApplyWetness(0f);

        Debug.Log("[TowelWetness] Towel reset to dry.");
    }
}