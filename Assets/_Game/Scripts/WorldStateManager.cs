using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shared world mood: time of day (Day / Sunset / Night) and rain.
/// Either player can change it; the host stores it and both phones see the same thing.
/// Changes blend smoothly (sun, colors, fog). The sky picture swaps instantly.
///
/// Setup: put this on an empty GameObject "WorldState" in the scene together with a NetworkObject.
/// Assign the Directional Light, the 3 presets (sky materials etc.) and the Rain particle system.
/// </summary>
public class WorldStateManager : NetworkBehaviour
{
    public enum TimeOfDay { Day = 0, Sunset = 1, Night = 2 }

    [Serializable]
    public class TimePreset
    {
        public string name = "Day";
        public Material skybox;
        [Tooltip("Rotation of the Directional Light (the sun or moon).")]
        public Vector3 sunRotation = new Vector3(50f, -30f, 0f);
        public Color sunColor = new Color(1f, 0.96f, 0.88f);
        public float sunIntensity = 1.2f;
        public Color fogColor = new Color(0.70f, 0.82f, 0.95f);
        [Tooltip("How bright the light from the sky is on everything (shadows side).")]
        public float ambientIntensity = 1f;
    }

    public static WorldStateManager Instance { get; private set; }

    /// <summary>Raised on every device when time or rain changes (used by music/sounds later).</summary>
    public event Action Changed;

    [SerializeField] Light sun;
    [SerializeField]
    TimePreset[] presets =
    {
        new TimePreset { name = "Day" },
        new TimePreset { name = "Sunset", sunRotation = new Vector3(8f, -60f, 0f),
            sunColor = new Color(1f, 0.55f, 0.30f), sunIntensity = 0.9f,
            fogColor = new Color(0.95f, 0.60f, 0.45f), ambientIntensity = 0.8f },
        new TimePreset { name = "Night", sunRotation = new Vector3(40f, 150f, 0f),
            sunColor = new Color(0.55f, 0.65f, 1f), sunIntensity = 0.25f,
            fogColor = new Color(0.08f, 0.10f, 0.18f), ambientIntensity = 0.4f },
    };

    [Header("Rain")]
    [SerializeField] ParticleSystem rain;
    [Tooltip("Rain clouds make the sun this much weaker (0.5 = half).")]
    [SerializeField] float rainLightMultiplier = 0.5f;
    [SerializeField] Color rainFogColor = new Color(0.50f, 0.55f, 0.60f);
    [Range(0f, 1f)][SerializeField] float rainFogBlend = 0.6f;
    [Tooltip("Rain spawns this high above the camera.")]
    [SerializeField] float rainHeight = 15f;

    [Header("Blending")]
    [SerializeField] float transitionSeconds = 2f;

    readonly NetworkVariable<int> timeOfDay = new NetworkVariable<int>(0);
    readonly NetworkVariable<bool> raining = new NetworkVariable<bool>(false);

    public TimeOfDay CurrentTime => (TimeOfDay)timeOfDay.Value;
    public bool IsRaining => raining.Value;

