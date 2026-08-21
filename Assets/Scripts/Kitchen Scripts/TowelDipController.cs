using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// TowelDipController — the Wet step's choreography.
//
// Moves the towel through a dip, soak, wring and lift using transform lerps
// in LOCAL space under TowelAnchor.
//
// The four realism rules — easing rather than linear, an arc rather than a
// straight line, rotation that leads the move, a settle rather than a stop —
// are unchanged and documented at each site below. The wring adds a fifth: an
// oscillation whose amplitude DECAYS, which reads as effort running out
// rather than as a mechanism.
//
// -------------------------------------------------------
// THIS FILE MOVES THE TOWEL. THE BONES CHANGE ITS SHAPE.
//
// The towel is rigged now — five bones, three clips. The division of labour:
//
//   THIS FILE owns TRAVEL:  where the towel sits relative to the hand, its
//                           rotation, the arc, the wring oscillation.
//   THE ANIMATOR owns SHAPE: the cloth's own bend, currently Towel_Idle's
//                           slow pendulum sway throughout the dip.
//
// They do not conflict, because they write different things: this writes the
// OBJECT's localPosition and localRotation, the Animator writes the BONES'
// local rotations underneath it. The sway continues through the whole dip,
// which is correct — a towel does not go rigid because your arm is moving.
//
// -------------------------------------------------------
// THE REST POSE IS CAPTURED LAZILY. THIS IS NOT AN OPTIMISATION.
//
// It used to be cached in Start(). That worked when a hidden towel lived
// under TowelAnchor from scene load, because Start() ran with the towel
// already at its held pose.
//
// It broke the moment the towel became a SINGLE object that starts draped on
// the timba and is flown into the hand by TowelGrab. Start() now runs while
// the towel is still on the bucket, so restLocalPosition would hold the
// TIMBA's coordinates — and every dip would return the towel to a point
// relative to the anchor that corresponds to where the bucket was. Parented
// to the camera. Drifting with the player's head.
//
// No error. No warning. Just a towel that ends every dip somewhere absurd.
//
// Capturing on the first PlayDip call fixes it by construction: by then the
// grab has completed, the towel is parented to TowelAnchor at (0,0,0), and
// whatever it is at IS the held pose.
// -------------------------------------------------------

public class TowelDipController : MonoBehaviour
{
    [Header("What Moves")]
    [Tooltip("The towel transform. By the time the Wet step runs it will be " +
             "parented under TowelAnchor on the PlayerCamera, flown there by " +
             "TowelGrab.\n\n" +
             "Leave empty to move THIS object.")]
    [SerializeField] private Transform towel;

    [Header("Dip Pose (local to the anchor)")]
    [Tooltip("Where the towel sits when it is down in the timba. Use " +
             "previewDipPose below to find these numbers rather than guessing.")]
    [SerializeField] private Vector3 dipLocalPosition = new Vector3(0f, -0.35f, 0.45f);

    [Tooltip("Rotation in the water. Tilting the towel forward reads as the " +
             "wrists turning to push it under.")]
    [SerializeField] private Vector3 dipLocalEuler = new Vector3(35f, 0f, 0f);

    [Tooltip("PLAY MODE ONLY, AND ONLY AFTER THE GRAB. Snaps the towel to the " +
             "dip pose so you can drag it into place in the Scene view, then " +
             "copy the numbers up. Untick before testing the step.\n\n" +
             "Does nothing before the grab, because the towel is not yet " +
             "parented to the anchor and local coordinates would be meaningless.")]
    [SerializeField] private bool previewDipPose = false;

    [Header("Motion Shape")]
    [Tooltip("How far the towel bows OUT from a straight line on its way down " +
             "and back. This is the difference between an arm movement and a " +
             "lift on rails. 0.12 is a gentle swing; 0 is a straight line.")]
    [SerializeField] private float arcHeight = 0.12f;

    [Tooltip("Which way the arc bows, in the anchor's local space. Right " +
             "(1,0,0) reads as the elbows swinging out; up (0,1,0) reads as " +
             "lifting over the bucket's edge.")]
    [SerializeField] private Vector3 arcDirection = new Vector3(1f, 0.3f, 0f);

