using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Firefly hunt, shared by both players.
/// - Every time it becomes Night, the host picks a new random layout (a seed); both phones build the same
///   fireflies from that seed, in little groups spread over the map.
/// - Walk into a firefly to catch it. The shared counter goes up for both of you.
/// - A caught firefly comes back after 10-15 seconds, so there are always plenty around.
/// - Catch "Goal" fireflies together -> a swirl of lights around both players and a message.
/// Fireflies only appear on the terrain, above the water.
///
/// Setup: add to the WorldState object (has NetworkObject). Assign the Firefly prefab
/// (and optionally the Celebration prefab, a collect sound and the Sea object).
/// </summary>
public class FireflyHunt : NetworkBehaviour
{
    [SerializeField] GameObject fireflyPrefab;
    [Tooltip("Optional: effect spawned around each player when the goal is reached.")]
    [SerializeField] GameObject celebrationPrefab;
    [SerializeField] AudioClip collectSound;

    [Header("How many")]
    [Tooltip("How many fireflies are in the world each night.")]
    [Range(1, 300)] [SerializeField] int count = 150;
    [Tooltip("Fireflies come in little groups of this size (1 = all spread out).")]
    [Range(1, 8)] [SerializeField] int groupSize = 4;
    [Tooltip("How many you need to catch together to win.")]
    [SerializeField] int goal = 20;
    [Tooltip("A caught firefly comes back after a random time between these seconds.")]
    [SerializeField] Vector2 respawnSeconds = new Vector2(10f, 15f);

    [Header("Where they appear")]
    [Tooltip("On: spread over the whole terrain. Off: only within Area Radius around Area Center.")]
    [SerializeField] bool wholeTerrain = true;
    [Tooltip("Center of the area (X, Z), used when Whole Terrain is off.")]
    [SerializeField] Vector3 areaCenter = new Vector3(-78.46f, 0f, -124.17f);
    [SerializeField] float areaRadius = 50f;
    [Tooltip("Keep this far away from the edge of the terrain.")]
    [SerializeField] float edgeMargin = 5f;
    [Tooltip("Optional: the Sea object. Fireflies won't appear where the ground is below the water.")]
    [SerializeField] Transform sea;
    [Tooltip("How far apart the fireflies in one group are.")]
    [SerializeField] float groupSpread = 3f;
    [SerializeField] float minHeight = 0.8f;
    [SerializeField] float maxHeight = 1.8f;
    [SerializeField] float collectDistance = 1.6f;

    readonly NetworkVariable<int> seed = new NetworkVariable<int>(0);
    readonly NetworkVariable<int> caughtTotal = new NetworkVariable<int>(0);
    NetworkList<byte> hidden; // 1 = caught and waiting to come back

    readonly List<GameObject> flies = new List<GameObject>();
    readonly List<Vector3> basePos = new List<Vector3>();
    readonly Dictionary<int, float> localPending = new Dictionary<int, float>(); // index -> time caught here
    readonly Dictionary<int, float> respawnAt = new Dictionary<int, float>();    // host only
    readonly List<int> respawnDue = new List<int>();
    int builtSeed = -1;
    WorldStateManager.TimeOfDay lastTime = WorldStateManager.TimeOfDay.Day;


    static bool IsNight =>
        WorldStateManager.Instance != null &&
        WorldStateManager.Instance.CurrentTime == WorldStateManager.TimeOfDay.Night;

    void Awake()
    {
        hidden = new NetworkList<byte>();
    }

    public override void OnNetworkDespawn()
    {
        Clear();
    }

    bool IsHidden(int i) =>
        localPending.ContainsKey(i) || (hidden != null && i < hidden.Count && hidden[i] != 0);

    void Update()
    {
        if (!IsSpawned) return;

        if (IsServer)
        {
            ServerCheckNightStart();
            ServerRespawn();
        }

        if (builtSeed != seed.Value) Build(seed.Value);

        // Forget local catches once the host confirmed them (or after 2 s if something went wrong).
        if (localPending.Count > 0)
        {
            respawnDue.Clear();
            foreach (var kv in localPending)
            {
                bool confirmed = hidden != null && kv.Key < hidden.Count && hidden[kv.Key] != 0;
                if (confirmed || Time.time - kv.Value > 2f) respawnDue.Add(kv.Key);
            }
            foreach (var i in respawnDue) localPending.Remove(i);
        }

        bool active = IsNight && seed.Value != 0;
        Transform me = LocalPlayer();
        for (int i = 0; i < flies.Count; i++)
        {
            bool visible = active && !IsHidden(i);
            if (flies[i].activeSelf != visible) flies[i].SetActive(visible);
            if (!visible) continue;

            // Gentle floating
            float t = Time.time + i * 1.7f;
            flies[i].transform.position = basePos[i] + new Vector3(
                Mathf.Sin(t * 0.7f) * 0.6f, Mathf.Sin(t * 1.3f) * 0.25f, Mathf.Cos(t * 0.5f) * 0.6f);

            if (me != null && Vector3.Distance(me.position + Vector3.up, flies[i].transform.position) < collectDistance)
            {
                localPending[i] = Time.time;
                flies[i].SetActive(false);
                if (collectSound != null) AudioSource.PlayClipAtPoint(collectSound, flies[i].transform.position, 0.7f);
                CatchRpc(i);
            }
        }
    }

