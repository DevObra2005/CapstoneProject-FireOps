using System.Collections.Generic;
using UnityEngine;

// -------------------------------------------------------
// KitchenInteractable — Kitchen's tap target, the counterpart to
// SimulationInteractable in Office.
//
// WHY A SEPARATE SCRIPT INSTEAD OF REUSING SimulationInteractable
//
// Two blockers, both of which fail SILENTLY rather than erroring:
//
// 1. THE ENUM. SimulationInteractable types requiredStep as SimStep, which
//    contains GrabExtinguisher and the five TPASS sub-actions. Kitchen runs
//    on KitchenStep. Assigning an extinguisher step to a towel and relying on
//    the integers happening to line up is exactly the coincidence that breaks
//    the day someone reorders an enum.
//
// 2. THE OUTLINE. SimulationInteractable builds its hover highlight from
//    GetComponentsInChildren<MeshFilter>(). A rigged towel has no MeshFilter —
//    its mesh lives on a SkinnedMeshRenderer. That loop finds nothing, creates
//    zero outline copies, and the player gets no hint that the towel can be
//    tapped. No error, just a dead-looking prop.
//
// So Kitchen gets its own. Office keeps working untouched.
//
// -------------------------------------------------------
// HOW THE OUTLINE WORKS HERE, AND WHY IT DIFFERS
//
// Office spawns a duplicate GameObject per mesh piece, wearing an inverted-
// hull shader. That cannot work on a skinned mesh — the copy would need the
// same bones, the same bind poses and the same per-frame skinning, which means
// rebuilding half of Unity's skinning pipeline.
//
// Instead this appends the outline material as an EXTRA SUBMESH SLOT on the
// existing renderer. The GPU skins the mesh once and draws it twice: normal
// pass, then the inverted hull. The outline deforms with the towel for free,
// because it IS the towel.
//
// Toggling is done by writing _OutlineWidth to 0 rather than by adding and
// removing array entries — reassigning renderer.materials allocates every
// call, and hover fires constantly as the player looks around.
//
// The material is per-instance, not static like Office's. One extra material
// for one object is nothing, and it buys per-object width control.
//
// -------------------------------------------------------
// THE GRAB HANDOFF — ONE OBJECT, EXACTLY LIKE THE EXTINGUISHER
//
// There is ONE towel. It starts draped over the sink, and on a correct tap
// TowelGrab flies it into TowelAnchor and parents it to the camera. Same
// pattern as ExtinguisherGrab, same feel to the player.
//
// So disableAfterCorrectUse must be FALSE on the towel. The object is not
// consumed by the tap — it BECOMES the held towel. Ticking it would hide the
// thing the player just picked up.
//
// -------------------------------------------------------
// THE SIM-ACTIVE GUARD — WHY IT IS THERE
//
// SimulationManager.RegisterCorrectAction opens with `if (!simActive) return;`
// and says nothing when it bails. So a tap landing before BeginSimulation()
// fires is SILENTLY DISCARDED: no feedback row, no step advanced, no entry in
// the results array.
//
// That would be survivable if nothing else happened. It is not — look at the
// order in Interact() below. towelGrab.Grab() runs FIRST. So the towel flew
// into the player's hands, looked exactly like a successful grab, and the step
// was never recorded. The admin panel then showed Wet as step 1 with GrabTowel
// missing entirely, and nothing anywhere pointed at the cause.
//
// Office has the same hole, but the extinguisher is on a wall across the room:
// you have to walk there, and by then the timer is running. The Kitchen towel
// sits on the sink, sometimes within tapping distance of where the player
// spawns — so Kitchen hit it and Office never did.
//
// The guard turns an invisible half-action into a clean no-op. It goes at the
// TOP, before anything with a side effect.
// -------------------------------------------------------

public class KitchenInteractable : MonoBehaviour, IInteractable
{
    [Header("Step Configuration")]
    [Tooltip("Which KitchenStep this object belongs to. Tapping it on any " +
             "other step is either a penalty (too early) or ignored (already " +
             "done).")]
    [SerializeField] private KitchenStep requiredStep = KitchenStep.GrabTowel;

    [Tooltip("Untick for decoy objects — things that LOOK like the right " +
             "answer but are not. Tapping one costs the full penalty.")]
    [SerializeField] private bool isCorrectAction = true;

