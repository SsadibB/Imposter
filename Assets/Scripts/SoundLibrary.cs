using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AudioEntry
{
    [Tooltip("Unique name or identifier for this audio entry")]
    public string id = "NewAudio";

    [Tooltip("The audio clip asset")]
    public AudioClip clip;

    [Range(0f, 1f), Tooltip("Volume level for this track/effect")]
    public float volume = 1f;

    [Tooltip("If enabled, automatically repeats after finishing. If disabled, plays only once.")]
    public bool loop = false;

    [Range(0.5f, 2f), Tooltip("Playback pitch multiplier")]
    public float pitch = 1f;

    public AudioEntry() { }

    public AudioEntry(string id, AudioClip clip, bool loop = false, float volume = 1f, float pitch = 1f)
    {
        this.id = id;
        this.clip = clip;
        this.loop = loop;
        this.volume = volume;
        this.pitch = pitch;
    }
}

public class SoundLibrary : MonoBehaviour
{
    public static SoundLibrary Instance { get; private set; }

    [Header("Volume Controls")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("=== MUSIC LIBRARY ===")]
    [Tooltip("Unlimited music tracks with individual Loop setting")]
    [SerializeField]
    public List<AudioEntry> music = new List<AudioEntry>()
    {
        new AudioEntry("Main_BGM", null, true, 0.8f),
        new AudioEntry("Tension_BGM", null, true, 0.8f)
    };

    [Header("=== SFX LIBRARY ===")]
    [Tooltip("Unlimited sound effects with individual Loop setting")]
    [SerializeField]
    public List<AudioEntry> sfx = new List<AudioEntry>()
    {
        new AudioEntry("Gun_Shot", null, false, 1f),
        new AudioEntry("Sheep_Burn", null, false, 0.9f),
        new AudioEntry("Sheep_Bleat", null, false, 0.8f),
        new AudioEntry("Alert_Lock", null, false, 0.7f),
        new AudioEntry("Victory_Fanfare", null, false, 1f),
        new AudioEntry("Defeat_Jingle", null, false, 1f)
    };

