using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// -------------------------------------------------------
// SimulationManager — the brain of Phase 2.
//
// Owns the 90-second timer, tracks the current step, applies penalties for
// wrong actions, collects step results, and ends the run as a Win or a Lose.
//
// STEP BREAKDOWN (Office / Classroom — TPASS, 8 steps):
//   1 Sound Alarm        -> right-hand press (PressAlarmController)
//   2 Grab Extinguisher  -> handled by ExtinguisherGrab
//   3 TPASS Twist        -> PinLayer, after the hand arrives
//   4 TPASS Pull         -> PinLayer, then PullSequence
//   5 TPASS Aim          -> HoseLayer, after the hand arrives
//   6 TPASS Squeeze      -> LeverLayer only
//   7 TPASS Sweep        -> HoseLayer, then everything relaxes
//   8 Evacuate           -> completed by Door, not RegisterCorrectAction
//
// STEP BREAKDOWN (Kitchen — WCTL, 5 steps):
//   1 Grab Towel     -> TowelGrab: flies from the sink into both hands
//   2 WCTL Wet       -> TowelDipController: dip, soak, wring, lift
//   3 WCTL Cover     -> TowelCoverController: throw, contact, settle
//   4 WCTL Turn Off  -> KitchenValveController: reach, turn, release
//   5 Evacuate       -> completed by Door
//
// THERE IS NO SOUND ALARM STEP IN KITCHEN. This is a home scenario, not a
// workplace with a panel on the wall — BFP doctrine for an LPG fire at home
// starts with smothering, not with raising an alarm.
//
// Nothing had to be REMOVED from this file for that. PlayClipForStep matches
// on SUBSTRINGS, and no KitchenStep name contains "Alarm", so that branch is
// simply unreachable in the Kitchen scene. It stays because Office needs it.
//
// What DOES have to be right is the Inspector:
//   maxStep = 5   in Kitchen  (KitchenStep runs 1..5)
//   maxStep = 8   in Office and Classroom
//   pressAlarm    EMPTY in Kitchen
//
// A Kitchen scene left at maxStep 8 keeps climbing past Evacuate and the run
// can never complete.
//
// -------------------------------------------------------
// THE ONLY MARKER ARROW IN PHASE 2 IS THE EVACUATE ONE.
//
// Phase 1 is the learning module and guides every action. Phase 2 is the
// ASSESSMENT — it feeds game_sessions, the penalty score, the pass threshold
// and the certificate. An arrow pointing at the alarm, then the extinguisher,
// then the fire would let a player finish a run without knowing TPASS at all,
// and the score would stop measuring competence.
//
// Evacuate is the deliberate exception. Finding the exit is a NAVIGATION
// problem, not a knowledge one — the player may never have been in this room
// before, and every scored decision has already been made by that point.
//
// The defensible line, in one sentence: guidance in learning, none in
// assessment, except wayfinding for evacuation.
//
// -------------------------------------------------------
// HOW ONE MANAGER SERVES BOTH SEQUENCES
//
// Office uses SimulationInteractable.SimStep. Kitchen uses KitchenStep. Both
// are numbered from 1, and both are SEPARATE enums on purpose — currentStep
// advances with a plain ++, and SimulationInteractable compares step numbers
// with > and < to judge whether a tap was too early. Non-sequential values
// would break that comparison silently, with no error to follow.
//
// This file never cared which enum a step came from: every use was
// step.ToString(). So the enum methods forward to STRING methods, and Kitchen
// calls those directly. Office and Classroom call sites are unchanged.
//
// TWO INSPECTOR FIELDS carry the difference:
//   maxStep         — 8 for TPASS, 5 for WCTL
//   useKitchenHints — which wrong-action hint table to read
//
// PlayClipForStep matches on SUBSTRINGS, which is why Kitchen gets its
// branches for free: "GrabTowel" hits Grab, "WCTL_Wet" hits Wet.
//
// -------------------------------------------------------
// THINGS THAT COST DAYS TO LEARN — do not undo these:
//
// * MASKED LAYERS. The six clips are frame ranges carved out of ONE
//   Blender timeline, so jumping to Twist's range also snapped the
//   hose to whatever pose was baked there. Three masked layers fix
//   it: a clip physically cannot write to a part outside its mask.
//
// * SQUEEZE IS LEVER-ONLY. Squeeze sits at frames 1-23, BEFORE Aim
//   at 24-66, so playing it on HoseLayer threw the hose back to its
//   pre-aim pose. The hose should not move during Squeeze anyway —
//   you do not re-aim while discharging.
//
// * THE DISCHARGE OUTLIVES THE BUTTON PRESS. Spray and thumb stop in
//   OnFireIsOut(), a FireController callback, not when Sweep is
//   tapped. Otherwise the player watches the fire die with nothing
//   hitting it — backwards for a training tool.
//
// * RESTORE LAYER WEIGHTS ON RESET. A run ends by fading HoseLayer
//   and LeverLayer to 0. A layer at weight 0 writes NOTHING, so a
//   replay would play Aim and Sweep correctly, log correctly, and
//   move absolutely nothing on screen. No error to follow.
//
// * INPUT LOCKOUT. Tapping Pull then Aim immediately left the hand
//   frozen and the Aim clip never playing. PullSequence ends with
//   ReleaseAll(), which runs through LeftHandIKController's
//   StartExclusive — and that STOPS whatever coroutine is running,
//   including the nozzle travel Aim had just started, and with it
//   the callback that plays the Aim clip. Silent, no error.
//   stepBusy blocks taps while a step is still animating. EVERY
//   branch of PlayClipForStep must clear it, or the buttons stay
//   dead until the Update() safety valve fires.
//
// * HANDS NEED NO CLIPS. Grip_Pin is parented to Pin, Grip_Nozzle to
//   a hose bone. Those are the IK targets — move the part and the
//   hand follows for free. One system animates the prop; IK keeps
//   the hands attached. Kitchen uses the same trick THREE times:
//   GripPoint_R and GripPoint_L under Towel_A1 and Towel_B1, and
//   ValveGrip under Regulator_Valve.
//
// * EVERY SCRIPT THAT MUTATES RUN STATE NEEDS ITS OWN RESET, AND
//   EVERY ONE MUST BE CALLED. Kitchen has SEVEN. See ResetKitchenState
//   below — the failure mode for each is silent, and three of them
//   stall the run outright on the second attempt.
// -------------------------------------------------------

public class SimulationManager : MonoBehaviour
{
    // --- SINGLETON ---
    public static SimulationManager Instance { get; private set; }

    // --- INSPECTOR SETTINGS ---
    [SerializeField] private float totalTime = 90f;
    [SerializeField] private UnityEvent onWin;
    [SerializeField] private UnityEvent onLose;

    [Header("Environment")]
    [Tooltip("How many steps this environment has. Office and Classroom run " +
             "TPASS across 8. Kitchen runs WCTL across 5.\n\n" +
             "The step counter stops climbing here, so a wrong value either " +
             "ends the run early or leaves it unfinishable. It MUST match the " +
             "highest value in the enum that scene uses.\n\n" +
             "WATCH OUT: a scene object saved BEFORE this field existed loads " +
             "it as 0, not 8. If a run freezes on step 1, check this first.")]
    [SerializeField] private int maxStep = 8;

