using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// TowelDipController — the Wet step's choreography.
//
// Moves a STATIC towel mesh through a dip, soak, wring and lift using
// transform lerps. No bone rig, no IK, no Animator layers.
//
// WHY NO RIG:
// The dip is the towel MOVING, not the towel DEFORMING. A bone rig would buy
// cloth bend on an object that is on screen for under three seconds at arm's
// length, at the cost of the entire Blender rig pipeline — weight painting,
// orphan-bone hunting, IK targets, masked layers. Everything that cost days on
// the extinguisher.
//
// This file is the same coroutine pattern as RelaxRoutine in
// SimulationManager. Nothing new to debug.
//
// -------------------------------------------------------
// WHAT MAKES IT READ AS REAL RATHER THAN AS A LERP
//
// 1. EASING, NOT LINEAR. A straight Lerp moves at constant speed and stops
//    dead — the single clearest tell that something is code-driven. SmoothStep
//    accelerates out of rest and decelerates into the target, which is how an
//    arm actually moves.
//
// 2. AN ARC, NOT A STRAIGHT LINE. A hand lowering a cloth swings through a
//    curve because it is pivoting at the elbow and shoulder. A sine bulge
//    perpendicular to the travel gives that for one line of maths. Straight-
//    line motion looks like a machine on rails.
//
// 3. ROTATION THAT LEADS THE MOVE. The wrist turns as the arm lowers, and it
//    turns slightly AHEAD of the position — real limbs orient toward where
//    they are going. Rotating on a slightly faster curve than the position
//    sells this.
//
// 4. A SETTLE, NOT A STOP. Coming back to rest, the towel overshoots a few
//    millimetres and eases back. Nothing in a hand stops perfectly still on
//    the first attempt.
//
// The wring adds a fifth: an oscillation whose amplitude DECAYS. A constant
// twist reads as a mechanism; a fading one reads as effort running out.
//
// -------------------------------------------------------
// SETTING THE TWO POSES
//
// restLocal* is read automatically from wherever you park the towel in the
// Hierarchy at edit time — that IS the held pose, so it never needs typing.
//
// For the dip pose: enter Play mode, tick `previewDipPose`, drag the towel in
// the Scene view until it sits in the sink, then copy its local Position and
// Rotation into the dip fields. Untick. This avoids guessing numbers.
// -------------------------------------------------------

public class TowelDipController : MonoBehaviour
{
    [Header("What Moves")]
    [Tooltip("The towel transform. Should be parented under TowelAnchor on the " +
             "PlayerCamera, exactly like ExtinguisherAnchor.\n\n" +
             "Leave empty to move THIS object.")]
    [SerializeField] private Transform towel;

    [Header("Dip Pose (local to the anchor)")]
    [Tooltip("Where the towel sits when it is under the tap. Use previewDipPose " +
             "below to find these numbers rather than guessing them.")]
    [SerializeField] private Vector3 dipLocalPosition = new Vector3(0f, -0.35f, 0.45f);

    [Tooltip("Rotation under the tap. Tilting the towel forward reads as the " +
             "wrist turning to hold it under running water.")]
    [SerializeField] private Vector3 dipLocalEuler = new Vector3(35f, 0f, 0f);

    [Tooltip("PLAY MODE ONLY. Snaps the towel to the dip pose so you can drag it " +
             "into place in the Scene view, then copy the numbers up. Untick " +
             "before testing the step.")]
    [SerializeField] private bool previewDipPose = false;

    [Header("Motion Shape")]
    [Tooltip("How far the towel bows OUT from a straight line on its way down " +
             "and back. This is the difference between an arm movement and a " +
             "lift on rails. 0.12 is a gentle swing; 0 is a straight line.")]
    [SerializeField] private float arcHeight = 0.12f;

    [Tooltip("Which way the arc bows, in the anchor's local space. Right (1,0,0) " +
             "reads as the elbow swinging out; up (0,1,0) reads as lifting over " +
             "the sink edge.")]
    [SerializeField] private Vector3 arcDirection = new Vector3(1f, 0.3f, 0f);

    [Tooltip("How far AHEAD of the position the rotation runs, 0-0.4. Real limbs " +
             "orient toward where they are going before they arrive. 0.15 is " +
             "subtle; above 0.3 starts to look like the wrist is broken.")]
    [Range(0f, 0.4f)]
    [SerializeField] private float rotationLead = 0.15f;

    [Header("Timing (seconds)")]
    [Tooltip("Lowering the towel to the tap.")]
    [SerializeField] private float lowerDuration = 0.55f;