    [Tooltip("How far AHEAD of the position the rotation runs, 0-0.4. Real " +
             "limbs orient toward where they are going before they arrive. " +
             "0.15 is subtle; above 0.3 starts to look like a broken wrist.")]
    [Range(0f, 0.4f)]
    [SerializeField] private float rotationLead = 0.15f;

    [Header("Timing (seconds)")]
    [Tooltip("Lowering the towel into the timba.")]
    [SerializeField] private float lowerDuration = 0.55f;

    [Tooltip("Held under the water. This is where the fabric darkens, so it " +
             "needs to last long enough to SEE — under 0.6 and the change " +
             "reads as a glitch rather than as soaking.\n\n" +
             "NOTE: TowelWetnessController has its own soakDuration for the " +
             "colour lerp. Set that one to about 0.65 so the darkening " +
             "finishes as the towel leaves the water, rather than continuing " +
             "through the wring.")]
    [SerializeField] private float soakDuration = 0.9f;

    [Tooltip("Wringing out the excess. BFP-accurate and not decorative: a " +
             "dripping cloth puts water into hot oil, which flashes to steam " +
             "and sprays burning oil outward.")]
    [SerializeField] private float wringDuration = 1.0f;

    [Tooltip("Lifting back to the ready pose.")]
    [SerializeField] private float liftDuration = 0.45f;

    [Header("Wring Motion")]
    [Tooltip("How far the towel twists each way, in degrees. 40 reads as " +
             "effort; much more and it looks like it is being unscrewed.")]
    [SerializeField] private float wringAngle = 40f;

    [Tooltip("How many full twists back and forth. Two is enough to read as " +
             "wringing without becoming a dance.")]
    [SerializeField] private float wringCycles = 2f;

    [Tooltip("How far the towel is pulled DOWN while wringing. Squeezing pulls " +
             "the cloth taut, so a few centimetres sells the effort.")]
    [SerializeField] private float wringPull = 0.04f;

    [Header("Settle On Return")]
    [Tooltip("How far the towel overshoots the rest pose before easing back, " +
             "in metres. Nothing held in a hand stops perfectly still on the " +
             "first attempt. 0.02 is barely conscious but the absence is " +
             "noticeable.")]
    [SerializeField] private float settleOvershoot = 0.02f;

    [SerializeField] private float settleDuration = 0.18f;

    [Header("Effects")]
    [Tooltip("Water disturbance in the timba. Played for the lower + soak " +
             "phases, stopped when the wring begins — the cloth comes out of " +
             "the water before it is wrung.")]
    [SerializeField] private ParticleSystem faucetWater;

    [Tooltip("Drips falling off the towel during the wring. Optional.")]
    [SerializeField] private ParticleSystem wringDrips;

    [Tooltip("Darkens the towel and flips IsWet. The soak is timed to start " +
             "partway through the water phase, so the fabric changes WHILE it " +
             "is submerged rather than before or after.")]
    [SerializeField] private TowelWetnessController wetness;

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip runningWaterClip;
    [SerializeField] private AudioClip wringClip;

    // Captured on the FIRST PlayDip call, not in Start. See the header — this
    // is the single most important line in the file to get right.
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private bool restCaptured = false;

    private Coroutine dipRoutine;
    private bool hasRun = false;

    /// <summary>
    /// TRUE while the dip is playing. SimulationManager holds the step lockout
    /// for this whole window, so nothing else needs to poll it — exposed for
    /// debugging and for anything that wants to suppress player movement.
    /// </summary>
    public bool IsDipping => dipRoutine != null;

    private void Start()
    {
        if (towel == null) towel = transform;
        // NO rest-pose caching here. See the header.
    }

    // -------------------------------------------------------
    // Captures the held pose the first time it is needed. By then TowelGrab
    // has parented the towel to TowelAnchor and zeroed its local transform, so
    // whatever it reads IS correct — there is nothing to get wrong.
    //
    // Guarded by a bool rather than a null check, because Vector3.zero is a
    // perfectly valid rest pose and in fact the expected one.
    // -------------------------------------------------------
    private void CaptureRestIfNeeded()
    {
        if (restCaptured) return;

        restLocalPosition = towel.localPosition;
        restLocalRotation = towel.localRotation;
        restCaptured = true;

        Debug.Log($"[TowelDip] Rest pose captured at {restLocalPosition}.");
    }

