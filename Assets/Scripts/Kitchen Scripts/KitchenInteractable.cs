using System.Collections.Generic;
using UnityEngine;

// -------------------------------------------------------
// KitchenInteractable — Kitchen's Phase 2 tap target, the counterpart to
// SimulationInteractable in Office.
//
// -------------------------------------------------------
// HOW THE REVEAL WORKS — same rule as ClickableHazard
// -------------------------------------------------------
// One ray is cast from the CENTRE OF THE SCREEN each frame. If it hits
// this object, the outline appears. That is the whole rule.
//
// Everything that used to be here — bounding boxes, frustum tests, focus
// tightness, a separate line-of-sight raycast — is gone. The centre ray
// answers all of it at once:
//
//   Occlusion — a cabinet in the way means the ray hits the CABINET.
//   Distance  — the ray has a length.
//   Off screen — cannot be hit.
//
// ONE RAY PER FRAME, TOTAL. Cached statically and stamped with the frame
// number, so ten props share one cast rather than each doing their own.
//
// It uses the SAME collider the tap system uses, so reveal and tap can
// never disagree: if it outlines, tapping it will work.
//
// -------------------------------------------------------
// WHY A SEPARATE SCRIPT FROM SimulationInteractable
//
// Two blockers, both of which fail SILENTLY rather than erroring:
//
// 1. THE ENUM. SimulationInteractable types requiredStep as SimStep, which
//    contains GrabExtinguisher and the five TPASS sub-actions. Kitchen runs
//    on KitchenStep. Assigning an extinguisher step to a towel and relying on
//    the integers happening to line up is exactly the coincidence that breaks
//    the day someone reorders an enum.
//
// 2. THE OUTLINE. SimulationInteractable builds its highlight from
//    GetComponentsInChildren<MeshFilter>(). A rigged towel has no MeshFilter —
//    its mesh lives on a SkinnedMeshRenderer. That loop finds nothing, creates
//    zero outline copies, and the player gets no hint the towel can be tapped.
//    No error, just a dead-looking prop.
//
// -------------------------------------------------------
// HOW THE OUTLINE WORKS HERE, AND WHY IT DIFFERS
//
// Office spawns a duplicate GameObject per mesh piece wearing an inverted-hull
// shader. That cannot work on a skinned mesh — the copy would need the same
// bones, bind poses and per-frame skinning, which means rebuilding half of
// Unity's skinning pipeline.
//
// Instead this appends the outline material as an EXTRA SUBMESH SLOT on the
// existing renderer. The GPU skins the mesh once and draws it twice: normal
// pass, then the inverted hull. The outline deforms with the towel for free,
// because it IS the towel.
//
// SCALE COMPENSATION — NEW, AND IT IS WHY THE TOWEL WAS A BLUE BALLOON.
//
// _OutlineWidth offsets vertices along their normals in OBJECT space. A prop
// with a large transform scale multiplies that offset, so the same 0.02 that
// gives a neat rim on an Office prop produced a shell several metres across on
// the towel — big enough to wrap the camera and fill the screen blue.
//
// The width is now divided by the object's lossy scale, so Outline Width means
// METRES on every prop and 0.02 looks the same everywhere. Props already at
// scale 1 are completely unaffected.
//
// -------------------------------------------------------
// THE ADVERTISING GATE — WHY THE OUTLINE REFUSES TO SHOW SOMETIMES
//
// Before the timer starts, a tap here is silently discarded (see the guard in
// Interact). Lighting up would promise something the object cannot deliver —
// and that is exactly the promise that caused players to tap the towel early
// and lose the step.
//
// After this object's step has passed it is inert. That matters MORE now that
// the towel survives its own step: it stays in the player's hands for the rest
// of the run, so without the gate it would light up every time they glanced
// down at it.
//
// -------------------------------------------------------
// THE GRAB HANDOFF — ONE OBJECT, EXACTLY LIKE THE EXTINGUISHER
//
// There is ONE towel. It starts draped over the sink, and on a correct tap
// TowelGrab flies it into TowelAnchor and parents it to the camera.
//
// So disableAfterCorrectUse must be FALSE on the towel. The object is not
// consumed by the tap — it BECOMES the held towel.
//
// -------------------------------------------------------
// THE SIM-ACTIVE GUARD — WHY IT IS THERE
//
// SimulationManager.RegisterCorrectAction opens with `if (!simActive) return;`
// and says nothing when it bails. So a tap landing before BeginSimulation()
// fires is SILENTLY DISCARDED: no feedback row, no step advanced, no entry in
// the results array.
//
// That would be survivable if nothing else happened. It is not — look at the
// order in Interact() below. towelGrab.Grab() runs FIRST. So the towel flew
// into the player's hands, looked exactly like a successful grab, and the step
// was never recorded. The admin panel then showed Wet as step 1 with GrabTowel
// missing entirely, and nothing anywhere pointed at the cause.
//
// The guard turns an invisible half-action into a clean no-op. It goes at the
// TOP, before anything with a side effect.
// -------------------------------------------------------

