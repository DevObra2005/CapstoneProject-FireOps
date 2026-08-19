using UnityEngine;
using UnityEngine.UI;

// -------------------------------------------------------
// WCTLButtonManager - Kitchen's equivalent of TPASSButtonManager.
//
// Drives the on-screen WCTL decision buttons (Wet, Cover, Turn Off) in the
// correct order, shuffled positions.
//
// SAME GATEKEEPER ROLE AS TPASS. This script answers four questions and
// nothing else:
//   1. Is the previous step still ANIMATING?      (IsStepBusy)
//   2. Is this the RIGHT step to tap?             (CurrentStep)
//   3. Is the player STANDING somewhere sensible? (position gate)
//   4. Is the towel actually WET?                 (Cover only)
// If all four pass, it hands off to SimulationManager and steps back.
//
// Read the header of TPASSButtonManager.cs before changing anything here.
// The one-owner-per-behaviour rule and the anti-spam reasoning both apply
// unchanged, and were expensive to learn.
//
// -------------------------------------------------------
// WHAT IS DIFFERENT FROM TPASS, AND WHY
// -------------------------------------------------------
//
// * THE POSITION GATE IS PER-BUTTON, NOT GLOBAL.
//   Office only gates Squeeze, always against the fire, so one fireTarget
//   field was enough. Kitchen gates three separate steps against three
//   different objects: Wet at the sink, Cover at the flame, Turn Off at the
//   tank valve. So the target, the ranges and the hints all live on the
//   BUTTON, not on the manager.
//
//   Leave positionTarget empty and that button has no gate at all. Same
//   fail-open principle as Office: a half-dressed scene should still be
//   playable, because the cost of a false block is a stuck simulation.
//
// * THERE IS NO "SWEEP" EQUIVALENT.
//   WCTL is four letters but only three buttons. "Leave" is the exit door,
//   exactly as Evacuate is in Office - completed by a trigger, not a tap.
//
// * THE DRY TOWEL GUARD IS A PENALTY, NOT A HINT.
//   Standing in the wrong place is a POSITIONING mistake - the player knows
//   what to do, they are just standing badly - so it costs no time.
//
//   Covering a flame with a DRY cloth is a KNOWLEDGE mistake. The cloth
//   catches fire. That is the single most dangerous thing a trainee can do
//   in this scenario, so it is charged at full penalty like any other wrong
//   action. Different category, different cost.
// -------------------------------------------------------

public class WCTLButtonManager : MonoBehaviour
{
    [System.Serializable]
    public class WCTLButton
    {
        public Button button;
        public KitchenStep step;

        [TextArea]
        [Tooltip("Educational tip shown when this button is tapped out of order.")]
        public string wrongTip;

        [Header("Position Gate (leave target empty to disable)")]
        [Tooltip("What the player must be standing near for this step to " +
                 "register. Sink for Wet, the flame for Cover, the tank valve " +
                 "for Turn Off.\n\n" +
                 "EMPTY = no gate. The step works from anywhere.")]
        public Transform positionTarget;

        [Tooltip("Closer than this and the player is warned to step back. " +
                 "For Cover this matters - you do not stand on top of a gas " +
                 "flame. For Wet at a sink, a small value is fine.")]
        public float tooCloseDistance = 0.4f;

        [Tooltip("Further than this and the action makes no sense - you cannot " +
                 "reach the tap, the flame or the valve from across the room.")]
        public float tooFarDistance = 2.5f;

        [Tooltip("Also require the player to be LOOKING at the target, not just " +
                 "standing near it. Worth ticking for Cover; usually " +
                 "unnecessary for Wet.")]
        public bool requireFacing = false;

        [TextArea] public string tooFarHint = "Move closer";
        [TextArea] public string tooCloseHint = "You're too close - step back";
        [TextArea] public string notFacingHint = "Face it before you act";
    }

    [Header("The WCTL Buttons (Wet, Cover, Turn Off)")]
    [Tooltip("Leave is NOT a button - the exit door completes it, exactly as " +
             "Evacuate does in Office.")]
    [SerializeField] private WCTLButton[] wctlButtons;

    [Header("Wrong Action Settings")]
    [SerializeField] private float timePenalty = 20f;

    [Header("Visibility")]
    [Tooltip("Kitchen steps: 1 Alarm, 2 Grab Towel, 3 Wet, 4 Cover, " +
             "5 Turn Off, 6 Evacuate.\n\n" +
             "So the buttons live from 3 to 5.")]
    [SerializeField] private int showFromStep = 3;
    [SerializeField] private int showToStep = 5;

    [Header("Position Check")]
    [Tooltip("Master switch for ALL position gates. Untick while testing the " +
             "step order without walking around the kitchen.")]
    [SerializeField] private bool requirePosition = true;

    [Tooltip("The player's camera. Used for BOTH distance and facing, because " +
             "in a first-person game the camera IS the player's eyes - " +
             "measuring from anywhere else would disagree with what they see.")]
    [SerializeField] private Transform playerCamera;

    [Tooltip("How far off-centre the target can be and still count as 'facing " +
             "it', in degrees. 60 is forgiving - it only has to be somewhere " +
             "in front of you, not dead centre.")]
    [Range(10f, 90f)]
    [SerializeField] private float facingAngle = 60f;

    [Header("Wet Towel Check (Cover only)")]
    [Tooltip("Blocks Cover while the towel is still dry. Leave empty to skip " +
             "the check entirely - useful before the towel system exists.")]
    [SerializeField] private TowelWetnessController towel;

