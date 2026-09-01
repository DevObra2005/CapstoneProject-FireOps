using UnityEngine;
using UnityEngine.UI;

// -------------------------------------------------------
// WHAT THIS DOES:
// Drives the 5 on-screen TPASS decision buttons (Twist, Pull,
// Aim, Squeeze, Sweep) in the correct order, shuffled positions.
//
// THIS SCRIPT IS A GATEKEEPER, NOT A CHOREOGRAPHER.
// It answers three questions and nothing else:
//   1. Is the previous step still ANIMATING?
//   2. Is this the RIGHT step to tap?
//   3. Is the player STANDING somewhere the action makes sense?
// If all three pass, it hands off to SimulationManager and steps back.
//
// WHY THE ANIMATION CALLS WERE REMOVED:
// This script used to also call leftHandIK.ReachPinAndTwist(),
// sprayVFX.Play(), fireController.WeakenFire() and so on. So did
// SimulationManager. Both fired on the same tap.
//
// Duplicate sprayVFX.Play() was harmless. Duplicate IK calls were
// NOT: each one runs StartExclusive, which STOPS the previous
// coroutine. So the second call silently killed the first - and
// with it the arrival callback that makes the Twist clip wait for
// the hand to actually reach the pin.
//
// One owner per behaviour. SimulationManager already owns clips,
// masked layers, thumb press and timing, so it owns the rest of
// the choreography too. This file owns the buttons.
//
// THE ANTI-SPAM GUARD (rewritten):
// Tapping Pull then Aim immediately left the hand frozen and the Aim
// clip never playing. PullSequence runs for about 0.9s and ends with
// leftHandIK.ReleaseAll(); a tap on Aim inside that window had already
// started a nozzle travel, and the late release went through
// StartExclusive and STOPPED it - taking the callback that plays the
// Aim clip with it. Silent, no error.
//
// The guard that was here did nothing:
//
//     if (HandAnimationController.Instance.IsAnimating) return;
//
// IsAnimating is only set inside RightDominantRoutine and SweepRoutine.
// Both are no-ops now that the IK flags own the arms and the masked
// Sweep clip owns the sweep, so neither runs and the flag is
// permanently false. It had been protecting nothing - it just looked
// like protection.
//
// SimulationManager.IsStepBusy replaces it. That flag is raised when a
// step's choreography starts and cleared by the routine that owns the
// real duration, so there is no timer here duplicating a number that
// lives somewhere else.
//
// A blocked tap costs NOTHING: no penalty, no step advanced, button
// stays live. The player is EARLY, not WRONG. It is also silent - the
// lock lasts well under a second, and a feedback row for every
// impatient tap would be noisier than the problem.
//
// FIRE POSITION VALIDATION:
// Squeeze is blocked unless the player is standing at a sensible
// distance from a fire AND facing it. This is a POSITIONING mistake,
// not a knowledge mistake - the player knows Squeeze is next, they are
// just standing wrong - so it costs no time. They get a hint and try
// again.
//
// Real fire training teaches standing BACK from the fire, roughly
// 6-8 feet, not walking up to it. So being too close is its own
// warning, not just being too far.
//
// TWO FIRES (OFFICE ONLY):
// The Office scene now burns TWO fires at once, and the player CHOOSES
// which to attack - that choice is the whole point of the decision
// scenario. So the check has to accept EITHER fire: it passes as soon
// as the player is correctly positioned relative to one of them.
//
// A single fireTarget would have blocked the player at whichever fire
// was not assigned, with the hint "Move closer to the fire" while they
// were standing directly in front of one. The step would never
// register and the run would stall with no error.
//
// Second Fire Target is OPTIONAL. Leave it EMPTY in Classroom and the
// check behaves exactly as it always has, measuring the one fire.
// -------------------------------------------------------

public class TPASSButtonManager : MonoBehaviour
{
    [System.Serializable]
    public class TPASSButton
    {
        public Button button;
        public SimulationInteractable.SimStep step;
        [TextArea] public string wrongTip;
    }

    [Header("The 5 TPASS Buttons")]
    [SerializeField] private TPASSButton[] tpassButtons;

    [Header("Wrong Action Settings")]
    [SerializeField] private float timePenalty = 20f;

    [Header("Visibility")]
    [SerializeField] private int showFromStep = 3;
    [SerializeField] private int showToStep = 7;

    // -------------------------------------------------------
    [Header("Fire Position Check (Squeeze only)")]
    [Tooltip("Require the player to be standing at a sensible distance " +
             "from a fire, and facing it, before Squeeze will register.\n\n" +
             "Untick to disable the whole check - useful while testing " +
             "other steps.")]
    [SerializeField] private bool requireFirePosition = true;

    [Tooltip("The fire object to measure against.\n\n" +
             "Leave empty and the check is skipped entirely, so a scene " +
             "with no fire assigned still plays through.")]
    [SerializeField] private Transform fireTarget;