    [Tooltip("Tick in the KITCHEN scene only.\n\n" +
             "Wrong-action hints are keyed by step NUMBER, and the same number " +
             "means a different instruction in each environment — so the " +
             "lookup needs to know which sequence it is describing.")]
    [SerializeField] private bool useKitchenHints = false;

    [Header("Evacuate — Marker Arrow")]
    [Tooltip("Show the green marker arrow on the exit once the fire is dealt " +
             "with and the Evacuate step becomes active.\n\n" +
             "This is the ONLY arrow in Phase 2. TPASS and WCTL steps stay " +
             "unguided on purpose — see the header note. Finding the exit is " +
             "navigation, not knowledge, so it gives away nothing being scored.")]
    [SerializeField] private bool useEvacuateArrow = true;

    [Tooltip("The exit the player must reach. Assign the Door object.\n\n" +
             "The door's renderer bounds reach the top of the frame, so a " +
             "bounds-based arrow floats near the ceiling. Put a " +
             "MarkerArrowAnchor on the door with an empty child positioned in " +
             "the DOORWAY at eye level, where a running player will see it.")]
    [SerializeField] private Transform exitTarget;

    [Tooltip("Seconds after the final step becomes active before the arrow " +
             "appears.\n\n" +
             "OFFICE NEEDS A DELAY. Sweep registers as correct while the fire " +
             "is still visibly fading — an arrow at that instant tells the " +
             "player to run before the flames are out, which is the opposite " +
             "of the drill. Around 1.5 lets the fire die first.\n\n" +
             "KITCHEN can be shorter. The fire dies on towel CONTACT, which is " +
             "already finished by the time the Turn Off step completes.")]
    [SerializeField] private float evacuateArrowDelay = 1.5f;

    [Header("Kitchen — Towel System")]
    [Tooltip("KITCHEN ONLY. Leave every field in this block EMPTY in Office " +
             "and Classroom — every call to them is null-guarded, so an empty " +
             "slot costs nothing there.\n\n" +
             "In KITCHEN, all seven must be assigned. Five of them exist purely " +
             "so a REPLAY works: see ResetKitchenState in this file for what " +
             "breaks, silently, when one is missed.")]
    [SerializeField] private TowelDipController towelDip;

    [Tooltip("Throws the towel onto the burning regulator and leaves it there.")]
    [SerializeField] private TowelCoverController towelCover;

    [Tooltip("Flies the towel from the sink into both hands on the Grab step. " +
             "Its reset is what puts the towel BACK on the sink — without it, " +
             "attempt two starts with the towel already in hand, nothing to " +
             "tap, and the run stalls on step 1 with no error.")]
    [SerializeField] private TowelGrab towelGrab;

    [Tooltip("Owns the IsWet flag and the darkening. Its reset is what makes " +
             "the dry-cloth guard fire again on a replay — miss it and the " +
             "player can skip the Wet step entirely on attempt two.")]
    [SerializeField] private TowelWetnessController towelWetness;

    [Tooltip("The tap target on the towel. Reset re-enables it and clears the " +
             "hover outline.")]
    [SerializeField] private KitchenInteractable towelInteractable;

    [Tooltip("The WCTL buttons. Each correct tap greys its own button out and " +
             "nothing ever put them back — attempt two used to start with all " +
             "three dead.")]
    [SerializeField] private WCTLButtonManager wctlButtons;

    [Header("Kitchen — Turn Off Step")]
    [Tooltip("Reaches the RIGHT hand to the LPG regulator, turns the valve " +
             "shut, and releases. Sits on LPG_Assembly.\n\n" +
             "Its reset re-opens the valve. Without it a second attempt starts " +
             "with the gas already off — the step still registers and still " +
             "logs correct, but the player turns a wheel that is visibly " +
             "already closed, and the one action that actually ends a gas fire " +
             "stops being demonstrated.\n\n" +
             "Leave EMPTY in Office and Classroom.")]
    [SerializeField] private KitchenValveController valveTurn;

    [Header("Results Audio")]
    [Tooltip("Plays when the run ends in a PASS. A short fanfare or chime.")]
    [SerializeField] private AudioClip winSound;

    [Tooltip("Plays when the run ends in a LOSS — timeout, or the Office " +
             "wrong-fire ending. Something flat and final, not harsh: the " +
             "player is about to read what they missed, and a punishing " +
             "sting reads as mockery in a training tool.")]
    [SerializeField] private AudioClip loseSound;

    [Header("Results Submission")]
    [SerializeField] private ResultsSubmitter resultsSubmitter;

    [Header("Alarm Press Animation")]
    [Tooltip("Plays the right-hand reach and press on Step 1. Leave empty " +
             "to skip the animation — the step still registers.\n\n" +
             "LEAVE EMPTY IN KITCHEN. There is no alarm step there, so this " +
             "would never fire anyway — but an assigned reference would still " +
             "have ResetState() called on it every run for no reason.")]
    [SerializeField] private PressAlarmController pressAlarm;

    [Header("Extinguisher Animation")]
    [Tooltip("The Animator on FireExtinguisher_ABC. Plays the six Blender clips.\n\n" +
             "Leave EMPTY in Kitchen — there is no extinguisher there, and the " +
             "null guard below lets WCTL steps past it so the dip still plays.")]
    [SerializeField] private Animator extinguisherAnimator;

    [Tooltip("Clip state names as they appear in the Animator Controller.")]
    [SerializeField] private string clipTwist = "Twist";
    [SerializeField] private string clipPull = "Pull";
    [SerializeField] private string clipPinDrop = "PinDrop";
    [SerializeField] private string clipAim = "Aim";
    [SerializeField] private string clipSqueeze = "Squeeze";
    [SerializeField] private string clipSweep = "Sweep";

    [Tooltip("Seconds to wait after Pull before the pin drops to the floor.")]
    [SerializeField] private float pinDropDelay = 0.4f;

    [Header("Animator Layers (masked)")]
    [Tooltip("0 = Base (empty)  1 = HoseLayer  2 = PinLayer  3 = LeverLayer\n\n" +
             "These MUST match the layer order in the Animator window, " +
             "counting from the top starting at 0. If nothing animates, " +
             "these values are probably out of order.")]
    [SerializeField] private int hoseLayerIndex = 1;

    [Tooltip("Layer holding Twist, Pull and PinDrop.")]
    [SerializeField] private int pinLayerIndex = 2;

    [Tooltip("Layer holding Squeeze. LeverMask covers only the lever chains, " +
             "so the hose keeps whatever pose Aim left it in.")]
    [SerializeField] private int leverLayerIndex = 3;

    [Tooltip("Seconds to blend between HOSE clips. Aim and Sweep sit next to " +
             "each other on the timeline, so a cut already lands on a sensible " +
             "pose — this just softens it. Set 0 for an instant cut.")]
    [SerializeField] private float hoseBlendDuration = 0.25f;

    [Header("After the Pin Drops")]
    [Tooltip("The Pin object. Hidden once PinDrop finishes — it has left the " +
             "extinguisher, so it should not float in the scene.")]
    [SerializeField] private GameObject pinObject;

    [Tooltip("Drives the reach-then-play timing for Twist and Aim, and releases " +
             "the left hand after PinDrop and when the fire goes out.")]
    [SerializeField] private LeftHandIKController leftHandIK;

