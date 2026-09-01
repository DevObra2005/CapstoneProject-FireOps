using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// -------------------------------------------------------
// WHAT THIS DOES:
// Hover highlight and tap detection for:
//   Phase 1 — ClickableHazard (identify the hazard)
//   Phase 1 — HazardActionTarget (perform the correct action)
//   Transition — AlarmTarget / ExtinguisherTarget
//   Phase 2 — SimulationInteractable
//   Phase 2 — IInteractable (the evacuation Door)
//
// ACTION LOCK:
// While a HazardActionTarget is armed (ActiveTarget != null), only that
// armed target responds; everything else is ignored.
//
// -------------------------------------------------------
// TAP ANYWHERE — the big change in this version
// -------------------------------------------------------
// OLD: the ray always came from the centre of the screen. You turned the
// camera until the crosshair sat on the object, then tapped anywhere.
//
// NEW (tapAnywhere = true): the ray comes from WHERE YOUR FINGER LANDS.
// Tap the object directly. No centring.
//
// WHY THIS DOES NOT BREAK LOOK CONTROL:
// Your right thumb does both jobs now, but they are already told apart
// by the existing tap thresholds — a quick press that barely moves is a
// TAP, anything longer or further is a LOOK-SWIPE. That logic was
// already here; it just was not being used to pick a ray origin.
//
// WHAT CHANGED ABOUT HOVER:
// Centre-aim always had an aim point, so the reticle could preview what
// you were about to hit. Tap-anywhere has no aim point until a finger
// lands. So hover now FOLLOWS YOUR FINGER: press to preview, lift to
// confirm. Your ClickableHazard.OnHoverEnter / OnHoverExit are called
// exactly as before — they did not need to change.
//
// If the finger travels far enough to become a look-swipe, hover is
// dropped immediately, so highlights do not flicker across the room
// while you are turning.
//
// THE RETICLE IS NOW DECORATIVE.
// A centre crosshair no longer represents anything you can act on. Keep
// it as a dot if you like the look, or clear the Reticle field to hide
// it entirely. The press-punch still fires either way.
//
// ROLLING BACK: untick Tap Anywhere. Everything returns to centre-aim
// exactly as it was. One click, no code edit.
//
// -------------------------------------------------------
// EVERYTHING ELSE CARRIED OVER FROM THE PREVIOUS PASSES
// -------------------------------------------------------
// 1. ONE CAST, ONE PRIORITY CHAIN. Hover and click used to each run
//    their own cast and their own copy of the six-branch chain. Adding
//    an interactable meant editing two places; missing one failed
//    silently — which is what happened with the Door.
//
// 2. AIM ASSIST. Precise ray first (deliberate aim always wins),
//    SphereCast second only if the precise ray found nothing useful.
//    Works from a finger position just as well as from screen centre.
//
// 3. IGNORED LAYERS. The held extinguisher no longer blocks the ray.
//
// 4. TAP TRACKS WHICH FINGER. One shared flag used to let the left
//    thumb's joystick release complete the right thumb's tap.
//
// 5. PER-TOUCH UI CHECK. The old check returned true if ANY touch was
//    over UI, so holding the joystick swallowed every world tap.
//
// 6. FRAME-RATE INDEPENDENT SMOOTHING. Lerp(a, b, deltaTime * speed) is
//    a very common mistake — it eases at a different real-world rate at
//    40fps than at 60. Correct form: t = 1 - exp(-speed * deltaTime).
//
// 7. ASYMMETRIC ENTER / EXIT. Snapping on fast and easing off slow reads
//    as responsive; one speed both ways reads as mushy.
//
// 8. PRESS PUNCH. The reticle squashes and springs back, landing before
//    any game logic responds.
//
// 9. HAPTICS + AUDIO. Fire only on a real target, so feedback always
//    means "that did something".
// -------------------------------------------------------

