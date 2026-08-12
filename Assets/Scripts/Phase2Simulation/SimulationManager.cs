using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class SimulationManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // The central brain of Phase 2. Owns the 90-second timer,
    // tracks which step the player is on (1-8), subtracts time
    // for wrong actions, collects missed tips AND structured
    // step results, and triggers Win or Lose when the simulation
    // ends.
    //
    // EXTINGUISHER ANIMATION:
    // Every correct TPASS action also plays the matching Blender
    // clip on the extinguisher. That is what makes the PIN twist
    // and pull out, and the HOSE bend and sweep.
    //
    // ALARM PRESS:
    // Step 1 also plays the right-hand press animation through
    // PressAlarmController. That is a HAND animation driven by IK,
    // not an extinguisher clip, so it is dispatched separately -
    // see the note inside PlayClipForStep.
    //
    // MASKED ANIMATOR LAYERS:
    // The six clips are frame ranges carved out of ONE long Blender
    // timeline (Squeeze 1-23, Aim 24-66, Sweep 66-115, Twist 130-152,
    // Pull 152-165, PinDrop 165-180). Jumping straight to Twist's
    // range also snapped the HOSE to whatever pose was baked at that
    // frame - the hose appeared to teleport rather than animate.
    //
    // The fix is three masked layers. HoseLayer can only write to
    // Bone.012 and below. PinLayer can only write to Pin. LeverLayer
    // can only write to the two lever chains. A clip therefore CANNOT
    // disturb a part it has no business touching, no matter what its
    // frames contain.
    //
    // SQUEEZE IS LEVER-ONLY:
    // Squeeze used to play on the hose layer as well, and the hose
    // dropped every time. Squeeze sits at frames 1-23, BEFORE Aim at
    // 24-66, so playing it on the hose layer jumps the hose back to a
    // pre-aim pose. CrossFade only smoothed the trip - the destination
    // was still wrong.
    //
    // The real answer is that the hose SHOULD NOT MOVE during Squeeze.
    // You do not re-aim while discharging. HoseLayer already holds the
    // last Aim pose perfectly well on its own; playing Squeeze there
    // was actively destroying it. So Squeeze now plays on LeverLayer
    // alone, and the hose simply stays where Aim left it.
    //
    // THE DISCHARGE OUTLIVES THE BUTTON PRESS:
    // Sweep used to stop the spray and lift the thumb the instant it
    // was tapped, then tell the fire to die over the next two seconds.
    // So the player watched the fire go out with NOTHING hitting it.
    //
    // That is backwards, and for a training tool it teaches the wrong
    // thing: the spray is what puts the fire out, so it has to still
    // be running while the fire is going out. Both the spray stop and
    // the thumb release now happen in OnFireIsOut(), a callback that
    // FireController fires when the flames have finished fading.
    //
    // -------------------------------------------------------
    // NEW IN THIS VERSION - RETURN TO REST
    //
    // Once the fire was out, everything used to freeze exactly where it
    // was: left hand clamped to the nozzle, hose held in the swept
    // pose, lever still depressed under a thumb that had already
    // lifted. The player then walked to the door holding a rigid
    // sculpture of the last frame.
    //
    // OnFireIsOut() now also relaxes everything:
    //
    //   LEFT HAND - leftHandIK.ReleaseAll(), the same call already
    //   used after PinDrop. It clears the grip and lerps the IK target
    //   back to restTarget.
    //
    //   HOSE and LEVER - there is no "return to rest" clip in the
    //   Blender timeline, so we cannot play our way back. Instead we
    //   fade the LAYER WEIGHT to 0. A masked layer at weight 1 fully
    //   overrides its bones; at weight 0 it writes nothing, and the
    //   bones fall back to the pose underneath - which is the model's
    //   own bind pose, hose hanging naturally and lever up. Fading
    //   rather than snapping is what makes it read as lowering the
    //   hose instead of teleporting it.
    //
    // THE TRAP THIS CREATES:
    // A run that ends with HoseLayer at weight 0 leaves that layer
    // switched OFF. The next run's Aim and Sweep would then do
    // absolutely nothing, with no error to explain why. So
    // ResetRuntimeState() restores every layer weight to 1. If Aim
    // ever stops working after a replay, that restore is the first
    // thing to check.
    //
    // WHY IT LIVES HERE:
    // RegisterCorrectAction already fires exactly once per
    // correct step, and already knows WHICH step it was. Putting
    // the animation call here means one place to maintain, and
    // it can never fire on a wrong action.
    //
    // WHY THE HANDS DO NOT NEED THEIR OWN CLIPS:
    // Grip_Pin is parented to Pin, and Grip_Nozzle to a hose bone.
    // Those are the IK targets. So when a clip moves the part, the
    // marker moves, and the hand follows for free. One system
    // animates the prop; IK keeps the hands attached.
    //
    // Step breakdown (Office/Classroom — TPASS):
    // 1 = Sound Alarm          <- right hand press (PressAlarmController)
    // 2 = Grab Extinguisher    <- handled by ExtinguisherGrab
    // 3 = TPASS Twist          <- PinLayer,  after the hand arrives
    // 4 = TPASS Pull           <- PinLayer
    // 5 = TPASS Aim            <- HoseLayer, after the hand arrives
    // 6 = TPASS Squeeze        <- LeverLayer only
    // 7 = TPASS Sweep          <- HoseLayer, then everything relaxes
    // 8 = Evacuate (completed by Door, not RegisterCorrectAction)
    // -------------------------------------------------------

    // --- SINGLETON ---
    public static SimulationManager Instance { get; private set; }

    // --- INSPECTOR SETTINGS ---
    [SerializeField] private float totalTime = 90f;
    [SerializeField] private UnityEvent onWin;
    [SerializeField] private UnityEvent onLose;

    [Header("Results Submission")]
    [SerializeField] private ResultsSubmitter resultsSubmitter;

    // -------------------------------------------------------
    [Header("Alarm Press Animation")]
    [Tooltip("The PressAlarmController. Plays the right-hand reach and " +
             "press when the player sounds the alarm on Step 1. " +
             "Leave empty to skip the animation entirely - the step still " +
             "registers normally.")]
    [SerializeField] private PressAlarmController pressAlarm;
    // -------------------------------------------------------

    // -------------------------------------------------------
    [Header("Extinguisher Animation")]
    [Tooltip("The Animator on FireExtinguisher_ABC. Plays the six Blender clips.")]
    [SerializeField] private Animator extinguisherAnimator;

    [Tooltip("Clip state names as they appear in the Animator Controller. " +
             "Change these only if you renamed the states.")]
    [SerializeField] private string clipTwist = "Twist";
    [SerializeField] private string clipPull = "Pull";
    [SerializeField] private string clipPinDrop = "PinDrop";
    [SerializeField] private string clipAim = "Aim";
    [SerializeField] private string clipSqueeze = "Squeeze";
    [SerializeField] private string clipSweep = "Sweep";

    [Tooltip("Seconds to wait after Pull before the pin drops to the floor.")]
    [SerializeField] private float pinDropDelay = 0.4f;

    [Header("Animator Layers (masked)")]
    [Tooltip("Which layer of ExtinguisherAnimator each clip lives on.\n\n" +
             "  0 = Base Layer  (empty, no mask, no states)\n" +
             "  1 = HoseLayer   (HoseMask  - Bone.012 and below)\n" +
             "  2 = PinLayer    (PinMask   - Pin only)\n" +
             "  3 = LeverLayer  (LeverMask - Bone and Bone.006 chains)\n\n" +
             "These MUST match the order of the layers in the Animator " +
             "window, counting from the top starting at 0. If nothing " +
             "animates, these values are probably out of order.")]
    [SerializeField] private int hoseLayerIndex = 1;

    [Tooltip("Layer holding Twist, Pull and PinDrop.")]
    [SerializeField] private int pinLayerIndex = 2;

    [Tooltip("Layer holding Squeeze. LeverMask covers only the two lever " +
             "chains, so this clip moves the lever the thumb presses and " +
             "nothing else - the hose keeps whatever pose Aim left it in.")]
    [SerializeField] private int leverLayerIndex = 3;

    [Tooltip("Seconds to blend when switching between HOSE clips.\n\n" +
             "Aim and Sweep sit next to each other on the shared timeline " +
             "(Aim 24-66, Sweep 66-115), so a cut between them already lands " +
             "on a sensible pose. The blend just softens the change.\n\n" +
             "Set to 0 for an instant cut. Pin and lever clips deliberately " +
             "do NOT use this - they are crisp and correct already, and " +
             "blending would only soften them.")]
    [SerializeField] private float hoseBlendDuration = 0.25f;

    [Header("After the Pin Drops")]
    [Tooltip("The Pin object on the extinguisher. It is hidden once the " +
             "PinDrop clip finishes - the pin has left the extinguisher, so " +
             "it should not stay floating in the scene.")]
    [SerializeField] private GameObject pinObject;

    [Tooltip("The LeftHandIKController. Drives the reach-then-play timing " +
             "for Twist and Aim. Its ReleaseAll() is called when PinDrop " +
             "finishes so the left hand lets go instead of hanging in mid-air " +
             "where the pin used to be - and again when the fire goes out, so " +
             "the hand lowers off the nozzle.")]
    [SerializeField] private LeftHandIKController leftHandIK;

    [Tooltip("How long the PinDrop clip runs. The pin is hidden and the hand " +
             "released after this. Check the clip length in the Inspector.")]
    [SerializeField] private float pinDropClipLength = 0.5f;

    [Header("Thumb Press")]
    [Tooltip("The FingerGripController on hand.R. Its SetSqueeze(true) is " +
             "called on the Squeeze step so the THUMB presses the lever down " +
             "while the rest of the hand keeps gripping the handle.\n\n" +
             "The lever motion itself comes from the Squeeze clip on " +
             "LeverLayer - this is only the thumb. Both start together, so " +
             "tune squeezeSpeed on the FingerGripController until the thumb " +
             "tip travels with the lever rather than lagging or leading it.\n\n" +
             "The thumb is released when the FIRE goes out, not when Sweep is " +
             "tapped - it moves together with the spray, because lifting the " +
             "thumb is what stops the discharge.\n\n" +
             "Leave empty to skip the thumb entirely; the lever still moves.")]
    [SerializeField] private FingerGripController rightHandGrip;

    [Header("Nozzle Spray")]
    [Tooltip("The ExtinguisherSprayVFX on the extinguisher. Its Play() is " +
             "called on Squeeze, and its Stop() when the FIRE finishes dying - " +
             "NOT when Sweep is tapped.\n\n" +
             "The spray is what is killing the fire, so it has to still be " +
             "running while the flames fade. Stopping it on the button press " +
             "showed the fire dying with nothing hitting it.\n\n" +
             "The particle system itself is parented to the hose tip bone, so " +
             "it follows the nozzle through Aim and Sweep with no code - same " +
             "trick as the grip markers.\n\n" +
             "Stop() lets particles already in the air finish their lifetime " +
             "rather than vanishing mid-flight, which is why the spray tails " +
             "off instead of cutting out.")]
    [SerializeField] private ExtinguisherSprayVFX sprayVFX;

    [Header("Fire Reaction")]
    [Tooltip("The FireController on the fire object. WeakenFire() is called " +
             "on Squeeze and ExtinguishFire() on Sweep, so the fire shrinks " +
             "partway then dies - it does not just switch off.\n\n" +
             "ExtinguishFire() is handed a callback that stops the spray, " +
             "lifts the thumb and relaxes everything back to rest once the " +
             "flames are actually out. The fade length (dieDuration) lives on " +
             "FireController and is deliberately NOT duplicated here.\n\n" +
             "These calls used to live in TPASSButtonManager as well, which " +
             "meant they fired twice per tap. One owner now.")]
    [SerializeField] private FireController fireController;

    // -------------------------------------------------------
    [Header("Return to Rest (after the fire is out)")]
    [Tooltip("Seconds for the hose and lever to settle back to their " +
             "modelled rest pose.\n\n" +
             "This is a FADE of the Animator layer weight from 1 to 0, not a " +
             "clip. At weight 0 the masked layer stops writing to its bones " +
             "and they fall back to the model's own bind pose.\n\n" +
             "Set to 0 for an instant snap - useful for checking WHERE it " +
             "lands before tuning HOW LONG it takes to get there. Around " +
             "0.8 reads as lowering the hose; much faster reads as a jump.")]
    [SerializeField] private float relaxDuration = 0.8f;

    [Tooltip("Return the LEFT HAND to rest when the fire goes out.\n\n" +
             "Untick to isolate a problem: if something looks wrong during " +
             "the relax, turning off one half at a time tells you which.")]
    [SerializeField] private bool releaseLeftHandOnFireOut = true;

    [Tooltip("Relax the HOSE and LEVER back to rest when the fire goes out.\n\n" +
             "The lever matters as much as the hose: SetSqueeze(false) lifts " +
             "the THUMB, but the lever bone is posed by the Squeeze clip on " +
             "LeverLayer and nothing else releases it. Without this the thumb " +
             "lifts off a lever that is still pressed down.")]
    [SerializeField] private bool relaxExtinguisherOnFireOut = true;
    // -------------------------------------------------------

    // --- RUNTIME STATE ---
    private float timeRemaining;
    [SerializeField] private int currentStep = 1;
    [SerializeField] private bool simActive = false;
    private List<string> missedTips = new List<string>();
    private List<StepResult> stepResults = new List<StepResult>();
    private int totalPenaltySeconds = 0;

    // The layer-weight fade, held so it can be cancelled. OnFireIsOut can
    // legitimately run twice - once from FireController's callback and
    // once from EndSimulation's safety net - and two coroutines lerping
    // the same weight from different start values would visibly fight.
    private Coroutine relaxRoutine;

    // --- PUBLIC READ-ONLY PROPERTIES ---
    public float TimeRemaining => timeRemaining;
    public int CurrentStep => currentStep;
    public List<string> MissedTips => missedTips;
    public bool IsSimActive => simActive;
    public int TotalPenaltySeconds => totalPenaltySeconds;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        // Bring the pin back for a fresh run - it was hidden last time.
        if (pinObject != null) pinObject.SetActive(true);

        // Withdraw the right arm and unfold the fingers, in case a run
        // was abandoned mid-press. Without this the hand could still be
        // held out at the button when the next run starts.
        if (pressAlarm != null) pressAlarm.ResetState();

        // Lift the thumb off the lever, in case a run ended mid-squeeze.
        if (rightHandGrip != null) rightHandGrip.SetSqueeze(false);

        // Kill any spray still running, in case a run ended mid-discharge.
        if (sprayVFX != null) sprayVFX.Stop();

        // CRITICAL: put every masked layer back to full strength.
        //
        // The previous run ended by fading HoseLayer and LeverLayer to 0
        // so the hose and lever could settle to rest. A layer at weight 0
        // writes NOTHING - so if this line is missing, the next run's Aim
        // and Sweep play correctly, report correctly in the Console, and
        // move absolutely nothing on screen. There is no error to follow.
        //
        // If Aim ever stops working on a replay, look here first.
        RestoreLayerWeights();

        timeRemaining = totalTime;
        currentStep = 1;
        simActive = false;
        totalPenaltySeconds = 0;
        missedTips.Clear();
        stepResults.Clear();
    }

    // -------------------------------------------------------
    // BEGIN SIMULATION
    // -------------------------------------------------------
    public void BeginSimulation()
    {
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1)
        {
            Debug.LogWarning("[SimulationManager] BeginSimulation called outside Phase 2 — ignored.");
            return;
        }

        if (simActive) return;

        simActive = true;
        Debug.Log("[SimulationManager] Simulation started. Timer running.");
    }

    private void Update()
    {
        if (!simActive) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    // -------------------------------------------------------
    // REGISTER CORRECT ACTION
    // -------------------------------------------------------
    public void RegisterCorrectAction(SimulationInteractable.SimStep step, string chosenAction)
    {
        if (!simActive) return;

        stepResults.Add(new StepResult
        {
            step_name = step.ToString(),
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = true,
            penalty_seconds = 0
        });

        // >>> FEEDBACK LOG: green row with a friendly label of the completed step.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowCorrect(StepNames.Friendly(step.ToString()));

        // >>> ANIMATION: play the hand or extinguisher animation for this step.
        PlayClipForStep(step);

        if (currentStep < 8)
            currentStep++;
    }

    // -------------------------------------------------------
    // STEP ANIMATION
    //
    // Matched on the step's NAME rather than by comparing enum
    // members directly. That keeps this working even if the enum
    // is renamed or reordered later - it only cares that the name
    // contains "Alarm", "Twist", "Pull", and so on.
    //
    // ORDER MATTERS HERE.
    // The alarm press is a HAND animation driven by IK. It does not
    // use extinguisherAnimator at all. So it must be dispatched
    // BEFORE the extinguisherAnimator null guard below - otherwise a
    // scene with no Animator assigned would silently swallow the
    // press, with no error and nothing on screen to explain why.
    //
    // Each extinguisher clip is played on its MASKED layer, so a clip
    // physically cannot write to bones outside its own part.
    // -------------------------------------------------------
    private void PlayClipForStep(SimulationInteractable.SimStep step)
    {
        string name = step.ToString();

        // --- HAND ANIMATION (no extinguisher Animator involved) ---
        if (name.Contains("Alarm"))
        {
            if (pressAlarm != null)
            {
                pressAlarm.PlayAlarmPress();
                Debug.Log("[SimulationManager] Alarm press animation started.");
            }
            return;
        }

        if (name.Contains("Grab"))
        {
            // Cancel any press still in flight. The press runs for about a
            // second, and nothing stops the player tapping Grab while it is
            // still withdrawing. If that happened, PressAlarmController would
            // be fading rightArmIK.weight 1 -> 0 while ExtinguisherGrab fades
            // the SAME value 0 -> 1 - two writers on one value, which stutters
            // or snaps the arm. ResetState() stops the coroutine and zeroes
            // the weight, so ExtinguisherGrab inherits a clean slate.
            if (pressAlarm != null)
            {
                pressAlarm.ResetState();
                Debug.Log("[SimulationManager] Alarm press cancelled - grab takes the arm.");
            }
            return;
        }

        // --- EXTINGUISHER CLIPS from here down ---
        if (extinguisherAnimator == null) return;

        if (name.Contains("Twist"))
        {
            // WAIT FOR THE HAND. Playing the clip straight away twisted the
            // pin while the hand was still travelling toward it - the pin
            // turning in mid-air with nothing touching it.
            //
            // The callback fires the moment the hand is actually holding
            // Grip_Pin, so the twist starts on contact. Deliberately NOT a
            // delay field here: that would duplicate moveDuration in a second
            // file, and the two would drift apart the first time one was
            // retuned, with nothing to warn you.
            if (leftHandIK != null)
                leftHandIK.ReachPinAndTwist(() => PlayClip(clipTwist, pinLayerIndex));
            else
                PlayClip(clipTwist, pinLayerIndex);
        }
        else if (name.Contains("Pull"))
        {
            PlayClip(clipPull, pinLayerIndex);
            // The pin leaves the hand, falls, then disappears - and the
            // left hand lets go and returns to rest.
            StartCoroutine(PullSequence());
        }
        else if (name.Contains("Aim"))
        {
            // Same reasoning as Twist: let the hand arrive at the nozzle
            // before the hose starts bending.
            if (leftHandIK != null)
                leftHandIK.GrabNozzle(() => PlayHoseClip(clipAim));
            else
                PlayHoseClip(clipAim);
        }
        else if (name.Contains("Squeeze"))
        {
            // LEVER ONLY - deliberately not played on the hose layer.
            //
            // Squeeze sits at frames 1-23, before Aim at 24-66. Playing it
            // on HoseLayer jumped the hose back to its pre-aim pose, which
            // read as the hose dropping the moment you squeezed. CrossFade
            // smoothed the trip but the destination was still wrong.
            //
            // The hose should not move during Squeeze anyway - you do not
            // re-aim while discharging. Leaving HoseLayer alone means it
            // simply holds whatever pose Aim finished on, which is correct.
            PlayClip(clipSqueeze, leverLayerIndex);

            // THUMB: press down with the lever. Only the thumb bones carry a
            // squeezeAngle, so the rest of the hand keeps its grip on the
            // handle - which is how you actually hold an extinguisher.
            //
            // It stays pressed until the FIRE goes out, not until Sweep is
            // tapped. See the Sweep branch below.
            if (rightHandGrip != null)
                rightHandGrip.SetSqueeze(true);

            // SPRAY: the discharge starts here and keeps running through
            // Sweep and all the way until the flames are actually out.
            if (sprayVFX != null)
            {
                sprayVFX.Play();
                Debug.Log("[SimulationManager] Nozzle spray started.");
            }

            // FIRE: shrinks partway. It does not go out until Sweep, which
            // is the teaching point - one burst is not enough.
            if (fireController != null)
                fireController.WeakenFire();
        }
        else if (name.Contains("Sweep"))
        {
            // The hose sweeps. This is the ONLY thing that should move -
            // HoseMask limits the clip to Bone.012 and below, so the right
            // hand stays on the handle and the extinguisher body stays put.
            //
            // If the right hand or the body ever swings on Sweep again, it
            // is NOT this line. It is something rotating ExtinguisherAnchor -
            // check HandAnimationController.sweepControlledByAnimation is
            // ticked, and check HoseMask has no lever bones left in it.
            PlayHoseClip(clipSweep);

            // FIRE: dies and fades out.
            //
            // The spray, the thumb and the return to rest are NOT handled
            // here. The discharge is what is killing the fire, so it has to
            // still be running while the flames fade - cutting it on the
            // button press showed the fire dying with nothing hitting it.
            //
            // Everything happens in OnFireIsOut below, which FireController
            // fires the moment the flames are gone.
            if (fireController != null)
            {
                fireController.ExtinguishFire(OnFireIsOut);
            }
            else
            {
                // No fire assigned (test scene). Nothing will ever call back,
                // so end the discharge now rather than leaving it running
                // forever with no way to stop.
                Debug.LogWarning("[SimulationManager] No FireController assigned - " +
                                 "stopping spray immediately instead of waiting for " +
                                 "the fire to go out.");
                OnFireIsOut();
            }
        }
        // Evacuate has no animation.
    }

    // -------------------------------------------------------
    // THE FIRE IS OUT
    //
    // Handed to FireController.ExtinguishFire() as a callback. It runs
    // when the flames have finished fading, NOT when Sweep was tapped.
    //
    // Everything that should end WITH the fire lives here:
    //   1. thumb comes off the lever
    //   2. spray stops
    //   3. left hand lets go of the nozzle and returns to rest
    //   4. hose and lever settle back to their modelled rest pose
    //
    // A named method rather than an inline lambda because it is also
    // called directly by EndSimulation as a safety net - see below.
    // -------------------------------------------------------
    private void OnFireIsOut()
    {
        // 1. Thumb comes off the lever - nothing left to discharge.
        if (rightHandGrip != null)
            rightHandGrip.SetSqueeze(false);

        // 2. Stop() lets particles already in the air finish their lifetime
        // instead of vanishing mid-flight, which is why the spray tails
        // off naturally rather than cutting out.
        if (sprayVFX != null)
        {
            sprayVFX.Stop();
            Debug.Log("[SimulationManager] Fire is out - nozzle spray stopped.");
        }

        // 3. The left hand has nothing left to hold. Same call used after
        // PinDrop: it clears the grip and lerps the IK target back to
        // restTarget, so the arm lowers instead of staying clamped to a
        // nozzle nobody is aiming any more.
        if (releaseLeftHandOnFireOut && leftHandIK != null)
        {
            leftHandIK.ReleaseAll();
            Debug.Log("[SimulationManager] Left hand released to rest.");
        }

        // 4. Hose and lever settle back to rest.
        if (relaxExtinguisherOnFireOut)
            RelaxExtinguisherToRest();
    }

    // -------------------------------------------------------
    // RELAX THE EXTINGUISHER
    //
    // There is no "return to rest" clip in the Blender timeline, so we
    // cannot play our way back. Instead we fade the masked layers' WEIGHT
    // from whatever it is now down to 0.
    //
    // A layer at weight 1 fully overrides the bones inside its mask. At
    // weight 0 it writes nothing at all, and those bones fall back to the
    // pose underneath - the model's own bind pose, which is the hose
    // hanging naturally and the lever up.
    //
    // Fading rather than snapping is the whole point: at 0.8 seconds this
    // reads as the player lowering the hose. At 0 seconds it reads as the
    // hose teleporting.
    //
    // BOTH layers, not just the hose. SetSqueeze(false) lifts the THUMB,
    // but the lever bone is posed by the Squeeze clip on LeverLayer and
    // nothing else releases it - so without this the thumb lifts off a
    // lever that is still pressed down.
    // -------------------------------------------------------
    private void RelaxExtinguisherToRest()
    {
        if (extinguisherAnimator == null) return;

        // Cancel any fade already running. OnFireIsOut can legitimately be
        // called twice - once by FireController and once by EndSimulation's
        // safety net - and two coroutines lerping the same weight from
        // different start values would visibly fight.
        if (relaxRoutine != null)
        {
            StopCoroutine(relaxRoutine);
            relaxRoutine = null;
        }

        // Instant snap when the duration is 0. Useful for checking WHERE
        // the rest pose actually lands before tuning how long it takes to
        // get there.
        if (relaxDuration <= 0f)
        {
            SetLayerWeightSafe(hoseLayerIndex, 0f);
            SetLayerWeightSafe(leverLayerIndex, 0f);
            Debug.Log("[SimulationManager] Extinguisher snapped to rest.");
            return;
        }

        relaxRoutine = StartCoroutine(RelaxRoutine());
    }

    private IEnumerator RelaxRoutine()
    {
        float hoseStart = GetLayerWeightSafe(hoseLayerIndex);
        float leverStart = GetLayerWeightSafe(leverLayerIndex);

        float elapsed = 0f;
        while (elapsed < relaxDuration)
        {
            elapsed += Time.deltaTime;

            // SmoothStep rather than a straight lerp, so the hose eases out
            // of the swept pose and settles gently rather than moving at a
            // constant speed and stopping dead.
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / relaxDuration));

            SetLayerWeightSafe(hoseLayerIndex, Mathf.Lerp(hoseStart, 0f, t));
            SetLayerWeightSafe(leverLayerIndex, Mathf.Lerp(leverStart, 0f, t));

            yield return null;
        }

        // Land exactly on 0 - a lerp can finish a hair short.
        SetLayerWeightSafe(hoseLayerIndex, 0f);
        SetLayerWeightSafe(leverLayerIndex, 0f);

        relaxRoutine = null;
        Debug.Log("[SimulationManager] Extinguisher relaxed to rest.");
    }

    // -------------------------------------------------------
    // RESTORE LAYER WEIGHTS
    //
    // Puts every masked layer back to full strength for a fresh run.
    // Without this, a replay inherits HoseLayer and LeverLayer at
    // weight 0 from the previous run's relax - and Aim, Squeeze and
    // Sweep would then move nothing at all, silently.
    // -------------------------------------------------------
    private void RestoreLayerWeights()
    {
        if (extinguisherAnimator == null) return;

        if (relaxRoutine != null)
        {
            StopCoroutine(relaxRoutine);
            relaxRoutine = null;
        }

        SetLayerWeightSafe(hoseLayerIndex, 1f);
        SetLayerWeightSafe(pinLayerIndex, 1f);
        SetLayerWeightSafe(leverLayerIndex, 1f);
    }

    // Guarded layer access. A layer index pointing past the end of the
    // Animator throws, and the index fields are hand-typed in the
    // Inspector - so an Animator with fewer layers than expected would
    // otherwise take down whatever coroutine happened to be running.
    private void SetLayerWeightSafe(int layer, float weight)
    {
        if (extinguisherAnimator == null) return;
        if (layer < 0 || layer >= extinguisherAnimator.layerCount) return;

        extinguisherAnimator.SetLayerWeight(layer, weight);
    }

    private float GetLayerWeightSafe(int layer)
    {
        if (extinguisherAnimator == null) return 0f;
        if (layer < 0 || layer >= extinguisherAnimator.layerCount) return 0f;

        return extinguisherAnimator.GetLayerWeight(layer);
    }

    // Play a state from its first frame, on a SPECIFIC layer. Play()
    // rather than CrossFade() because the clips animate different parts,
    // so there is nothing to blend between - and no exit-time transitions
    // exist, so playback stops at the end of the clip.
    //
    // The layer argument matters. Passing 0 would target the empty
    // Base Layer, which holds no states and no mask, so nothing would
    // play at all.
    private void PlayClip(string stateName, int layer)
    {
        if (extinguisherAnimator == null || string.IsNullOrEmpty(stateName)) return;

        extinguisherAnimator.Play(stateName, layer, 0f);
        Debug.Log($"[SimulationManager] Extinguisher clip: {stateName} (layer {layer})");
    }

    // Play a HOSE clip with a short blend rather than an instant cut, so
    // the change between Aim and Sweep reads as a movement rather than a
    // jump. Falls back to a plain cut when hoseBlendDuration is 0, which
    // is useful for comparing the two.
    private void PlayHoseClip(string stateName)
    {
        if (extinguisherAnimator == null || string.IsNullOrEmpty(stateName)) return;

        if (hoseBlendDuration <= 0f)
        {
            PlayClip(stateName, hoseLayerIndex);
            return;
        }

        extinguisherAnimator.CrossFade(stateName, hoseBlendDuration, hoseLayerIndex, 0f);
        Debug.Log($"[SimulationManager] Extinguisher clip: {stateName} " +
                  $"(layer {hoseLayerIndex}, blended {hoseBlendDuration}s)");
    }

    // -------------------------------------------------------
    // PULL SEQUENCE
    //
    // Runs the whole post-Pull choreography in one place, so the
    // timing is easy to read and easy to retune:
    //
    //   1. wait  -> the Pull clip finishes carrying the pin out
    //   2. drop  -> PinDrop clip plays, pin falls
    //   3. wait  -> the drop finishes
    //   4. hide  -> pin disappears (it is gone from the extinguisher)
    //   5. release -> left hand lets go and returns to rest, so it is
    //                 not left hanging where the pin used to be
    // -------------------------------------------------------
    private IEnumerator PullSequence()
    {
        // 1 + 2: let the Pull clip carry the pin out, then drop it.
        yield return new WaitForSeconds(pinDropDelay);
        PlayClip(clipPinDrop, pinLayerIndex);

        // 3: let the drop finish.
        yield return new WaitForSeconds(pinDropClipLength);

        // 4: the pin is gone from the extinguisher - hide it.
        if (pinObject != null)
        {
            pinObject.SetActive(false);
            Debug.Log("[SimulationManager] Pin hidden after drop.");
        }

        // 5: the left hand has nothing left to hold - return it to rest.
        if (leftHandIK != null)
        {
            leftHandIK.ReleaseAll();
            Debug.Log("[SimulationManager] Left hand released to rest.");
        }
    }

    // -------------------------------------------------------
    // REGISTER WRONG ACTION
    // -------------------------------------------------------
    public void RegisterWrongAction(SimulationInteractable.SimStep step, string chosenAction, float timePenalty, string tip)
    {
        if (!simActive) return;

        timeRemaining -= timePenalty;
        missedTips.Add(tip);

        int penaltyInt = Mathf.RoundToInt(timePenalty);
        totalPenaltySeconds += penaltyInt;

        stepResults.Add(new StepResult
        {
            step_name = step.ToString(),
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = false,
            penalty_seconds = penaltyInt
        });

        // >>> FEEDBACK LOG: red row. The hint points at the CURRENT step
        // the player should be doing (currentStep), NOT the button they
        // mis-tapped — so it guides them to the right next move.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowWrong(StepNames.Hint(currentStep), penaltyInt);

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    public void EndSimulation(bool won)
    {
        if (!simActive) return;
        simActive = false;

        // SAFETY NET: the run can end at ANY moment - the 90-second timer
        // can expire mid-discharge, or a penalty can drive the clock to
        // zero between Squeeze and the fire going out. In either case the
        // FireController callback may never arrive, and the spray would
        // keep firing over the results screen with the hand still clamped
        // to the nozzle.
        //
        // Calling this directly is harmless when the fire already went out:
        // Stop() on a stopped particle system, SetSqueeze(false) on an
        // already-released thumb, and a relax fade restarting from a weight
        // already at 0 all do nothing visible.
        OnFireIsOut();

        GameModeManager modeManager = FindFirstObjectByType<GameModeManager>();
        if (modeManager != null)
            modeManager.ResetToPhase1();

        if (won)
        {
            int finalScore = Mathf.RoundToInt(timeRemaining);
            Debug.Log($"[SimulationManager] Completed. Submitting for validation. Local score: {finalScore}");

            ResultsUIManager resultsUI = FindFirstObjectByType<ResultsUIManager>();
            if (resultsUI != null)
                resultsUI.ShowSubmitting();

            onWin.Invoke();

            if (resultsSubmitter != null)
            {
                resultsSubmitter.Submit(finalScore, totalPenaltySeconds, stepResults);
            }
            else
            {
                Debug.LogWarning("[SimulationManager] No ResultsSubmitter assigned — results were NOT sent to Laravel.");
            }
        }
        else
        {
            Debug.Log("[SimulationManager] LOSE.");
            onLose.Invoke();
        }
    }
}