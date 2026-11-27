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
        if (!UiTheme.MenuOpen) return;
        UiTheme.Begin();
        float u = UiTheme.U;

        bool open = UiTheme.OpenPanel == "music";
        if (UiTheme.RoundButton(UiTheme.MenuSlot(1), "music", open)) UiTheme.OpenPanel = open ? null : "music";
        if (!open) return;

        int n = tracks != null ? tracks.Length : 0;
        string title = current.Value >= 0 && current.Value < n ? tracks[current.Value].title : Loc.T("Nothing playing");

        var p = UiTheme.MenuPanel(1, 300f, 214f);
        UiTheme.PanelBox(p);
        float x = p.x + 18f * u, w = p.width - 36f * u;

        GUI.Label(new Rect(x, p.y + 14f * u, w, 18f * u), Loc.T("NOW PLAYING  ·  FOR BOTH OF US"), UiTheme.Caption);
        GUI.Label(new Rect(x, p.y + 34f * u, w, 30f * u), title, UiTheme.Title);

        float size = 58f * u, gap = 18f * u, rowX = p.center.x - (size * 3f + gap * 2f) * 0.5f, rowY = p.y + 72f * u;
        if (UiTheme.RoundButton(new Rect(rowX, rowY, size, size), "prev", false, n > 0))
            Play(current.Value < 0 ? n - 1 : current.Value - 1);
        bool playing = current.Value >= 0;
        if (UiTheme.RoundButton(new Rect(rowX + size + gap, rowY, size, size), playing ? "stop" : "play", true, n > 0))
        {
            if (playing) Stop(); else Play(0);
        }
        if (UiTheme.RoundButton(new Rect(rowX + 2f * (size + gap), rowY, size, size), "next", false, n > 0))
            Play(current.Value < 0 ? 0 : current.Value + 1);

        var vol = UiTheme.Icon("volume");
        if (vol != null) GUI.DrawTexture(new Rect(x, p.y + 158f * u, 26f * u, 26f * u), vol, ScaleMode.ScaleToFit);
        float v = GUI.HorizontalSlider(new Rect(x + 36f * u, p.y + 166f * u, w - 36f * u, 20f * u), Volume, 0f, 1f);
        if (!Mathf.Approximately(v, Volume)) Volume = v;
        GUI.Label(new Rect(x, p.yMax - 22f * u, w, 18f * u), Loc.T("Volume is only for your phone"), UiTheme.Small);
    }
}