using UnityEngine;

public class SimulationInteractable : MonoBehaviour, IInteractable
{
    // --- WHAT STEP DOES THIS OBJECT BELONG TO? ---
    // 8 steps — TPASS broken into its 5 sub-actions.
    public enum SimStep
    {
        SoundAlarm = 1,
        GrabExtinguisher = 2,
        TPASS_Twist = 3,
        TPASS_Pull = 4,
        TPASS_Aim = 5,
        TPASS_Squeeze = 6,
        TPASS_Sweep = 7,
        Evacuate = 8
    }

    // -------------------------------------------------------
    // INSPECTOR FIELDS
    // -------------------------------------------------------

    [Header("Step Configuration")]
    [SerializeField] private SimStep requiredStep;
    [SerializeField] private bool isCorrectAction = true;

    [Header("Reporting")]
    [Tooltip("Readable label sent to Laravel for what this object represents, e.g. 'AlarmPanel' or 'WrongObject'. Leave blank to just use this GameObject's name.")]
    [SerializeField] private string actionName = "";

    [Header("Wrong Action Settings")]
    [SerializeField] private float timePenalty = 20f;

    [TextArea]
    [SerializeField] private string educationalTip = "Enter tip here...";

    [Header("One-Time Use")]
    [SerializeField] private bool disableAfterCorrectUse = true;

    [Header("Extinguisher Grab (only for GrabExtinguisher step)")]
    // If this object is the grabbable extinguisher, drag its
    // ExtinguisherGrab component here. On a correct Grab tap,
    // we call Grab() to fly it into the hands.
    [SerializeField] private ExtinguisherGrab extinguisherGrab;

    // -------------------------------------------------------
    // HOVER HIGHLIGHT — blue outline effect
    //
    // Instead of changing the object's own color, we create a
    // hidden, slightly-larger, inside-out BLUE COPY of every
    // piece of this object when the game starts. We simply show
    // or hide these copies when the player looks at (or away
    // from) the object.
    //
    // The outline material is built AUTOMATICALLY in code below —
    // you never need to create or drag in a material yourself.
    // Every object using this script shares the exact same one.
    // -------------------------------------------------------

    // "static" = shared by EVERY object using this script, instead
    // of each one making its own separate copy. Since they should
    // all look identical, we only need to build this ONE time,
    // total, no matter how many extinguishers/objects use it.
    private static Material sharedOutlineMaterial;

    // Builds the outline material the FIRST time any object needs
    // it. Every object after that just reuses this same one.
    private static Material GetOutlineMaterial()
    {
        if (sharedOutlineMaterial == null)
        {
            // Shader.Find looks up our outline shader by its name,
            // the same way you'd search for a file by its filename.
            Shader outlineShader = Shader.Find("Custom/OutlineUnlit");

            if (outlineShader == null)
            {
                Debug.LogWarning("[SimulationInteractable] Could not find the 'Custom/OutlineUnlit' shader. " +
                                  "Make sure OutlineUnlit.shader is in your project, and added to " +
                                  "Project Settings > Graphics > Always Included Shaders.");
                return null;
            }

            sharedOutlineMaterial = new Material(outlineShader);
            sharedOutlineMaterial.SetColor("_OutlineColor", new Color(0.2f, 0.6f, 1f, 1f)); // blue
            sharedOutlineMaterial.SetFloat("_OutlineWidth", 0.02f);
        }

        return sharedOutlineMaterial;
    }

    // Keeps track of every outline copy THIS object creates, so we
    // can show/hide them all together later.
    private System.Collections.Generic.List<GameObject> outlineCopies
        = new System.Collections.Generic.List<GameObject>();

    private void Start()
    {
        Material outlineMat = GetOutlineMaterial();
        if (outlineMat == null) return; // warning already logged above

        // Find every visible piece of this object (Tank, Nozzle,
        // Pin, Triggers, etc.) — GetComponentsInChildren (plural)
        // catches ALL of them, not just one.
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();

        foreach (MeshFilter originalFilter in meshFilters)
        {
            // Create a new, empty child object to hold the outline
            // copy of this specific piece.
            GameObject outlineObj = new GameObject(originalFilter.gameObject.name + "_Outline");
            outlineObj.transform.SetParent(originalFilter.transform, false);

            // Copy the SAME shape (mesh) as the original piece —
            // the outline needs to match the exact silhouette.
            MeshFilter outlineFilter = outlineObj.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = originalFilter.sharedMesh;

            // Paint it using our auto-built outline material.
            MeshRenderer outlineRenderer = outlineObj.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMat;

            // Hidden until the player actually hovers over it.
            outlineObj.SetActive(false);

            outlineCopies.Add(outlineObj);
        }
    }