    // ---------- Host ----------
    void ServerCheckNightStart()
    {
        var world = WorldStateManager.Instance;
        if (world == null) return;
        var now = world.CurrentTime;
        if (now == WorldStateManager.TimeOfDay.Night && lastTime != WorldStateManager.TimeOfDay.Night)
        {
            respawnAt.Clear();
            hidden.Clear();
            for (int i = 0; i < count; i++) hidden.Add(0);
            caughtTotal.Value = 0;
            seed.Value = Random.Range(1, int.MaxValue); // new fireflies every night
        }
        lastTime = now;
    }

    void ServerRespawn()
    {
        if (respawnAt.Count == 0) return;
        respawnDue.Clear();
        foreach (var kv in respawnAt)
            if (Time.time >= kv.Value) respawnDue.Add(kv.Key);
        foreach (var i in respawnDue)
        {
            respawnAt.Remove(i);
            if (i < hidden.Count) hidden[i] = 0;
        }
    }

    [Rpc(SendTo.Server)]
    void CatchRpc(int index)
    {
        if (index < 0 || index >= hidden.Count || hidden[index] != 0) return;

        hidden[index] = 1;
        respawnAt[index] = Time.time + Random.Range(respawnSeconds.x, respawnSeconds.y);

        int before = caughtTotal.Value;
        caughtTotal.Value = before + 1;
        if (before < goal && caughtTotal.Value >= goal) CelebrateRpc();
    }

    // ---------- Everyone ----------
    [Rpc(SendTo.Everyone)]
    void CelebrateRpc()
    {
        UiTheme.Toast(Loc.T("You caught the fireflies together!"), 6f);

        if (celebrationPrefab == null) return;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            var fx = Instantiate(celebrationPrefab, pc.transform);
            fx.transform.localPosition = Vector3.up * 1f;
            Destroy(fx, 10f);
        }
    }

    void Build(int s)
    {
        Clear();
        builtSeed = s;
        if (s == 0 || fireflyPrefab == null) return;

        var rng = new System.Random(s); // same seed -> same positions on both phones
        var terrain = Terrain.activeTerrain;
        float waterY = sea != null ? sea.position.y : float.MinValue;
        int size = Mathf.Max(1, groupSize);

        Vector3 groupCenter = areaCenter;
        for (int i = 0; i < count; i++)
        {
            // New group: find a valid spot on the map.
            if (i % size == 0) groupCenter = RandomSpot(rng, terrain, waterY);

            // Member of the group: a valid spot near the group center.
            Vector3 p = groupCenter;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var c = groupCenter + new Vector3(
                    ((float)rng.NextDouble() * 2f - 1f) * groupSpread, 0f,
                    ((float)rng.NextDouble() * 2f - 1f) * groupSpread);
                if (Valid(terrain, c, waterY)) { p = c; break; }
            }
            p.y = Ground(terrain, p) + minHeight + (float)rng.NextDouble() * (maxHeight - minHeight);

            var go = Instantiate(fireflyPrefab, p, Quaternion.identity, transform);
            go.SetActive(false);
            flies.Add(go);
            basePos.Add(p);
        }
    }

    Vector3 RandomSpot(System.Random rng, Terrain terrain, float waterY)
    {
        Vector3 candidate = areaCenter;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (wholeTerrain && terrain != null)
            {
                Vector3 min = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                candidate = new Vector3(
                    min.x + edgeMargin + (float)rng.NextDouble() * (size.x - 2f * edgeMargin), 0f,
                    min.z + edgeMargin + (float)rng.NextDouble() * (size.z - 2f * edgeMargin));
            }
            else
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float radius = Mathf.Sqrt((float)rng.NextDouble()) * areaRadius;
                candidate = new Vector3(areaCenter.x + Mathf.Cos(angle) * radius, 0f,
                                        areaCenter.z + Mathf.Sin(angle) * radius);
            }
            if (Valid(terrain, candidate, waterY)) return candidate;
        }
        return candidate;
    }

    bool Valid(Terrain terrain, Vector3 p, float waterY)
    {
        if (terrain != null)
        {
            Vector3 min = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (p.x < min.x + edgeMargin || p.x > min.x + size.x - edgeMargin ||
                p.z < min.z + edgeMargin || p.z > min.z + size.z - edgeMargin) return false;
        }
        return Ground(terrain, p) >= waterY + 0.3f;
    }

    static float Ground(Terrain terrain, Vector3 p) =>
        terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;

    void Clear()
    {
        foreach (var f in flies) if (f != null) Destroy(f);
        flies.Clear();
        basePos.Clear();
        localPending.Clear();
        builtSeed = -1;
    }

    Transform LocalPlayer()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null
            ? nm.LocalClient.PlayerObject.transform : null;
    }

    // ---------- Counter + message (top center) ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI || !IsSpawned) return;
        if (!IsNight || seed.Value == 0) return;
        UiTheme.Begin();
        int caught = caughtTotal.Value + localPending.Count;
        UiTheme.PillLabel(UiTheme.CounterRect(0),
            caught >= goal ? Loc.F("Fireflies caught: {0}  <3", caught) : Loc.F("Fireflies  {0} / {1}", caught, goal));
    }
}