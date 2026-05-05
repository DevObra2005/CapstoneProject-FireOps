using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip clickSound;

    public void PlayClick()
    {
        audioSource.PlayOneShot(clickSound);
    }
}