    private void Update()
    {
        // Authoring aid only. Snaps to the dip pose so it can be dragged into
        // place in the Scene view, then the numbers copied up.
        //
        // Requires the rest pose to already exist, which means the grab must
        // have happened. Before that, local coordinates are relative to
        // whatever the towel is parented to on the timba and mean nothing.
        if (previewDipPose && !IsDipping && Application.isPlaying && restCaptured)
        {
            towel.localPosition = dipLocalPosition;
            towel.localRotation = Quaternion.Euler(dipLocalEuler);
        }
    }

    // -------------------------------------------------------
    // PLAY THE DIP
    //
    // onComplete is handed EndStepLockout by SimulationManager, so the buttons
    // stay dead for exactly as long as the choreography actually runs. No
    // duration is duplicated anywhere else — the routine that owns the timing
    // owns the unlock. Same rule that fixed the Pull -> Aim bug.
    // -------------------------------------------------------
    public void PlayDip(System.Action onComplete = null)
    {
        // A second call mid-dip would leave two coroutines writing the same
        // transform, which stutters visibly. The step lockout should make this
        // unreachable, but the guard costs one line.
        if (dipRoutine != null)
        {
            Debug.Log("[TowelDip] Dip already running — ignored.");
            return;
        }

        if (towel == null) towel = transform;

        CaptureRestIfNeeded();

        dipRoutine = StartCoroutine(DipRoutine(onComplete));
    }

    private IEnumerator DipRoutine(System.Action onComplete)
    {
        previewDipPose = false;   // authoring aid must not fight the routine
        hasRun = true;

        Vector3 dipPos = dipLocalPosition;
        Quaternion dipRot = Quaternion.Euler(dipLocalEuler);
        Vector3 arc = arcDirection.normalized * arcHeight;

        // ---- 1. LOWER ----
        // Water disturbance starts BEFORE the towel arrives, so the surface is
        // already moving rather than reacting on contact like a trigger.
        PlayWater(true);

        yield return MoveArc(restLocalPosition, dipPos,
                             restLocalRotation, dipRot,
                             arc, lowerDuration);

        // ---- 2. SOAK ----
        // The fabric darkens here, under the water. Started slightly after
        // arrival so there is a beat of dry cloth breaking the surface first.
        yield return new WaitForSeconds(soakDuration * 0.25f);

        if (wetness != null) wetness.SetWet();

        yield return new WaitForSeconds(soakDuration * 0.75f);

        // ---- 3. WRING ----
        // The cloth comes OUT of the water before it is wrung. Leaving the
        // water effect running through the wring would undo the point of the
        // step.
        PlayWater(false);

        if (wringDrips != null) wringDrips.Play();
        if (audioSource != null && wringClip != null)
            audioSource.PlayOneShot(wringClip);

        yield return WringRoutine(dipPos, dipRot);

        if (wringDrips != null) wringDrips.Stop();

        // ---- 4. LIFT ----
        // Overshoot slightly past rest, then settle back. The arc reverses on
        // the way up, which is what an arm does.
        Vector3 overshoot = restLocalPosition
                          + (restLocalPosition - dipPos).normalized * settleOvershoot;

        yield return MoveArc(dipPos, overshoot,
                             dipRot, restLocalRotation,
                             -arc, liftDuration);

        // ---- 5. SETTLE ----
        yield return MoveArc(overshoot, restLocalPosition,
                             restLocalRotation, restLocalRotation,
                             Vector3.zero, settleDuration);

        // Land exactly on rest — a lerp can finish a hair short, and a towel
        // that drifts a millimetre per run visibly wanders over five attempts.
        towel.localPosition = restLocalPosition;
        towel.localRotation = restLocalRotation;

        dipRoutine = null;
        onComplete?.Invoke();

        Debug.Log("[TowelDip] Dip complete — towel is wet.");
    }