public class KitchenInteractable : MonoBehaviour, IInteractable
{
    [Header("Step Configuration")]
    [Tooltip("Which KitchenStep this object belongs to. Tapping it on any " +
             "other step is either a penalty (too early) or ignored (already " +
             "done).")]
    [SerializeField] private KitchenStep requiredStep = KitchenStep.GrabTowel;

    [Tooltip("Untick for decoy objects — things that LOOK like the right " +
             "answer but are not. Tapping one costs the full penalty.")]
    [SerializeField] private bool isCorrectAction = true;

    [Header("Reporting")]
    [Tooltip("Readable label sent to Laravel, e.g. 'TowelOnSink'. Leave blank " +
             "to fall back to this GameObject's name.")]
    [SerializeField] private string actionName = "";

    [Header("Wrong Action Settings")]
    [SerializeField] private float timePenalty = 20f;

    [TextArea]
    [SerializeField] private string educationalTip = "Enter tip here...";

    [Header("One-Time Use")]
    [Tooltip("Hide this object after a correct tap.\n\n" +
             "MUST BE FALSE ON THE TOWEL. The towel is not consumed by the " +
             "tap — it flies into the player's hands and stays there.")]
    [SerializeField] private bool disableAfterCorrectUse = false;

    [Header("Towel Grab (GrabTowel step only)")]
    [Tooltip("The TowelGrab component on this same object. Left empty, it is " +
             "found automatically.")]
    [SerializeField] private TowelGrab towelGrab;

    [Header("Hover Outline")]
    [Tooltip("Untick to skip the outline entirely.")]
    [SerializeField] private bool showOutline = true;

    [SerializeField] private Color outlineColor = new Color(0.2f, 0.6f, 1f, 1f);

    [Tooltip("Outline thickness in METRES at full press.\n\n" +
             "This is divided by the object's scale before it reaches the " +
             "shader, so 0.02 looks the same on the towel as it does on an " +
             "Office prop. Without that division a scaled-up rig produced a " +
             "shell metres across.")]
    [SerializeField] private float outlineWidth = 0.02f;

    [Header("Look Reveal (centre of screen)")]
    [Tooltip("Outline appears when the centre of the screen is on this " +
             "object. Still gated — nothing shows before the timer starts or " +
             "after this object's step has passed.")]
    [SerializeField] private bool enableLookReveal = true;

    [Range(0f, 1f)]
    [Tooltip("Outline width on the look tier as a fraction of full press " +
             "width. Keep it well below 1 — a glance should read clearly " +
             "differently from a press.")]
    [SerializeField] private float revealStrength = 0.35f;

    [Tooltip("How fast the look outline fades in and out. Higher is snappier.")]
    [SerializeField] private float revealFadeSpeed = 10f;

    [Header("Shared Ray Settings (same on every prop)")]
    [Tooltip("How far the centre ray reaches, in metres.")]
    [SerializeField] private float lookRayDistance = 8f;

    [Tooltip("Forgiveness radius in metres. The centre does not have to be " +
             "dead on the object. 0 disables the assist.")]
    [SerializeField] private float lookAssistRadius = 0.25f;

    [Header("Feel")]
    [Range(0f, 1f)]
    [Tooltip("Drives the press spring. Low is a slow swell, high is an " +
             "instant snap with a visible kick.")]
    [SerializeField] private float snappiness = 0.7f;

    [Range(1f, 1.5f)]
    [Tooltip("How far the spring overshoots before settling. 1 = no pop.")]
    [SerializeField] private float popAmount = 1.25f;

    [Range(0f, 1f)]
    [Tooltip("How much the colour whitens at the peak of the pop.")]
    [SerializeField] private float popFlash = 0.5f;

    [Range(0f, 0.5f)]
    [Tooltip("Slow breath on the look tier. 0 holds perfectly steady.")]
    [SerializeField] private float breatheAmount = 0.12f;

    [Tooltip("Breaths per second, roughly.")]
    [SerializeField] private float breatheSpeed = 2.2f;

