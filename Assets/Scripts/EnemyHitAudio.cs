using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class EnemyHitAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] AudioSource audioSource;

    [Header("Clips")]
    [SerializeField] AudioClip enemyHitClip;

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;
            audioSource.dopplerLevel = 0f;
        }
    }

    public void PlayHit()
    {
        if (audioSource == null || enemyHitClip == null)
            return;

        audioSource.PlayOneShot(enemyHitClip, GameRuntimeSettings.GetSfxBusVolume());
    }
}
