using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FootstepController : MonoBehaviour
{
    [Header("Footstep Clips")]
    [Tooltip("3 or more short single-step sounds. One is picked at random " +
             "per step, so the same clip is never heard twice in a row.")]
    public AudioClip[] footstepClips;

    [Header("Timing")]
    [Tooltip("Seconds between steps while walking. Lower = faster pace.")]
    public float stepInterval = 0.5f;

    [Tooltip("How fast the player must be moving before steps play. " +
             "Filters out joystick drift and gravity settling.")]
    public float minSpeed = 0.5f;

    [Header("Volume")]
    [Range(0f, 1f)]
    [Tooltip("Footsteps should sit UNDER the music, not on top of it.")]
    public float volume = 0.5f;

    [Tooltip("Random pitch range. Small variation stops repeated clips " +
             "sounding identical.")]
    [Range(0f, 0.3f)]
    public float pitchVariation = 0.1f;

    private CharacterController controller;
    private AudioSource source;
    private float stepTimer;
    private int lastClipIndex = -1;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Own AudioSource rather than AudioManager's shared one, because
        // pitch has to be randomised per step and that would affect every
        // other SFX sharing the source.
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
    }

    void Update()
    {
        if (footstepClips == null || footstepClips.Length == 0) return;

        // Horizontal speed only. controller.velocity includes the downward
        // gravity term, which is never zero — so the raw magnitude would
        // read as "moving" even when standing still.
        Vector3 flat = controller.velocity;
        flat.y = 0f;

        bool moving = flat.magnitude > minSpeed && controller.isGrounded;

        if (!moving)
        {
            // Reset so the first step after standing still fires immediately
            // rather than partway through a leftover countdown.
            stepTimer = 0f;
            return;
        }

        stepTimer -= Time.deltaTime;

        if (stepTimer <= 0f)
        {
            PlayStep();
            stepTimer = stepInterval;
        }
    }

    private void PlayStep()
    {
        int index = Random.Range(0, footstepClips.Length);

        // Never the same clip twice in a row — with only 3 clips, a repeat
        // is very audible and reads as a bug.
        if (footstepClips.Length > 1 && index == lastClipIndex)
            index = (index + 1) % footstepClips.Length;

        lastClipIndex = index;

        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(footstepClips[index], volume);
    }
}