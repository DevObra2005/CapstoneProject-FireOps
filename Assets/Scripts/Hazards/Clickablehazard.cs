using System.Collections.Generic;
using UnityEngine;

// -------------------------------------------------------
// ClickableHazard — Phase 1 tap target and its highlight.
//
// -------------------------------------------------------
// HOW THE REVEAL WORKS
// -------------------------------------------------------
// A single ray is cast from the CENTRE OF THE SCREEN each frame. If it
// hits this hazard, the hazard glows. That is the whole rule.
//
// WHY THIS BEATS EVERYTHING ELSE TRIED HERE:
//
//   Sprawling meshes  — the ray hits the actual cable. No bounding box
//                       to wrap the whole room and always pass.
//   Occlusion         — a desk in the way means the ray hits the DESK.
//                       No line-of-sight check needed at all.
//   Distance          — the ray has a length. No distance maths.
//   Off screen        — cannot be hit. No frustum test.
//   Camera confusion  — one place resolves the camera, not three.
//
// ONE RAY PER FRAME, TOTAL. It is cached statically and stamped with
// the frame number, so ten hazards share one cast rather than each
// doing their own.
//
// It uses the SAME collider the tap system uses, so reveal and tap can
// never disagree: if it glows, tapping it will work.
//
// -------------------------------------------------------
// TWO TIERS
// -------------------------------------------------------
//   LOOK  — dim, breathing. The centre ray is on this object.
//   HOVER — bright, springs on with a pop and a flash. A finger is
//           actually pressing it.
//
// On a phone there is no cursor, so hover only exists while a finger is
// down. Look reveal is what makes the highlight visible the rest of the
// time.
//
// -------------------------------------------------------
// THE EXCLUSION LIST
// -------------------------------------------------------
// The highlight hits every child renderer, including the monitor's
// ScreenDisplay quad — already bright, and the glow ADDS to existing
// emission, which is what turned it cyan. One drag each to skip them.
//
// -------------------------------------------------------
// HIGHLIGHT MODE
// -------------------------------------------------------
// Emission — glow on the surface. Safe on anything, weak on thin things
//            and nearly invisible on very dark ones.
// Outline  — inverted-hull silhouette. NEVER on thin geometry: the hull
//            pushes vertices outward along their normals, so on a cable
//            it passes through itself and shows its back faces as a
//            blob. Object scale multiplies this.
// -------------------------------------------------------

public class ClickableHazard : MonoBehaviour
{
    public enum HighlightMode { Emission, Outline, Both }

    [Header("Highlight")]
    [Tooltip("Emission — glow on the surface. Safe on anything.\n" +
             "Outline  — silhouette. Never on thin geometry like cables.\n" +
             "Both     — each doing half the work.")]
    public HighlightMode highlightMode = HighlightMode.Emission;

    [Tooltip("Renderers that must NEVER be highlighted. Drag the monitor's " +
             "ScreenDisplay here, plus any lamp — they are already bright, " +
             "and the glow ADDS to existing emission.")]
    public List<Renderer> excludedRenderers = new List<Renderer>();

    [Tooltip("Warm white reads as premium. Avoid saturated warning colours.")]
    public Color hoverGlow = new Color(1f, 0.957f, 0.878f);

    [Range(0f, 3f)]
    [Tooltip("Emission brightness at full hover.\n\n" +
             "Around 0.55 suits a mid-tone prop in a dim room. A very DARK " +
             "object needs much more — 1.5 or higher — because emission is " +
             "ADDED to the surface, and a small addition to near-black is " +
             "invisible.")]
    public float glowIntensity = 0.55f;

    [Tooltip("Outline thickness at full hover. Outline mode only.")]
    public float outlineWidth = 0.02f;

    [Header("Look Reveal (centre of screen)")]
    [Tooltip("Glow when the centre of the screen is on this object. Untick " +
             "for a hazard that should never hint at itself.")]
    public bool enableLookReveal = true;

    [Range(0f, 1f)]
    [Tooltip("Brightness of the look tier as a fraction of hover.\n\n" +
             "Keep it well below 1 — a glance should read clearly " +
             "differently from a press.")]
    public float revealStrength = 0.35f;

    [Tooltip("How fast the look glow fades in and out. Higher is snappier.")]
    public float revealFadeSpeed = 10f;

    [Header("Feel")]
    [Range(0f, 1f)]
    [Tooltip("Drives the hover spring. Low is a slow swell, high is an " +
             "instant snap with a visible kick.")]
    public float snappiness = 0.7f;

    [Range(1f, 1.5f)]
    [Tooltip("How far the spring overshoots before settling. 1 = no pop.")]
    public float popAmount = 1.25f;

    [Range(0f, 1f)]
    [Tooltip("How much the colour whitens at the peak of the pop.")]
    public float popFlash = 0.55f;

    [Range(0f, 0.5f)]
    [Tooltip("Slow breath on the look tier. 0 holds perfectly steady.")]
    public float breatheAmount = 0.15f;