    [Tooltip("How long the PinDrop clip runs. Check the clip length in the Inspector.\n\n" +
             "This plus Pin Drop Delay is also how long the TPASS buttons stay " +
             "locked after Pull — PullSequence clears the lockout itself rather " +
             "than a separate timer duplicating these numbers.")]
    [SerializeField] private float pinDropClipLength = 0.5f;

    [Header("Thumb Press")]
    [Tooltip("Presses the THUMB down on Squeeze while the rest of the hand keeps " +
             "gripping the handle. The lever itself moves via the Squeeze clip — " +
             "this is only the thumb. Released when the FIRE goes out, not when " +
             "Sweep is tapped. Leave empty to skip the thumb.")]
    [SerializeField] private FingerGripController rightHandGrip;

    [Header("Nozzle Spray")]
    [Tooltip("Play() on Squeeze, Stop() when the FIRE finishes dying. The particle " +
             "system is parented to the hose tip bone, so it follows the nozzle " +
             "through Aim and Sweep with no code — same trick as the grip markers.")]
    [SerializeField] private ExtinguisherSprayVFX sprayVFX;

    [Header("Fire Reaction")]
    [Tooltip("OFFICE: WeakenFire() on Squeeze, ExtinguishFire() on Sweep — the " +
             "fire shrinks partway, then dies.\n\n" +
             "KITCHEN: ExtinguishFire() fires from TowelCoverController's " +
             "onContact callback, at the exact frame the cloth touches the " +
             "flame. There is no weaken stage — a smothered flame does not " +
             "shrink halfway, it starves.")]
    [SerializeField] private FireController fireController;
    [Header("Office — Two-Fire Decision")]
    [Tooltip("OFFICE ONLY. Leave EMPTY in Kitchen and Classroom.\n\n" +
             "When assigned, the Squeeze and Sweep steps route through " +
             "TwoFireDecision instead of the single Fire Controller above, so " +
             "the player is graded on WHICH fire they attack first.\n\n" +
             "When empty, every path below falls through to the original " +
             "single-fire behaviour, unchanged. That is what protects " +
             "Classroom — it uses TPASS and does reach these branches, so it " +
             "relies on this field being empty.\n\n" +
             "KITCHEN CANNOT BE AFFECTED EITHER WAY. Its step names contain " +
             "no 'Squeeze' or 'Sweep', so it never reaches the branches that " +
             "read this field at all.")]

    [SerializeField] private TwoFireDecision twoFireDecision;

    [Header("Return to Rest (after the fire is out)")]
    [Tooltip("Seconds for the hose and lever to settle back to rest. This is a FADE " +
             "of the Animator layer weight to 0, not a clip — at weight 0 the masked " +
             "layer stops writing and the bones fall back to the bind pose. Around " +
             "0.8 reads as lowering the hose; much faster reads as a jump.")]
    [SerializeField] private float relaxDuration = 0.8f;

    [Tooltip("Return the LEFT HAND to rest when the fire goes out. Untick to isolate " +
             "a problem during the relax.")]
    [SerializeField] private bool releaseLeftHandOnFireOut = true;

    [Tooltip("Relax the HOSE and LEVER back to rest. The lever matters as much as " +
             "the hose: SetSqueeze(false) lifts the THUMB, but nothing else releases " +
             "the lever bone — without this the thumb lifts off a pressed lever.")]
    [SerializeField] private bool relaxExtinguisherOnFireOut = true;

    [Header("Input Lockout (anti spam-click)")]
    [Tooltip("How long the step buttons stay locked after a WRONG tap.\n\n" +
             "The only real timer in this file. A wrong tap has no animation to " +
             "hang the unlock off, so it needs a genuine cooldown.\n\n" +
             "It complements the penalty clamp in RegisterWrongAction rather than " +
             "duplicating it: the clamp stops the RECORDED penalty exceeding the " +
             "time budget, this stops five panicked taps draining the whole clock " +
             "and losing the run outright. 0.6 breaks a double-tap without making " +
             "a deliberate correction feel sluggish.")]
    [SerializeField] private float wrongActionCooldown = 0.6f;

    [Tooltip("SAFETY VALVE, not a timing setting.\n\n" +
             "The buttons unlock when a step's animation reports it is finished — " +
             "never on a timer. But if a branch ever forgets to unlock, every " +
             "button would stay dead for the rest of the run.\n\n" +
             "So the lockout is force-cleared after this many seconds and a warning " +
             "is logged naming the step. A missed unlock becomes a delay you can " +
             "find in the Console rather than a frozen simulation you cannot.\n\n" +
             "KITCHEN NEEDS 4 AT MINIMUM. The towel dip runs about 2.9s, and a " +
             "valve set to 3 fires with a tenth of a second to spare — one frame " +
             "hitch and it trips mid-dip. The Cover step runs about 1.65s and the " +
             "valve turn about 1.75s, both comfortably inside.\n\n" +
             "If you ever see the warning, do NOT raise this further — find the " +
             "missing unlock.")]
    [SerializeField] private float maxStepLockout = 4f;

    // --- RUNTIME STATE ---
    private float timeRemaining;
    [SerializeField] private int currentStep = 1;
    [SerializeField] private bool simActive = false;
    private List<string> missedTips = new List<string>();
    private List<StepResult> stepResults = new List<StepResult>();
    private int totalPenaltySeconds = 0;

    // Held so it can be cancelled. OnFireIsOut can legitimately run twice —
    // once from FireController's callback, once from EndSimulation's safety
    // net — and two coroutines lerping the same weight would visibly fight.
    private Coroutine relaxRoutine;

    // TRUE while a step is still animating, or during a wrong-tap cooldown.
    // TPASSButtonManager and WCTLButtonManager both read IsStepBusy and
    // ignore taps while it is up.
    private bool stepBusy = false;
    private float stepBusyStartedAt = 0f;

    // The timed unlock used by wrong taps, held so it can be cancelled.
    private Coroutine lockoutRoutine;

    // TRUE from the moment Pull starts until PullSequence finishes — UNLESS a
    // later step claims the left hand first, which clears it early and makes
    // PullSequence skip its release. Second guard on the Pull -> Aim bug.
    private bool pullSequenceOwnsHand = false;

    // The delayed arrow reveal, held so it can be cancelled if the run ends
    // during the wait.
    private Coroutine evacuateArrowRoutine;

    // TRUE only while THIS script is the one showing the arrow. See the note
    // above HideEvacuateArrow for why that distinction matters.
    private bool evacuateArrowShown = false;

    // --- PUBLIC READ-ONLY PROPERTIES ---
    public float TimeRemaining => timeRemaining;
    public int CurrentStep => currentStep;
    public List<string> MissedTips => missedTips;
    public bool IsSimActive => simActive;
    public int TotalPenaltySeconds => totalPenaltySeconds;

    /// <summary>
    /// The last step number in this environment's sequence. Exposed so an exit
    /// trigger can check it is completing the real final step rather than a
    /// hardcoded 8.
    /// </summary>
    public int MaxStep => maxStep;

