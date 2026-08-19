using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// TowelCoverController — the Cover step's choreography.
//
// Arcs the wet towel out of the player's hands and down onto the burning
// regulator, then LEAVES IT THERE.
//
// Same transform-lerp approach as TowelDipController. Read that file's header
// first — the four realism rules (easing, arc, leading rotation, settle) apply
// here unchanged and are not repeated.
//
// -------------------------------------------------------
// THE ONE THING THAT IS GENUINELY DIFFERENT: PARENTING
//
// The dip never leaves the hand, so it lerps in LOCAL space under TowelAnchor
// and the camera can move freely throughout.
//
// Cover does the opposite. The towel is being placed on a fixed object in the
// world, and it stays there. If it were still parented to the camera anchor
// during the travel, turning your head mid-throw would drag the towel sideways
// through the air — and once landed, walking away would carry the tank cover
// along with you like a flag.
//
// So the towel is UNPARENTED at the start of the travel and lerps in WORLD
// space toward coverTarget. On arrival it is parented to the tank instead, so
// it inherits any future movement of the assembly rather than floating if the
// tank is ever repositioned.
//
// This is also why ResetToRest has more work to do here than in the dip: it
// must put the towel back under the anchor AND restore its local pose, or the
// next run starts with no towel in hand and no error to say why.
//
// -------------------------------------------------------
// WHY THE FIRE DIES ON A SEPARATE CALLBACK
//
// The extinguish must fire at the exact frame the cloth touches the flame —
// not when the button was tapped, and not when the whole animation finishes.
// Either of those reads wrong: one kills the fire before anything reaches it,
// the other leaves it burning through a cloth already lying on top of it.
//
// So PlayCover takes TWO callbacks. onContact fires at the moment of landing;
// onComplete fires when the settle finishes and the buttons should unlock.
//
// SimulationManager passes fireController.ExtinguishFire into onContact, which
// keeps fire ownership where it already lives while letting the coroutine own
// the timing. One owner per behaviour, same rule as everywhere else.
// -------------------------------------------------------

public class TowelCoverController : MonoBehaviour
{
    [Header("What Moves")]
    [Tooltip("The towel transform. Should be parented under TowelAnchor on the " +
             "PlayerCamera at the start of a run.\n\n" +
             "Leave empty to move THIS object.")]
    [SerializeField] private Transform towel;

    [Header("Where It Lands")]
    [Tooltip("An empty GameObject positioned and rotated where the towel should " +
             "come to rest on the tank collar.\n\n" +
             "Place it BY EYE in the Scene view — the regulator is a lumpy " +
             "target and no set of typed numbers will beat dragging it into " +
             "position. Sit it a centimetre or two proud of the geometry so the " +
             "cloth does not clip through the regulator body.")]
    [SerializeField] private Transform coverTarget;

    [Tooltip("What the towel is parented to after landing. Usually the LPG " +
             "assembly root, so the cover moves with the tank if it is ever " +
             "repositioned.\n\n" +
             "Leave empty to unparent into world space instead.")]
    [SerializeField] private Transform parentAfterLanding;

    [Header("Motion Shape")]
    [Tooltip("How far the towel bows UP on its way across. You lift a cloth over " +
             "a flame and lower it, rather than sliding it in flat — the arc is " +
             "what makes that read.\n\n" +
             "Larger than the dip's arc on purpose: this is a bigger, more " +
             "deliberate movement.")]
    [SerializeField] private float arcHeight = 0.25f;

    [Tooltip("Which way the arc bows, in WORLD space. Up (0,1,0) is almost " +
             "always right here.")]
    [SerializeField] private Vector3 arcDirection = Vector3.up;

    [Tooltip("How far AHEAD of the position the rotation runs, 0-0.4. The cloth " +
             "turns flat before it arrives, which is what a hand does when " +
             "laying something down.")]
    [Range(0f, 0.4f)]
    [SerializeField] private float rotationLead = 0.2f;

    [Header("Timing (seconds)")]
    [Tooltip("The throw. Deliberately not fast — this is a careful placement " +
             "over a flame, not a toss.")]
    [SerializeField] private float travelDuration = 0.8f;

    [Tooltip("The cloth settling onto the collar after contact. Short.")]
    [SerializeField] private float settleDuration = 0.35f;

    [Header("Landing Squash")]
    [Tooltip("How much the towel flattens as it settles, on its local up axis. " +
             "0.6 means it compresses to 60% thickness.\n\n" +
             "A static mesh cannot drape, so this squash is doing the work a " +
             "cloth simulation would. It is crude, and at this distance on a " +
             "phone it is entirely convincing.")]
    [Range(0.2f, 1f)]
    [SerializeField] private float landingSquash = 0.6f;

    [Tooltip("How far the cloth spreads outward as it flattens. Real fabric " +
             "widens when it settles — squashing without spreading reads as the " +
             "towel shrinking rather than draping.")]
    [Range(1f, 1.6f)]
    [SerializeField] private float landingSpread = 1.15f;

    [Header("Effects")]
    [Tooltip("Smoke or steam puffing out from under the cloth on contact. " +
             "Optional, but it is the clearest signal that the flame is being " +
             "starved rather than simply hidden.")]
    [SerializeField] private ParticleSystem smotherPuff;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip coverClip;

    // Cached at Start so ResetToRest can rebuild the held pose exactly.
    private Transform originalParent;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private Vector3 restLocalScale;

