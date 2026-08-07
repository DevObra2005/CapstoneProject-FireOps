using UnityEngine;

public class HazardActionTarget : MonoBehaviour
{
    // -------------------------------------------------------
    // Global "who is armed right now" reference.
    // While this is non-null, an action target is waiting to be
    // tapped, and the interaction manager blocks clicks/hover on
    // every OTHER hazard so the player must finish the current
    // action before starting a new one.
    // -------------------------------------------------------
    public static HazardActionTarget ActiveTarget { get; private set; }

    private ClickableHazard owner;

    [Header("Highlight")]
    [Tooltip("Bright green — reads clearly as 'perform this action'")]
    public Color highlightColor = new Color(0.239f, 0.910f, 0.318f);

    [Range(0f, 5f)]
    [Tooltip("Keep low — the pulse does the work, not raw brightness")]
    public float glowIntensity = 0.5f;

    [Tooltip("Breathing speed")]
    public float pulseSpeed = 1.8f;

    [Range(0f, 1f)]
    [Tooltip("1 = full breath from dark to bright")]
    public float pulseDepth = 1f;

    [Tooltip("How fast the glow eases in when armed")]
    public float fadeInSpeed = 5f;

    private Renderer[] rends;
    private Color[] originalEmissions;
    private bool[] hadEmission;
    private bool pulsing = false;
    private float armBlend = 0f;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    public void Setup(ClickableHazard hazard)
    {
        owner = hazard;
        CacheRenderers();

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[HazardActionTarget] '{gameObject.name}' has no Collider!");

        armBlend = 0f;
        pulsing = true;

        // Register as the currently-armed target so the interaction
        // manager locks out all other hazards until this is tapped.
        ActiveTarget = this;
    }

    // Remember each material's starting emission so we can restore it perfectly.
    private void CacheRenderers()
    {
        rends = GetComponentsInChildren<Renderer>();
        originalEmissions = new Color[rends.Length];
        hadEmission = new bool[rends.Length];

        for (int i = 0; i < rends.Length; i++)
        {
            var mat = rends[i].material;
            hadEmission[i] = mat.IsKeywordEnabled("_EMISSION");
            originalEmissions[i] = mat.HasProperty(EmissionColorID)
                ? mat.GetColor(EmissionColorID)
                : Color.black;
        }
    }

    void Update()
    {
        if (!pulsing || rends == null) return;

        // Ease the glow in when first armed, so it doesn't pop
        armBlend = Mathf.MoveTowards(armBlend, 1f, Time.deltaTime * fadeInSpeed);

        // Breathing pulse — never fully dark, so it stays readable
        float wave = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        float pulse = (1f - pulseDepth) + pulseDepth * wave;

        ApplyEmission(armBlend * pulse);
    }

    private void ApplyEmission(float strength)
    {
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;

            var mat = rends[i].material;
            if (!mat.HasProperty(EmissionColorID)) continue;

            if (strength > 0.001f)
            {
                mat.EnableKeyword("_EMISSION");
                Color add = highlightColor * glowIntensity * strength;
                mat.SetColor(EmissionColorID, originalEmissions[i] + add);
            }
            else
            {
                mat.SetColor(EmissionColorID, originalEmissions[i]);
                if (!hadEmission[i])
                    mat.DisableKeyword("_EMISSION");
            }
        }
    }

    private void ClearGlow()
    {
        if (rends == null) return;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            var mat = rends[i].material;
            if (!mat.HasProperty(EmissionColorID)) continue;
            mat.SetColor(EmissionColorID, originalEmissions[i]);
            if (!hadEmission[i])
                mat.DisableKeyword("_EMISSION");
        }
    }

    public void OnClicked()
    {
        // Guard — only responds once the hazard has armed it via Setup()
        if (owner == null) return;

        pulsing = false;
        armBlend = 0f;
        ClearGlow();

        // Clear the global lock so other hazards become interactable again.
        if (ActiveTarget == this)
            ActiveTarget = null;

        owner.CompleteHazard();
    }

    public void OnHoverEnter() { }
    public void OnHoverExit() { }
}