public class HazardInteractionManager : MonoBehaviour
{
    [Header("Targeting Mode")]
    [Tooltip("ON  — tap the object directly, anywhere on screen.\n" +
             "OFF — old behaviour: aim the centre crosshair, then tap.\n\n" +
             "Untick to roll back to centre-aim with no code changes.")]
    [SerializeField] private bool tapAnywhere = true;

    [Header("Raycast")]
    [Tooltip("How far the player can reach, in metres.\n\n" +
             "NOTE with Tap Anywhere: you can now SEE and tap distant " +
             "objects that are still out of range, and nothing will " +
             "happen. If that feels broken rather than intentional, raise " +
             "this — but a short reach is also what forces the player to " +
             "walk to the hazard.")]
    public float maxRayDistance = 8f;

    [Tooltip("Aim assist radius in metres. Widens the ray so near-misses " +
             "still register.\n\n" +
             "0    = off, hairline ray, must be dead-on\n" +
             "0.25 = comfortable on a phone (recommended)\n" +
             "0.6+ = too loose. Nearby objects steal each other's taps.")]
    [SerializeField] private float aimAssistRadius = 0.25f;

    [Tooltip("Layers the ray IGNORES completely. Tick the layer your HELD " +
             "extinguisher and towel live on, plus your Player layer.\n\n" +
             "Replaces ExtinguisherRaycastToggle. Leave empty and this " +
             "behaves exactly like the old version.")]
    [SerializeField] private LayerMask ignoredLayers;

    public GameObject interactPrompt;

    [Header("Reticle — Colour")]
    [Tooltip("Optional. With Tap Anywhere on, the centre crosshair is " +
             "decorative — clear this field to hide it entirely.")]
    public UnityEngine.UI.Image reticle;
    public Color reticleIdleColor = new Color(1f, 1f, 1f, 0.55f);
    public Color reticleHitColor = new Color(0.98f, 0.76f, 0.18f, 1f);

    [Header("Reticle — Motion")]
    public float reticleScaleOnHit = 1.35f;

    [Tooltip("How fast the reticle reacts when it FINDS a target. " +
             "Should be clearly faster than exit speed.")]
    [SerializeField] private float reticleEnterSpeed = 22f;

    [Tooltip("How fast it relaxes when it LOSES a target. Lower than " +
             "enter speed — fast on, slow off reads as responsive.")]
    [SerializeField] private float reticleExitSpeed = 9f;

    [Header("Reticle — Press Punch")]
    [Tooltip("How far the reticle squashes on tap. 0.72 = 72% of normal. " +
             "Lower is punchier; below about 0.6 looks broken.\n\n" +
             "To CONFIRM it is working, temporarily set this to 0.4 and " +
             "Recover Speed to 3 — it becomes impossible to miss.")]
    [SerializeField] private float tapPunchScale = 0.72f;

    [Tooltip("How fast it springs back. Higher = snappier.")]
    [SerializeField] private float tapPunchRecoverSpeed = 9f;

    [Header("Feel — Audio")]
    [Tooltip("An AudioSource on this camera. Leave empty for no sound.")]
    [SerializeField] private AudioSource feedbackSource;

    [Tooltip("Soft tick when a target is first previewed. Keep it VERY " +
             "quiet — it fires often.")]
    [SerializeField] private AudioClip hoverClip;

    [Tooltip("Played on a successful tap of a real target.")]
    [SerializeField] private AudioClip tapClip;

    [Header("Feel — Haptics")]
    [Tooltip("Short buzz on a successful tap. Requires the VIBRATE " +
             "permission in your Android manifest or it fails silently.")]
    [SerializeField] private bool hapticOnTap = true;

    [Tooltip("Very light buzz when a target is previewed. OFF by default " +
             "— it can get buzzy. Try it and judge for yourself.")]
    [SerializeField] private bool hapticOnHover = false;

    [Header("Tap Tuning")]
    [Tooltip("Dead time after a successful tap, in seconds.\n\n" +
             "This USED to be 0.8s and was applied AFTER the tap was " +
             "handled instead of before — the ordering bug behind " +
             "double-recorded steps. Applied before now, so it can safely " +
             "be much shorter.\n\n" +
             "IF DOUBLE-RECORDING COMES BACK: raise this toward 0.8.")]
    [SerializeField] private float interactCooldownDuration = 0.35f;