    [TextArea]
    [Tooltip("Shown when the player tries to smother the flame with a dry " +
             "cloth. This is the most dangerous mistake in the scenario, so " +
             "it is charged at full penalty, not given as a free hint.")]
    [SerializeField]
    private string dryTowelTip =
        "A dry cloth will catch fire. Wet it at the sink first.";

    private CanvasGroup canvasGroup;
    private bool wasVisible = false;

    private void Start()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        foreach (WCTLButton entry in wctlButtons)
        {
            WCTLButton captured = entry;
            captured.button.onClick.AddListener(() => OnButtonTapped(captured));
        }

        SetVisible(false);
    }

    private void Update()
    {
        if (SimulationManager.Instance == null) return;

        int currentStep = SimulationManager.Instance.CurrentStep;

        bool shouldShow = (currentStep >= showFromStep && currentStep <= showToStep)
                          && PlayerPrefs.GetInt("SimulationMode", 0) == 1;

        if (shouldShow && !wasVisible)
        {
            SetVisible(true);
            ShuffleButtonPositions();
        }
        else if (!shouldShow && wasVisible)
        {
            SetVisible(false);
        }

        wasVisible = shouldShow;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    // Shuffled so the player reads the labels rather than memorising
    // "the answer is always the second button".
    private void ShuffleButtonPositions()
    {
        int count = wctlButtons.Length;
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            wctlButtons[i].button.transform.SetSiblingIndex(j);
        }
    }

    // -------------------------------------------------------
    // Called when ANY WCTL button is tapped.
    // -------------------------------------------------------
    private void OnButtonTapped(WCTLButton tapped)
    {
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1) return;
        if (SimulationManager.Instance == null) return;

        // ---- STILL ANIMATING (anti-spam) ----
        // The previous step's choreography has not finished. Ignore the tap
        // entirely: no penalty, no step advanced, button stays live.
        // The player is EARLY, not WRONG.
        //
        // Deliberately silent - the lock lasts under a second, and a feedback
        // row for every impatient tap would be noisier than the problem.
        if (SimulationManager.Instance.IsStepBusy)
        {
            Debug.Log($"[WCTL] {tapped.step} ignored - previous step still animating.");
            return;
        }

        int currentStep = SimulationManager.Instance.CurrentStep;

        // ---- WRONG STEP ----
        if (currentStep != (int)tapped.step)
        {
            Debug.Log($"[WCTL] Wrong! Tapped {tapped.step} but step is {currentStep}. " +
                      $"Penalty -{timePenalty}s");

            KitchenStep expectedStep = (KitchenStep)currentStep;

            SimulationManager.Instance.RegisterWrongAction(
                expectedStep.ToString(),
                tapped.step.ToString(),
                timePenalty,
                tapped.wrongTip
            );
            return;
        }

        // ---- RIGHT STEP, DRY TOWEL ----
        // Checked BEFORE the position gate, because it is the more serious
        // mistake and the more useful thing to be told. Standing in the right
        // place with a dry cloth is still about to start a second fire.
        if (tapped.step == KitchenStep.WCTL_Cover && towel != null && !towel.IsWet)
        {
            Debug.Log("[WCTL] Cover blocked - towel is still dry. Full penalty.");

            SimulationManager.Instance.RegisterWrongAction(
                KitchenStep.WCTL_Cover.ToString(),
                "DryTowel",
                timePenalty,
                dryTowelTip
            );
            return;
        }

        // ---- RIGHT STEP, WRONG PLACE ----
        // Checked BEFORE the step is registered, so a blocked tap changes
        // nothing at all - no time lost, no step advanced, button still
        // enabled to retry.
        string positionHint = CheckPosition(tapped);
        if (positionHint != null)
        {
            Debug.Log($"[WCTL] {tapped.step} blocked - {positionHint}");

            if (ActionFeedbackManager.Instance != null)
                ActionFeedbackManager.Instance.ShowHint(positionHint);

            return;
        }

        // ---- CORRECT ----
        Debug.Log($"[WCTL] Correct! {tapped.step} for step {currentStep}");

        SimulationManager.Instance.RegisterCorrectAction(
            tapped.step.ToString(),
            tapped.step.ToString());

        tapped.button.interactable = false;   // grey out this one
    }

    // -------------------------------------------------------
    // POSITION CHECK
    //
    // Returns NULL when the player is standing correctly, or the hint to show
    // when they are not. Null-as-success reads oddly at first, but it means
    // the caller is a single if - and there is no way to forget to check a
    // bool and then not know which hint to show.
    //
    // Any missing reference disables the check rather than blocking the
    // player. Failing open matters more than failing safe here: the cost of a
    // false block is a simulation nobody can finish.
    // -------------------------------------------------------
    private string CheckPosition(WCTLButton entry)
    {
        if (!requirePosition) return null;
        if (entry.positionTarget == null) return null;
        if (playerCamera == null) return null;

        // Distance is measured flat, ignoring height. The tank sits on the
        // floor and the camera at eye level, so a 3D distance would read as
        // "too far" even standing right over it.
        Vector3 toTarget = entry.positionTarget.position - playerCamera.position;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;

        if (distance < entry.tooCloseDistance) return entry.tooCloseHint;
        if (distance > entry.tooFarDistance) return entry.tooFarHint;

        if (!entry.requireFacing) return null;

        // FACING: the angle between where the camera is looking and where the
        // target actually is. Flattened the same way, so looking slightly up
        // or down does not count as looking away.
        Vector3 lookDirection = playerCamera.forward;
        lookDirection.y = 0f;

        float angle = Vector3.Angle(lookDirection, toTarget);
        if (angle > facingAngle) return entry.notFacingHint;

        return null;   // standing correctly
    }
}