    /// <summary>
    /// TRUE while the previous step is still animating, or during a wrong-tap
    /// cooldown. Taps are IGNORED while this is up — not penalised. The player
    /// is early or flustered, not wrong.
    /// </summary>
    public bool IsStepBusy => stepBusy;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // A scene object saved before maxStep existed deserialises it as 0,
        // and `currentStep < 0` is never true — the run would freeze on step 1
        // with no error at all. Fall back to the TPASS length rather than
        // shipping a simulation nobody can finish.
        if (maxStep <= 0)
        {
            Debug.LogWarning("[SimulationManager] Max Step was 0 — falling back to 8. " +
                             "Set it explicitly in the Inspector to match this " +
                             "scene's step enum. Kitchen should be 5.");
            maxStep = 8;
        }
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

    // -------------------------------------------------------
    // KITCHEN RESET — all seven, in dependency order.
    //
    // Every script that MUTATES run state needs its own reset, and every one
    // must be called. Each failure below is SILENT — no error, no warning,
    // just a second attempt that behaves wrongly:
    //
    //   towelGrab          the towel stays in the player's hands and the sink
    //                      is empty. There is nothing to tap, the Grab step
    //                      never registers, and the run sits on step 1 until
    //                      the timer kills it.
    //
    //   towelWetness       the towel starts WET. WCTLButtonManager's dry-cloth
    //                      guard never fires, so the player taps straight to
    //                      Cover and the entire W of WCTL stops being taught.
    //                      The most damaging one, because the run still
    //                      "works" — it just teaches the wrong thing.
    //
    //   valveTurn          the gas is already off. Same shape of failure: the
    //                      step registers, the log says correct, and the player
    //                      turns a wheel that was already closed. It also
    //                      leaves RightArmIk still pointed at ValveGrip, so the
    //                      next run's towel grab would fade the right arm in
    //                      toward the LPG tank instead of the towel.
    //
    //   wctlButtons        each correct tap greys its own button out, and
    //                      nothing ever put them back. Attempt two starts with
    //                      Wet, Cover and Turn Off all dead.
    //
    //   towelInteractable  the tap target stays disabled or still outlined.
    //
    //   towelDip           a run abandoned mid-dip leaves the towel in the sink
    //                      pose AND the coroutine handle set — so PlayDip's own
    //                      guard silently refuses the next attempt's WET tap.
    //
    //   towelCover         the towel is still parented to the LPG tank, so the
    //                      player starts empty-handed.
    //
    // ORDER MATTERS IN ONE PLACE. towelDip and towelCover restore LOCAL poses
    // relative to whatever the towel is parented to. towelGrab then rewrites
    // the parent and a WORLD pose, so it has to run after them — otherwise the
    // two controllers would write local coordinates against the sink.
    //
    // valveTurn is independent of the towel and can sit anywhere in the list.
    //
    // No-ops entirely in Office and Classroom, where all seven are null.
    // -------------------------------------------------------
    private void ResetKitchenState()
    {
        if (towelDip != null) towelDip.ResetToRest();
        if (towelCover != null) towelCover.ResetToRest();
        if (towelGrab != null) towelGrab.ResetToTimba();
        if (towelWetness != null) towelWetness.ResetToDry();
        if (towelInteractable != null) towelInteractable.ResetForReplay();
        if (valveTurn != null) valveTurn.ResetToStart();
        if (wctlButtons != null) wctlButtons.ResetForReplay();
    }

    private void ResetRuntimeState()
    {
        // No exit arrow left over from a previous run. Guarded by
        // evacuateArrowShown, so this is a no-op when Phase 1 is the one
        // using the shared arrow.
        HideEvacuateArrow();

        // Bring the pin back — it was hidden last run.
        if (pinObject != null) pinObject.SetActive(true);

        // Withdraw the right arm, in case a run was abandoned mid-press.
        if (pressAlarm != null) pressAlarm.ResetState();

        // Lift the thumb, in case a run ended mid-squeeze.
        if (rightHandGrip != null) rightHandGrip.SetSqueeze(false);

        // Kill any spray still running.
        if (sprayVFX != null) sprayVFX.Stop();

        // KITCHEN — towel back on the sink, dry, tappable, valve open,
        // buttons live.
        ResetKitchenState();

        // OFFICE — clear the recorded fire choice and put both fires back.
        // No-op when null, which is Kitchen and Classroom.
        if (twoFireDecision != null) twoFireDecision.ResetForReplay();
        // CRITICAL — see the header note. Without this a replay inherits
        // HoseLayer and LeverLayer at weight 0 and moves nothing, silently.
        RestoreLayerWeights();

        // Buttons live again for the new run.
        EndStepLockout();
        pullSequenceOwnsHand = false;

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

        // SAFETY VALVE. The lockout is meant to be cleared by the animation
        // that raised it; a missed clear would leave every button dead for the
        // rest of the run. This turns that into a delay plus a warning naming
        // the step, so it is findable. Seeing this means a branch is missing
        // its unlock — raising maxStepLockout would only hide it.
        if (stepBusy && Time.time - stepBusyStartedAt > maxStepLockout)
        {
            Debug.LogWarning($"[SimulationManager] Step lockout exceeded {maxStepLockout}s " +
                             $"on step {currentStep} — forcing buttons back on. " +
                             "Something did not clear the lockout.");
            EndStepLockout();
        }

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    // -------------------------------------------------------
    // INPUT LOCKOUT
    //
    // Raised as a step begins, cleared when that step reports it is done.
    // Correct steps clear it from a callback owned by whichever routine holds
    // the real duration — never from a copy of that duration here.
    //
    // Wrong taps have no routine to wait for, so they use BeginTimedLockout
    // with wrongActionCooldown. That is the only genuine timer.
    // -------------------------------------------------------
    private void BeginStepLockout()
    {
        stepBusy = true;
        stepBusyStartedAt = Time.time;
    }

    private void EndStepLockout()
    {
        stepBusy = false;

        if (lockoutRoutine != null)
        {
            StopCoroutine(lockoutRoutine);
            lockoutRoutine = null;
        }
    }

    private void BeginTimedLockout(float seconds)
    {
        BeginStepLockout();

        if (lockoutRoutine != null) StopCoroutine(lockoutRoutine);
        lockoutRoutine = StartCoroutine(ClearLockoutAfter(seconds));
    }

    private IEnumerator ClearLockoutAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // Null the handle BEFORE unlocking, so EndStepLockout does not try to
        // stop the coroutine it is currently being called from.
        lockoutRoutine = null;
        EndStepLockout();
    }

    // -------------------------------------------------------
    // REGISTER CORRECT ACTION
    //
    // Two entry points, one body.
    //
    // The enum version is what Office and Classroom have always called, and
    // its signature has not changed — SimulationInteractable and
    // TPASSButtonManager need no edits.
    //
    // Kitchen calls the string version directly with KitchenStep.ToString(),
    // because KitchenStep is a different type and there is no sensible cast
    // between them. That is fine: the enum was only ever used as a string in
    // here anyway.
    // -------------------------------------------------------
    public void RegisterCorrectAction(SimulationInteractable.SimStep step, string chosenAction)
    {
        RegisterCorrectAction(step.ToString(), chosenAction);
    }