    [Header("Reporting")]
    [Tooltip("Readable label sent to Laravel, e.g. 'TowelOnSink'. Leave blank " +
             "to fall back to this GameObject's name.")]
    [SerializeField] private string actionName = "";

    [Header("Wrong Action Settings")]
    [SerializeField] private float timePenalty = 20f;

    [TextArea]
    [SerializeField] private string educationalTip = "Enter tip here...";

    [Header("One-Time Use")]
    [Tooltip("Hide this object after a correct tap.\n\n" +
             "MUST BE FALSE ON THE TOWEL. The towel is not consumed by the " +
             "tap — it flies into the player's hands and stays there. Ticking " +
             "this would hide the thing they just picked up.\n\n" +
             "Same rule as the grabbable extinguisher in Office.")]
    [SerializeField] private bool disableAfterCorrectUse = false;

    [Header("Towel Grab (GrabTowel step only)")]
    [Tooltip("The TowelGrab component on this same object. On a correct tap " +
             "it flies the towel from the sink into TowelAnchor and fades the " +
             "arm IK in, exactly as ExtinguisherGrab does for the wall " +
             "extinguisher.\n\n" +
             "Left empty, this is found automatically on the same GameObject.")]
    [SerializeField] private TowelGrab towelGrab;

    [Header("Hover Outline")]
    [Tooltip("Untick to skip the outline entirely — useful for objects that " +
             "are already visually obvious, or while debugging.")]
    [SerializeField] private bool showOutline = true;

    [SerializeField] private Color outlineColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private float outlineWidth = 0.02f;

    private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthID = Shader.PropertyToID("_OutlineWidth");

    private Material outlineInstance;

    private void Start()
    {
        // Convenience: the towel's grab component lives on this same object,
        // so find it rather than making it another Inspector slot to forget.
        if (towelGrab == null) towelGrab = GetComponent<TowelGrab>();

        if (showOutline) SetupOutline();
    }

    // -------------------------------------------------------
    // Appends the outline material as an extra slot on the first renderer
    // found. Works on MeshRenderer and SkinnedMeshRenderer alike, because it
    // never touches the mesh — it only adds a draw pass.
    // -------------------------------------------------------
    private void SetupOutline()
    {
        Shader outlineShader = Shader.Find("Custom/OutlineUnlit");

        if (outlineShader == null)
        {
            Debug.LogWarning("[KitchenInteractable] Shader 'Custom/OutlineUnlit' not found. " +
                             "Add OutlineUnlit.shader to Project Settings > Graphics > " +
                             "Always Included Shaders.");
            return;
        }

        // GetComponentInChildren, because an FBX import puts the mesh on a
        // child of the object the script sits on.
        Renderer target = GetComponentInChildren<Renderer>();
        if (target == null) return;

        outlineInstance = new Material(outlineShader);
        outlineInstance.SetColor(OutlineColorID, outlineColor);
        outlineInstance.SetFloat(OutlineWidthID, 0f);   // hidden until hovered

        List<Material> mats = new List<Material>(target.sharedMaterials);
        mats.Add(outlineInstance);
        target.materials = mats.ToArray();
    }

    public void OnHoverEnter()
    {
        // Do not advertise a tap that would do nothing.
        //
        // Before the timer starts, a tap is silently ignored (see the guard in
        // Interact), so lighting up would promise something the object cannot
        // deliver — and that is exactly the promise that caused players to tap
        // the towel early and lose the step.
        if (SimulationManager.Instance == null) return;
        if (!SimulationManager.Instance.IsSimActive) return;

        // Once the step is past, this object is inert. This matters MORE now
        // that the towel survives its own step — it stays in the player's
        // hands for the rest of the run, so without this guard it would light
        // up blue every time they glanced down at it.
        if (SimulationManager.Instance.CurrentStep > (int)requiredStep) return;

        if (outlineInstance != null)
            outlineInstance.SetFloat(OutlineWidthID, outlineWidth);
    }

    public void OnHoverExit()
    {
        if (outlineInstance != null)
            outlineInstance.SetFloat(OutlineWidthID, 0f);
    }

    private string ResolveActionName()
    {
        return string.IsNullOrEmpty(actionName) ? gameObject.name : actionName;
    }

    // -------------------------------------------------------
    // Called by whatever routes taps in the Kitchen scene, via IInteractable.
    // -------------------------------------------------------
    public void Interact()
    {
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1) return;
        if (SimulationManager.Instance == null) return;