    // -------------------------------------------------------
    // WHAT IS UNDER THE POINTER — filled once per frame.
    // -------------------------------------------------------
    private enum TargetKind
    {
        None,
        Alarm,
        Extinguisher,
        ActionTarget,
        Hazard,
        Sim,
        Interactable
    }

    private struct AimTarget
    {
        public TargetKind kind;
        public AlarmTarget alarm;
        public ExtinguisherTarget extinguisher;
        public HazardActionTarget actionTarget;
        public ClickableHazard hazard;
        public SimulationInteractable sim;
        public IInteractable interactable;

        // Detects "this is a DIFFERENT object than last frame", which is
        // what triggers the hover tick. Comparing kind alone would stay
        // silent when sliding between two hazards of the same type.
        public Object identity;

        public string hitName;
    }

    private AimTarget currentTarget;
    private Object lastIdentity;

    private ClickableHazard currentHovered;
    private SimulationInteractable currentHoveredSim;

    private Camera playerCamera;
    private Vector3 reticleBaseScale;
    private Vector3 reticleSmoothedScale;
    private float punch;             // 1 = fully pressed, 0 = resting

    private float interactCooldown = 0f;

    // Tap state — tied to ONE specific finger.
    private Vector2 tapStartPos;
    private Vector2 tapCurrentPos;
    private float tapStartTime;
    private int tapTouchId = -1;
    private bool tapIsCandidate;     // false once it becomes a look-swipe
    private const float TAP_MAX_DURATION = 0.35f;
    private const float TAP_MAX_MOVEMENT = 40f;

    private void OnEnable() { EnhancedTouchSupport.Enable(); }
    private void OnDisable() { EnhancedTouchSupport.Disable(); }

    private void Start()
    {
        playerCamera = GetComponent<Camera>();
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (interactPrompt != null)
            interactPrompt.SetActive(false);

        if (reticle != null)
        {
            reticleBaseScale = reticle.transform.localScale;
            reticleSmoothedScale = reticleBaseScale;
            reticle.color = reticleIdleColor;
        }
    }

    private void Update()
    {
        // Dialogue open — no aiming, no tapping, no highlight.
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive())
        {
            currentTarget = default;
            lastIdentity = null;
            ApplyHover(currentTarget);
            return;
        }

        // Touch state is read FIRST so hover can follow the finger this
        // same frame. It also keeps running during cooldown, so previews
        // never freeze just because a tap was recently handled.
        bool tapped = DetectTap(out Vector2 tapPosition);

        currentTarget = ResolveHoverTarget();

        // Landed on something NEW? That is the hover tick.
        if (currentTarget.identity != lastIdentity)
        {
            if (currentTarget.identity != null)
                PlayHoverFeedback();

            lastIdentity = currentTarget.identity;
        }

        ApplyHover(currentTarget);

        if (interactCooldown > 0f)
        {
            interactCooldown -= Time.deltaTime;
            return;
        }