    public void RegisterCorrectAction(string stepName, string chosenAction)
    {
        if (!simActive) return;

        stepResults.Add(new StepResult
        {
            step_name = stepName,
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = true,
            penalty_seconds = 0
        });

        // FEEDBACK LOG: green row naming the completed step.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowCorrect(StepNames.Friendly(stepName));

        // Lock BEFORE dispatching — some branches finish synchronously and
        // clear it again on the very same line.
        BeginStepLockout();

        PlayClipForStep(stepName);

        // maxStep, not a hardcoded 8 — Kitchen's sequence is shorter.
        if (currentStep < maxStep)
        {
            currentStep++;

            // The last step is Evacuate in BOTH sequences — 8 in Office, 5 in
            // Kitchen. Reaching it is the cue to point at the exit. Reading
            // maxStep rather than matching on the step NAME means this works
            // in every environment without knowing which enum it uses.
            if (currentStep >= maxStep)
                ShowEvacuateArrow();
        }
    }

    // -------------------------------------------------------
    // REGISTER WRONG ACTION
    //
    // Same two-entry-point pattern as above.
    // -------------------------------------------------------
    public void RegisterWrongAction(SimulationInteractable.SimStep step, string chosenAction, float timePenalty, string tip)
    {
        RegisterWrongAction(step.ToString(), chosenAction, timePenalty, tip);
    }

    // showTipDirectly — OFFICE TWO-FIRE DECISION ONLY.
    //
    // Normally the red row shows the hint for the CURRENT step, because a
    // wrong tap means the player is on the wrong step and needs pointing at
    // the right one.
    //
    // A fire choice is different: the player performed the RIGHT step, on the
    // wrong target. The step hint would tell them to do what they just did.
    // With this true, the caller's own tip is shown instead.
    //
    // Defaults to false, so every existing call site — Office, Classroom,
    // Kitchen, and the enum overload above — behaves exactly as before.
    public void RegisterWrongAction(string stepName, string chosenAction, float timePenalty, string tip, bool showTipDirectly = false)
    {
        if (!simActive) return;

        // Only take the time that ACTUALLY existed to lose. timeRemaining
        // floors at 0, but totalPenaltySeconds used to add the full nominal
        // penalty regardless — so a 20s hit with 10s left recorded 20, and
        // five of those reported 100 seconds of penalties out of a 90-second
        // budget. Impossible, and it reads as a bug in the admin panel.
        float appliedPenalty = Mathf.Min(timePenalty, timeRemaining);

        timeRemaining -= appliedPenalty;
        missedTips.Add(tip);

        int penaltyInt = Mathf.RoundToInt(appliedPenalty);
        totalPenaltySeconds += penaltyInt;

        stepResults.Add(new StepResult
        {
            step_name = stepName,
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = false,
            penalty_seconds = penaltyInt
        });

        // FEEDBACK LOG: red row. The hint points at the CURRENT step the
        // player should be doing, NOT the button they mis-tapped — it
        // guides them to the right next move rather than scolding.
        //
        // useKitchenHints picks the table. With it false the returned line is
        // byte-for-byte what Office and Classroom got before.
        //
        // The DISPLAYED penalty is the full nominal one, not the clamped
        // figure. With 5s left, a 20s mistake still cost them 20 seconds'
        // worth of trouble; showing "-5s" understates it, and at 0 seconds
        // "-0s" would read as if wrong actions were free.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowWrong(
                showTipDirectly ? tip : StepNames.Hint(currentStep, useKitchenHints),
                Mathf.RoundToInt(timePenalty));

        // COOLDOWN. The clamp above stops the RECORDED penalty exceeding the
        // budget, but it does not stop the run being lost — five taps at 20s
        // still drains 90 seconds, clamped to 20/20/20/20/10. This gives the
        // player a beat to read the hint instead of losing to their own thumb.
        BeginTimedLockout(wrongActionCooldown);

