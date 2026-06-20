using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class UIAudioController : MonoBehaviour
{
    public static UIAudioController ActiveInstance { get; private set; }

    [Header("References")]
    [SerializeField] AudioSource uiAudioSource;

    [Header("Clips")]
    [SerializeField] AudioClip buttonClickClip;
    [SerializeField] AudioClip inventoryClickClip;
    [SerializeField] AudioClip inventoryOpenSound;
    [SerializeField] AudioClip fishingStartSound;
    [SerializeField] AudioClip eatSound;

    void Awake()
    {
        if (uiAudioSource == null)
            uiAudioSource = GetComponent<AudioSource>();
        if (uiAudioSource == null)
            uiAudioSource = gameObject.AddComponent<AudioSource>();

        if (uiAudioSource != null)
        {
            uiAudioSource.playOnAwake = false;
            uiAudioSource.loop = false;
            uiAudioSource.spatialBlend = 0f;
        }
    }

    void OnEnable()
    {
        ActiveInstance = this;
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    public void PlayButtonClick()
    {
        if (uiAudioSource == null || buttonClickClip == null)
            return;

        uiAudioSource.PlayOneShot(buttonClickClip, GameRuntimeSettings.GetSfxBusVolume());
    }

    public void PlayInventoryClick()
    {
        if (uiAudioSource == null || inventoryClickClip == null)
            return;

        uiAudioSource.PlayOneShot(inventoryClickClip, GameRuntimeSettings.GetSfxBusVolume());
    }

    public void PlayInventoryOpenSound()
    {
        if (uiAudioSource == null || inventoryOpenSound == null)
            return;

        uiAudioSource.PlayOneShot(inventoryOpenSound, GameRuntimeSettings.GetSfxBusVolume());
    }

    public void PlayFishingStartSound()
    {
        if (uiAudioSource == null || fishingStartSound == null)
            return;

        uiAudioSource.PlayOneShot(fishingStartSound, GameRuntimeSettings.GetSfxBusVolume());
    }

    public void PlayEatSound()
    {
        if (uiAudioSource == null || eatSound == null)
            return;

        uiAudioSource.PlayOneShot(eatSound, GameRuntimeSettings.GetSfxBusVolume());
    }
}
