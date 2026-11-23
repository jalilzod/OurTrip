using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shared music radio: either player picks a song, it plays for both from the same moment.
/// The host stores which song and when it started (in server time); every phone calculates
/// where in the song it should be, so you hear the same part even if you join later.
/// Volume is local to each phone (saved).
///
/// Setup: add this AND an AudioSource to the WorldState object (it already has a NetworkObject).
/// Fill the Tracks list with your songs.
/// </summary>
public class MusicRadio : NetworkBehaviour
{
    [Serializable]
    public class Track
    {
        public string title = "Song";
        public AudioClip clip;
    }

    const string VolumeKey = "musicVolume";

    [SerializeField] Track[] tracks;
    [SerializeField] AudioSource source;
    [Range(0f, 1f)] [SerializeField] float defaultVolume = 0.6f;
    [Tooltip("If the song drifts more than this many seconds from your partner, jump back in sync.")]
    [SerializeField] float resyncThreshold = 0.5f;

    readonly NetworkVariable<int> current = new NetworkVariable<int>(-1);       // -1 = off
    readonly NetworkVariable<double> startTime = new NetworkVariable<double>(0); // server time when it started

    bool panelOpen;
    float nextDriftCheck;

    float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, defaultVolume);
        set { PlayerPrefs.SetFloat(VolumeKey, value); if (source != null) source.volume = value; }
    }

    void Awake()
    {
        if (source == null) source = GetComponent<AudioSource>();
        if (source != null)
        {
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f; // 2D: same volume everywhere
            source.volume = Volume;
        }
    }

    public override void OnNetworkSpawn()
    {
        current.OnValueChanged += OnTrackChanged;
        startTime.OnValueChanged += OnStartChanged;
        Sync();
    }

    public override void OnNetworkDespawn()
    {
        current.OnValueChanged -= OnTrackChanged;
        startTime.OnValueChanged -= OnStartChanged;
        if (source != null) source.Stop();
    }

    void OnTrackChanged(int oldValue, int newValue) => Sync();
    void OnStartChanged(double oldValue, double newValue) => Sync();

    // ---------- Requests (from either player, run on the host) ----------
    public void Play(int index) { if (IsSpawned) PlayRpc(index); }
    public void Stop() { if (IsSpawned) PlayRpc(-1); }

    [Rpc(SendTo.Server)]
    void PlayRpc(int index)
    {
        int n = tracks != null ? tracks.Length : 0;
        startTime.Value = NetworkManager.ServerTime.Time;
        current.Value = (index < 0 || n == 0) ? -1 : ((index % n) + n) % n;
    }

    // ---------- Playback ----------
    AudioClip CurrentClip =>
        tracks != null && current.Value >= 0 && current.Value < tracks.Length ? tracks[current.Value].clip : null;

    float ExpectedTime(AudioClip clip)
    {
        double elapsed = NetworkManager.ServerTime.Time - startTime.Value;
        if (elapsed < 0) elapsed = 0;
        return (float)(elapsed % clip.length);
    }

    void Sync()
    {
        if (source == null) return;

        var clip = CurrentClip;
        if (clip == null) { source.Stop(); return; }

        if (source.clip != clip) source.clip = clip;
        if (!source.isPlaying) source.Play();
        source.time = ExpectedTime(clip);
    }

    void Update()
    {
        if (!IsSpawned || source == null || !source.isPlaying) return;
        if (Time.time < nextDriftCheck) return;
        nextDriftCheck = Time.time + 5f;

        var clip = CurrentClip;
        if (clip == null) return;

        float expected = ExpectedTime(clip);
        float diff = Mathf.Abs(source.time - expected);
        diff = Mathf.Min(diff, clip.length - diff); // around the loop point
        if (diff > resyncThreshold) source.time = expected;
    }

    // ---------- Temporary buttons (top-left, next to "World"). Replaced by designed UI later. ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient && !nm.IsServer) return;

        float s = Screen.height / 540f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        if (GUI.Button(new Rect(130f, 10f, 110f, 44f), panelOpen ? "Close" : "Music"))
            panelOpen = !panelOpen;
        if (!panelOpen) return;

        int n = tracks != null ? tracks.Length : 0;
        string title = current.Value >= 0 && current.Value < n ? tracks[current.Value].title : "(off)";

        GUILayout.BeginArea(new Rect(130f, 62f, 260f, 260f));
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.Label("Now playing: " + title);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", GUILayout.Height(40)) && n > 0)
            Play(current.Value < 0 ? n - 1 : current.Value - 1);
        if (GUILayout.Button(current.Value < 0 ? "Play" : "Stop", GUILayout.Height(40)))
        {
            if (current.Value < 0) Play(0); else Stop();
        }
        if (GUILayout.Button(">", GUILayout.Height(40)) && n > 0)
            Play(current.Value < 0 ? 0 : current.Value + 1);
        GUILayout.EndHorizontal();

        GUILayout.Label("My volume");
        float v = GUILayout.HorizontalSlider(Volume, 0f, 1f, GUILayout.Height(30));
        if (!Mathf.Approximately(v, Volume)) Volume = v;

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}