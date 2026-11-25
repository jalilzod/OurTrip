using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Firefly hunt, shared by both players.
/// Every time it becomes Night, the host picks a new random layout (a seed); both phones build the same
/// fireflies from that seed. Walk into a firefly to catch it; the shared counter goes up for both.
/// Catch all of them together -> a swirl of lights around both players and a message.
///
/// Setup: add to the WorldState object (has NetworkObject). Assign the Firefly prefab
/// (and optionally the Celebration prefab and a collect sound).
/// </summary>
public class FireflyHunt : NetworkBehaviour
{
    [SerializeField] GameObject fireflyPrefab;
    [Tooltip("Optional: effect spawned around each player when all fireflies are caught.")]
    [SerializeField] GameObject celebrationPrefab;
    [SerializeField] AudioClip collectSound;
    [Range(1, 30)][SerializeField] int count = 20;

    [Header("Where they appear")]
    [Tooltip("Center of the area (X, Z). Defaults to the spawn point.")]
    [SerializeField] Vector3 areaCenter = new Vector3(-78.46f, 0f, -124.17f);
    [SerializeField] float areaRadius = 35f;
    [SerializeField] float minHeight = 0.8f;
    [SerializeField] float maxHeight = 1.8f;
    [SerializeField] float collectDistance = 1.4f;

    readonly NetworkVariable<int> seed = new NetworkVariable<int>(0);
    readonly NetworkVariable<int> caughtMask = new NetworkVariable<int>(0); // bit i = firefly i caught

    readonly List<GameObject> flies = new List<GameObject>();
    readonly List<Vector3> basePos = new List<Vector3>();
    int builtSeed = -1;
    int localPendingMask;               // caught on this phone, waiting for the host to confirm
    WorldStateManager.TimeOfDay lastTime = WorldStateManager.TimeOfDay.Day;

    string message;
    float messageUntil;
    GUIStyle counterStyle, messageStyle;

    int AllMask => (1 << count) - 1;
    int CaughtCount => CountBits(caughtMask.Value | localPendingMask);

    static bool IsNight =>
        WorldStateManager.Instance != null &&
        WorldStateManager.Instance.CurrentTime == WorldStateManager.TimeOfDay.Night;

    public override void OnNetworkDespawn()
    {
        Clear();
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsServer) ServerCheckNightStart();

        if (builtSeed != seed.Value) Build(seed.Value);

        bool active = IsNight && seed.Value != 0;
        int caught = caughtMask.Value | localPendingMask;
        if ((caughtMask.Value & localPendingMask) == localPendingMask) localPendingMask = 0; // confirmed

        // Show / animate / collect
        Transform me = LocalPlayer();
        for (int i = 0; i < flies.Count; i++)
        {
            bool visible = active && (caught & (1 << i)) == 0;
            if (flies[i].activeSelf != visible) flies[i].SetActive(visible);
            if (!visible) continue;

            // Gentle floating
            float t = Time.time + i * 1.7f;
            flies[i].transform.position = basePos[i] + new Vector3(
                Mathf.Sin(t * 0.7f) * 0.6f, Mathf.Sin(t * 1.3f) * 0.25f, Mathf.Cos(t * 0.5f) * 0.6f);

            if (me != null && Vector3.Distance(me.position + Vector3.up, flies[i].transform.position) < collectDistance)
            {
                localPendingMask |= 1 << i;
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
            caughtMask.Value = 0;
            seed.Value = Random.Range(1, int.MaxValue); // new fireflies every night
        }
        lastTime = now;
    }

    [Rpc(SendTo.Server)]
    void CatchRpc(int index)
    {
        if (index < 0 || index >= count) return;
        int before = caughtMask.Value;
        caughtMask.Value = before | (1 << index);
        if (before != AllMask && caughtMask.Value == AllMask) CelebrateRpc();
    }

    // ---------- Everyone ----------
    [Rpc(SendTo.Everyone)]
    void CelebrateRpc()
    {
        message = "You caught all the fireflies together!";
        messageUntil = Time.time + 6f;

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
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float radius = Mathf.Sqrt((float)rng.NextDouble()) * areaRadius;
            Vector3 p = new Vector3(areaCenter.x + Mathf.Cos(angle) * radius, 0f, areaCenter.z + Mathf.Sin(angle) * radius);
            float ground = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;
            p.y = ground + minHeight + (float)rng.NextDouble() * (maxHeight - minHeight);

            var go = Instantiate(fireflyPrefab, p, Quaternion.identity, transform);
            go.SetActive(false);
            flies.Add(go);
            basePos.Add(p);
        }
    }

    void Clear()
    {
        foreach (var f in flies) if (f != null) Destroy(f);
        flies.Clear();
        basePos.Clear();
        localPendingMask = 0;
        builtSeed = -1;
    }

    Transform LocalPlayer()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null
            ? nm.LocalClient.PlayerObject.transform : null;
    }

    static int CountBits(int v)
    {
        int c = 0;
        while (v != 0) { c += v & 1; v >>= 1; }
        return c;
    }

    // ---------- Counter + message (top center) ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI || !IsSpawned) return;

        if (counterStyle == null)
        {
            counterStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
            messageStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }
        float u = Screen.height / 540f;
        counterStyle.fontSize = Mathf.RoundToInt(20 * u);
        messageStyle.fontSize = Mathf.RoundToInt(22 * u);
        GUI.matrix = Matrix4x4.identity;
        float sw = Screen.width;

        if (IsNight && seed.Value != 0)
        {
            int caught = Mathf.Min(CaughtCount, count);
            string text = caught >= count ? "All fireflies caught!" : "Fireflies  " + caught + " / " + count;
            GUI.Box(new Rect(sw * 0.5f - 130f * u, 10f * u, 260f * u, 40f * u), text, counterStyle);
        }

        if (message != null && Time.time < messageUntil)
            GUI.Box(new Rect(sw * 0.5f - 220f * u, 70f * u, 440f * u, 60f * u), message, messageStyle);
    }
}