    [Tooltip("OFFICE ONLY — the SECOND fire. Leave EMPTY in Classroom.\n\n" +
             "The Office decision scenario burns two fires and lets the " +
             "player choose which to attack. With only one assigned, " +
             "standing in front of the OTHER fire would be rejected as " +
             "'move closer to the fire' — the step would never register " +
             "and the run would stall with no error.\n\n" +
             "When both are assigned the check passes as soon as the player " +
             "is correctly positioned relative to EITHER one. Which fire " +
             "they actually chose is TwoFireDecision's job, not this " +
             "script's — this only asks whether they are standing somewhere " +
             "spraying makes sense.")]
    [SerializeField] private Transform secondFireTarget;

    [Tooltip("The player's camera. Used for BOTH distance and facing, " +
             "because in a first-person game the camera IS the player's " +
             "eyes - measuring from anywhere else would disagree with " +
             "what they can see.")]
    [SerializeField] private Transform playerCamera;

    [Tooltip("Closer than this and the player is warned to step back. " +
             "Real training teaches keeping your distance - walking up to " +
             "a fire to spray it is the mistake, not the goal.")]
    [SerializeField] private float tooCloseDistance = 1.5f;

    [Tooltip("Further than this and the spray would not reach. Most " +
             "portable extinguishers have a range of about 3-4 metres.")]
    [SerializeField] private float tooFarDistance = 4f;

    [Tooltip("How far off-centre the fire can be and still count as " +
             "'facing it', in degrees. 60 is forgiving - the fire only has " +
             "to be somewhere in front of you, not dead centre.")]
    [Range(10f, 90f)]
    [SerializeField] private float facingAngle = 60f;

    [Tooltip("Shown when the player is too far from the fire.")]
    [TextArea]
    [SerializeField] private string tooFarHint = "Move closer to the fire";

    [Tooltip("Shown when the player is standing too close.")]
    [TextArea]
    [SerializeField] private string tooCloseHint = "You're too close — step back";

    [Tooltip("Shown when the fire is not in front of the player.")]
    [TextArea]
    [SerializeField] private string notFacingHint = "Face the fire before you spray";
    // -------------------------------------------------------

    private CanvasGroup canvasGroup;
    private bool wasVisible = false;

    private void Start()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        foreach (TPASSButton entry in tpassButtons)
        {
            TPASSButton captured = entry;
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

    private void ShuffleButtonPositions()
    {
        int count = tpassButtons.Length;
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            tpassButtons[i].button.transform.SetSiblingIndex(j);
        }
    }

    // -------------------------------------------------------
    // Called when ANY TPASS button is tapped.
    // -------------------------------------------------------
    private void OnButtonTapped(TPASSButton tapped)
    {
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1) return;
        if (SimulationManager.Instance == null) return;

        // ---- STILL ANIMATING (anti-spam) ----
        // The previous step's choreography has not finished. Ignore the tap
        // entirely: no penalty, no step advanced, button stays live.
        //
        // This is what stops the Pull -> Aim bug. It also covers the
        // wrong-tap cooldown, so five panicked taps cannot drain the whole
        // clock in half a second.
        //
        // Deliberately silent - the lock lasts under a second.
        if (SimulationManager.Instance.IsStepBusy)
        {
            Debug.Log($"[TPASS] {tapped.step} ignored - previous step still animating.");
            return;
        }

        int currentStep = SimulationManager.Instance.CurrentStep;

        // ---- WRONG STEP ----
        if (currentStep != (int)tapped.step)
        {
            Debug.Log($"[TPASS] Wrong! Tapped {tapped.step} but step is {currentStep}. Penalty -{timePenalty}s");

            SimulationInteractable.SimStep expectedStep = (SimulationInteractable.SimStep)currentStep;

            SimulationManager.Instance.RegisterWrongAction(
                expectedStep,
                tapped.step.ToString(),
                timePenalty,
                tapped.wrongTip
            );
            return;
        }

        // ---- RIGHT STEP, WRONG PLACE ----
        // Only Squeeze cares where the player is standing. Checked BEFORE
        // the step is registered, so a blocked tap changes nothing at all -
        // no time lost, no step advanced, button still enabled to retry.
        if (tapped.step == SimulationInteractable.SimStep.TPASS_Squeeze)
        {
            string positionHint = CheckFirePosition();
            if (positionHint != null)
            {
                Debug.Log($"[TPASS] Squeeze blocked - {positionHint}");

                if (ActionFeedbackManager.Instance != null)
                    ActionFeedbackManager.Instance.ShowHint(positionHint);

                return;
            }
        }

        // ---- CORRECT ----
        Debug.Log($"[TPASS] Correct! {tapped.step} for step {currentStep}");

        // Right-hand FK animations. The left hand, extinguisher clips,
        // spray and fire are all SimulationManager's job now - see the
        // note at the top of this file.
        PlayHandAnimationForStep(tapped.step);

        // Hand the left arm over to IK on the first TPASS step. From here
        // SimulationManager drives it, with arrival callbacks so each clip
        // waits for the hand to actually reach its grip.
        if (tapped.step == SimulationInteractable.SimStep.TPASS_Twist &&
            HandAnimationController.Instance != null)
        {
            HandAnimationController.Instance.leftHandControlledByIK = true;
        }

