using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class BoatHitAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] BoatHealthController boatHealthController;
    [SerializeField] AudioSource audioSource;

    [Header("Clips")]
    [SerializeField] AudioClip[] boatHitClips;
    [FormerlySerializedAs("boatHitClip")]
    [SerializeField, HideInInspector] AudioClip legacyBoatHitClip;

    [Header("Playback")]
    [SerializeField] float retriggerCooldownSeconds = 0.12f;
    // Whirlpool damage ticks every physics frame; this longer cooldown keeps the
    // strain sound at a comfortable cadence instead of a machine-gun rattle.
    [SerializeField] float whirlpoolRetriggerCooldownSeconds = 0.7f;

    float nextPlayableTime;
    float whirlpoolNextPlayableTime;
    AudioClip[] shuffledBoatHitClips;
    int nextShuffledClipIndex;

    void Awake()
    {
        if (boatHealthController == null)
            boatHealthController = GetComponent<BoatHealthController>();
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

        retriggerCooldownSeconds = Mathf.Max(0f, retriggerCooldownSeconds);
        whirlpoolRetriggerCooldownSeconds = Mathf.Max(0f, whirlpoolRetriggerCooldownSeconds);
    }

    void OnEnable()
    {
        if (boatHealthController == null)
            boatHealthController = GetComponent<BoatHealthController>();

        if (boatHealthController != null)
            boatHealthController.OnDamagedWithSource += HandleBoatDamaged;
    }

    void OnDisable()
    {
        if (boatHealthController != null)
            boatHealthController.OnDamagedWithSource -= HandleBoatDamaged;
    }

    void HandleBoatDamaged(float damageAmount, BoatDamageSource damageSource)
    {
        if (damageSource == BoatDamageSource.HullWear)
            return;

        if (damageAmount <= 0f || audioSource == null)
            return;

        bool isWhirlpool = damageSource == BoatDamageSource.Whirlpool;
        if (Time.time < (isWhirlpool ? whirlpoolNextPlayableTime : nextPlayableTime))
            return;

        AudioClip clipToPlay = GetNextBoatHitClip();
        if (clipToPlay == null)
            return;

        if (isWhirlpool)
            whirlpoolNextPlayableTime = Time.time + whirlpoolRetriggerCooldownSeconds;
        else
            nextPlayableTime = Time.time + retriggerCooldownSeconds;

        audioSource.PlayOneShot(clipToPlay, GameRuntimeSettings.GetSfxBusVolume());
    }

    AudioClip GetNextBoatHitClip()
    {
        int validClipCount = CountValidBoatHitClips();
        if (validClipCount <= 0)
            return legacyBoatHitClip;

        if (validClipCount == 1)
            return GetSingleValidBoatHitClip();

        if (shuffledBoatHitClips == null || shuffledBoatHitClips.Length != validClipCount || nextShuffledClipIndex >= shuffledBoatHitClips.Length)
            RebuildShuffleBag(validClipCount);

        if (shuffledBoatHitClips == null || shuffledBoatHitClips.Length == 0)
            return null;

        return shuffledBoatHitClips[nextShuffledClipIndex++];
    }

    int CountValidBoatHitClips()
    {
        if (boatHitClips == null || boatHitClips.Length == 0)
            return 0;

        int count = 0;
        for (int i = 0; i < boatHitClips.Length; i++)
        {
            if (boatHitClips[i] != null)
                count++;
        }

        return count;
    }

    AudioClip GetSingleValidBoatHitClip()
    {
        if (boatHitClips == null)
            return null;

        for (int i = 0; i < boatHitClips.Length; i++)
        {
            if (boatHitClips[i] != null)
                return boatHitClips[i];
        }

        return null;
    }

    void RebuildShuffleBag(int validClipCount)
    {
        shuffledBoatHitClips = new AudioClip[validClipCount];

        int writeIndex = 0;
        for (int i = 0; i < boatHitClips.Length; i++)
        {
            if (boatHitClips[i] == null)
                continue;

            shuffledBoatHitClips[writeIndex++] = boatHitClips[i];
        }

        for (int i = shuffledBoatHitClips.Length - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            (shuffledBoatHitClips[i], shuffledBoatHitClips[swapIndex]) = (shuffledBoatHitClips[swapIndex], shuffledBoatHitClips[i]);
        }

        nextShuffledClipIndex = 0;
    }
}