    public void OnHoverEnter()
    {
        // NEW: don't show the outline once this object's step is
        // already done (e.g. the extinguisher after it's been
        // grabbed). Tapping it again does nothing useful, so
        // showing "you can interact with this" would be misleading.
        if (SimulationManager.Instance != null)
        {
            int currentStep = SimulationManager.Instance.CurrentStep;
            if (currentStep > (int)requiredStep) return;
        }

        foreach (GameObject copy in outlineCopies)
        {
            if (copy != null) copy.SetActive(true);
        }
    }

    public void OnHoverExit()
    {
        foreach (GameObject copy in outlineCopies)
        {
            if (copy != null) copy.SetActive(false);
        }
    }

    // Returns actionName if set, otherwise falls back to the
    // GameObject's own name — so nothing breaks on objects you
    // haven't customized yet.
    private string ResolveActionName()
    {
        return string.IsNullOrEmpty(actionName) ? gameObject.name : actionName;
    }

    // -------------------------------------------------------
    // CALLED BY — HazardInteractionManager when this object is hit.
    // -------------------------------------------------------
    public void Interact()
    {
        // PHASE GUARD — only run in Phase 2.
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1) return;

        // Safety check — if SimulationManager doesn't exist, do nothing.
        if (SimulationManager.Instance == null) return;

        // Block all taps while a hand animation is currently playing.
        if (HandAnimationController.Instance != null &&
            HandAnimationController.Instance.IsAnimating) return;

        // Get the current step from SimulationManager.
        int currentStep = SimulationManager.Instance.CurrentStep;

        // Is the player on the right step for this object?
        if (currentStep != (int)requiredStep)
        {
            if ((int)requiredStep > currentStep)
            {
                // Tapped an object belonging to a FUTURE step — e.g.
                // grabbing the extinguisher before sounding the alarm.
                // This is a real mistake: penalize it.
                Debug.Log($"{gameObject.name} — tapped too early! " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}. Penalty: -{timePenalty}s");

                SimulationInteractable.SimStep expectedStep = (SimulationInteractable.SimStep)currentStep;

                SimulationManager.Instance.RegisterWrongAction(
                    expectedStep,
                    ResolveActionName(),
                    timePenalty,
                    educationalTip
                );
            }
            else
            {
                // Tapped an object from an ALREADY completed step (e.g.
                // the extinguisher, which stays active/grabbable — not
                // a real mistake, just a leftover tap. No penalty.
                Debug.Log($"{gameObject.name} tapped but step already completed. " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}");
            }

            return;
        }

        if (isCorrectAction)
        {
            // --- CORRECT TAP ---
            Debug.Log($"{gameObject.name} — correct action for step {currentStep}!");

            // Play the matching hand animation for TPASS sub-steps.
            PlayHandAnimationForStep(requiredStep);

            // If this is the grab step, fly the extinguisher into the hands.
            if (requiredStep == SimStep.GrabExtinguisher && extinguisherGrab != null)
            {
                extinguisherGrab.Grab();
            }

            SimulationManager.Instance.RegisterCorrectAction(requiredStep, ResolveActionName());

            // Turn this object off so it can't be tapped again.
            // NOTE: for the grab step we usually do NOT disable, because
            // the same object becomes the held extinguisher. So leave
            // disableAfterCorrectUse = false on the grabbable extinguisher.
            if (disableAfterCorrectUse)
                gameObject.SetActive(false);
        }
        else
        {
            // --- WRONG TAP ---
            Debug.Log($"{gameObject.name} — WRONG action! Penalty: -{timePenalty}s");
            SimulationManager.Instance.RegisterWrongAction(requiredStep, ResolveActionName(), timePenalty, educationalTip);
        }
    }

    // -------------------------------------------------------
    // Maps each TPASS sub-step to its matching hand animation.
    // -------------------------------------------------------
    private void PlayHandAnimationForStep(SimStep step)
    {
        if (HandAnimationController.Instance == null) return;

        switch (step)
        {
            case SimStep.TPASS_Twist:
                HandAnimationController.Instance.PlayTwist();
                break;
            case SimStep.TPASS_Pull:
                HandAnimationController.Instance.PlayPull();
                break;
            case SimStep.TPASS_Aim:
                HandAnimationController.Instance.PlayAim();
                break;
            case SimStep.TPASS_Squeeze:
                HandAnimationController.Instance.PlaySqueeze();
                break;
            case SimStep.TPASS_Sweep:
                HandAnimationController.Instance.PlaySweep();
                break;
                // SoundAlarm, GrabExtinguisher, Evacuate — no hand animation needed
        }
    }
}