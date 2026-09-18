using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Plays the aftermath of a fire being put out, in three layers:
//
//   POWDER CLOUD  the extinguisher's. Pale, forms where the spray lands
//                 while the player is still squeezing, settles downward.
//   SMOKE         the fire's. Dark, appears as the flames lose, rises.
//   RESIDUE       what is LEFT. Powder settled on the surface. Fades in as
//                 the cloud drops, then STAYS for the rest of the run.
//
// FireController kills the fire; this shows what the fire and the
// extinguisher left behind. Without it, ExtinguishFire() fades the flames
// to nothing and the spot is simply empty - which is why it reads as "the
// fire disappeared" rather than "the fire was put out."
//
// The residue is the part that matters most. Clouds clear, so a player who
// turns around ten seconds later sees no evidence of anything. The patch on
// the desk is the only lasting proof, and it is what makes the room feel
// like something happened in it.
//
// -------------------------------------------------------
// WHY THE RESIDUE IS A QUAD AND NOT A PARTICLE
//
// Particles are always moving and always costing frame time. Residue does
// neither: it lands once and never changes again. A flat quad lying on the
// surface with a transparent material is the cheapest possible way to hold
// a mark on screen indefinitely - on Android it is effectively free once
// the fade has finished.
//
// The material must be URP/Lit rather than Unlit. Lit takes the room's
// lighting, so the patch sits in the same shade as the desk under it.
// Unlit would hold full brightness in a dim room, which is the single
// clearest tell of a texture pasted on top of a scene.
//
// -------------------------------------------------------
// WHY IT FADES WITH A PROPERTY BLOCK AND NOT renderer.material.color
//
// Touching renderer.sharedMaterial edits the .mat ASSET ON DISK. Fading it
// to zero in the editor would leave the material asset at zero alpha after
// you stop playing - the residue would then be invisible forever, in every
// scene using it, with nothing obviously wrong to find.
//
// renderer.material avoids that but silently clones a new material every
// run, which then leaks until the scene unloads.
//
// A MaterialPropertyBlock overrides the colour for THIS renderer only,
// touches no asset and allocates nothing. The material asset keeps its own
// alpha, which is also why the patches stay visible in the Scene view while
// you position them - only Play mode hides them.
//
// -------------------------------------------------------
// WHY PlayOut TAKES dieDuration INSTEAD OF STORING ITS OWN COPY
//
// All three layers start PART WAY THROUGH the flames dying, not after. If
// they only appeared at the end they would look like separate effects
// switching on.
//
// That timing depends on dieDuration, which lives on FireController. A
// second copy here would drift the first time the fade was retuned, with
// nothing to warn you. So FireController passes its own number in and each
// delay is worked out as a FRACTION of it.
//
// Residue Fade Duration is the exception, and deliberately so: it is how
// long powder takes to SETTLE, which has nothing to do with how long the
// fire takes to die. Different quantity, so it gets its own field.
//
// -------------------------------------------------------
// KITCHEN IS UNAFFECTED
//
// FireController calls this through a null-guarded field. Kitchen's fire
// has no aftermath object assigned, so nothing plays. That is deliberate:
// Kitchen is WCTL - a wet towel smothering an LPG fire - so dry chemical
// residue would be the wrong protocol on screen.
// -------------------------------------------------------

public class FireExtinguishEffects : MonoBehaviour
{
    [Header("Powder cloud (the extinguisher's)")]
    [Tooltip("Drag the PowderCloud particle system here. Leave empty and no " +
             "cloud plays - no error, no log spam.")]
    [SerializeField] private ParticleSystem powder;

    [Range(0f, 1f)]
    [Tooltip("WHEN the powder appears, as a fraction of the fire's Die " +
             "Duration.\n\n" +
             "Keep this EARLY - around 0.1. The powder leaves the nozzle the " +
             "moment the player sweeps, so the cloud should build while the " +
             "flames are still tall.")]
    [SerializeField] private float powderStartFraction = 0.1f;

    [Header("Smoke (the fire's)")]
    [Tooltip("Drag the Smoke particle system here. Leave empty and no smoke " +
             "plays.")]
    [SerializeField] private ParticleSystem smoke;

    [Range(0f, 1f)]
    [Tooltip("WHEN the smoke starts, as a fraction of the fire's Die " +
             "Duration.\n\n" +
             "0.4 with a 4-second fade = smoke begins at 1.6 seconds, while " +
             "the flames are still visibly shrinking. The overlap is what " +
             "makes it read as one event rather than two.")]
    [SerializeField] private float smokeStartFraction = 0.4f;

    [Header("Residue (what stays)")]
    [Tooltip("Drag the residue quads here - Residue_1, and later _2 and _3 " +
             "for broken, overlapping coverage.\n\n" +
             "All of them fade in together. Vary their SCALE and ROTATION in " +
             "the Scene view rather than their timing: powder that lands in " +
             "stages looks animated, and residue should look settled.")]
    [SerializeField] private Renderer[] residuePatches;

    [Range(0f, 1f)]
    [Tooltip("WHEN the residue starts fading in, as a fraction of the fire's " +
             "Die Duration.\n\n" +
             "0.62 with a 4-second fade = it begins at about 2.5 seconds, as " +
             "the cloud starts dropping. Earlier and powder appears on the " +
             "desk while the fire is still burning on top of it.")]
    [SerializeField] private float residueStartFraction = 0.62f;

