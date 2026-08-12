using UnityEngine;
using UnityEngine.UI;

// -------------------------------------------------------
// WHAT THIS DOES:
// Drives the 5 on-screen TPASS decision buttons (Twist, Pull,
// Aim, Squeeze, Sweep) in the correct order, shuffled positions.
//
// THIS SCRIPT IS NOW A GATEKEEPER, NOT A CHOREOGRAPHER.
// It answers two questions and nothing else:
//   1. Is this the RIGHT step to tap?
//   2. Is the player STANDING somewhere the action makes sense?
// If both pass, it hands off to SimulationManager and steps back.
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
// FIRE POSITION VALIDATION:
// Squeeze is blocked unless the player is standing at a sensible
// distance from the fire AND facing it. This is a POSITIONING
// mistake, not a knowledge mistake - the player knows Squeeze is
// next, they are just standing wrong - so it costs no time. They
// get a hint and try again.
//
// Real fire training teaches standing BACK from the fire, roughly
// 6-8 feet, not walking up to it. So being too close is its own
// warning, not just being too far.
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
             "from the fire, and facing it, before Squeeze will register.\n\n" +
             "Untick to disable the whole check - useful while testing " +
             "other steps.")]
    [SerializeField] private bool requireFirePosition = true;

    [Tooltip("The fire object to measure against. Usually " +
             "VFX_Fire_01_Big_Smoke under Phase2Objects.\n\n" +
             "Leave empty and the check is skipped entirely, so a scene " +
             "with no fire assigned still plays through.")]
    [SerializeField] private Transform fireTarget;

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

        // Block taps while a hand animation is mid-play (anti-spam).
        if (HandAnimationController.Instance != null &&
            HandAnimationController.Instance.IsAnimating) return;

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
    // Returns NULL when the player is standing correctly, or the hint
    // to show when they are not. Null-as-success reads oddly at first,
    // but it means the caller is a single if - and there is no way to
    // forget to check a bool and then not know which hint to show.
    // -------------------------------------------------------
    private string CheckFirePosition()
    {
        // Any missing reference disables the check rather than blocking
        // the player. A scene with no fire assigned should still be
        // playable - failing open matters more than failing safe here,
        // because the cost of a false block is a stuck simulation.
        if (!requireFirePosition) return null;
        if (fireTarget == null || playerCamera == null) return null;

        // Distance is measured flat, ignoring height. The fire sits on
        // the floor and the camera at eye level, so a 3D distance would
        // read as "too far" even standing right in front of it.
        Vector3 toFire = fireTarget.position - playerCamera.position;
        toFire.y = 0f;

        float distance = toFire.magnitude;

        if (distance < tooCloseDistance) return tooCloseHint;
        if (distance > tooFarDistance) return tooFarHint;

        // FACING: the angle between where the camera is looking and where
        // the fire actually is. Flattened the same way so looking slightly
        // up or down does not count as looking away.
        Vector3 lookDirection = playerCamera.forward;
        lookDirection.y = 0f;

        float angle = Vector3.Angle(lookDirection, toFire);
        if (angle > facingAngle) return notFacingHint;

        return null;   // standing correctly
    }

    // -------------------------------------------------------
    // RIGHT-HAND animations per step.
    // TPASS_Twist has NO case on purpose — the left IK hand acts
    // alone while the right hand holds the tank steady.
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