        // A penalty can drive the clock to zero, which ends the run just as
        // surely as the timer expiring in Update(). Without this the
        // simulation kept running at 0 seconds with no way to finish.
        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    // -------------------------------------------------------
    // STEP ANIMATION
    //
    // Matched on the step's NAME rather than the enum member, so this
    // survives a rename or reorder — it only cares that the name contains
    // "Alarm", "Twist", "Pull", and so on.
    //
    // That substring matching is also what lets Kitchen share this method.
    // "GrabTowel" hits the Grab branch; "WCTL_Wet", "WCTL_Cover" and
    // "WCTL_TurnOff" hit their own branches further down. NO KitchenStep NAME
    // CONTAINS "Alarm", which is why Kitchen needs no edit here to skip the
    // alarm step — that branch is simply unreachable there.
    //
    // ORDER MATTERS. The alarm press is a HAND animation and does not use
    // extinguisherAnimator at all, so it must be dispatched BEFORE the
    // null guard below — otherwise a scene with no Animator assigned would
    // silently swallow the press. WCTL steps are exempted from that guard
    // for exactly the same reason.
    //
    // EVERY branch must end the lockout, either immediately or by handing
    // EndStepLockout into a callback. A branch that forgets leaves the
    // buttons dead until the Update() safety valve fires.
    // -------------------------------------------------------
    private void PlayClipForStep(string name)
    {
        // --- HAND ANIMATION (no extinguisher Animator involved) ---
        //
        // OFFICE AND CLASSROOM ONLY. Kitchen never reaches this branch: its
        // step names are GrabTowel, WCTL_Wet, WCTL_Cover, WCTL_TurnOff and
        // Evacuate, none of which contain "Alarm".
        if (name.Contains("Alarm"))
        {
            if (pressAlarm != null)
            {
                pressAlarm.PlayAlarmPress();
                Debug.Log("[SimulationManager] Alarm press animation started.");
            }

            // No lockout needed: the step buttons are not visible on step 1,
            // and the next action is a world tap on the extinguisher.
            EndStepLockout();
            return;
        }

        if (name.Contains("Grab"))
        {
            // Catches BOTH "GrabExtinguisher" (Office) and "GrabTowel"
            // (Kitchen). Neither needs anything from this method — the flight
            // is owned by ExtinguisherGrab and TowelGrab respectively, both
            // started from their own Interactable on the correct tap.
            //
            // Cancel any press still in flight. Nothing stops the player
            // tapping Grab while the arm is still withdrawing — if that
            // happened, PressAlarmController would fade rightArmIK.weight
            // 1 -> 0 while ExtinguisherGrab fades the SAME value 0 -> 1.
            // Two writers on one value, which stutters or snaps the arm.
            //
            // Null in Kitchen, so this is a no-op there.
            if (pressAlarm != null)
            {
                pressAlarm.ResetState();
                Debug.Log("[SimulationManager] Alarm press cancelled - grab takes the arm.");
            }

            EndStepLockout();
            return;
        }

        // --- EXTINGUISHER CLIPS from here down ---
        //
        // WCTL steps are EXEMPT from this guard. Kitchen has no extinguisher
        // and leaves extinguisherAnimator empty, so without the exemption
        // every WCTL step would return right here — the towel dip would never
        // play, and there would be no error to explain why.
        //
        // Everything below still null-checks the Animator through PlayClip
        // and PlayHoseClip, so a WCTL step reaching a TPASS branch by
        // accident is harmless rather than a crash.
        if (extinguisherAnimator == null && !name.Contains("WCTL"))
        {
            EndStepLockout();
            return;
        }

        if (name.Contains("Twist"))
        {
            // WAIT FOR THE HAND. Playing straight away twisted the pin
            // while the hand was still travelling toward it. The callback
            // fires the moment the hand is holding Grip_Pin.
            //
            // Deliberately NOT a delay field: that would duplicate
            // moveDuration in a second file, and the two would drift apart
            // the first time one was retuned, with nothing to warn you.
            if (leftHandIK != null)
            {
                leftHandIK.ReachPinAndTwist(() =>
                {
                    PlayClip(clipTwist, pinLayerIndex);
                    EndStepLockout();
                });
            }
            else
            {
                PlayClip(clipTwist, pinLayerIndex);
                EndStepLockout();
            }
        }
        else if (name.Contains("Pull"))
        {
            // The left hand belongs to PullSequence until it finishes —
            // unless a later step claims it, which clears this flag.
            pullSequenceOwnsHand = true;

            PlayClip(clipPull, pinLayerIndex);

            // PullSequence clears the lockout ~0.9s later. That window is
            // where the Pull -> Aim bug used to slip through.
            StartCoroutine(PullSequence());
        }
        else if (name.Contains("Aim"))
        {
            // The left hand is ours now. If PullSequence is somehow still
            // running it will see this cleared and skip its ReleaseAll()
            // instead of cancelling the travel we are about to start.
            //
            // The lockout should make this unreachable — but it is one line,
            // and the bug it guards against produces no error at all.
            pullSequenceOwnsHand = false;

            // Same reasoning as Twist: let the hand arrive at the nozzle
            // before the hose starts bending.
            if (leftHandIK != null)
            {
                leftHandIK.GrabNozzle(() =>
                {
                    PlayHoseClip(clipAim);
                    EndStepLockout();
                });
            }
            else
            {
                PlayHoseClip(clipAim);
                EndStepLockout();
            }
        }
        else if (name.Contains("Squeeze"))
        {
            pullSequenceOwnsHand = false;

            // LEVER ONLY — see the header note on why this must not play
            // on HoseLayer.
            PlayClip(clipSqueeze, leverLayerIndex);

            // THUMB: presses down with the lever, stays until the FIRE
            // goes out — not until Sweep is tapped.
            if (rightHandGrip != null)
                rightHandGrip.SetSqueeze(true);

            // SPRAY: starts here and runs through Sweep until the flames
            // are actually out.
            if (sprayVFX != null)
            {
                sprayVFX.Play();
                Debug.Log("[SimulationManager] Nozzle spray started.");
            }

            // OFFICE TWO-FIRE DECISION. When assigned, TwoFireDecision picks
            // the fire nearest the player, grades that choice, and weakens
            // whichever one they targeted.
            //
            // When NULL — Classroom, or Office before the two fires are
            // placed — this falls straight through to the original line
            // below, unchanged.
            //
            // Kitchen never reaches this branch: no KitchenStep name contains
            // "Squeeze".
            if (twoFireDecision != null)
                twoFireDecision.HandleSqueeze(name, EndStepLockout);
            else if (fireController != null)
                fireController.WeakenFire(EndStepLockout);
            else
                EndStepLockout();
        }
        else if (name.Contains("Sweep"))
        {
            pullSequenceOwnsHand = false;

            // Only the hose should move — HoseMask limits this clip to
            // Bone.012 and below. If the right hand or the body ever
            // swings on Sweep, it is NOT this line: check that
            // HandAnimationController.sweepControlledByAnimation is
            // ticked, and that HoseMask has no lever bones in it.
            PlayHoseClip(clipSweep);

            // OFFICE TWO-FIRE DECISION. Kills the fire chosen at Squeeze —
            // NOT whichever one the player happens to be nearest now. The
            // target is locked for the whole discharge, which matches the
            // doctrine already in this file's header: you do not re-aim
            // while discharging.
            //
            // OnFireIsOut still runs exactly as before...
            if (twoFireDecision != null)
            {
                twoFireDecision.HandleSweep(OnFireIsOut);
            }
            else if (fireController != null)
            {
                fireController.ExtinguishFire(OnFireIsOut);
            }
            else
            {
                // No fire assigned (test scene). Nothing will call back, so
                // end the discharge now rather than leaving it running.
                Debug.LogWarning("[SimulationManager] No FireController assigned - " +
                                 "stopping spray immediately instead of waiting for " +
                                 "the fire to go out.");
                OnFireIsOut();
            }

            // The buttons are hidden after step 7 anyway, but clearing keeps
            // the flag honest and stops the safety valve firing a warning.
            EndStepLockout();
        }
        else if (name.Contains("Wet"))
        {
            // KITCHEN — dip, soak, wring, lift. About 2.9 seconds.
            //
            // The dip OWNS its own duration, so it also owns the unlock:
            // EndStepLockout is handed in as the completion callback rather
            // than fired from a timer here. Same rule that fixed the
            // Pull -> Aim bug — one owner per behaviour, and no duration
            // duplicated in a second file where the two could drift apart.
            //
            // TowelDipController also calls TowelWetnessController.SetWet()
            // partway through the soak, which is what unblocks the Cover
            // button. So this one line drives the choreography, the material
            // change AND the step gate.
            if (towelDip != null)
            {
                towelDip.PlayDip(EndStepLockout);
            }
            else
            {
                Debug.LogWarning("[SimulationManager] Wet step reached with no " +
                                 "TowelDipController assigned — skipping the dip. " +
                                 "The towel will never become wet, so the Cover " +
                                 "button will stay blocked.");
                EndStepLockout();
            }
        }
        else if (name.Contains("Cover"))
        {
            // KITCHEN — throw, contact, settle. About 1.65 seconds.
            //
            // TWO callbacks, and the split is the whole point.
            //
            // onContact fires at the exact frame the cloth reaches the flame.
            // The fire dies THERE — not when the button was tapped, and not
            // when the animation finishes. Either of those reads wrong: one
            // kills the fire before anything touches it, the other leaves it
            // burning through a cloth already lying on top of it.
            //
            // onComplete fires ~0.85s later, once the bone animation has
            // finished its wrap and settle. THAT is when the buttons unlock,
            // so the player cannot start the valve while the towel is still
            // visibly dropping onto the tank.
            //
            // ExtinguishFire takes null here rather than OnFireIsOut, because
            // OnFireIsOut is the EXTINGUISHER's teardown — spray off, thumb
            // up, hose relaxed. None of that exists in Kitchen. Passing it
            // would be harmless (every reference inside is null there) but it
            // would also be a lie about what happens on this step.
            if (towelCover != null)
            {
                towelCover.PlayCover(
                    onContact: () => { if (fireController != null) fireController.ExtinguishFire(null); },
                    onComplete: EndStepLockout);
            }
            else
            {
                Debug.LogWarning("[SimulationManager] Cover step reached with no " +
                                 "TowelCoverController assigned — killing the fire " +
                                 "without the animation.");

                if (fireController != null) fireController.ExtinguishFire(null);
                EndStepLockout();
            }
        }
        else if (name.Contains("TurnOff"))
        {
            // KITCHEN — reach, turn, release. About 1.75 seconds.
            //
            // MUST SIT ABOVE THE CATCH-ALL BELOW. Anything after a bare else
            // is unreachable, and a branch that never runs looks identical to
            // a branch that runs and does nothing.
            //
            // The hand needs no separate animation. ValveGrip is a CHILD of
            // Regulator_Valve, so rotating the valve orbits the marker and the
            // IK constraint drags the wrist around with it — the same trick
            // Grip_Pin uses on the extinguisher, and GripPoint_R on the towel.
            //
            // Same one-owner rule as the dip and the cover: the routine that
            // holds the real duration also owns the unlock, so EndStepLockout
            // is handed in rather than fired from a timer here.
            //
            // NOTE ON THE RIGHT ARM. By this point TowelCoverController has
            // already faded both arm IKs to zero and released the fingers, so
            // the arm is sitting in its FK rest pose with nothing competing
            // for it. That is why the reach can be a plain weight fade rather
            // than a travel from wherever the hand happened to be.
            if (valveTurn != null)
            {
                valveTurn.PlayTurn(EndStepLockout);
            }
            else
            {
                Debug.LogWarning("[SimulationManager] Turn Off step reached with no " +
                                 "KitchenValveController assigned — skipping the " +
                                 "animation. The step still registers, but the valve " +
                                 "will not move and the gas stays visibly on.");
                EndStepLockout();
            }
        }
        else
        {
            // Evacuate, or any step with no animation yet. Nothing to wait for.
            //
            // When a new step gets its choreography, add a branch ABOVE this
            // one — anything after a catch-all else is unreachable — and make
            // it clear the lockout itself, either directly or through a
            // completion callback.
            EndStepLockout();
        }
    }

