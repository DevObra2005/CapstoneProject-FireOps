using UnityEngine;
using UnityEngine.UI;

// -------------------------------------------------------
// WHAT THIS DOES:
// Drives the 5 on-screen TPASS decision buttons (Twist, Pull,
// Aim, Squeeze, Sweep) in the correct order, shuffled positions.
//
// PHYSICAL ACTORS:
//   TWIST -> left IK hand reaches pin + twists; pin glues to hand
//            (right hand stays still on purpose)
//   PULL  -> left IK hand pulls; pin rides out, then hides
//   AIM   -> left IK hand reaches DOWN to the hanging handle,
//            NozzleController glues the hose tip to the hand,
//            hand LIFTS the nozzle to the holding pose
//   SQUEEZE -> fire weakens, spray starts
//   SWEEP   -> fire extinguished, spray stops (hand + hose ride
//              the sweep anchor automatically)
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

    [Header("Fire Reaction")]
    [SerializeField] private FireController fireController;

    [Header("Extinguisher Pin (for Twist and Pull)")]
    [SerializeField] private PinController pinController;

    [Header("Extinguisher Spray (for Squeeze and Sweep)")]
    [SerializeField] private ExtinguisherSprayVFX sprayVFX;

    [Header("Nozzle / Hose (for Aim)")]
    // ── NEW ── Drag the object holding NozzleController here
    // (the FireExtinguisherWrapper in our setup).
    [SerializeField] private NozzleController nozzleController;

    [Header("Left Hand IK (Animation Rigging)")]
    [SerializeField] private LeftHandIKController leftHandIK;

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

        if (currentStep == (int)tapped.step)
        {
            // ---- CORRECT ----
            Debug.Log($"[TPASS] Correct! {tapped.step} for step {currentStep}");

            PlayHandAnimationForStep(tapped.step);

            // ── LEFT HAND IK reactions ─────────────────────────────
            if (leftHandIK != null)
            {
                if (tapped.step == SimulationInteractable.SimStep.TPASS_Twist)
                {
                    // THE HAND-OFF: from this moment, IK owns the left arm.
                    if (HandAnimationController.Instance != null)
                        HandAnimationController.Instance.leftHandControlledByIK = true;

                    leftHandIK.ReachPinAndTwist();
                }
                else if (tapped.step == SimulationInteractable.SimStep.TPASS_Pull)
                {
                    leftHandIK.PullPin();
                }
                else if (tapped.step == SimulationInteractable.SimStep.TPASS_Aim)
                {
                    leftHandIK.GrabNozzle();
                }
                // Squeeze/Sweep: nothing needed — the hand rides the
                // hold pose (and the hose rides the hand).
            }

            // ── PIN reactions (Twist attaches, Pull removes) ──────
            if (pinController != null)
            {
                if (tapped.step == SimulationInteractable.SimStep.TPASS_Twist)
                    pinController.Twist();
                else if (tapped.step == SimulationInteractable.SimStep.TPASS_Pull)
                    pinController.Pull();
            }

            // ── NEW: HOSE reaction (Aim glues hose tip to the hand) ─
            if (nozzleController != null)
            {
                if (tapped.step == SimulationInteractable.SimStep.TPASS_Aim)
                    nozzleController.Grab();
            }

            // ── FIRE reactions ─────────────────────────────────────
            if (fireController != null)
            {
                if (tapped.step == SimulationInteractable.SimStep.TPASS_Squeeze)
                    fireController.WeakenFire();
                else if (tapped.step == SimulationInteractable.SimStep.TPASS_Sweep)
                    fireController.ExtinguishFire();
            }

            // ── SPRAY reactions ────────────────────────────────────
            if (sprayVFX != null)
            {
                if (tapped.step == SimulationInteractable.SimStep.TPASS_Squeeze)
                    sprayVFX.Play();
                else if (tapped.step == SimulationInteractable.SimStep.TPASS_Sweep)
                    sprayVFX.Stop();
            }

            SimulationManager.Instance.RegisterCorrectAction(tapped.step, tapped.step.ToString());

            tapped.button.interactable = false;   // grey out this one
        }
        else
        {
            // ---- WRONG ----
            Debug.Log($"[TPASS] Wrong! Tapped {tapped.step} but step is {currentStep}. Penalty -{timePenalty}s");

            SimulationInteractable.SimStep expectedStep = (SimulationInteractable.SimStep)currentStep;

            SimulationManager.Instance.RegisterWrongAction(
                expectedStep,
                tapped.step.ToString(),
                timePenalty,
                tapped.wrongTip
            );
        }
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