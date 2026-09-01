using UnityEngine;

// Highlights Teaching Target 2 during the Phase 1 -> Phase 2 bridge.
// (Office: fire extinguisher | Kitchen: LPG valve)
// Added at runtime by PhaseTransitionManager.HighlightTarget2().
public class ExtinguisherTarget : MonoBehaviour
{
    private PhaseTransitionManager owner;

    [Header("Marker Arrow")]
    [Tooltip("Float a green arrow above this object when it becomes the objective")]
    public bool useMarkerArrow = true;

    [Header("Legacy Tint (off by default)")]
    [Tooltip("The old amber colour pulse. Tick to use it instead of, or alongside, the arrow.")]
    public bool useTint = false;

    public Color highlightColor = new Color(1f, 0.66f, 0.17f);
    public float pulseSpeed = 2.5f;

    private Renderer[] rends;
    private Color[] baseColors;
    private bool pulsing = false;

    public void Setup(PhaseTransitionManager manager)
    {
        owner = manager;

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[ExtinguisherTarget] '{gameObject.name}' has no Collider!");

        if (useMarkerArrow)
            ShowArrow();

        if (useTint)
        {
            CacheColors();
            pulsing = true;
        }
    }

    // -------------------------------------------------------
    // Marker arrow
    // -------------------------------------------------------

    private void ShowArrow()
    {
        if (MarkerArrowManager.Instance == null)
        {
            Debug.LogWarning("[ExtinguisherTarget] No MarkerArrowManager in the scene.");
            return;
        }

        MarkerArrowManager.Instance.PointAt(transform);
    }

    private void HideArrow()
    {
        if (MarkerArrowManager.Instance == null) return;
        MarkerArrowManager.Instance.Hide();
    }

    // -------------------------------------------------------
    // Legacy tint
    // -------------------------------------------------------

    private void CacheColors()
    {
        rends = GetComponentsInChildren<Renderer>();
        baseColors = new Color[rends.Length];

        for (int i = 0; i < rends.Length; i++)
            baseColors[i] = rends[i].material.color;
    }

    void Update()
    {
        if (!pulsing || rends == null) return;

        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;

        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
                rends[i].material.color = Color.Lerp(baseColors[i], highlightColor, t);
        }
    }

    private void ClearTint()
    {
        if (rends == null) return;

        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
                rends[i].material.color = baseColors[i];
        }
    }

    // -------------------------------------------------------
    // Interaction
    // -------------------------------------------------------

    public void OnClicked()
    {
        if (owner == null) return;

        pulsing = false;
        ClearTint();
        HideArrow();

        owner.OnTarget2Tapped();
    }

    void OnDisable()
    {
        if (owner == null) return;
        HideArrow();
    }

    public void OnHoverEnter() { }
    public void OnHoverExit() { }
}