    // -------------------------------------------------------
    // ARC MOVE
    //
    // Position eases along a curved path; rotation eases on a slightly FASTER
    // curve so the wrists lead the arms. Both use SmoothStep rather than a
    // straight Lerp — constant-speed motion that stops dead is the clearest
    // tell that something is code-driven rather than performed.
    // -------------------------------------------------------
    private IEnumerator MoveArc(Vector3 fromPos, Vector3 toPos,
                                Quaternion fromRot, Quaternion toRot,
                                Vector3 arcOffset, float duration)
    {
        if (duration <= 0f)
        {
            towel.localPosition = toPos;
            towel.localRotation = toRot;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float raw = Mathf.Clamp01(elapsed / duration);

            float t = Mathf.SmoothStep(0f, 1f, raw);

            // Rotation runs ahead of position. Clamped so a high lead value
            // cannot push it past the target and snap back.
            float rotT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(raw + rotationLead));

            // Sine bulge peaks at the midpoint and returns to zero at both
            // ends, so the path curves without missing either pose.
            float bulge = Mathf.Sin(raw * Mathf.PI);

            towel.localPosition = Vector3.Lerp(fromPos, toPos, t) + arcOffset * bulge;
            towel.localRotation = Quaternion.Slerp(fromRot, toRot, rotT);

            yield return null;
        }

        towel.localPosition = toPos;
        towel.localRotation = toRot;
    }

    // -------------------------------------------------------
    // WRING
    //
    // A twist oscillation whose amplitude DECAYS, plus a downward pull.
    //
    // The decay is what makes it read as a person rather than a mechanism: a
    // constant-amplitude twist looks motorised, while one that fades looks
    // like effort running out. Same reason the arc matters more than the
    // distance.
    // -------------------------------------------------------
    private IEnumerator WringRoutine(Vector3 dipPos, Quaternion dipRot)
    {
        if (wringDuration <= 0f) yield break;

        float elapsed = 0f;

        while (elapsed < wringDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / wringDuration);

            // Amplitude fades from full to nothing across the wring.
            float decay = 1f - t;

            float angle = Mathf.Sin(t * Mathf.PI * 2f * wringCycles)
                        * wringAngle * decay;

            // Squeezing pulls the cloth taut, so it dips slightly further down
            // mid-wring and eases back. Sine again, peaking in the middle.
            float pull = Mathf.Sin(t * Mathf.PI) * wringPull;

            towel.localRotation = dipRot * Quaternion.AngleAxis(angle, Vector3.forward);
            towel.localPosition = dipPos + Vector3.down * pull;

            yield return null;
        }

        towel.localRotation = dipRot;
        towel.localPosition = dipPos;
    }

    private void PlayWater(bool on)
    {
        if (faucetWater != null)
        {
            if (on) faucetWater.Play();
            else faucetWater.Stop();
        }

        if (audioSource == null || runningWaterClip == null) return;

        if (on)
        {
            audioSource.clip = runningWaterClip;
            audioSource.loop = true;
            audioSource.Play();
        }
        else if (audioSource.clip == runningWaterClip)
        {
            audioSource.Stop();
        }
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // Call from wherever the Kitchen run resets, alongside
    // TowelGrab.ResetToTimba(), TowelWetnessController.ResetToDry(),
    // TowelCoverController.ResetToRest(), KitchenInteractable.ResetForReplay()
    // and WCTLButtonManager.ResetForReplay().
    //
    // Without it, a run abandoned mid-dip leaves the towel stuck in the bucket
    // pose on the next attempt — and the coroutine handle still set, so
    // PlayDip's guard would refuse to run the step at all. The player would
    // tap WET and nothing would happen, with no error.
    //
    // restCaptured is cleared too, because TowelGrab is about to put the towel
    // back on the timba. Keeping the old capture would be harmless in practice
    // — the held pose is the same every run — but a stale cache that happens
    // to be right is a bug waiting for the day the anchor moves.
    // -------------------------------------------------------
    public void ResetToRest()
    {
        if (dipRoutine != null)
        {
            StopCoroutine(dipRoutine);
            dipRoutine = null;
        }

        PlayWater(false);
        if (wringDrips != null) wringDrips.Stop();

        if (towel != null && hasRun && restCaptured)
        {
            towel.localPosition = restLocalPosition;
            towel.localRotation = restLocalRotation;
        }

        restCaptured = false;
        previewDipPose = false;

        Debug.Log("[TowelDip] Reset to rest pose.");
    }
}