    [Tooltip("Breaths per second, roughly.")]
    public float breatheSpeed = 1.8f;

    // -------------------------------------------------------
    // THE SHARED CENTRE RAY
    //
    // Stamped with the frame number so the cast happens ONCE per frame no
    // matter how many hazards ask for it.
    //
    // A precise ray first, then a fat SphereCast if that missed. Precision
    // wins, so aiming straight at one of two nearby objects still picks the
    // one you meant — same two-pass rule the tap system uses.
    // -------------------------------------------------------
    [Header("Shared Ray Settings (same on every hazard)")]
    [Tooltip("How far the centre ray reaches, in metres. Beyond this, " +
             "nothing reveals.")]
    public float lookRayDistance = 8f;

    [Tooltip("Forgiveness radius in metres. The centre does not have to be " +
             "dead on the object. 0 disables the assist.")]
    public float lookAssistRadius = 0.25f;

    private static int lookFrame = -1;
    private static Transform lookHitTransform;

    private void UpdateSharedLookRay()
    {
        // Already cast this frame by another hazard.
        if (lookFrame == Time.frameCount) return;
        lookFrame = Time.frameCount;
        lookHitTransform = null;

        Camera c = ResolveCamera();
        if (c == null) return;

        Ray ray = c.ScreenPointToRay(
            new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

        // Precise pass.
        if (Physics.Raycast(ray, out RaycastHit hit, lookRayDistance,
                            Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Collide))
        {
            lookHitTransform = hit.transform;
            return;
        }

        // Forgiving pass. Started forward of the camera, because a sphere
        // beginning AT the camera is already overlapping the player's own
        // collider and Unity returns a useless zero-distance hit.
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
    // Camera.main returns whichever camera holds the MainCamera tag, and
    // both scenes have a stationary "Main Camera" as well as PlayerCamera.
    // If the tag is on the stationary one, the ray is cast from a camera
    // that never moves.
    //
    // HazardInteractionManager sits ON the camera that renders and takes
    // taps, so borrowing its camera removes the guesswork.
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
    private Renderer[] highlightRenderers;
    private Color[] originalEmissions;
    private Material[] outlineInstances;
    private MaterialPropertyBlock mpb;

    private bool isHovered;
    private float hoverBlend;
    private float hoverVelocity;
    private float lookBlend;
    private float breathPhase;

    private float lastStrength = -1f;
    private Color lastTint;

    private float clickCooldown = 0f;
    private const float COOLDOWN_TIME = 1.2f;

    private bool hasBeenFound = false;

    private HazardDialogue dialogueData;
    private bool awaitingAction = false;

    private ScreenPower screenPower;
    private OilCleanupEffect oilCleanup;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    private bool UsesEmission => highlightMode != HighlightMode.Outline;
    private bool UsesOutline => highlightMode != HighlightMode.Emission;

    private void Start()
    {
        GatherRenderers();

        // Outline FIRST — it replaces the materials array, which would throw
        // away emission instances created before it.
        if (UsesOutline) SetupOutlines();
        if (UsesEmission) SetupEmission();

        if (GetComponent<Collider>() == null)
            Debug.LogWarning($"[ClickableHazard] '{gameObject.name}' has no Collider! " +
                             "Without one the centre ray can never hit it, so it will " +
                             "never reveal and never be tappable.");

        dialogueData = GetComponent<HazardDialogue>();
        if (dialogueData == null)
            Debug.LogWarning($"[ClickableHazard] '{gameObject.name}' has no HazardDialogue!");

        screenPower = GetComponent<ScreenPower>();
        oilCleanup = GetComponent<OilCleanupEffect>();

        // Own breath phase, so a room of hazards does not pulse in lockstep.
        breathPhase = Random.Range(0f, Mathf.PI * 2f);

        HazardCounterManager.Instance.RegisterHazard();
    }

    private void GatherRenderers()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>();
        List<Renderer> kept = new List<Renderer>(all.Length);

        foreach (var r in all)
        {
            if (r == null) continue;
            if (excludedRenderers != null && excludedRenderers.Contains(r)) continue;
            kept.Add(r);
        }

        highlightRenderers = kept.ToArray();
    }

    private void SetupEmission()
    {
        originalEmissions = new Color[highlightRenderers.Length];
        mpb = new MaterialPropertyBlock();

        for (int i = 0; i < highlightRenderers.Length; i++)
        {
            var mat = highlightRenderers[i].material;

            originalEmissions[i] = mat.HasProperty(EmissionColorID)
                ? mat.GetColor(EmissionColorID)
                : Color.black;

            // Set ONCE and never touched again. At black an enabled emission
            // keyword is visually free, and never toggling it avoids the
            // per-frame keyword thrash the original version paid for.
            if (mat.HasProperty(EmissionColorID))
                mat.EnableKeyword("_EMISSION");
        }
    }

    private void SetupOutlines()
    {
        Shader outlineShader = Shader.Find("Custom/OutlineUnlit");

        if (outlineShader == null)
        {
            Debug.LogWarning("[ClickableHazard] Shader 'Custom/OutlineUnlit' not found. " +
                             "Add it to Project Settings > Graphics > Always Included " +
                             "Shaders, or set Highlight Mode to Emission.");
            outlineInstances = new Material[0];
            return;
        }

        outlineInstances = new Material[highlightRenderers.Length];

        for (int i = 0; i < highlightRenderers.Length; i++)
        {
            var r = highlightRenderers[i];

            var m = new Material(outlineShader);
            m.SetColor(OutlineColorID, hoverGlow);
            m.SetFloat(OutlineWidthID, 0f);

            var mats = new List<Material>(r.sharedMaterials);
            mats.Add(m);
            r.materials = mats.ToArray();

            outlineInstances[i] = m;
        }
    }

    private void Update()
    {
        if (clickCooldown > 0f)
            clickCooldown -= Time.deltaTime;

        UpdateLookReveal();
        UpdateHighlight();
    }

    // -------------------------------------------------------
    // LOOK REVEAL — is the centre of the screen on this object.
    // -------------------------------------------------------
    private void UpdateLookReveal()
    {
        bool looked = false;

        if (enableLookReveal &&
            !(dialogueData != null && dialogueData.isComplete))
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

    // -------------------------------------------------------
    // THE HIGHLIGHT — combine both tiers, write only if it moved.
    // -------------------------------------------------------
    private void UpdateHighlight()
    {
        if (highlightRenderers == null) return;

        StepSpring();

        // Breath is on the look tier only. A highlight that keeps pulsing
        // while you press it feels uncertain; a steady one feels confirmed.
        float breath = 1f;
        if (breatheAmount > 0f)
        {
            breath = 1f + Mathf.Sin(Time.time * breatheSpeed + breathPhase) * breatheAmount;
        }

        float look = lookBlend * revealStrength * breath;

        // Max, not add — otherwise pressing something you are also centred on
        // would be brighter than pressing something off to the side, and the
        // same action would look different depending on the angle.
        float strength = Mathf.Max(look, hoverBlend);

        // FLASH — while the spring overshoots, push toward white, then settle
        // into the real tint on the way back down.
        Color tint = hoverGlow;
        if (popAmount > 1.001f && strength > 1f)
        {
            float over = Mathf.InverseLerp(1f, popAmount, strength);
            tint = Color.Lerp(hoverGlow, Color.white, over * popFlash);
        }

        // DIRTY CHECK. Once settled at rest this stops all writes, so an idle
        // room costs one float comparison per hazard.
        if (Mathf.Abs(strength - lastStrength) < 0.002f &&
            (strength <= 0.002f || ColorsClose(tint, lastTint)))
            return;

        lastStrength = strength;
        lastTint = tint;

        if (UsesEmission) ApplyEmission(strength, tint);
        if (UsesOutline) ApplyOutline(strength, tint);
    }

    // -------------------------------------------------------
    // THE SPRING — the press only.
    //
    // Damping is applied as exp(-d * dt) rather than a plain multiply, so
    // the settle rate does not change with frame rate.
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

        // Overshoot ABOVE 1 is intentional — that brief extra brightness is
        // the pop. Below 0 is not; it would invert the emission.
        hoverBlend = Mathf.Clamp(hoverBlend, 0f, popAmount);

        if (!isHovered && hoverBlend < 0.001f && Mathf.Abs(hoverVelocity) < 0.001f)
        {
            hoverBlend = 0f;
            hoverVelocity = 0f;
        }
    }

    private void ApplyEmission(float strength, Color tint)
    {
        if (originalEmissions == null) return;

        Color add = tint * glowIntensity * strength;

        for (int i = 0; i < highlightRenderers.Length; i++)
        {
            if (highlightRenderers[i] == null) continue;

            highlightRenderers[i].GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColorID, originalEmissions[i] + add);
            highlightRenderers[i].SetPropertyBlock(mpb);
        }
    }

    private void ApplyOutline(float strength, Color tint)
    {
        if (outlineInstances == null) return;

        float width = outlineWidth * strength;

        for (int i = 0; i < outlineInstances.Length; i++)
        {
            if (outlineInstances[i] == null) continue;

            outlineInstances[i].SetFloat(OutlineWidthID, width);
            outlineInstances[i].SetColor(OutlineColorID, tint);
        }
    }

    private static bool ColorsClose(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f
            && Mathf.Abs(a.g - b.g) < 0.01f
            && Mathf.Abs(a.b - b.b) < 0.01f;
    }

    // -------------------------------------------------------
    // INTERACTION — unchanged.
    // -------------------------------------------------------
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

        if (oilCleanup != null)
            oilCleanup.Clean();

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
        // Completed hazards no longer highlight.
        if (dialogueData != null && dialogueData.isComplete) return;
        isHovered = true;
    }

    public void OnHoverExit()
    {
        isHovered = false;
    }
}