    // -------------------------------------------------------
    // EVACUATE ARROW
    //
    // The only marker arrow in Phase 2. Raised once the final step becomes
    // the current step, pointed at exitTarget, and torn down by every path
    // that ends a run.
    //
    // WHY THE DELAY. In Office, Sweep registers as correct the moment it is
    // tapped — but FireController then spends about a second fading the
    // flames. Showing the arrow on that same frame reads as "run now" while
    // the fire is still visibly burning, which is the opposite of what the
    // drill teaches.
    //
    // WHY evacuateArrowShown EXISTS. HideEvacuateArrow is called from
    // ResetRuntimeState, which runs in Start() — INCLUDING when the scene
    // loads in Phase 1. Without the flag it would call Hide() on the shared
    // MarkerArrowManager and could clear a Phase 1 hazard arrow that has
    // nothing to do with this script. The flag makes Hide a no-op unless
    // this script is the one that raised the arrow.
    // -------------------------------------------------------
    private void ShowEvacuateArrow()
    {
        if (!useEvacuateArrow) return;

        // OFFICE TWO-FIRE DECISION. A wrong fire choice ends the run in a loss
        // a few seconds from now. Pointing the player at a door they are about
        // to lose to would read as a bug, not a lesson.
        //
        // Null in Kitchen and Classroom, so this is a no-op there.
        if (twoFireDecision != null && twoFireDecision.WrongFireChosen) return;

        if (exitTarget == null)
        {
            Debug.LogWarning("[SimulationManager] Evacuate step reached but no Exit " +
                             "Target assigned — no arrow shown. Assign the Door in " +
                             "the Inspector.");
            return;
        }

        if (evacuateArrowRoutine != null)
            StopCoroutine(evacuateArrowRoutine);

        evacuateArrowRoutine = StartCoroutine(ShowEvacuateArrowAfterDelay());
    }

    private IEnumerator ShowEvacuateArrowAfterDelay()
    {
        if (evacuateArrowDelay > 0f)
            yield return new WaitForSeconds(evacuateArrowDelay);

        evacuateArrowRoutine = null;

        // The run can end DURING the delay — the timer expiring, or a penalty
        // driving the clock to zero. Without this check the arrow would pop up
        // over the results screen after the simulation was already over.
        if (!simActive) yield break;

        if (MarkerArrowManager.Instance == null)
        {
            Debug.LogWarning("[SimulationManager] No MarkerArrowManager in this scene " +
                             "— no evacuate arrow. Add one, or untick Use Evacuate Arrow.");
            yield break;
        }

        MarkerArrowManager.Instance.PointAt(exitTarget);
        evacuateArrowShown = true;

        Debug.Log($"[SimulationManager] Evacuate arrow on '{exitTarget.name}'.");
    }

    private void HideEvacuateArrow()
    {
        if (evacuateArrowRoutine != null)
        {
            StopCoroutine(evacuateArrowRoutine);
            evacuateArrowRoutine = null;
        }

        // Only clear an arrow THIS script raised — see the header note.
        if (!evacuateArrowShown) return;
        evacuateArrowShown = false;

        if (MarkerArrowManager.Instance != null)
            MarkerArrowManager.Instance.Hide();
    }

    // -------------------------------------------------------
    // THE FIRE IS OUT
    //
    // Handed to FireController.ExtinguishFire() as a callback on the OFFICE
    // Sweep step. Runs when the flames have finished fading, NOT when Sweep
    // was tapped.
    //
    // Kitchen does not use this. Its Cover step passes null instead, because
    // every reference below belongs to the extinguisher.
    //
    // A named method rather than a lambda because EndSimulation calls it
    // directly as a safety net.
    // -------------------------------------------------------
    private void OnFireIsOut()
    {
        // 1. Thumb off the lever — nothing left to discharge.
        if (rightHandGrip != null)
            rightHandGrip.SetSqueeze(false);

        // 2. Stop() lets airborne particles finish their lifetime instead
        // of vanishing mid-flight, so the spray tails off naturally.
        if (sprayVFX != null)
        {
            sprayVFX.Stop();
            Debug.Log("[SimulationManager] Fire is out - nozzle spray stopped.");
        }

        // 3. The left hand has nothing left to hold.
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
    // There is no "return to rest" clip, so we cannot play our way back.
    // Instead we fade the masked layers' WEIGHT to 0 — at which point they
    // write nothing and the bones fall back to the model's bind pose.
    // -------------------------------------------------------
    private void RelaxExtinguisherToRest()
    {
        if (extinguisherAnimator == null) return;

        if (relaxRoutine != null)
        {
            StopCoroutine(relaxRoutine);
            relaxRoutine = null;
        }

        // Instant snap at 0 — useful for checking WHERE the rest pose
        // lands before tuning how long it takes to get there.
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
            // of the swept pose instead of moving at constant speed and
            // stopping dead.
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / relaxDuration));

            SetLayerWeightSafe(hoseLayerIndex, Mathf.Lerp(hoseStart, 0f, t));
            SetLayerWeightSafe(leverLayerIndex, Mathf.Lerp(leverStart, 0f, t));

