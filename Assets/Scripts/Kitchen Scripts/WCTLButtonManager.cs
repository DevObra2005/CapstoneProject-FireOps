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
//   different objects: Wet at the timba, Cover at the flame, Turn Off at the
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
//
// -------------------------------------------------------
// STEP NUMBERS - READ THIS BEFORE CHANGING showFromStep
//
// KitchenStep runs 1..5:
//   1 GrabTowel, 2 WCTL_Wet, 3 WCTL_Cover, 4 WCTL_TurnOff, 5 Evacuate
//
// So the three buttons live from 2 to 4. Grab is a world tap on the draped
// towel; Evacuate is the exit door. Neither is a button.
//
// These defaults are derived from the enum, not typed independently. If the
// enum ever changes, change them here in the same commit - a mismatch does
// not error, it just hides the button the player needs.
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
                 "register. Timba for Wet, the flame for Cover, the tank valve " +
                 "for Turn Off.\n\n" +
                 "EMPTY = no gate. The step works from anywhere.")]
        public Transform positionTarget;

        [Tooltip("Closer than this and the player is warned to step back. " +
                 "For Cover this matters - you do not stand on top of a gas " +
                 "flame. For Wet at the timba, a small value is fine.")]
        public float tooCloseDistance = 0.4f;

        [Tooltip("Further than this and the action makes no sense - you cannot " +
                 "reach the bucket, the flame or the valve from across the room.")]
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
    [Tooltip("KitchenStep: 1 GrabTowel, 2 Wet, 3 Cover, 4 TurnOff, 5 Evacuate.\n\n" +
             "So the buttons live from 2 to 4.")]
    [SerializeField] private int showFromStep = (int)KitchenStep.WCTL_Wet;
    [SerializeField] private int showToStep = (int)KitchenStep.WCTL_TurnOff;

    [Tooltip("Re-shuffle every time a step completes, instead of once when the " +
             "buttons first appear.\n\n" +
             "OFF is the safer default: positions stay put for the whole run, " +
             "so the player is not re-hunting labels mid-emergency. Turn ON " +
             "only if testing shows people are memorising positions within a " +
             "single attempt.")]
    [SerializeField] private bool reshuffleEachStep = false;

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
        "A dry cloth will catch fire. Wet it in the timba first.";

    private CanvasGroup canvasGroup;
    private bool wasVisible = false;
    private int lastShuffledStep = -1;

    // Cached once. SimulationMode is written before the scene loads and never
    // changes during a run, so re-reading PlayerPrefs every frame in Update
    // buys nothing.
    private bool isSimulationMode;

    private void Start()
    {
        isSimulationMode = PlayerPrefs.GetInt("SimulationMode", 0) == 1;

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        WireButtons();
        SetVisible(false);
    }

    // -------------------------------------------------------
    // Null-guarded on purpose. An unassigned Button slot in the Inspector used
    // to throw at Start and take the WHOLE manager down with it - all three
    // buttons dead, no visible cause. A missing slot should cost you one
    // button and a warning, not the step system.
    // -------------------------------------------------------
    private void WireButtons()
    {
        if (wctlButtons == null) return;

        foreach (WCTLButton entry in wctlButtons)
        {
            if (entry == null || entry.button == null)
            {
                Debug.LogWarning("[WCTL] A button slot is empty in the Inspector - skipped.");
                continue;
            }

            WCTLButton captured = entry;
            captured.button.onClick.AddListener(() => OnButtonTapped(captured));
        }
    }

    private void Update()
    {
        if (!isSimulationMode) return;
        if (SimulationManager.Instance == null) return;

        int currentStep = SimulationManager.Instance.CurrentStep;
        bool shouldShow = currentStep >= showFromStep && currentStep <= showToStep;

        if (shouldShow && !wasVisible)
        {
            SetVisible(true);
            ShuffleButtonPositions();
            lastShuffledStep = currentStep;
        }
        else if (!shouldShow && wasVisible)
        {
            SetVisible(false);
        }
        else if (shouldShow && reshuffleEachStep && currentStep != lastShuffledStep)
        {
            ShuffleButtonPositions();
            lastShuffledStep = currentStep;
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

    // -------------------------------------------------------
    // SHUFFLE
    //
    // Shuffled so the player reads the labels rather than memorising "the
    // answer is always the second button".
    //
    // THE OLD VERSION WAS NOT A SHUFFLE. It looped the array calling
    // SetSiblingIndex(random) directly on each button. SetSiblingIndex MOVES a
    // child and shifts everything after it - it does not SWAP. So each call
    // disturbed the placements made by every previous call, index 0 was never
    // moved at all, and some orderings came up far more often than others.
    //
    // The fix is to shuffle a list of INDICES with a real Fisher-Yates, then
    // apply the finished permutation in ascending order. Assigning sibling
    // index 0, then 1, then 2 lands each button exactly where intended,
    // because every earlier assignment is already settled.
    // -------------------------------------------------------
    private void ShuffleButtonPositions()
    {
        if (wctlButtons == null || wctlButtons.Length < 2) return;

        int count = wctlButtons.Length;

        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = order[i];
            order[i] = order[j];
            order[j] = temp;
        }

        for (int i = 0; i < count; i++)
        {
            WCTLButton entry = wctlButtons[order[i]];
            if (entry != null && entry.button != null)
                entry.button.transform.SetSiblingIndex(i);
        }
    }

    // -------------------------------------------------------
    // Called when ANY WCTL button is tapped.
    // -------------------------------------------------------
    private void OnButtonTapped(WCTLButton tapped)
    {
        if (!isSimulationMode) return;
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

            SimulationManager.Instance.RegisterWrongAction(
                DescribeStep(currentStep),
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

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // Call from wherever the Kitchen run resets, alongside
    // TowelWetnessController.ResetToDry(), TowelDipController.ResetToRest()
    // and KitchenInteractable.ResetForReplay().
    //
    // THIS WAS MISSING ENTIRELY. Every correct tap sets
    // button.interactable = false, and nothing ever set it back. A second
    // attempt in the same session started with all three WCTL buttons greyed
    // out - the player could not tap Wet, could not advance past step 2, and
    // there was no error to explain why. The run simply ended on the timer.
    //
    // Every script that mutates run state needs one of these. If you add a
    // fourth, add its reset in the same commit.
    // -------------------------------------------------------
    public void ResetForReplay()
    {
        if (wctlButtons != null)
        {
            foreach (WCTLButton entry in wctlButtons)
            {
                if (entry != null && entry.button != null)
                    entry.button.interactable = true;
            }
        }

        wasVisible = false;
        lastShuffledStep = -1;
        SetVisible(false);

        Debug.Log("[WCTL] Buttons reset for replay.");
    }
}