        if (tapped)
        {
            // Visual punch fires for EVERY tap, hit or miss. The thumb is
            // acknowledged instantly, before any game logic runs.
            punch = 1f;

            // Cast at the exact release point. In tap-anywhere this is
            // what the player actually pointed at; in centre-aim mode the
            // hover result is already correct, so reuse it.
            AimTarget clicked = tapAnywhere
                ? ResolveTarget(tapPosition)
                : currentTarget;

            // Sound and buzz only on a real target, so feedback always
            // means "that did something".
            if (clicked.kind != TargetKind.None)
                PlayTapFeedback();

            // COOLDOWN IS SET FIRST, ON PURPOSE.
            // HandleClick can start a coroutine, open a dialogue, or
            // reload the scene. Anything re-entering before the line below
            // runs would fire the same step twice. Set the guard, THEN act.
            interactCooldown = interactCooldownDuration;
            HandleClick(clicked);
        }
    }

    // -------------------------------------------------------
    // WHERE HOVER LOOKS
    //
    // Centre-aim  → always the middle of the screen.
    // Tap-anywhere → the finger currently pressed, and only while that
    //                press is still a tap candidate. No finger down means
    //                no preview, which is correct: nothing is being aimed
    //                at.
    // -------------------------------------------------------
    private AimTarget ResolveHoverTarget()
    {
        if (playerCamera == null) return default;

        if (!tapAnywhere)
            return ResolveTarget(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

        Vector2 point = tapCurrentPos;
        bool havePoint = tapTouchId != -1 && tapIsCandidate;

#if UNITY_EDITOR
        // In the Editor the cursor is always visible, so let hover follow
        // it without needing the button held. Makes testing far easier.
        if (Mouse.current != null)
        {
            point = Mouse.current.position.ReadValue();
            havePoint = true;
        }
#endif

        if (!havePoint) return default;

        return ResolveTarget(point);
    }

    // -------------------------------------------------------
    // THE CAST — two passes, precise then forgiving.
    // Takes a screen point so it serves both targeting modes.
    // -------------------------------------------------------
    private AimTarget ResolveTarget(Vector2 screenPoint)
    {
        if (playerCamera == null) return default;

        Ray ray = playerCamera.ScreenPointToRay(screenPoint);

        // DefaultRaycastLayers already excludes Unity's built-in
        // "Ignore Raycast" layer. We remove our own ignored layers on top.
        int mask = Physics.DefaultRaycastLayers & ~ignoredLayers.value;

        // PASS 1 — precise. A deliberate hit beats aim assist, so when two
        // objects sit close you can always pick the one you mean.
        if (Physics.Raycast(ray, out RaycastHit preciseHit, maxRayDistance,
                            mask, QueryTriggerInteraction.Collide))
        {
            AimTarget precise = Classify(preciseHit);
            if (precise.kind != TargetKind.None)
                return precise;
        }

        // PASS 2 — forgiving. Only runs when the precise ray found nothing
        // useful, so this can never override a deliberate aim.
        if (aimAssistRadius > 0f)
        {
            // Start the sphere forward of the camera. Starting it AT the
            // camera has it already overlapping the player's own collider,
            // and Unity returns a useless zero-distance hit in that case.
            Vector3 origin = ray.origin + ray.direction * aimAssistRadius;
            float distance = Mathf.Max(0f, maxRayDistance - aimAssistRadius);

            if (Physics.SphereCast(origin, aimAssistRadius, ray.direction,
                                   out RaycastHit wideHit, distance,
                                   mask, QueryTriggerInteraction.Collide))
            {
                return Classify(wideHit);
            }
        }

        return default;
    }

    // -------------------------------------------------------
    // THE PRIORITY CHAIN — the single copy.
    // Order matters. Armed targets outrank everything.
    // -------------------------------------------------------
    private AimTarget Classify(RaycastHit hit)
    {
        AimTarget t = default;
        t.hitName = hit.collider.name;

        t.alarm = hit.collider.GetComponentInParent<AlarmTarget>();
        if (t.alarm != null)
        {
            t.kind = TargetKind.Alarm;
            t.identity = t.alarm;
            return t;
        }

        t.extinguisher = hit.collider.GetComponentInParent<ExtinguisherTarget>();
        if (t.extinguisher != null)
        {
            t.kind = TargetKind.Extinguisher;
            t.identity = t.extinguisher;
            return t;
        }

        t.actionTarget = hit.collider.GetComponentInParent<HazardActionTarget>();
        if (t.actionTarget != null)
        {
            t.kind = TargetKind.ActionTarget;
            t.identity = t.actionTarget;
            return t;
        }

        // ACTION LOCK. A target is armed and this is not it, so nothing
        // else in the scene may respond.
        //
        // Returns None rather than a special "locked" kind so pass 2 still
        // gets to run — otherwise a precise ray clipping a wall would
        // cancel aim assist and you could not use assist to reach the
        // armed target itself.
        if (HazardActionTarget.ActiveTarget != null)
            return default;

        t.hazard = hit.collider.GetComponentInParent<ClickableHazard>();
        if (t.hazard != null)
        {
            t.kind = TargetKind.Hazard;
            t.identity = t.hazard;
            return t;
        }

        if (PlayerPrefs.GetInt("SimulationMode", 0) == 1)
        {
            t.sim = hit.collider.GetComponentInParent<SimulationInteractable>();
            if (t.sim != null)
            {
                t.kind = TargetKind.Sim;
                t.identity = t.sim;
                return t;
            }

            // The Door implements IInteractable, not SimulationInteractable.
            t.interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (t.interactable != null)
            {
                t.kind = TargetKind.Interactable;
                t.identity = t.interactable as Object;
                return t;
            }
        }

        return default;
    }

    // -------------------------------------------------------
    // FEEDBACK
    // -------------------------------------------------------
    private void PlayHoverFeedback()
    {
        if (feedbackSource != null && hoverClip != null)
            feedbackSource.PlayOneShot(hoverClip);

        if (hapticOnHover)
            Haptics.Light();
    }

    private void PlayTapFeedback()
    {
        if (feedbackSource != null && tapClip != null)
            feedbackSource.PlayOneShot(tapClip);

        if (hapticOnTap)
            Haptics.Medium();
    }

    // -------------------------------------------------------
    // HOVER — reads the resolved target, no casting of its own.
    // -------------------------------------------------------
    private void ApplyHover(AimTarget target)
    {
        if (target.kind == TargetKind.Hazard)
        {
            if (currentHovered != target.hazard)
            {
                ClearHazardHover();
                currentHovered = target.hazard;
                currentHovered.OnHoverEnter();
            }
        }
        else ClearHazardHover();

        if (target.kind == TargetKind.Sim)
        {
            if (currentHoveredSim != target.sim)
            {
                ClearSimHover();
                currentHoveredSim = target.sim;
                currentHoveredSim.OnHoverEnter();
            }
        }
        else ClearSimHover();

        bool tappable = target.kind != TargetKind.None;
        DriveReticle(tappable);

        if (interactPrompt != null)
            interactPrompt.SetActive(tappable);
    }

    private void ClearHazardHover()
    {
        if (currentHovered != null)
        {
            currentHovered.OnHoverExit();
            currentHovered = null;
        }
    }

    private void ClearSimHover()
    {
        if (currentHoveredSim != null)
        {
            currentHoveredSim.OnHoverExit();
            currentHoveredSim = null;
        }
    }

    // -------------------------------------------------------
    // RETICLE
    //
    // Two independent scale layers multiplied together:
    //   reticleSmoothedScale — the slow hover grow/shrink
    //   punch                — the fast press squash
    //
    // Keeping them separate means a tap reads clearly even while the
    // hover scale is still mid-transition. One combined value would have
    // them fighting each other.
    // -------------------------------------------------------
    private void DriveReticle(bool targetable)
    {
        if (reticle == null) return;

        // Frame-rate independent smoothing. NOT Time.deltaTime * speed.
        float speed = targetable ? reticleEnterSpeed : reticleExitSpeed;
        float t = 1f - Mathf.Exp(-speed * Time.deltaTime);

        Color wantColor = targetable ? reticleHitColor : reticleIdleColor;
        reticle.color = Color.Lerp(reticle.color, wantColor, t);

        Vector3 wantScale = reticleBaseScale * (targetable ? reticleScaleOnHit : 1f);
        reticleSmoothedScale = Vector3.Lerp(reticleSmoothedScale, wantScale, t);

        // Punch decays on its own clock, independent of hover state.
        punch = Mathf.Lerp(punch, 0f, 1f - Mathf.Exp(-tapPunchRecoverSpeed * Time.deltaTime));
        if (punch < 0.001f) punch = 0f;

        float punchMultiplier = Mathf.Lerp(1f, tapPunchScale, punch);

        reticle.transform.localScale = reticleSmoothedScale * punchMultiplier;
    }

    // -------------------------------------------------------
    // TAP DETECTION — one finger owns the tap from start to finish.
    // Reports WHERE it was released so the caster knows what to hit.
    // -------------------------------------------------------
    private bool DetectTap(out Vector2 tapPosition)
    {
        tapPosition = Vector2.zero;
        bool tapped = false;

#if UNITY_EDITOR
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                tapPosition = Mouse.current.position.ReadValue();
                tapped = true;
            }
        }