        SimulationManager.Instance.RegisterCorrectAction(tapped.step, tapped.step.ToString());

        tapped.button.interactable = false;   // grey out this one
    }

    // -------------------------------------------------------
    // FIRE POSITION CHECK
    //
    // Returns NULL when the player is standing correctly at SOME fire, or
    // the hint to show when they are not standing correctly at any of
    // them. Null-as-success reads oddly at first, but it means the caller
    // is a single if - and there is no way to forget to check a bool and
    // then not know which hint to show.
    //
    // WITH TWO FIRES, ONE PASS IS ENOUGH. The player is choosing which
    // fire to fight; being correctly positioned at either is correct
    // positioning. Whether they picked the RIGHT fire is graded by
    // TwoFireDecision on the same tap - a separate judgement, with its own
    // penalty, made after this gate has already let them through.
    //
    // WHEN BOTH FAIL, the hint comes from the NEARER fire. That is the one
    // the player is most likely trying to reach, so "step back" or "face
    // the fire" describes the situation they are actually in rather than
    // a fire across the room.
    //
    // A DEACTIVATED FIRE IS SKIPPED. FireController hides a fire once it
    // has died, so an extinguished one stops being a valid target
    // automatically - no bookkeeping needed here.
    // -------------------------------------------------------
    private string CheckFirePosition()
    {
        // Any missing reference disables the check rather than blocking
        // the player. A scene with no fire assigned should still be
        // playable - failing open matters more than failing safe here,
        // because the cost of a false block is a stuck simulation.
        if (!requireFirePosition) return null;
        if (playerCamera == null) return null;

        string nearestHint = null;
        float nearestDistance = float.MaxValue;
        bool anyFireChecked = false;

        // Both slots, in order. secondFireTarget is null in Classroom, so
        // that scene evaluates exactly one fire and behaves as it always
        // has.
        Transform[] fires = { fireTarget, secondFireTarget };

        foreach (Transform fire in fires)
        {
            if (fire == null) continue;

            // Already extinguished and hidden — not a target any more.
            if (!fire.gameObject.activeInHierarchy) continue;

            anyFireChecked = true;

            string hint = EvaluateFire(fire, out float distance);

            // Correctly positioned at THIS fire. Nothing else to check.
            if (hint == null) return null;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestHint = hint;
            }
        }

        // No fire assigned, or every fire already out. Fail open.
        if (!anyFireChecked) return null;

        return nearestHint;
    }

    // -------------------------------------------------------
    // Is the player standing correctly relative to ONE fire?
    //
    // Returns null when yes, or the hint describing what is wrong.
    // distance is passed back so the caller can pick the nearest fire's
    // hint when every fire fails.
    // -------------------------------------------------------
    private string EvaluateFire(Transform fire, out float distance)
    {
        // Distance is measured flat, ignoring height. The fire sits on
        // the floor and the camera at eye level, so a 3D distance would
        // read as "too far" even standing right in front of it.
        Vector3 toFire = fire.position - playerCamera.position;
        toFire.y = 0f;

        distance = toFire.magnitude;

        if (distance < tooCloseDistance) return tooCloseHint;
        if (distance > tooFarDistance) return tooFarHint;

        // FACING: the angle between where the camera is looking and where
        // the fire actually is. Flattened the same way so looking slightly
        // up or down does not count as looking away.
        Vector3 lookDirection = playerCamera.forward;
        lookDirection.y = 0f;

        float angle = Vector3.Angle(lookDirection, toFire);
        if (angle > facingAngle) return notFacingHint;

        return null;   // standing correctly at this fire
    }

    // -------------------------------------------------------
    // RIGHT-HAND animations per step.
    // TPASS_Twist has NO case on purpose — the left IK hand acts
    // alone while the right hand holds the tank steady.
    //
    // NOTE: every one of these is currently a NO-OP. PlayPull, PlayAim
    // and PlaySqueeze do nothing while rightHandControlledByIK and
    // leftHandControlledByIK are ticked, and PlaySweep does nothing
    // while sweepControlledByAnimation is ticked - they only fire
    // OnAnimationComplete and return.
    //
    // Kept anyway, for two reasons. It is the fallback path if a future
    // environment ever needs FK arm motion, and something outside these
    // files may still be subscribed to OnAnimationComplete. Removing it
    // is a safe cleanup AFTER defense, once you can grep the project for
    // that subscription - not before.
    // -------------------------------------------------------
    private void PlayHandAnimationForStep(SimulationInteractable.SimStep step)
    {
        if (HandAnimationController.Instance == null) return;

        switch (step)
        {
            case SimulationInteractable.SimStep.TPASS_Pull:
                HandAnimationController.Instance.PlayPull();
                break;
            case SimulationInteractable.SimStep.TPASS_Aim:
                HandAnimationController.Instance.PlayAim();
                break;
            case SimulationInteractable.SimStep.TPASS_Squeeze:
                HandAnimationController.Instance.PlaySqueeze();
                break;
            case SimulationInteractable.SimStep.TPASS_Sweep:
                HandAnimationController.Instance.PlaySweep();
                break;
        }
    }
}