using UnityEngine;

public class ClickableHazard : MonoBehaviour
{
    [Header("Interaction")]
    public float interactDistance = 5f;

    [Header("Hover Highlight")]
    [Tooltip("Warm white reads as premium; avoid saturated warning colours")]
    public Color hoverGlow = new Color(1f, 0.957f, 0.878f);

    [Range(0f, 3f)]
    public float glowIntensity = 0.55f;

    [Tooltip("Slow breathing feels calm and expensive")]
    public float pulseSpeed = 1.6f;

    [Range(0f, 1f)]
    public float pulseAmount = 0.2f;

    [Tooltip("Snappy response to looking at things")]
    public float fadeSpeed = 14f;

    private Renderer[] objectRenderers;
    private Color[] originalEmissions;
    private bool[] hadEmission;

    private bool isHovered = false;
    private float glowBlend = 0f;

    private float clickCooldown = 0f;
    private const float COOLDOWN_TIME = 1.2f;

    private bool hasBeenFound = false;

    private HazardDialogue dialogueData;
    private bool awaitingAction = false;

    private ScreenPower screenPower;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private void Start()
    {
        CacheRenderers();

        if (GetComponent<Collider>() == null)
            Debug.LogWarning($"[ClickableHazard] '{gameObject.name}' has no Collider!");

        dialogueData = GetComponent<HazardDialogue>();

        if (dialogueData == null)
            Debug.LogWarning($"[ClickableHazard] '{gameObject.name}' has no HazardDialogue!");

        screenPower = GetComponent<ScreenPower>();

        HazardCounterManager.Instance.RegisterHazard();
    }

    // Store each renderer's starting emission so we can restore it exactly.
    private void CacheRenderers()
    {
        objectRenderers = GetComponentsInChildren<Renderer>();
        originalEmissions = new Color[objectRenderers.Length];
        hadEmission = new bool[objectRenderers.Length];

        for (int i = 0; i < objectRenderers.Length; i++)
        {
            var mat = objectRenderers[i].material;
            hadEmission[i] = mat.IsKeywordEnabled("_EMISSION");
            originalEmissions[i] = mat.HasProperty(EmissionColorID)
                ? mat.GetColor(EmissionColorID)
                : Color.black;
        }
    }

    private void Update()
    {
        if (clickCooldown > 0f)
            clickCooldown -= Time.deltaTime;

        UpdateGlow();
    }

    // Smoothly eases the glow in/out, with a soft pulse while hovered.
    private void UpdateGlow()
    {
        if (objectRenderers == null) return;

        float target = isHovered ? 1f : 0f;
        glowBlend = Mathf.MoveTowards(glowBlend, target, Time.deltaTime * fadeSpeed);

        if (glowBlend <= 0.001f && target == 0f)
        {
            ApplyEmission(0f);
            return;
        }

        // Sine pulse keeps it feeling alive rather than a flat highlight
        float pulse = 1f - pulseAmount
            + pulseAmount * ((Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);

        ApplyEmission(glowBlend * pulse);
    }

    private void ApplyEmission(float strength)
    {
        for (int i = 0; i < objectRenderers.Length; i++)
        {
            if (objectRenderers[i] == null) continue;

            var mat = objectRenderers[i].material;
            if (!mat.HasProperty(EmissionColorID)) continue;

            if (strength > 0.001f)
            {
                mat.EnableKeyword("_EMISSION");
                Color add = hoverGlow * glowIntensity * strength;
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

    public void OnClicked()
    {
        if (clickCooldown > 0f)
        {
            Debug.Log("[ClickableHazard] Cooling down...");
            return;
        }

        clickCooldown = COOLDOWN_TIME;

        if (dialogueData == null)
        {
            Debug.LogWarning($"[ClickableHazard] '{gameObject.name}' has no HazardDialogue to show!");
            return;
        }

        HandleDialogueFlow();
    }

    private void HandleDialogueFlow()
    {
        if (dialogueData.isComplete) return;
        if (awaitingAction) return;
        if (DialogueManager.Instance == null) return;
        if (DialogueManager.Instance.IsDialogueActive()) return;

        DialogueManager.Instance.StartDialogue(dialogueData.lines, OnDialogueFinished);
    }

    private void OnDialogueFinished()
    {
        if (dialogueData.actionTarget == null)
        {
            CompleteHazard();
            return;
        }

        awaitingAction = true;

        HazardActionTarget target =
            dialogueData.actionTarget.GetComponent<HazardActionTarget>();
        if (target == null)
            target = dialogueData.actionTarget.AddComponent<HazardActionTarget>();

        target.Setup(this);
    }

    public void CompleteHazard()
    {
        if (dialogueData != null && dialogueData.isComplete) return;

        awaitingAction = false;
        isHovered = false;

        if (screenPower != null)
            screenPower.TurnOff();

        if (dialogueData != null)
        {
            dialogueData.isComplete = true;

            if (dialogueData.completionLines != null &&
                dialogueData.completionLines.Length > 0 &&
                DialogueManager.Instance != null)
            {
                DialogueManager.Instance.StartDialogue(
                    dialogueData.completionLines,
                    FinishAndCount,
                    true);
                return;
            }
        }

        FinishAndCount();
    }

    private void FinishAndCount()
    {
        if (!hasBeenFound)
        {
            hasBeenFound = true;
            HazardCounterManager.Instance.HazardFound();
        }

        Debug.Log("[ClickableHazard] Hazard complete: " + gameObject.name);
    }

    public void OnHoverEnter()
    {
        // Completed hazards no longer highlight
        if (dialogueData != null && dialogueData.isComplete) return;
        isHovered = true;
    }

    public void OnHoverExit()
    {
        isHovered = false;
    }
}