#endif

        foreach (var touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                // A finger landing on the joystick or a TPASS button is not
                // aiming at the world. Ignore it; let the UI have it.
                if (IsTouchOverUI(touch.touchId))
                    continue;

                tapStartPos = touch.screenPosition;
                tapCurrentPos = touch.screenPosition;
                tapStartTime = Time.time;
                tapTouchId = touch.touchId;
                tapIsCandidate = true;
                continue;
            }

            // Only the finger that STARTED the tap can finish it.
            if (touch.touchId != tapTouchId) continue;

            tapCurrentPos = touch.screenPosition;

            // Travelled too far — this is a look-swipe, not a tap. Drop the
            // candidacy now so hover stops previewing and highlights do not
            // flicker across the room while turning.
            if (tapIsCandidate &&
                Vector2.Distance(tapStartPos, touch.screenPosition) >= TAP_MAX_MOVEMENT)
            {
                tapIsCandidate = false;
            }

            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                float duration = Time.time - tapStartTime;

                // Second UI check. The EventSystem sometimes has not
                // registered a brand-new touch on the frame it began, so
                // the check at Began can miss. Either one catches it.
                bool overUI = IsTouchOverUI(touch.touchId);

                if (tapIsCandidate && !overUI && duration < TAP_MAX_DURATION)
                {
                    tapPosition = touch.screenPosition;
                    tapped = true;
                }

                tapTouchId = -1;
                tapIsCandidate = false;
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                tapTouchId = -1;
                tapIsCandidate = false;
            }
        }

        return tapped;
    }

    // Checks ONE finger, not all of them.
    //
    // The old version returned true if ANY active touch was over UI, so the
    // left thumb resting on the joystick blocked every world tap. You could
    // not interact while walking.
    private bool IsTouchOverUI(int touchId)
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject(touchId);
    }

    // -------------------------------------------------------
    // CLICK — a switch over the already-resolved target.
    // No casting, no duplicated chain.
    // -------------------------------------------------------
    private void HandleClick(AimTarget target)
    {
        switch (target.kind)
        {
            case TargetKind.Alarm:
                Log("Alarm clicked: " + target.hitName);
                target.alarm.OnClicked();
                break;

            case TargetKind.Extinguisher:
                Log("Extinguisher clicked: " + target.hitName);
                target.extinguisher.OnClicked();
                break;

            case TargetKind.ActionTarget:
                Log("Action target clicked: " + target.hitName);
                target.actionTarget.OnClicked();
                break;

            case TargetKind.Hazard:
                Log("Hazard clicked: " + target.hitName);
                target.hazard.OnClicked();
                break;

            case TargetKind.Sim:
                Log("Phase 2 sim clicked: " + target.hitName);
                target.sim.Interact();
                break;

            case TargetKind.Interactable:
                Log("Phase 2 IInteractable clicked: " + target.hitName);
                target.interactable.Interact();
                break;

            case TargetKind.None:
                if (HazardActionTarget.ActiveTarget != null)
                    Log("Action in progress — ignoring tap on other objects.");
                break;
        }
    }

    // Editor-only. Stripped from the APK, so the phone build carries no
    // string building and no log spam.
    private void Log(string message)
    {
#if UNITY_EDITOR
        Debug.Log("[HazardInteractionManager] " + message);
#endif
    }
}