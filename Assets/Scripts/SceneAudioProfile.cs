using UnityEngine;

public class SceneAudioProfile : MonoBehaviour
{
    public static SceneAudioProfile Instance { get; private set; }

    [Header("Phase 1 - Hazard Identification")]
    public AudioClip phase1Music;
    public AudioClip phase1Ambient;
    [Tooltip("Tick = music starts as soon as the scene loads.")]
    public bool phase1PlayOnStart = true;

    [Header("Phase 2 - Emergency Simulation")]
    public AudioClip phase2Music;
    public AudioClip phase2Ambient;
    [Tooltip("Untick = music waits until the briefing calls BeginPhaseAudio().")]
    public bool phase2PlayOnStart = false;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        bool isPhase2 = PlayerPrefs.GetInt("SimulationMode", 0) == 1;

        if (isPhase2 ? phase2PlayOnStart : phase1PlayOnStart)
            BeginPhaseAudio();
    }

    public void BeginPhaseAudio()
    {
        if (AudioManager.Instance == null)
        {
            Debug.LogWarning("[SceneAudioProfile] No AudioManager found.");
            return;
        }

        bool isPhase2 = PlayerPrefs.GetInt("SimulationMode", 0) == 1;

        AudioManager.Instance.PlayMusic(isPhase2 ? phase2Music : phase1Music);
        AudioManager.Instance.PlayAmbient(isPhase2 ? phase2Ambient : phase1Ambient);
    }
}