    // -------------------------------------------------------
    // THE SHARED CENTRE RAY
    //
    // Stamped with the frame number so the cast happens ONCE per frame no
    // matter how many props ask for it.
    //
    // A precise ray first, then a fat SphereCast if that missed. Precision
    // wins, so aiming straight at one of two nearby props still picks the one
    // you meant — the same two-pass rule the tap system uses.
    // -------------------------------------------------------
    private static int lookFrame = -1;
    private static Transform lookHitTransform;

    private void UpdateSharedLookRay()
    {
        if (lookFrame == Time.frameCount) return;
        lookFrame = Time.frameCount;
        lookHitTransform = null;

        Camera c = ResolveCamera();
        if (c == null) return;

        Ray ray = c.ScreenPointToRay(
            new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, lookRayDistance,
                            Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Collide))
        {
            lookHitTransform = hit.transform;
            return;
        }

        // Started forward of the camera, because a sphere beginning AT the
        // camera already overlaps the player's own collider and Unity returns
        // a useless zero-distance hit.
        if (lookAssistRadius > 0f)
        {
            Vector3 origin = ray.origin + ray.direction * lookAssistRadius;
            float distance = Mathf.Max(0f, lookRayDistance - lookAssistRadius);

            if (Physics.SphereCast(origin, lookAssistRadius, ray.direction,
                                   out RaycastHit wide, distance,
                                   Physics.DefaultRaycastLayers,
                                   QueryTriggerInteraction.Collide))
            {
                lookHitTransform = wide.transform;
            }
        }
    }

    // -------------------------------------------------------
    // WHICH CAMERA
    //
    // Camera.main returns whichever camera holds the MainCamera tag, and the
    // scene has a stationary "Main Camera" as well as PlayerCamera. If the tag
    // is on the stationary one, the ray is cast from a camera that never moves.
    //
    // HazardInteractionManager sits ON the camera that renders and takes taps,
    // so borrowing its camera removes the guesswork.
    // -------------------------------------------------------
    private static Camera cachedCam;

    private static Camera ResolveCamera()
    {
        if (cachedCam != null) return cachedCam;

        var manager = Object.FindFirstObjectByType<HazardInteractionManager>();
        if (manager != null) cachedCam = manager.GetComponent<Camera>();

        if (cachedCam == null) cachedCam = Camera.main;

        return cachedCam;
    }

    // -------------------------------------------------------
    private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    private Material outlineInstance;
    private Renderer outlineRenderer;
    private float scaleCompensation = 1f;

    private bool isHovered;
    private float hoverBlend;
    private float hoverVelocity;
    private float lookBlend;
    private float breathPhase;

    private float lastWidth = -1f;
    private Color lastColor;

    private void Start()
    {
        // Convenience: the towel's grab component lives on this same object,
        // so find it rather than making it another Inspector slot to forget.
        if (towelGrab == null) towelGrab = GetComponent<TowelGrab>();

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[KitchenInteractable] '{gameObject.name}' has no Collider! " +
                             "Without one the centre ray can never hit it, so it will " +
                             "never reveal and never be tappable.");

        breathPhase = Random.Range(0f, Mathf.PI * 2f);

        if (showOutline) SetupOutline();
    }

    private void SetupOutline()
    {
        Shader outlineShader = Shader.Find("Custom/OutlineUnlit");

        if (outlineShader == null)
        {
            Debug.LogWarning("[KitchenInteractable] Shader 'Custom/OutlineUnlit' not found. " +
                             "Add OutlineUnlit.shader to Project Settings > Graphics > " +
                             "Always Included Shaders.");
            return;
        }

        // GetComponentInChildren, because an FBX import puts the mesh on a
        // child of the object the script sits on.
        outlineRenderer = GetComponentInChildren<Renderer>();
        if (outlineRenderer == null) return;

        // SCALE COMPENSATION — see the header. The shader offsets vertices in
        // OBJECT space, so a scaled-up rig multiplies the width. Dividing by
        // the largest scale axis makes Outline Width mean metres everywhere.
        Vector3 ls = outlineRenderer.transform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        scaleCompensation = maxScale > 0.0001f ? 1f / maxScale : 1f;

        outlineInstance = new Material(outlineShader);
        outlineInstance.SetColor(OutlineColorID, outlineColor);
        outlineInstance.SetFloat(OutlineWidthID, 0f);   // hidden until revealed

        List<Material> mats = new List<Material>(outlineRenderer.sharedMaterials);
        mats.Add(outlineInstance);
        outlineRenderer.materials = mats.ToArray();
    }

    private void Update()
    {
        if (outlineInstance == null) return;

        UpdateLookReveal();
        UpdateOutline();
    }

    // -------------------------------------------------------
    // IS THIS OBJECT ALLOWED TO ADVERTISE ITSELF RIGHT NOW
    //
    // Both tiers share this. Advertising a tap that would be silently
    // discarded is what taught players to grab the towel early.
    // -------------------------------------------------------
    private bool IsAdvertisable()
    {
        if (SimulationManager.Instance == null) return false;
        if (!SimulationManager.Instance.IsSimActive) return false;
        if (SimulationManager.Instance.CurrentStep > (int)requiredStep) return false;
        return true;
    }

    // -------------------------------------------------------
    // LOOK REVEAL — is the centre of the screen on this object.
    // -------------------------------------------------------
    private void UpdateLookReveal()
    {
        bool looked = false;

        if (enableLookReveal && IsAdvertisable())
        {
            UpdateSharedLookRay();

            // IsChildOf returns true for the transform itself too, so this
            // catches a hit on any piece of a multi-part prop.
            looked = lookHitTransform != null &&
                     lookHitTransform.IsChildOf(transform);
        }

        // Simple exponential fade — no spring here. The pop belongs to the
        // press; a glance should just ease in.
        float t = 1f - Mathf.Exp(-revealFadeSpeed * Time.deltaTime);
        lookBlend = Mathf.Lerp(lookBlend, looked ? 1f : 0f, t);

        if (lookBlend < 0.001f) lookBlend = 0f;
    }

    private void UpdateOutline()
    {
        StepSpring();

        // Breath is on the LOOK tier only. An outline that keeps pulsing while
        // you press it feels uncertain; a steady one feels confirmed.
        float breath = 1f;
        if (breatheAmount > 0f)
        {
            breath = 1f + Mathf.Sin(Time.time * breatheSpeed + breathPhase) * breatheAmount;
        }

        float look = lookBlend * revealStrength * breath;

        // Max, not add — otherwise pressing something you are also centred on
        // would outline thicker than pressing something off to the side.
        float strength = Mathf.Max(look, hoverBlend);

        // FLASH — whiten at the peak of the overshoot, settle into the real
        // colour on the way back down. Computed BEFORE the dirty check so a
        // colour-only change is not swallowed.
        Color tint = outlineColor;
        if (popAmount > 1.001f && strength > 1f)
        {
            float over = Mathf.InverseLerp(1f, popAmount, strength);
            tint = Color.Lerp(outlineColor, Color.white, over * popFlash);
        }

        float width = outlineWidth * strength * scaleCompensation;

        // Dirty check. Writing shader values every frame for every prop is
        // waste once the outline has settled.
        if (Mathf.Abs(width - lastWidth) < 0.000001f && ColorsClose(tint, lastColor))
            return;

        lastWidth = width;
        lastColor = tint;

        outlineInstance.SetFloat(OutlineWidthID, width);
        outlineInstance.SetColor(OutlineColorID, tint);
    }

    // -------------------------------------------------------
    // THE SPRING — the press only.
    //
    // Damping is applied as exp(-d * dt) rather than a plain multiply, so the
    // settle rate does not change with frame rate.
    // -------------------------------------------------------
    private void StepSpring()
    {
        float stiffness = Mathf.Lerp(55f, 340f, snappiness);
        float damping = Mathf.Lerp(26f, 10f, snappiness);

        float target = isHovered ? 1f : 0f;
        float dt = Time.deltaTime;

        hoverVelocity += (target - hoverBlend) * stiffness * dt;
        hoverVelocity *= Mathf.Exp(-damping * dt);
        hoverBlend += hoverVelocity * dt;

        // Overshoot above 1 is intentional. Below 0 is not — a negative width
        // would invert the hull.
        hoverBlend = Mathf.Clamp(hoverBlend, 0f, popAmount);

        if (!isHovered && hoverBlend < 0.001f && Mathf.Abs(hoverVelocity) < 0.001f)
        {
            hoverBlend = 0f;
            hoverVelocity = 0f;
        }
    }

    private static bool ColorsClose(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f
            && Mathf.Abs(a.g - b.g) < 0.01f
            && Mathf.Abs(a.b - b.b) < 0.01f;
    }

    public void OnHoverEnter()
    {
        // Do not advertise a tap that would do nothing.
        if (!IsAdvertisable()) return;
        isHovered = true;
    }

    public void OnHoverExit()
    {
        isHovered = false;
    }

    private string ResolveActionName()
    {
        return string.IsNullOrEmpty(actionName) ? gameObject.name : actionName;
    }

    // -------------------------------------------------------
    // Called by HazardInteractionManager via IInteractable.
    // -------------------------------------------------------
    public void Interact()
    {
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1) return;
        if (SimulationManager.Instance == null) return;

        // THE TIMER IS NOT RUNNING YET.
        //
        // Must come BEFORE the grab below. RegisterCorrectAction would drop
        // the step in silence, but towelGrab.Grab() would still fly the towel
        // into the player's hands — so the grab LOOKED successful while
        // GrabTowel never reached the results array. See the header.
        if (!SimulationManager.Instance.IsSimActive)
        {
            Debug.Log($"[Kitchen] {gameObject.name} ignored — simulation has not started yet.");
            return;
        }

        // ANTI-SPAM. The previous step's choreography is still playing, so the
        // player is EARLY, not WRONG. Ignored silently — no penalty, no step
        // advanced. Same rule WCTLButtonManager applies to its buttons.
        if (SimulationManager.Instance.IsStepBusy)
        {
            Debug.Log($"[Kitchen] {gameObject.name} ignored — step still animating.");
            return;
        }

        int currentStep = SimulationManager.Instance.CurrentStep;

        // ---- WRONG STEP ----
        if (currentStep != (int)requiredStep)
        {
            if ((int)requiredStep > currentStep)
            {
                // A FUTURE step — e.g. covering the flame before wetting the
                // towel. A real mistake, charged accordingly.
                Debug.Log($"[Kitchen] {gameObject.name} tapped too early. " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}. " +
                          $"Penalty -{timePenalty}s");

                SimulationManager.Instance.RegisterWrongAction(
                    DescribeStep(currentStep),
                    ResolveActionName(),
                    timePenalty,
                    educationalTip
                );
            }
            else
            {
                // An already-completed step. A stray tap on the towel now in
                // hand, not a knowledge failure. No penalty.
                Debug.Log($"[Kitchen] {gameObject.name} tapped after its step. " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}");
            }

            return;
        }

        // ---- CORRECT STEP ----
        if (isCorrectAction)
        {
            Debug.Log($"[Kitchen] {gameObject.name} — correct for step {currentStep}.");

            // THE HANDOFF. Started BEFORE registering the step, so the flight
            // is already underway as the step advances. Safe to keep that
            // order ONLY because of the IsSimActive guard above.
            //
            // The flight is NOT awaited. TowelGrab owns its own timing and the
            // step lockout in SimulationManager covers the window.
            if (requiredStep == KitchenStep.GrabTowel && towelGrab != null)
            {
                towelGrab.Grab();
            }

            SimulationManager.Instance.RegisterCorrectAction(
                requiredStep.ToString(),
                ResolveActionName()
            );

            // Kill the outline immediately rather than letting the spring ease
            // it out over the grab animation.
            isHovered = false;
            hoverBlend = 0f;
            hoverVelocity = 0f;
            lookBlend = 0f;

            if (disableAfterCorrectUse)
                gameObject.SetActive(false);
        }
        else
        {
            // ---- DECOY ----
            Debug.Log($"[Kitchen] {gameObject.name} — WRONG object. Penalty -{timePenalty}s");

            SimulationManager.Instance.RegisterWrongAction(
                requiredStep.ToString(),
                ResolveActionName(),
                timePenalty,
                educationalTip
            );
        }
    }

    // -------------------------------------------------------
    // Casting a raw int straight to KitchenStep produces a garbage enum value
    // when the number is outside 1..5, and ToString() on a garbage value
    // returns the NUMBER as text. That would send "7" to Laravel as a step
    // name and silently corrupt the attempt record. Range-check first.
    // -------------------------------------------------------
    private string DescribeStep(int step)
    {
        return System.Enum.IsDefined(typeof(KitchenStep), step)
            ? ((KitchenStep)step).ToString()
            : $"Step{step}";
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // Call from wherever the Kitchen run resets, alongside
    // TowelGrab.ResetToTimba(), TowelWetnessController.ResetToDry(),
    // TowelDipController.ResetToRest(), TowelCoverController.ResetToRest(),
    // KitchenValveController.ResetToStart() and
    // WCTLButtonManager.ResetForReplay().
    //
    // THIS script only restores its own state — active, un-outlined, tappable
    // again. Putting the towel back on the sink belongs to TowelGrab, which is
    // what moved it. Reset only this one and the towel stays in the player's
    // hands while the tap target believes it is still on the sink.
    // -------------------------------------------------------
    public void ResetForReplay()
    {
        gameObject.SetActive(true);

        isHovered = false;
        hoverBlend = 0f;
        hoverVelocity = 0f;
        lookBlend = 0f;
        lastWidth = -1f;   // force the next write through

        if (outlineInstance != null)
            outlineInstance.SetFloat(OutlineWidthID, 0f);
    }
}