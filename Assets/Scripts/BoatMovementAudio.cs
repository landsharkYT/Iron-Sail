using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoatController))]
public class BoatMovementAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] BoatController boatController;
    [SerializeField] AudioSource rowingAudioSource;

    [Header("Clips")]
    [SerializeField] AudioClip boatRowClip;

    [Header("Playback")]
    [SerializeField] float rowVolume = 1f;

    bool warnedMissingClip;

    void Awake()
    {
        if (boatController == null)
            boatController = GetComponent<BoatController>();
        if (rowingAudioSource == null)
            rowingAudioSource = gameObject.AddComponent<AudioSource>();

        ConfigureAudioSource(rowingAudioSource);
        ApplyPlaybackSettings();
    }

    void OnEnable()
    {
        GameRuntimeSettings.SettingsChanged += HandleSettingsChanged;
        ApplyPlaybackSettings();
    }

    void OnDisable()
    {
        GameRuntimeSettings.SettingsChanged -= HandleSettingsChanged;
    }

    void Update()
    {
        bool shouldPlay = boatController != null
            && boatController.IsPaddlingBackward
            && boatRowClip != null;

        if (!shouldPlay)
        {
            if (!warnedMissingClip && boatController != null && boatController.IsPaddlingBackward && boatRowClip == null)
            {
                Debug.LogWarning("[BoatMovementAudio] boatRowClip is not assigned, so backward rowing audio cannot play.", this);
                warnedMissingClip = true;
            }

            if (rowingAudioSource != null && rowingAudioSource.isPlaying)
                rowingAudioSource.Stop();

            return;
        }

        if (rowingAudioSource.clip != boatRowClip)
            rowingAudioSource.clip = boatRowClip;

        if (!rowingAudioSource.isPlaying)
            rowingAudioSource.Play();
    }

    void OnValidate()
    {
        rowVolume = Mathf.Max(0f, rowVolume);
        ApplyPlaybackSettings();
    }

    void ApplyPlaybackSettings()
    {
        if (rowingAudioSource == null)
            return;

        rowingAudioSource.volume = rowVolume * GameRuntimeSettings.GetSfxBusVolume();
    }

    void HandleSettingsChanged()
    {
        ApplyPlaybackSettings();
    }

    static void ConfigureAudioSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
    }
}