        // THE TIMER IS NOT RUNNING YET.
        //
        // Must come BEFORE the grab below. RegisterCorrectAction would drop
        // the step in silence, but towelGrab.Grab() would still fly the towel
        // into the player's hands — so the grab LOOKED successful while
        // GrabTowel never reached the results array. See the header.
        //
        // Deliberately silent, like the anti-spam guard: the player has not
        // done anything wrong, the simulation simply has not started.
        if (!SimulationManager.Instance.IsSimActive)
        {
            Debug.Log($"[Kitchen] {gameObject.name} ignored — simulation has not started yet.");
            return;
        }

        // ANTI-SPAM. The previous step's choreography is still playing, so the
        // player is EARLY, not WRONG. Ignored silently — no penalty, no step
        // advanced. Same rule WCTLButtonManager applies to its buttons.
        if (SimulationManager.Instance.IsStepBusy)
        {
            Debug.Log($"[Kitchen] {gameObject.name} ignored — step still animating.");
            return;
        }

        int currentStep = SimulationManager.Instance.CurrentStep;

        // ---- WRONG STEP ----
        if (currentStep != (int)requiredStep)
        {
            if ((int)requiredStep > currentStep)
            {
                // A FUTURE step — e.g. covering the flame before wetting the
                // towel. A real mistake, charged accordingly.
                Debug.Log($"[Kitchen] {gameObject.name} tapped too early. " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}. " +
                          $"Penalty -{timePenalty}s");

                SimulationManager.Instance.RegisterWrongAction(
                    DescribeStep(currentStep),
                    ResolveActionName(),
                    timePenalty,
                    educationalTip
                );
            }
            else
            {
                // An already-completed step. A stray tap on the towel now in
                // hand, not a knowledge failure. No penalty.
                Debug.Log($"[Kitchen] {gameObject.name} tapped after its step. " +
                          $"Current: {currentStep}, Required: {(int)requiredStep}");
            }

            return;
        }

        // ---- CORRECT STEP ----
        if (isCorrectAction)
        {
            Debug.Log($"[Kitchen] {gameObject.name} — correct for step {currentStep}.");

            // THE HANDOFF. Started BEFORE registering the step, so the flight
            // is already underway as the step advances — same ordering as
            // SimulationInteractable calling extinguisherGrab.Grab().
            //
            // Safe to keep that order ONLY because of the IsSimActive guard
            // above. Without it, this line ran while the registration silently
            // did not, and the two came apart.
            //
            // The flight is NOT awaited. TowelGrab owns its own timing and the
            // step lockout in SimulationManager covers the window, exactly as
            // it does for the extinguisher.
            if (requiredStep == KitchenStep.GrabTowel && towelGrab != null)
            {
                towelGrab.Grab();
            }

            SimulationManager.Instance.RegisterCorrectAction(
                requiredStep.ToString(),
                ResolveActionName()
            );

            // Kill the outline immediately. The towel is now in hand and the
            // step is past, so leaving it lit would suggest another tap does
            // something.
            OnHoverExit();

            if (disableAfterCorrectUse)
                gameObject.SetActive(false);
        }
        else
        {
            // ---- DECOY ----
            Debug.Log($"[Kitchen] {gameObject.name} — WRONG object. Penalty -{timePenalty}s");

            SimulationManager.Instance.RegisterWrongAction(
                requiredStep.ToString(),
                ResolveActionName(),
                timePenalty,
                educationalTip
            );
        }
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
    // RESET FOR A REPLAY
    //
    // Call from wherever the Kitchen run resets, alongside
    // TowelGrab.ResetToTimba(), TowelWetnessController.ResetToDry(),
    // TowelDipController.ResetToRest(), TowelCoverController.ResetToRest(),
    // KitchenValveController.ResetToStart() and
    // WCTLButtonManager.ResetForReplay().
    //
    // Note the split of responsibility: THIS script only restores its own
    // state — active, un-outlined, tappable again. Putting the towel back on
    // the sink belongs to TowelGrab, which is what moved it.
    //
    // Calling both is required. Reset only this one and the towel stays in the
    // player's hands while the tap target believes it is still on the sink.
    // -------------------------------------------------------
    public void ResetForReplay()
    {
        gameObject.SetActive(true);
        OnHoverExit();
    }
}