    // Current (blended) and target lighting values
    Quaternion curRot, tgtRot;
    Color curSunColor, tgtSunColor, curFog, tgtFog;
    float curIntensity, tgtIntensity, curAmbient, tgtAmbient;
    bool initialized;
    bool menuOpen;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Show the Day look already in the menu, before anyone connects.
        if (!IsSpawned) ApplyTargets(0, false, instant: true);
    }

    public override void OnNetworkSpawn()
    {
        timeOfDay.OnValueChanged += OnTimeChanged;
        raining.OnValueChanged += OnRainChanged;
        ApplyTargets(timeOfDay.Value, raining.Value, instant: true);
    }

    public override void OnNetworkDespawn()
    {
        timeOfDay.OnValueChanged -= OnTimeChanged;
        raining.OnValueChanged -= OnRainChanged;
    }

    void OnTimeChanged(int oldValue, int newValue) => ApplyTargets(newValue, raining.Value, instant: false);
    void OnRainChanged(bool oldValue, bool newValue) => ApplyTargets(timeOfDay.Value, newValue, instant: false);

    // ---------- Requests from either player (sent to the host) ----------
    public void RequestTime(TimeOfDay t) { if (IsSpawned) SetTimeRpc((int)t); }
    public void RequestRain(bool on) { if (IsSpawned) SetRainRpc(on); }

    [Rpc(SendTo.Server)]
    void SetTimeRpc(int t) => timeOfDay.Value = Mathf.Clamp(t, 0, presets.Length - 1);

    [Rpc(SendTo.Server)]
    void SetRainRpc(bool on) => raining.Value = on;

    // ---------- Applying the look ----------
    void ApplyTargets(int timeIndex, bool isRaining, bool instant)
    {
        if (presets == null || presets.Length == 0) return;
        var p = presets[Mathf.Clamp(timeIndex, 0, presets.Length - 1)];

        if (p.skybox != null)
        {
            RenderSettings.skybox = p.skybox;
            DynamicGI.UpdateEnvironment(); // refresh sky lighting for the new sky
        }

        tgtRot = Quaternion.Euler(p.sunRotation);
        tgtSunColor = p.sunColor;
        tgtIntensity = p.sunIntensity * (isRaining ? rainLightMultiplier : 1f);
        tgtFog = isRaining ? Color.Lerp(p.fogColor, rainFogColor, rainFogBlend) : p.fogColor;
        tgtAmbient = p.ambientIntensity * (isRaining ? 0.8f : 1f);

        if (instant || !initialized)
        {
            curRot = tgtRot; curSunColor = tgtSunColor; curIntensity = tgtIntensity;
            curFog = tgtFog; curAmbient = tgtAmbient;
            initialized = true;
            PushToScene();
        }

        if (rain != null)
        {
            if (isRaining && !rain.isPlaying) rain.Play();
            else if (!isRaining && rain.isPlaying) rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        Changed?.Invoke();
    }

    void Update()
    {
        if (!initialized) return;

        float k = transitionSeconds <= 0f ? 1f : 1f - Mathf.Exp(-4f * Time.deltaTime / transitionSeconds);
        curRot = Quaternion.Slerp(curRot, tgtRot, k);
        curSunColor = Color.Lerp(curSunColor, tgtSunColor, k);
        curIntensity = Mathf.Lerp(curIntensity, tgtIntensity, k);
        curFog = Color.Lerp(curFog, tgtFog, k);
        curAmbient = Mathf.Lerp(curAmbient, tgtAmbient, k);
        PushToScene();
    }

    void LateUpdate()
    {
        // Rain follows the camera so it's always raining around you, wherever you walk.
        if (rain != null && Camera.main != null)
            rain.transform.position = Camera.main.transform.position + Vector3.up * rainHeight;
    }

    void PushToScene()
    {
        if (sun != null)
        {
            sun.transform.rotation = curRot;
            sun.color = curSunColor;
            sun.intensity = curIntensity;
        }
        RenderSettings.fogColor = curFog;
        RenderSettings.ambientIntensity = curAmbient;
    }

    // ---------- Temporary buttons (top-left). Replaced by designed UI later. ----------
    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient && !nm.IsServer) return;

        float s = Screen.height / 540f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        if (GUI.Button(new Rect(10f, 10f, 110f, 44f), menuOpen ? "Close" : "World"))
            menuOpen = !menuOpen;
        if (!menuOpen) return;

        float y = 62f;
        for (int i = 0; i < presets.Length; i++)
        {
            string label = presets[i].name + (timeOfDay.Value == i ? "  *" : "");
            if (GUI.Button(new Rect(10f, y, 110f, 44f), label)) RequestTime((TimeOfDay)i);
            y += 50f;
        }
        if (GUI.Button(new Rect(10f, y, 110f, 44f), raining.Value ? "Rain: ON" : "Rain: OFF"))
            RequestRain(!raining.Value);
    }
}