    [Tooltip("How long the residue takes to reach full strength, in SECONDS.\n\n" +
             "Not a fraction, and not tied to Die Duration: this is how long " +
             "powder takes to settle out of the air, which is its own thing. " +
             "Around 2.5 reads as settling. Under 0.5 it pops.")]
    [SerializeField] private float residueFadeDuration = 2.5f;

    [Range(0f, 1f)]
    [Tooltip("How strong the residue ends up.\n\n" +
             "0.35 = a subtle dusting, 0.6 = clearly visible from across the " +
             "room, 0.85 = heavy coating. 0.6 reads as a mark without " +
             "looking like spilled flour.")]
    [SerializeField] private float residueOpacity = 0.6f;

    // One handle each, so a second call cannot stack a second delayed start
    // on top of the first.
    private Coroutine powderRoutine;
    private Coroutine smokeRoutine;
    private Coroutine residueRoutine;

    // Each patch's own colour, read from the material ONCE so the fade can
    // keep the tint and change only the alpha.
    private Color[] patchColors;
    private MaterialPropertyBlock block;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (residuePatches == null) return;

        patchColors = new Color[residuePatches.Length];

        for (int i = 0; i < residuePatches.Length; i++)
        {
            if (residuePatches[i] == null) continue;

            // sharedMaterial is READ here, never written - reading does not
            // touch the asset. See the note at the top of this file.
            var mat = residuePatches[i].sharedMaterial;

            patchColors[i] = (mat != null && mat.HasProperty(BaseColorId))
                ? mat.GetColor(BaseColorId)
                : Color.white;
        }

        // Hidden before the fire has been touched. Awake rather than Start so
        // no frame ever renders them at full strength.
        SetResidueAlpha(0f);
    }

    // -------------------------------------------------------
    // PUBLIC: called by FireController the moment SWEEP is tapped -
    // at the START of the death fade, not the end.
    //
    //   dieDuration  FireController's own fade length, passed in so this
    //                script never keeps a second copy of it.
    // -------------------------------------------------------
    public void PlayOut(float dieDuration)
    {
        if (powder != null)
        {
            if (powderRoutine != null) StopCoroutine(powderRoutine);
            powderRoutine = StartCoroutine(
                PlayAfter(powder, dieDuration * powderStartFraction, "powder cloud"));
        }

        if (smoke != null)
        {
            if (smokeRoutine != null) StopCoroutine(smokeRoutine);
            smokeRoutine = StartCoroutine(
                PlayAfter(smoke, dieDuration * smokeStartFraction, "smoke"));
        }

        if (residuePatches != null && residuePatches.Length > 0)
        {
            if (residueRoutine != null) StopCoroutine(residueRoutine);
            residueRoutine = StartCoroutine(
                FadeResidueIn(dieDuration * residueStartFraction));
        }
    }

    // Shared by both particle layers, because the only difference between
    // them is the wait.
    private IEnumerator PlayAfter(ParticleSystem system, float delay, string label)
    {
        // WaitForSeconds is scaled time, so pausing the game pauses the
        // aftermath with it. A real-time wait would run on behind the pause
        // menu and be half finished when the player came back.
        if (delay > 0f) yield return new WaitForSeconds(Mathf.Max(0f, delay));

        // Clear first so a replayed run cannot inherit particles left over
        // from the previous attempt.
        system.Clear(true);
        system.Play(true);

        Debug.Log($"[FireExtinguishEffects] Aftermath {label} started.");
    }

    private IEnumerator FadeResidueIn(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(Mathf.Max(0f, delay));

        float duration = Mathf.Max(0.01f, residueFadeDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // SmoothStep rather than a straight lerp: powder settles fastest
            // in the middle of the fall and eases out as the air clears.
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            SetResidueAlpha(eased * residueOpacity);

            yield return null;
        }

        // Land exactly on the target - the loop can exit a fraction short.
        SetResidueAlpha(residueOpacity);

        Debug.Log("[FireExtinguishEffects] Residue settled.");

        residueRoutine = null;
    }

    private void SetResidueAlpha(float alpha)
    {
        if (residuePatches == null || patchColors == null) return;

        for (int i = 0; i < residuePatches.Length; i++)
        {
            if (residuePatches[i] == null) continue;

            Color c = patchColors[i];
            c.a = alpha;

            residuePatches[i].GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            residuePatches[i].SetPropertyBlock(block);
        }
    }

    // -------------------------------------------------------
    // PUBLIC: put this back to its starting state.
    //
    // Insurance, the same way FireController.ResetToFullStrength is. The
    // Office scene reloads between attempts, which resets everything anyway -
    // but a run restarted WITHOUT a scene reload would otherwise begin with
    // last attempt's smoke in the air and its powder already on the desk.
    // -------------------------------------------------------
    public void ResetForReplay()
    {
        if (powderRoutine != null) { StopCoroutine(powderRoutine); powderRoutine = null; }
        if (smokeRoutine != null) { StopCoroutine(smokeRoutine); smokeRoutine = null; }
        if (residueRoutine != null) { StopCoroutine(residueRoutine); residueRoutine = null; }

        StopAndClear(powder);
        StopAndClear(smoke);

        SetResidueAlpha(0f);
    }

    private void StopAndClear(ParticleSystem system)
    {
        if (system == null) return;

        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.Clear(true);
    }
}