    private Coroutine coverRoutine;
    private bool hasCovered = false;

    /// <summary>
    /// TRUE once the towel is lying on the tank. Useful for anything that needs
    /// to know the player's hands are empty — the valve reach, for instance.
    /// </summary>
    public bool HasCovered => hasCovered;

    public bool IsCovering => coverRoutine != null;

    private void Start()
    {
        if (towel == null) towel = transform;

        originalParent = towel.parent;
        restLocalPosition = towel.localPosition;
        restLocalRotation = towel.localRotation;
        restLocalScale = towel.localScale;
    }

    // -------------------------------------------------------
    // PLAY THE COVER
    //
    // onContact — the instant the cloth reaches the flame. Fire dies here.
    // onComplete — the settle has finished and the buttons should unlock.
    // -------------------------------------------------------
    public void PlayCover(System.Action onContact = null, System.Action onComplete = null)
    {
        if (coverRoutine != null)
        {
            Debug.Log("[TowelCover] Cover already running — ignored.");
            return;
        }

        if (coverTarget == null)
        {
            // Fail loudly but do not hang. A missing target with a silent
            // return would leave the step lockout raised until the safety
            // valve fired, and the player staring at dead buttons.
            Debug.LogError("[TowelCover] No coverTarget assigned — skipping the " +
                           "animation. The fire will still go out, but the towel " +
                           "will not move.");
            onContact?.Invoke();
            onComplete?.Invoke();
            return;
        }

        coverRoutine = StartCoroutine(CoverRoutine(onContact, onComplete));
    }

    private IEnumerator CoverRoutine(System.Action onContact, System.Action onComplete)
    {
        // UNPARENT FIRST. From here the towel travels in world space, so
        // turning the camera mid-throw no longer drags it sideways.
        //
        // worldPositionStays: true keeps it exactly where it appears right now
        // rather than snapping to the origin.
        towel.SetParent(null, true);

        Vector3 startPos = towel.position;
        Quaternion startRot = towel.rotation;
        Vector3 startScale = towel.localScale;

        Vector3 endPos = coverTarget.position;
        Quaternion endRot = coverTarget.rotation;

        Vector3 arc = arcDirection.normalized * arcHeight;

        if (audioSource != null && coverClip != null)
            audioSource.PlayOneShot(coverClip);

        // ---- TRAVEL ----
        float elapsed = 0f;

        while (elapsed < travelDuration)
        {
            elapsed += Time.deltaTime;
            float raw = Mathf.Clamp01(elapsed / travelDuration);

            float t = Mathf.SmoothStep(0f, 1f, raw);

            // Rotation leads, so the cloth is already flat when it arrives.
            float rotT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(raw + rotationLead));

            // Sine bulge peaks mid-travel and returns to zero at both ends, so
            // the towel rises over the flame and comes down onto it.
            float bulge = Mathf.Sin(raw * Mathf.PI);

            towel.position = Vector3.Lerp(startPos, endPos, t) + arc * bulge;
            towel.rotation = Quaternion.Slerp(startRot, endRot, rotT);

            yield return null;
        }

        towel.position = endPos;
        towel.rotation = endRot;

        // ---- CONTACT ----
        // The cloth is on the flame. Everything that depends on that moment
        // happens on this line and not before it.
        hasCovered = true;

        if (smotherPuff != null) smotherPuff.Play();

        onContact?.Invoke();

        Debug.Log("[TowelCover] Contact — cloth is on the flame.");

        // Parent to the tank AFTER landing, so the cover travels with the
        // assembly if it is ever repositioned. Done here rather than before
        // the travel so a moving tank cannot drag the towel through its throw.
        if (parentAfterLanding != null)
            towel.SetParent(parentAfterLanding, true);

        // ---- SETTLE ----
        // A static mesh cannot drape, so it squashes and spreads instead. Crude,
        // and at this distance on a phone it does the job a cloth sim would.
        Vector3 landedScale = new Vector3(
            startScale.x * landingSpread,
            startScale.y * landingSquash,
            startScale.z * landingSpread);

        elapsed = 0f;

        while (elapsed < settleDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / settleDuration));

            towel.localScale = Vector3.Lerp(startScale, landedScale, t);

            yield return null;
        }

        towel.localScale = landedScale;

        coverRoutine = null;
        onComplete?.Invoke();

        Debug.Log("[TowelCover] Cover complete — hands are free for the valve.");
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // More work than the dip's reset, because the towel has been REPARENTED.
    // Putting the local pose back is not enough on its own — without restoring
    // the parent first, the towel would still be sitting on the tank and the
    // player would start the next run empty-handed, with no error to say why.
    //
    // The scale restore matters too: skip it and every replay compounds the
    // squash until the towel is a sheet of paper.
    // -------------------------------------------------------
    public void ResetToRest()
    {
        if (coverRoutine != null)
        {
            StopCoroutine(coverRoutine);
            coverRoutine = null;
        }

        if (smotherPuff != null) smotherPuff.Stop();

        hasCovered = false;

        if (towel == null) return;

        towel.SetParent(originalParent, false);
        towel.localPosition = restLocalPosition;
        towel.localRotation = restLocalRotation;
        towel.localScale = restLocalScale;

        Debug.Log("[TowelCover] Reset — towel back in hand.");
    }
}