            yield return null;
        }

        // Land exactly on 0 — a lerp can finish a hair short.
        SetLayerWeightSafe(hoseLayerIndex, 0f);
        SetLayerWeightSafe(leverLayerIndex, 0f);

        relaxRoutine = null;
        Debug.Log("[SimulationManager] Extinguisher relaxed to rest.");
    }

    // Puts every masked layer back to full strength for a fresh run.
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

    // Guarded layer access. A layer index past the end of the Animator
    // throws, and these indices are hand-typed in the Inspector — so a
    // mismatch would otherwise take down whatever coroutine is running.
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

    // Play a state from frame 0 on a SPECIFIC layer. Play() rather than
    // CrossFade() because these clips animate different parts — there is
    // nothing to blend between.
    //
    // The layer argument matters: passing 0 targets the empty Base Layer,
    // which holds no states and no mask, so nothing would play at all.
    private void PlayClip(string stateName, int layer)
    {
        if (extinguisherAnimator == null || string.IsNullOrEmpty(stateName)) return;

        extinguisherAnimator.Play(stateName, layer, 0f);
        Debug.Log($"[SimulationManager] Extinguisher clip: {stateName} (layer {layer})");
    }

    // Play a HOSE clip with a short blend, so the change between Aim and
    // Sweep reads as a movement rather than a jump.
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
    // The whole post-Pull choreography in one place:
    //   1. wait    -> the Pull clip carries the pin out
    //   2. drop    -> PinDrop plays, pin falls
    //   3. wait    -> the drop finishes
    //   4. hide    -> pin disappears
    //   5. release -> left hand lets go, IF it is still ours
    //   6. unlock  -> TPASS buttons live again
    //
    // Step 5 is what used to break Aim. A tap on Aim during this sequence
    // started a nozzle travel, and this release then went through
    // StartExclusive and STOPPED it — killing the callback that plays the
    // Aim clip. The lockout should make that impossible now, but
    // pullSequenceOwnsHand is checked anyway: two independent guards on a
    // bug that produces no error message is proportionate.
    // -------------------------------------------------------
    private IEnumerator PullSequence()
    {
        yield return new WaitForSeconds(pinDropDelay);
        PlayClip(clipPinDrop, pinLayerIndex);

        yield return new WaitForSeconds(pinDropClipLength);

        if (pinObject != null)
        {
            pinObject.SetActive(false);
            Debug.Log("[SimulationManager] Pin hidden after drop.");
        }

        if (pullSequenceOwnsHand)
        {
            if (leftHandIK != null)
            {
                leftHandIK.ReleaseAll();
                Debug.Log("[SimulationManager] Left hand released to rest.");
            }
        }
        else
        {
            Debug.Log("[SimulationManager] Pin drop release SKIPPED — a later step " +
                      "already claimed the left hand.");
        }

        pullSequenceOwnsHand = false;
        EndStepLockout();
    }

    // -------------------------------------------------------
    // END SIMULATION
    // -------------------------------------------------------
    // failReason — OPTIONAL, and only for losses Unity can identify that
    // the server cannot.
    //
    // Laravel infers the reason from what it can see: not passed, no time
    // left, so "timeout". That was right while a timeout was the only loss
    // Unity could detect.
    //
    // It stopped being right when the Office decision scenario started
    // ending runs early. A player who clears the wrong fire loses with
    // fifty seconds still on the clock — the lose panel says WRONG
    // DECISION and the admin panel said "Ran out of time" for the same
    // attempt.
    //
    // Leave it out and nothing changes: Kitchen, Classroom, the timer path
    // and Door all still call this with one argument, Laravel receives an
    // empty string, and falls back to exactly the inference it does today.
    public void EndSimulation(bool won, string failReason = null)
    {
        if (!simActive) return;
        simActive = false;

        AudioManager.Instance?.StopMusic();
        AudioManager.Instance?.StopAmbient();
        // SAFETY NET: a run can end at ANY moment — the timer can expire
        // mid-discharge, or a penalty can drive the clock to zero between
        // Squeeze and the fire going out. The FireController callback may
        // then never arrive, and the spray would keep firing over the
        // results screen with the hand clamped to the nozzle.
        //
        // Harmless when the fire already went out: Stop() on a stopped
        // system and a relax fade restarting from 0 both do nothing.
        // Harmless in Kitchen too, where every reference here is null.
        OnFireIsOut();

        // KITCHEN — same reasoning, for the towel and the valve. The timer can
        // expire mid-dip, mid-throw or mid-turn, leaving the towel frozen in
        // the sink pose, parented to the LPG tank, or the right arm still
        // welded to a half-turned valve over the results screen — with
        // coroutine handles still set into the next run.
        //
        // The full seven, not just the controllers: a run that ends on step 3
        // leaves the Wet button greyed out and the towel wet, and neither
        // would be put back by Start() alone if the scene is not reloaded.
        ResetKitchenState();

        // Clear the lockout so a run that ended mid-choreography does not
        // leave the flag set for whatever comes next.
        EndStepLockout();
        pullSequenceOwnsHand = false;

        // The run is over — win, loss or timeout. The exit arrow has nothing
        // left to point at, and would otherwise hang over the results screen.
        // Also cancels a delayed reveal still counting down.
        HideEvacuateArrow();



        GameModeManager modeManager = FindFirstObjectByType<GameModeManager>();
        if (modeManager != null)
            modeManager.ResetToPhase1();

        int finalScore = Mathf.RoundToInt(timeRemaining);

        // ── SUBMIT EVERY ATTEMPT ─────────────────────────────────────
        // This used to run only inside the `won` branch, so timeouts were
        // never sent anywhere — failed runs did not exist as far as the
        // system was concerned, and "attempt counts" were really
        // successful-attempt counts.
        //
        // `won` becomes phase2_passed:
        //   won = true   -> finished all steps with time left
        //   won = false  -> the timer hit zero (the only loss Unity detects)
        //
        // A low-score failure arrives here as won = true — the player DID
        // beat the clock. Laravel scores the penalties and decides they
        // did not pass. The server owns pass/fail, which is why
        // ResultsSubmitter branches on the RESPONSE, not on this flag.
        if (won)
        {
            Debug.Log($"[SimulationManager] Completed. Submitting for validation. Local score: {finalScore}");

            // Fires here, NOT after the server responds. `won` means the player
            // beat the clock, which is the thing they just did and the thing
            // they are owed feedback on. Laravel may still fail them on
            // penalties — that verdict arrives seconds later in the panel, and
            // pairing a sound with it would make the audio lag the moment.
            AudioManager.Play(winSound);

            ResultsUIManager resultsUI = FindFirstObjectByType<ResultsUIManager>();
            if (resultsUI != null)
                resultsUI.ShowSubmitting();

            onWin.Invoke();
        }
        else
        {
            Debug.Log("[SimulationManager] LOSE — timer expired. Recording the attempt.");

            AudioManager.Play(loseSound);

            onLose.Invoke();
        }

        // A WRONG DECISION CAN ALSO END AS A TIMEOUT, and both are real.
        //
        // TwoFireDecision holds the run for about six seconds after the
        // wrong fire dies so the player can watch the doorway close. If
        // the clock happens to run out inside that window, Update() ends
        // the run first — with no reason — and the attempt would be filed
        // as a plain timeout even though the player saw WRONG DECISION on
        // screen.
        //
        // So when no reason was passed and the wrong fire was chosen, name
        // it. The panel and the record then agree, which is the whole point
        // of sending this at all.
        //
        // Null in Kitchen and Classroom, so this is a no-op there.
        if (string.IsNullOrEmpty(failReason) && !won &&
            twoFireDecision != null && twoFireDecision.WrongFireChosen)
        {
            failReason = "wrong_decision";
        }

        if (resultsSubmitter != null)
        {
            resultsSubmitter.Submit(finalScore, totalPenaltySeconds, stepResults, won, failReason);
        }
        else
        {
            Debug.LogWarning("[SimulationManager] No ResultsSubmitter assigned — results were NOT sent to Laravel.");
        }
    }
}