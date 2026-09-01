using System.Collections.Generic;
using UnityEngine;

// -------------------------------------------------------
// SimulationInteractable — Office Phase 2 tap target.
//
// -------------------------------------------------------
// TWO IMPORTANT FIXES IN THIS VERSION — READ THESE
// -------------------------------------------------------
//
// 1. THE ANTI-SPAM GUARD WAS DEAD.
//
//    The old line was:
//        if (HandAnimationController.Instance.IsAnimating) return;
//
//    IsAnimating is only ever set inside HandAnimationController's two
//    coroutines. Once the arms moved to Animation Rigging and all three
//    hand-off flags were ticked, neither coroutine runs — every Play
//    method now returns early. So IsAnimating is permanently false and
//    that guard has been blocking nothing.
//
//    Office Phase 2 therefore had NO lockout while a step's choreography
//    was playing: the player could spam through TPASS faster than the
//    animations, and steps could register out of order.
//
//    Kitchen never had this problem because KitchenInteractable checks
//    SimulationManager.IsStepBusy instead. Office now checks the same
//    thing. TEST THIS — it is a real behaviour change.
//
// 2. THE OUTLINE MATERIAL WAS STATIC.
//
//    sharedOutlineMaterial was shared by EVERY object using this script.
//    That was fine when the outline was toggled with SetActive, but the
//    width is now animated — and writing a width to a shared material
//    would drive every prop in the scene at once.
//
//    It is now one material per object. The copies belonging to a single
//    object still share theirs, so a multi-part extinguisher is still
//    one material, not six.
//
// 3. THE SIM-ACTIVE GUARD, added to match Kitchen.
//
//    SimulationManager.RegisterCorrectAction opens with
//    `if (!simActive) return;` and says nothing when it bails. A tap
//    landing before the timer starts is silently discarded — but
//    extinguisherGrab.Grab() would still run, so the grab LOOKED
//    successful while the step never reached the results array.
//
//    Kitchen hit this because the towel sits near the spawn point.
//    Office's extinguisher is across the room, so it never showed up —
//    but the hole was the same. Guarded now, before anything with a side
//    effect.
//
// -------------------------------------------------------
// THE HIGHLIGHT — TWO TIERS, matching Phase 1
// -------------------------------------------------------
//   LOOK  — thin outline, breathing. Strongest near the middle of the
//           view, fading toward the screen edges.
//   HOVER — full width, springs on with a pop and a flash. Fires when a
//           finger is actually on the object.
//
// WHY LOOK REVEAL IS NEEDED AT ALL:
// In the Editor hover follows the mouse continuously. A phone has no
// cursor, so with tap-anywhere targeting "hover" on device means A
// FINGER IS PRESSED RIGHT NOW — nothing hinted that anything was
// tappable until the player had already guessed and pressed.
//
// This is easier to justify in Phase 2 than Phase 1: the alarm and the
// extinguisher are not hidden, and what is being assessed is the ORDER
// and the technique. Showing what is in play says nothing about what to
// do with it or when.
//
// -------------------------------------------------------
// WRONG-ACTION FEEDBACK
// -------------------------------------------------------
// A wrong tap used to produce only a red row in the HUD log. Now the
// object itself flashes red and the phone gives a heavy buzz.
//
// This is not decoration. Making the wrong action FEEL wrong — before it
// is read as text — is the teaching. The feedback lands in about a fifth
// of a second, well before anyone has finished reading the tip.
// -------------------------------------------------------

public class SimulationInteractable : MonoBehaviour, IInteractable
{
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

    [Header("Step Configuration")]
    [SerializeField] private SimStep requiredStep;

    [Tooltip("Untick for decoy objects — things that LOOK like the right " +
             "answer but are not. Tapping one costs the full penalty.")]
    [SerializeField] private bool isCorrectAction = true;

    [Header("Reporting")]
    [Tooltip("Readable label sent to Laravel, e.g. 'AlarmPanel'. Leave blank " +
             "to fall back to this GameObject's name.")]
    [SerializeField] private string actionName = "";

    [Header("Wrong Action Settings")]
    [SerializeField] private float timePenalty = 20f;

    [TextArea]
    [SerializeField] private string educationalTip = "Enter tip here...";

    [Header("One-Time Use")]
    [Tooltip("Hide this object after a correct tap.\n\n" +
             "MUST BE FALSE on the grabbable extinguisher — that object is " +
             "not consumed by the tap, it BECOMES the held extinguisher.")]
    [SerializeField] private bool disableAfterCorrectUse = true;