    private AudioSource _musicSource;
    private List<AudioSource> _sfxPool = new List<AudioSource>();
    private Dictionary<string, AudioSource> _loopingSfxSources = new Dictionary<string, AudioSource>();
    private const int InitialSfxPoolSize = 6;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetupAudioSources();
    }

    private void SetupAudioSources()
    {
        // 1. Dedicated Music AudioSource
        if (_musicSource == null)
        {
            GameObject mObj = new GameObject("Music_AudioSource");
            mObj.transform.SetParent(transform, false);
            _musicSource = mObj.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
        }

        // 2. Pre-allocated SFX pool
        for (int i = 0; i < InitialSfxPoolSize; i++)
        {
            CreatePooledSfxSource();
        }
    }

    private AudioSource CreatePooledSfxSource()
    {
        GameObject sfxObj = new GameObject("SFX_Source_" + _sfxPool.Count);
        sfxObj.transform.SetParent(transform, false);
        AudioSource src = sfxObj.AddComponent<AudioSource>();
        src.playOnAwake = false;
        _sfxPool.Add(src);
        return src;
    }

    private AudioSource GetAvailableSfxSource()
    {
        for (int i = 0; i < _sfxPool.Count; i++)
        {
            if (_sfxPool[i] != null && !_sfxPool[i].isPlaying && !_sfxPool[i].loop)
            {
                return _sfxPool[i];
            }
        }
        return CreatePooledSfxSource();
    }

    // ==================== MUSIC METHODS ====================

    public void PlayMusic(string id, bool? loopOverride = null)
    {
        AudioEntry entry = GetMusicEntry(id);
        if (entry == null)
        {
            Debug.LogWarning($"[SoundLibrary] Music entry '{id}' not found.");
            return;
        }

        if (entry.clip == null)
        {
            Debug.Log($"[SoundLibrary] Music '{id}' has no AudioClip assigned.");
            return;
        }

        if (_musicSource == null) SetupAudioSources();

        _musicSource.clip = entry.clip;
        _musicSource.loop = loopOverride.HasValue ? loopOverride.Value : entry.loop;
        _musicSource.volume = entry.volume * musicVolume * masterVolume;
        _musicSource.pitch = entry.pitch;
        _musicSource.Play();
    }

    public void PlayMusic(int index, bool? loopOverride = null)
    {
        if (index < 0 || index >= music.Count)
        {
            Debug.LogWarning($"[SoundLibrary] Music index {index} is out of bounds.");
            return;
        }
        PlayMusic(music[index].id, loopOverride);
    }

    public void StopMusic()
    {
        if (_musicSource != null)
        {
            _musicSource.Stop();
        }
    }

    public void PauseMusic()
    {
        if (_musicSource != null && _musicSource.isPlaying)
        {
            _musicSource.Pause();
        }
    }

    public void ResumeMusic()
    {
        if (_musicSource != null && !_musicSource.isPlaying)
        {
            _musicSource.UnPause();
        }
    }

    public bool IsMusicPlaying => _musicSource != null && _musicSource.isPlaying;

    // ==================== SFX METHODS ====================

    public AudioSource PlaySFX(string id, bool? loopOverride = null)
    {
        AudioEntry entry = GetSFXEntry(id);
        if (entry == null)
        {
            Debug.LogWarning($"[SoundLibrary] SFX entry '{id}' not found.");
            return null;
        }

        if (entry.clip == null)
        {
            Debug.Log($"[SoundLibrary] SFX '{id}' has no AudioClip assigned.");
            return null;
        }

        bool shouldLoop = loopOverride.HasValue ? loopOverride.Value : entry.loop;

        if (shouldLoop)
        {
            // If already looping this sound, don't duplicate
            if (_loopingSfxSources.TryGetValue(id, out AudioSource existingSrc) && existingSrc != null && existingSrc.isPlaying)
            {
                return existingSrc;
            }

            AudioSource loopSrc = GetAvailableSfxSource();
            loopSrc.clip = entry.clip;
            loopSrc.loop = true;
            loopSrc.volume = entry.volume * sfxVolume * masterVolume;
            loopSrc.pitch = entry.pitch;
            loopSrc.Play();
            _loopingSfxSources[id] = loopSrc;
            return loopSrc;
        }
        else
        {
            AudioSource src = GetAvailableSfxSource();
            src.clip = entry.clip;
            src.loop = false;
            src.volume = entry.volume * sfxVolume * masterVolume;
            src.pitch = entry.pitch;
            src.Play();
            return src;
        }
    }

    public AudioSource PlaySFX(int index, bool? loopOverride = null)
    {
        if (index < 0 || index >= sfx.Count)
        {
            Debug.LogWarning($"[SoundLibrary] SFX index {index} is out of bounds.");
            return null;
        }
        return PlaySFX(sfx[index].id, loopOverride);
    }

    public void StopSFX(string id)
    {
        if (_loopingSfxSources.TryGetValue(id, out AudioSource src) && src != null)
        {
            src.Stop();
            src.loop = false;
            _loopingSfxSources.Remove(id);
        }
    }

    public void StopAllSFX()
    {
        foreach (var kvp in _loopingSfxSources)
        {
            if (kvp.Value != null)
            {
                kvp.Value.Stop();
                kvp.Value.loop = false;
            }
        }
        _loopingSfxSources.Clear();

        for (int i = 0; i < _sfxPool.Count; i++)
        {
            if (_sfxPool[i] != null)
            {
                _sfxPool[i].Stop();
                _sfxPool[i].loop = false;
            }
        }
    }

    // ==================== LOOKUP & MANAGEMENT ====================

    public AudioEntry GetMusicEntry(string id)
    {
        for (int i = 0; i < music.Count; i++)
        {
            if (music[i] != null && string.Equals(music[i].id, id, System.StringComparison.OrdinalIgnoreCase))
                return music[i];
        }
        return null;
    }

    public AudioEntry GetSFXEntry(string id)
    {
        for (int i = 0; i < sfx.Count; i++)
        {
            if (sfx[i] != null && string.Equals(sfx[i].id, id, System.StringComparison.OrdinalIgnoreCase))
                return sfx[i];
        }
        return null;
    }

    public void AddMusic(string id, AudioClip clip, bool loop = true, float volume = 1f)
    {
        music.Add(new AudioEntry(id, clip, loop, volume));
    }

    public void AddSFX(string id, AudioClip clip, bool loop = false, float volume = 1f)
    {
        sfx.Add(new AudioEntry(id, clip, loop, volume));
    }

    public void SetMasterVolume(float vol)
    {
        masterVolume = Mathf.Clamp01(vol);
        UpdateVolumeLevels();
    }

    public void SetMusicVolume(float vol)
    {
        musicVolume = Mathf.Clamp01(vol);
        UpdateVolumeLevels();
    }

    public void SetSFXVolume(float vol)
    {
        sfxVolume = Mathf.Clamp01(vol);
        UpdateVolumeLevels();
    }

    private void UpdateVolumeLevels()
    {
        if (_musicSource != null && _musicSource.isPlaying)
        {
            AudioEntry entry = GetMusicEntry(_musicSource.clip ? _musicSource.clip.name : "");
            float baseVol = entry != null ? entry.volume : 1f;
            _musicSource.volume = baseVol * musicVolume * masterVolume;
        }

        foreach (var kvp in _loopingSfxSources)
        {
            if (kvp.Value != null && kvp.Value.isPlaying)
            {
                AudioEntry entry = GetSFXEntry(kvp.Key);
                float baseVol = entry != null ? entry.volume : 1f;
                kvp.Value.volume = baseVol * sfxVolume * masterVolume;
            }
        }
    }
}
