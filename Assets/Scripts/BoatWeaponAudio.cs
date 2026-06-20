using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BoatWeaponAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] AudioSource gunAudioSource;
    [SerializeField] AudioSource cannonAudioSource;
    [SerializeField] AudioSource noAmmoAudioSource;

    [Header("Clips")]
    [SerializeField] AudioClip gunSound;
    [SerializeField] AudioClip cannonSound;
    [SerializeField] AudioClip noAmmoClick;

    [Header("Gunfire")]
    [SerializeField] float gunVolume = 1f;
    [SerializeField] float gunPitchJitter = 0.025f;

    [Header("Cannonfire")]
    [SerializeField] float cannonVolume = 1f;
    [SerializeField] float cannonPitchJitter = 0.025f;
    [SerializeField] float doubleBroadsideDelaySeconds = 0.045f;
    [SerializeField] float doubleBroadsideSecondVolume = 0.92f;

    [Header("No Ammo")]
    [SerializeField] float noAmmoVolume = 1f;
    [SerializeField] float noAmmoCooldownSeconds = 0.15f;

    float nextNoAmmoPlayableTime;

    void Awake()
    {
        ConfigureAudioSource(gunAudioSource);
        ConfigureAudioSource(cannonAudioSource);
        ConfigureAudioSource(noAmmoAudioSource);
        gunVolume = Mathf.Max(0f, gunVolume);
        cannonVolume = Mathf.Max(0f, cannonVolume);
        noAmmoVolume = Mathf.Max(0f, noAmmoVolume);
        noAmmoCooldownSeconds = Mathf.Max(0f, noAmmoCooldownSeconds);
        doubleBroadsideDelaySeconds = Mathf.Max(0f, doubleBroadsideDelaySeconds);
        doubleBroadsideSecondVolume = Mathf.Clamp01(doubleBroadsideSecondVolume);
    }

    public void PlayGunFire()
    {
        PlayOneShotWithPitch(gunAudioSource, gunSound, gunVolume, gunPitchJitter);
    }

    public void PlayCannonFire(bool doubleBroadside)
    {
        if (!PlayOneShotWithPitch(cannonAudioSource, cannonSound, cannonVolume, cannonPitchJitter))
            return;

        if (doubleBroadside)
            StartCoroutine(PlayDelayedCannonLayer());
    }

    public void PlayNoAmmoClick()
    {
        if (Time.time < nextNoAmmoPlayableTime)
            return;

        if (!PlayOneShotWithPitch(noAmmoAudioSource, noAmmoClick, noAmmoVolume, 0f))
            return;

        nextNoAmmoPlayableTime = Time.time + noAmmoCooldownSeconds;
    }

    IEnumerator PlayDelayedCannonLayer()
    {
        if (doubleBroadsideDelaySeconds > 0f)
            yield return new WaitForSeconds(doubleBroadsideDelaySeconds);

        PlayOneShotWithPitch(cannonAudioSource, cannonSound, cannonVolume * doubleBroadsideSecondVolume, cannonPitchJitter);
    }

    bool PlayOneShotWithPitch(AudioSource source, AudioClip clip, float volumeScale, float pitchJitter)
    {
        if (source == null || clip == null)
            return false;

        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.PlayOneShot(clip, volumeScale * GameRuntimeSettings.GetSfxBusVolume());
        return true;
    }

    static void ConfigureAudioSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
    }
}