    [Header("Extinguisher Grab (GrabExtinguisher step only)")]
    [Tooltip("The ExtinguisherGrab component on the grabbable extinguisher. " +
             "On a correct tap it flies into the hands.")]
    [SerializeField] private ExtinguisherGrab extinguisherGrab;

    [Header("Outline")]
    [Tooltip("Untick to skip the outline entirely.")]
    [SerializeField] private bool showOutline = true;

    [SerializeField] private Color outlineColor = new Color(0.2f, 0.6f, 1f, 1f);

    [Tooltip("Full width at full hover. Scale it with the prop.")]
    [SerializeField] private float outlineWidth = 0.02f;

    [Header("Look Reveal (no finger needed)")]
    [Tooltip("Outline appears when the player LOOKS toward this object. " +
             "Still gated — nothing shows before the timer starts or after " +
             "this object's step has passed.")]
    [SerializeField] private bool enableLookReveal = true;

    [Range(0f, 1f)]
    [Tooltip("How near the middle of the view the object must be.\n\n" +
             "0   = ANYWHERE on screen, full strength, no falloff\n" +
             "0.5 = roughly the middle third\n" +
             "1   = almost dead centre only")]
    [SerializeField] private float focusTightness = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Outline width on the look tier as a fraction of full hover " +
             "width. Keep it well below 1 — a glance should read clearly " +
             "differently from a press.")]
    [SerializeField] private float revealStrength = 0.35f;

    [Tooltip("Metres. Beyond this, no reveal however directly the player " +
             "looks — so the whole room cannot be read from the doorway.")]
    [SerializeField] private float maxRevealDistance = 8f;

    [Tooltip("Hide the reveal when a wall or desk is in the way. The check " +
             "runs five times a second, not every frame.")]
    [SerializeField] private bool requireLineOfSight = true;

    [Header("Feel")]
    [Range(0f, 1f)]
    [Tooltip("Drives the hover spring. Low is a slow swell, high is an " +
             "instant snap with a visible kick.")]
    [SerializeField] private float snappiness = 0.7f;

    [Range(1f, 1.5f)]
    [Tooltip("How far the spring overshoots before settling. Set to 1 for " +
             "no pop.")]
    [SerializeField] private float popAmount = 1.25f;

    [Range(0f, 1f)]
    [Tooltip("How much the colour whitens at the peak of the pop.")]
    [SerializeField] private float popFlash = 0.5f;

    [Range(0f, 0.5f)]
    [Tooltip("Slow breath on the look tier. 0 holds perfectly steady.")]
    [SerializeField] private float breatheAmount = 0.15f;

    [Tooltip("Breaths per second, roughly.")]
    [SerializeField] private float breatheSpeed = 1.8f;

    [Header("Wrong Action Feedback")]
    [Tooltip("Flash this object red on a wrong tap. Lands well before the " +
             "player has finished reading the tip in the HUD log.")]
    [SerializeField] private bool flashOnWrong = true;

    [SerializeField] private Color wrongFlashColor = new Color(1f, 0.15f, 0.1f, 1f);

    [Tooltip("Seconds the red flash lasts.")]
    [SerializeField] private float wrongFlashDuration = 0.45f;

    [Tooltip("Heavy buzz on a wrong tap. Needs the VIBRATE permission in " +
             "the Android manifest or it fails silently on device.")]
    [SerializeField] private bool hapticOnWrong = true;

    // -------------------------------------------------------
    private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    // ONE material per object, not one for the whole project. The copies
    // belonging to this object share it, so a six-piece extinguisher is
    // still one material rather than six.
    private Material outlineMaterial;
    private readonly List<Renderer> outlineRenderers = new List<Renderer>();

    private bool isHovered;
    private float hoverBlend;
    private float hoverVelocity;
    private float lookBlend;
    private float breathPhase;
    private float wrongFlashTimer;

    private float lastWidth = -1f;
    private Color lastColor;

    private Camera cam;
    private Plane[] frustumPlanes;
    private Renderer[] boundsRenderers;
    private Collider ownCollider;
    private bool sightClear = true;
    private float nextSightCheck;
    private const float SIGHT_INTERVAL = 0.2f;

    private void Start()
    {
        ownCollider = GetComponentInChildren<Collider>();

        // Gathered BEFORE any outline geometry is created, so the outline
        // shells never inflate the bounds they are meant to trace.
        boundsRenderers = GetComponentsInChildren<Renderer>();
        cam = ResolveCamera();

        // Own breath phase and own check offset, so a room of props does not
        // pulse in lockstep or raycast on the same frame.
        breathPhase = Random.Range(0f, Mathf.PI * 2f);
        nextSightCheck = Time.time + Random.Range(0f, SIGHT_INTERVAL);

        if (showOutline) BuildOutline();
    }

    // -------------------------------------------------------
    // Creates a slightly larger, inside-out copy of every mesh piece,
    // wearing the outline shader. The copies are parented to the pieces
    // they mirror, so they follow any movement for free.
    //
    // Note this needs MeshFilters. A skinned mesh has none — that is why
    // the Kitchen towel uses a different technique entirely.
    // -------------------------------------------------------
    private void BuildOutline()
    {
        Shader outlineShader = Shader.Find("Custom/OutlineUnlit");

        if (outlineShader == null)
        {
            Debug.LogWarning("[SimulationInteractable] Shader 'Custom/OutlineUnlit' not " +
                             "found. Add OutlineUnlit.shader to Project Settings > " +
                             "Graphics > Always Included Shaders.");
            return;
        }

        outlineMaterial = new Material(outlineShader);
        outlineMaterial.SetColor(OutlineColorID, outlineColor);
        outlineMaterial.SetFloat(OutlineWidthID, 0f);   // hidden until revealed

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();

        foreach (MeshFilter originalFilter in meshFilters)
        {
            GameObject outlineObj = new GameObject(originalFilter.gameObject.name + "_Outline");
            outlineObj.transform.SetParent(originalFilter.transform, false);

            MeshFilter outlineFilter = outlineObj.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = originalFilter.sharedMesh;

            MeshRenderer r = outlineObj.AddComponent<MeshRenderer>();
            r.sharedMaterial = outlineMaterial;

            // An outline is a hint, not an object. It must not darken the
            // room or turn up in reflections as a floating shell.
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // Renderer disabled rather than the GameObject deactivated —
            // cheaper to flip, and the transform keeps following its parent.
            r.enabled = false;

            outlineRenderers.Add(r);
        }
    }

    private void Update()
    {
        if (outlineMaterial == null) return;

        if (wrongFlashTimer > 0f)
            wrongFlashTimer -= Time.deltaTime;

        UpdateLookReveal();
        UpdateOutline();
    }

    // -------------------------------------------------------
    // IS THIS OBJECT ALLOWED TO ADVERTISE ITSELF RIGHT NOW
    //
    // Both tiers share this. Advertising a tap that would be silently
    // discarded is what taught Kitchen players to grab the towel early.
    // -------------------------------------------------------
    private bool IsAdvertisable()
    {
        if (SimulationManager.Instance == null) return false;
        if (!SimulationManager.Instance.IsSimActive) return false;

        // Past its step this object is inert. Matters most for the
        // extinguisher, which stays in the player's hands for the rest of the
        // run — without this it would light up every time they looked down.
        if (SimulationManager.Instance.CurrentStep > (int)requiredStep) return false;

        return true;
    }

    // -------------------------------------------------------
    // LOOK REVEAL — how directly is the player looking at this.
    // -------------------------------------------------------
    private void UpdateLookReveal()
    {
        if (!enableLookReveal || !IsAdvertisable())
        {
            lookBlend = 0f;
            return;
        }

        if (cam == null)
        {
            cam = ResolveCamera();
            if (cam == null) { lookBlend = 0f; return; }
        }

        // WHOLE-OBJECT SAMPLING.
        //
        // This used to test ONE point — the collider's bounds centre — for
        // distance, on-screen and line of sight alike. Fine for a monitor.
        // Wrong for the extension cord, which snakes metres across the floor.
        //
        // With a single point the WHOLE cord lit or went dark depending on
        // where that midpoint happened to be. Look down, the midpoint lands
        // on screen and it glows; tilt up, the midpoint leaves the screen and
        // the entire cord goes dark with half of it still plainly visible at
        // the bottom. That is the works-at-the-bottom-not-the-top behaviour.
        //
        // The BOUNDING BOX is tested now, so any visible part counts.
        Bounds b = ComputeBounds();

        Vector3 point = b.center;
        Vector3 eye = cam.transform.position;

        // Distance to the NEAREST part of the object, not its centre. A cord
        // you are standing on top of is close, even if its midpoint is across
        // the room. SqrDistance returns 0 when the point is inside the box.
        if (b.SqrDistance(eye) > maxRevealDistance * maxRevealDistance)
        {
            lookBlend = 0f;
            return;
        }

        // Frustum test against the whole box. This is what catches an object
        // whose centre is off screen while part of it is still in view.
        if (frustumPlanes == null) frustumPlanes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(cam, frustumPlanes);

        if (!GeometryUtility.TestPlanesAABB(frustumPlanes, b))
        {
            lookBlend = 0f;
            return;
        }

        Vector3 vp = cam.WorldToViewportPoint(point);

        // The centre can sit behind the camera while part of the object is
        // still in front — a long cord running past your feet does this.
        // WorldToViewportPoint mirrors coordinates for negative z, so rather
        // than reject it, treat it as dead centre. The frustum test above has
        // already confirmed something is visible.
        if (vp.z <= 0f) vp = new Vector3(0.5f, 0.5f, 1f);

        // 0 at dead centre, 1 at the edge of the screen.
        Vector2 offset = new Vector2(vp.x - 0.5f, vp.y - 0.5f) * 2f;
        float off = offset.magnitude;

        // FULL-SCREEN MODE. At 0 there is no falloff at all — anything
        // visible reveals at full strength wherever it sits on screen,
        // including the very corners.
        //
        // The graded falloff only kicks in above 0. Its outer edge reaches
        // 1.6, past the screen corner at 1.414, so even a low-but-nonzero
        // tightness still lights the corners rather than clipping them.
        if (focusTightness <= 0.001f)
        {
            lookBlend = 1f;
        }
        else
        {
            float outer = Mathf.Lerp(1.6f, 0.30f, focusTightness);
            float inner = outer * 0.3f;

            float t = Mathf.InverseLerp(outer, inner, off);
            lookBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        }

        if (requireLineOfSight)
        {
            if (Time.time >= nextSightCheck)
            {
                nextSightCheck = Time.time + SIGHT_INTERVAL;
                sightClear = HasLineOfSight(eye, b);
            }

            if (!sightClear) lookBlend = 0f;
        }
    }

    // -------------------------------------------------------
    // THE OUTLINE — combine both tiers plus the wrong flash.
    // -------------------------------------------------------
    private void UpdateOutline()
    {
        StepSpring();

        // Breath is on the LOOK tier only. An outline that keeps pulsing
        // while you press it feels uncertain; a steady one feels confirmed.
        float breath = 1f;
        if (breatheAmount > 0f)
        {
            breath = 1f + Mathf.Sin(Time.time * breatheSpeed + breathPhase) * breatheAmount;
        }

        float look = lookBlend * revealStrength * breath;

        // Max, not add — otherwise pressing an object you are also looking
        // straight at would outline thicker than pressing one off to the
        // side, and the same action would look different by angle.
        float strength = Mathf.Max(look, hoverBlend);

        Color colour = outlineColor;

        // WRONG FLASH overrides everything. It has to be readable even on an
        // object the player is not looking at and not pressing — that is the
        // whole point, since a decoy tap usually comes from a glance.
        if (wrongFlashTimer > 0f && wrongFlashDuration > 0f)
        {
            float f = Mathf.Clamp01(wrongFlashTimer / wrongFlashDuration);

            // Sharp arrival, slower decay. A wrong action should hit, not swell.
            float shape = f * f;

            strength = Mathf.Max(strength, shape * popAmount);
            colour = Color.Lerp(outlineColor, wrongFlashColor, shape);
        }
        else if (popAmount > 1.001f && strength > 1f)
        {
            // POP FLASH — whiten at the peak of the overshoot, settle into
            // the real colour on the way back down.
            float over = Mathf.InverseLerp(1f, popAmount, strength);
            colour = Color.Lerp(outlineColor, Color.white, over * popFlash);
        }

        float width = outlineWidth * strength;

        // Dirty check. Once settled this stops all writes.
        if (Mathf.Abs(width - lastWidth) < 0.0001f && ColorsClose(colour, lastColor))
            return;

        lastWidth = width;
        lastColor = colour;

        outlineMaterial.SetFloat(OutlineWidthID, width);
        outlineMaterial.SetColor(OutlineColorID, colour);

        // Flip the renderers rather than leaving invisible geometry drawing
        // every frame.
        bool visible = width > 0.0001f;
        for (int i = 0; i < outlineRenderers.Count; i++)
        {
            if (outlineRenderers[i] == null) continue;
            if (outlineRenderers[i].enabled != visible)
                outlineRenderers[i].enabled = visible;
        }
    }

    // -------------------------------------------------------
    // WHICH CAMERA
    //
    // Camera.main returns whichever camera holds the MainCamera tag. Both
    // scenes have a stationary "Main Camera" at the top of the hierarchy
    // AND a PlayerCamera under Player. If the tag is on the stationary
    // one, every frustum and viewport test here is computed from a camera
    // that never moves — and the reveal then has nothing to do with where
    // the player is actually looking.
    //
    // HazardInteractionManager sits ON the camera that renders and
    // receives taps, so borrowing its camera removes the guesswork
    // entirely and guarantees reveal and tapping never disagree.
    //
    // Cached statically: every object in the scene wants the same answer,
    // so the search runs once rather than once per object. A destroyed
    // camera compares equal to null, so a scene change re-resolves it.
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
    // WHAT COUNTS AS "THIS OBJECT" ON SCREEN
    //
    // Renderer bounds, not the collider. The collider is a rough box
    // placed for tapping — on the extension cord it is offset two metres
    // from the object origin and does not follow the cable at all. The
    // renderers are what the player actually SEES, so they are what
    // "visible on screen" has to be measured against.
    // -------------------------------------------------------
    private Bounds ComputeBounds()
    {
        if (boundsRenderers != null && boundsRenderers.Length > 0)
        {
            bool started = false;
            Bounds result = new Bounds(transform.position, Vector3.zero);

            for (int i = 0; i < boundsRenderers.Length; i++)
            {
                if (boundsRenderers[i] == null) continue;
                if (!boundsRenderers[i].enabled) continue;

                if (!started) { result = boundsRenderers[i].bounds; started = true; }
                else result.Encapsulate(boundsRenderers[i].bounds);
            }

            if (started) return result;
        }

        if (ownCollider != null) return ownCollider.bounds;
        return new Bounds(transform.position, Vector3.one * 0.1f);
    }

    // -------------------------------------------------------
    // LINE OF SIGHT — two probes, not one.
    //
    // A single cast at the bounds centre fails on a long floor cable: its
    // midpoint can sit inside a desk, which marks the whole cord blocked
    // from every angle no matter how plainly visible it is. The second
    // probe aims at the nearest part of the box to the camera, which is
    // usually the part the player can actually see.
    // -------------------------------------------------------
    private bool HasLineOfSight(Vector3 eye, Bounds b)
    {
        if (ProbeClear(eye, b.center)) return true;

        Vector3 near = b.ClosestPoint(eye);
        if ((near - b.center).sqrMagnitude > 0.0001f && ProbeClear(eye, near))
            return true;

        return false;
    }

    private bool ProbeClear(Vector3 eye, Vector3 target)
    {
        if (Physics.Linecast(eye, target, out RaycastHit hit,
                             Physics.DefaultRaycastLayers,
                             QueryTriggerInteraction.Ignore))
        {
            // IsChildOf returns true for the transform itself too.
            return hit.transform.IsChildOf(transform);
        }

        // Nothing in the way at all.
        return true;
    }

    // -------------------------------------------------------
    // THE SPRING
    //
    // Damping is applied as exp(-d * dt) rather than a plain multiply, so
    // the settle rate does not change with frame rate — a spring tuned at
    // 60fps must not wobble differently at 40.
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

    private void TriggerWrongFeedback()
    {
        if (flashOnWrong) wrongFlashTimer = wrongFlashDuration;
        if (hapticOnWrong) Haptics.Heavy();
    }

    // -------------------------------------------------------
    // CALLED BY HazardInteractionManager when this object is hit.
    // -------------------------------------------------------
    public void Interact()
    {
        // PHASE GUARD — Phase 2 only.
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1) return;
        if (SimulationManager.Instance == null) return;

        // THE TIMER IS NOT RUNNING YET.
        //
        // Must come BEFORE the grab further down. RegisterCorrectAction would
        // drop the step in silence, but extinguisherGrab.Grab() would still
        // fly the extinguisher into the player's hands — so the grab LOOKED
        // successful while the step never reached the results array.
        //
        // Deliberately silent: the player has not done anything wrong, the
        // simulation simply has not started.
        if (!SimulationManager.Instance.IsSimActive)
        {
            Debug.Log($"[Sim] {gameObject.name} ignored — simulation has not started yet.");
            return;
        }

        // ANTI-SPAM — REPLACES THE DEAD IsAnimating CHECK.
        //
        // The old guard read HandAnimationController.IsAnimating, which is now
        // permanently false because the arms moved to Animation Rigging and
        // its coroutines never run. So nothing was blocking taps during a
        // step's choreography. This is the same guard Kitchen has always used.
        //
        // The player is EARLY, not WRONG — ignored silently, no penalty.
        if (SimulationManager.Instance.IsStepBusy)
        {
            Debug.Log($"[Sim] {gameObject.name} ignored — step still animating.");
            return;
        }

        int currentStep = SimulationManager.Instance.CurrentStep;

        // ---- WRONG STEP ----
        if (currentStep != (int)requiredStep)
        {
            if ((int)requiredStep > currentStep)
            {
                // A FUTURE step — e.g. grabbing the extinguisher before
                // sounding the alarm. A real mistake, charged accordingly.
                Debug.Log($"[Sim] {gameObject.name} tapped too early. " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}. " +
                          $"Penalty -{timePenalty}s");

                TriggerWrongFeedback();

                SimStep expectedStep = (SimStep)currentStep;

                SimulationManager.Instance.RegisterWrongAction(
                    expectedStep,
                    ResolveActionName(),
                    timePenalty,
                    educationalTip
                );
            }
            else
            {
                // An already-completed step. A stray tap on the extinguisher
                // now in hand, not a knowledge failure. No penalty, and no
                // flash — flashing here would punish a harmless tap.
                Debug.Log($"[Sim] {gameObject.name} tapped after its step. " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}");
            }

            return;
        }

        // ---- CORRECT STEP ----
        if (isCorrectAction)
        {
            Debug.Log($"[Sim] {gameObject.name} — correct for step {currentStep}.");

            PlayHandAnimationForStep(requiredStep);

            // THE HANDOFF. Started BEFORE registering the step, so the flight
            // is already underway as the step advances. Safe to keep that
            // order ONLY because of the IsSimActive guard above.
            if (requiredStep == SimStep.GrabExtinguisher && extinguisherGrab != null)
            {
                extinguisherGrab.Grab();
            }

            SimulationManager.Instance.RegisterCorrectAction(requiredStep, ResolveActionName());

            // Drop the outline immediately rather than letting the spring ease
            // it out over the animation. The step is past, so leaving it lit
            // would suggest another tap does something.
            isHovered = false;
            hoverBlend = 0f;
            hoverVelocity = 0f;

            // NOTE: leave disableAfterCorrectUse FALSE on the grabbable
            // extinguisher — that object is not consumed, it becomes the held
            // extinguisher.
            if (disableAfterCorrectUse)
                gameObject.SetActive(false);
        }
        else
        {
            // ---- DECOY ----
            Debug.Log($"[Sim] {gameObject.name} — WRONG object. Penalty -{timePenalty}s");

            TriggerWrongFeedback();

            SimulationManager.Instance.RegisterWrongAction(
                requiredStep,
                ResolveActionName(),
                timePenalty,
                educationalTip
            );
        }
    }

    // -------------------------------------------------------
    // Maps each TPASS sub-step to its matching hand animation.
    //
    // With the hand-off flags ticked these are all no-ops that simply fire
    // OnAnimationComplete — the real motion comes from the masked Animator
    // layers and the IK targets. Kept because unticking a flag restores the
    // legacy path in one click.
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
                // SoundAlarm, GrabExtinguisher, Evacuate — no hand animation.
        }
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    // -------------------------------------------------------
    public void ResetForReplay()
    {
        gameObject.SetActive(true);

        isHovered = false;
        hoverBlend = 0f;
        hoverVelocity = 0f;
        lookBlend = 0f;
        wrongFlashTimer = 0f;
        lastWidth = -1f;   // force the next write through

        if (outlineMaterial != null)
            outlineMaterial.SetFloat(OutlineWidthID, 0f);

        for (int i = 0; i < outlineRenderers.Count; i++)
            if (outlineRenderers[i] != null) outlineRenderers[i].enabled = false;
    }

    private void OnDestroy()
    {
        if (outlineMaterial != null) Destroy(outlineMaterial);
    }
}