    [Tooltip("Held under running water. This is where the fabric darkens, so it " +
             "needs to last long enough to SEE — under 0.6 and the change reads " +
             "as a glitch rather than as soaking.")]
    [SerializeField] private float soakDuration = 0.9f;

    [Tooltip("Wringing out the excess. BFP-accurate and not decorative: a " +
             "dripping cloth puts water into hot oil, which flashes to steam and " +
             "sprays burning oil outward.")]
    [SerializeField] private float wringDuration = 1.0f;

    [Tooltip("Lifting back to the ready pose.")]
    [SerializeField] private float liftDuration = 0.45f;

    [Header("Wring Motion")]
    [Tooltip("How far the towel twists each way, in degrees. 40 reads as effort; " +
             "much more and it looks like it is being unscrewed.")]
    [SerializeField] private float wringAngle = 40f;

    [Tooltip("How many full twists back and forth. Two is enough to read as " +
             "wringing without becoming a dance.")]
    [SerializeField] private float wringCycles = 2f;

    [Tooltip("How far the towel is pulled DOWN while wringing. Squeezing pulls " +
             "the cloth taut, so a few centimetres sells the effort.")]
    [SerializeField] private float wringPull = 0.04f;

    [Header("Settle On Return")]
    [Tooltip("How far the towel overshoots the rest pose before easing back, in " +
             "metres. Nothing held in a hand stops perfectly still on the first " +
             "attempt. 0.02 is barely conscious but the absence is noticeable.")]
    [SerializeField] private float settleOvershoot = 0.02f;

    [SerializeField] private float settleDuration = 0.18f;

    [Header("Effects")]
    [Tooltip("Faucet water stream. Played for the lower + soak phases, stopped " +
             "when the wring begins — you take the cloth out of the water before " +
             "you wring it.")]
    [SerializeField] private ParticleSystem faucetWater;

    [Tooltip("Drips falling off the towel during the wring. Optional.")]
    [SerializeField] private ParticleSystem wringDrips;

    [Tooltip("Darkens the towel and flips IsWet. The soak is timed to start " +
             "partway through the water phase, so the fabric changes WHILE the " +
             "stream is on it rather than before or after.")]
    [SerializeField] private TowelWetnessController wetness;

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip runningWaterClip;
    [SerializeField] private AudioClip wringClip;

    // Cached at Start — wherever the towel is parked in the Hierarchy IS the
    // held pose, so it never needs typing into the Inspector.
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;

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

        restLocalPosition = towel.localPosition;
        restLocalRotation = towel.localRotation;
    }

    private void Update()
    {
        // Authoring aid only. Snaps to the dip pose so it can be dragged into
        // place in the Scene view, then the numbers copied up.
        if (previewDipPose && !IsDipping && Application.isPlaying)
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
        // Water starts BEFORE the towel arrives. A tap that switches on at the
        // exact moment the cloth reaches it reads as a trigger; one already
        // running reads as a tap someone opened.
        PlayWater(true);

        yield return MoveArc(restLocalPosition, dipPos,
                             restLocalRotation, dipRot,
                             arc, lowerDuration);

        // ---- 2. SOAK ----
        // The fabric darkens here, under the stream. Started slightly after
        // arrival so there is a beat of water hitting dry cloth first.
        yield return new WaitForSeconds(soakDuration * 0.25f);

        if (wetness != null) wetness.SetWet();

        yield return new WaitForSeconds(soakDuration * 0.75f);

        // ---- 3. WRING ----
        // Water off first. You take the cloth OUT of the stream before you
        // wring it — leaving the tap running through the wring would undo the
        // point of the step.
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
    // curve so the wrist leads the arm. Both use SmoothStep rather than a
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
    // constant-amplitude twist looks motorised, while one that fades looks like
    // effort running out. Same reason the arc matters more than the distance.
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
    // Call this from wherever the Kitchen run resets, alongside
    // TowelWetnessController.ResetToDry().
    //
    // Without it, a run abandoned mid-dip leaves the towel stuck in the sink
    // pose on the next attempt — and the coroutine handle still set, so
    // PlayDip's guard would refuse to run the step at all. The player would
    // tap WET and nothing would happen, with no error.
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

        if (towel != null && hasRun)
        {
            towel.localPosition = restLocalPosition;
            towel.localRotation = restLocalRotation;
        }

        Debug.Log("[TowelDip] Reset to rest pose.");
    }
}