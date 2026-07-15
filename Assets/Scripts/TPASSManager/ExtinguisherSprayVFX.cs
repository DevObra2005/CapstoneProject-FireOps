using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// A simple "remote control" for the nozzle spray particle effect.
// This script doesn't decide WHEN to spray — it just knows HOW.
// Other scripts (like TPASSButtonManager) call Play() and Stop()
// at the right moments in the TPASS sequence.
//
// This separation matters: if the spray effect ever needs to
// change (different particle system, different sound, etc.),
// we only edit THIS file — nothing else needs to know how the
// spray actually works internally.
// -------------------------------------------------------

public class ExtinguisherSprayVFX : MonoBehaviour
{
    [Tooltip("Drag the NozzleSprayVFX Particle System here.")]
    [SerializeField] private ParticleSystem sprayParticles;

    // ── Called to START the spray ─────────────────────────────────
    public void Play()
    {
        if (sprayParticles == null) return;

        // Play() starts emitting particles continuously, following
        // whatever settings you configured in the Inspector.
        sprayParticles.Play();
    }

    // ── Called to STOP the spray ──────────────────────────────────
    public void Stop()
    {
        if (sprayParticles == null) return;

        // Stop() halts NEW particles from being created, but lets
        // any particles already in the air finish their lifetime
        // naturally — looks more natural than particles vanishing
        // instantly mid-air.
        sprayParticles.Stop();
    }
}
