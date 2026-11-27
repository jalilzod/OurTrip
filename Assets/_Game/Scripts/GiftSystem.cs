using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gifts: pick flowers in the meadow and seashells on the beach (walk into them), then give them
/// to your partner when you're close. A bouquet costs 3 flowers, a seashell costs 1.
/// The gift appears in your partner's right hand on both phones, with hearts.
/// Picked flowers/shells grow back after a while. Your collection is saved on your phone.
///
/// Setup: add to the WorldState object (has NetworkObject). Assign the prefabs and the Sea.
/// </summary>
public class GiftSystem : NetworkBehaviour
{
    public enum GiftType { Bouquet = 0, Shell = 1 }

    [Header("Things to pick up")]
    [SerializeField] GameObject flowerPrefab;   // one small flower on the ground
    [SerializeField] GameObject shellPrefab;    // one seashell on the sand
    [SerializeField] int flowerCount = 40;
    [SerializeField] int shellCount = 25;
    [Tooltip("Flowers appear within this distance of Area Center (X, Z).")]
    [SerializeField] Vector3 areaCenter = new Vector3(-78.46f, 0f, -124.17f);
    [SerializeField] float flowerRadius = 60f;
    [Tooltip("The Sea object: shells appear on the sand just above the water line.")]
    [SerializeField] Transform sea;
    [SerializeField] float beachBand = 1.5f;
    [SerializeField] float pickDistance = 1.4f;
    [SerializeField] float regrowSeconds = 90f;

    [Header("Giving")]
    [SerializeField] int flowersPerBouquet = 3;
    [SerializeField] float giveDistance = 2.5f;
    [Tooltip("What your partner holds: a bouquet and a shell (held in the right hand).")]
    [SerializeField] GameObject heldBouquetPrefab;
    [SerializeField] GameObject heldShellPrefab;
    [SerializeField] Vector3 heldOffset = new Vector3(0f, 0.05f, 0.05f);
    [SerializeField] Vector3 heldRotation = new Vector3(0f, 0f, -90f);
    [SerializeField] float holdSeconds = 45f;
    [SerializeField] ParticleSystem heartsPrefab;
    [SerializeField] AudioClip pickSound;
    [SerializeField] AudioClip giveSound;

    const string FlowersKey = "gift_flowers", ShellsKey = "gift_shells";

    readonly NetworkVariable<int> seed = new NetworkVariable<int>(0);
    NetworkList<byte> picked; // 1 = picked, waiting to grow back

    readonly List<GameObject> items = new List<GameObject>();
    readonly HashSet<int> localPending = new HashSet<int>();
    readonly Dictionary<int, float> regrowAt = new Dictionary<int, float>(); // host only
    readonly List<int> due = new List<int>();
    readonly Dictionary<ulong, GameObject> held = new Dictionary<ulong, GameObject>();
    int builtSeed = -1;

    int Total => flowerCount + shellCount;
    bool IsShell(int i) => i >= flowerCount;

    static int Flowers { get => PlayerPrefs.GetInt(FlowersKey, 0); set { PlayerPrefs.SetInt(FlowersKey, value); PlayerPrefs.Save(); } }
    static int Shells { get => PlayerPrefs.GetInt(ShellsKey, 0); set { PlayerPrefs.SetInt(ShellsKey, value); PlayerPrefs.Save(); } }

    void Awake()
    {
        picked = new NetworkList<byte>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            picked.Clear();
            for (int i = 0; i < Total; i++) picked.Add(0);
            seed.Value = Random.Range(1, int.MaxValue);
        }
    }

    public override void OnNetworkDespawn()
    {
        foreach (var g in items) if (g != null) Destroy(g);
        items.Clear();
        foreach (var h in held.Values) if (h != null) Destroy(h);
        held.Clear();
        builtSeed = -1;
    }

    bool IsPicked(int i) => localPending.Contains(i) || (picked != null && i < picked.Count && picked[i] != 0);

    // ---------- Per frame ----------
    void Update()
    {
        if (!IsSpawned) return;

        if (IsServer && regrowAt.Count > 0)
        {
            due.Clear();
            foreach (var kv in regrowAt) if (Time.time >= kv.Value) due.Add(kv.Key);
            foreach (var i in due) { regrowAt.Remove(i); if (i < picked.Count) picked[i] = 0; }
        }

        if (builtSeed != seed.Value) Build(seed.Value);

        // Confirmed picks no longer need the local "pending" mark
        if (localPending.Count > 0)
        {
            due.Clear();
            foreach (var i in localPending) if (i < picked.Count && picked[i] != 0) due.Add(i);
            foreach (var i in due) localPending.Remove(i);
        }

        var me = LocalPlayer();
        for (int i = 0; i < items.Count; i++)
        {
            bool visible = !IsPicked(i);
            if (items[i].activeSelf != visible) items[i].SetActive(visible);
            if (!visible || me == null) continue;

            Vector3 d = items[i].transform.position - me.position; d.y = 0f;
            if (d.magnitude < pickDistance)
            {
                localPending.Add(i);
                items[i].SetActive(false);
                if (IsShell(i)) Shells = Shells + 1; else Flowers = Flowers + 1;
                if (pickSound != null) AudioSource.PlayClipAtPoint(pickSound, items[i].transform.position, 0.7f);
                PickRpc(i);
            }
        }
    }

    // ---------- Host ----------
    [Rpc(SendTo.Server)]
    void PickRpc(int i)
    {
        if (i < 0 || i >= picked.Count || picked[i] != 0) return;
        picked[i] = 1;
        regrowAt[i] = Time.time + regrowSeconds * Random.Range(0.8f, 1.2f);
    }

    // ---------- Giving ----------
    Transform LocalPlayer()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
    }

    static PlayerController PlayerOf(ulong clientId)
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (pc.OwnerClientId == clientId) return pc;
        return null;
    }

    PlayerController Partner()
    {
        var nm = NetworkManager.Singleton;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (pc.OwnerClientId != nm.LocalClientId) return pc;
        return null;
    }

    bool PartnerClose(out PlayerController partner)
    {
        partner = Partner();
        var me = LocalPlayer();
        if (partner == null || me == null) return false;
        Vector3 d = partner.transform.position - me.position; d.y = 0f;
        return d.magnitude <= giveDistance;
    }

    void Give(GiftType type)
    {
        if (!PartnerClose(out var partner)) return;
        if (type == GiftType.Bouquet) { if (Flowers < flowersPerBouquet) return; Flowers = Flowers - flowersPerBouquet; }
        else { if (Shells < 1) return; Shells = Shells - 1; }
        GiveRpc((int)type, NetworkManager.LocalClientId, partner.OwnerClientId);
        UiTheme.Toast(Loc.T("Gift given  <3"), 2.5f);
    }

    [Rpc(SendTo.Everyone)]
    void GiveRpc(int type, ulong fromId, ulong toId)
    {
        var receiver = PlayerOf(toId);
        if (receiver == null) return;

        // Put it in the receiver's right hand (replaces a gift they were already holding)
        if (held.TryGetValue(toId, out var old) && old != null) Destroy(old);
        var prefab = type == (int)GiftType.Bouquet ? heldBouquetPrefab : heldShellPrefab;
        var visuals = receiver.GetComponent<CharacterVisuals>();
        var anim = visuals != null ? visuals.Animator : null;
        Transform hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (prefab != null)
        {
            var g = hand != null ? Instantiate(prefab, hand) : Instantiate(prefab, receiver.transform);
            g.transform.localPosition = hand != null ? heldOffset : new Vector3(0.3f, 1.0f, 0.3f);
            g.transform.localRotation = Quaternion.Euler(heldRotation);
            held[toId] = g;
            Destroy(g, holdSeconds);
        }

        Vector3 pos = receiver.transform.position + Vector3.up * 1.9f;
        if (heartsPrefab != null) Destroy(Instantiate(heartsPrefab, pos, Quaternion.identity).gameObject, 4f);
        if (giveSound != null) AudioSource.PlayClipAtPoint(giveSound, pos, 0.8f);

        if (toId == NetworkManager.LocalClientId)
            UiTheme.Toast(Loc.T(type == (int)GiftType.Bouquet ? "You got a bouquet!  <3" : "You got a seashell!  <3"), 4f);
    }

    // ---------- Building the flowers and shells (same on both phones) ----------
    void Build(int s)
    {
        foreach (var g in items) if (g != null) Destroy(g);
        items.Clear();
        builtSeed = s;
        if (s == 0) return;

        var rng = new System.Random(s);
        var terrain = Terrain.activeTerrain;
        float waterY = sea != null ? sea.position.y : float.MinValue;

        for (int i = 0; i < Total; i++)
        {
            bool shell = IsShell(i);
            Vector3 p = areaCenter;
            for (int attempt = 0; attempt < 80; attempt++)
            {
                Vector3 c;
                if (shell && terrain != null)
                {
                    Vector3 min = terrain.transform.position, size = terrain.terrainData.size;
                    c = new Vector3(min.x + 3f + (float)rng.NextDouble() * (size.x - 6f), 0f,
                                    min.z + 3f + (float)rng.NextDouble() * (size.z - 6f));
                }
                else
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    float r = Mathf.Sqrt((float)rng.NextDouble()) * flowerRadius;
                    c = new Vector3(areaCenter.x + Mathf.Cos(a) * r, 0f, areaCenter.z + Mathf.Sin(a) * r);
                }
                float g = Ground(terrain, c);
                bool ok = shell
                    ? (sea != null && g > waterY + 0.05f && g < waterY + beachBand)
                    : (g > waterY + beachBand + 0.5f && OnTerrain(terrain, c));
                if (ok) { p = c; p.y = g; break; }
            }
            var prefab = shell ? shellPrefab : flowerPrefab;
            if (prefab == null) { items.Add(new GameObject("missing")); continue; }
            var go = Instantiate(prefab, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), transform);
            go.SetActive(false);
            items.Add(go);
        }
    }

    static float Ground(Terrain t, Vector3 p) => t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;

    static bool OnTerrain(Terrain t, Vector3 p)
    {
        if (t == null) return true;
        Vector3 min = t.transform.position, size = t.terrainData.size;
        return p.x > min.x + 3f && p.x < min.x + size.x - 3f && p.z > min.z + 3f && p.z < min.z + size.z - 3f;
    }

    // ---------- UI ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI || !IsSpawned) return;
        UiTheme.Begin();

        int f = Flowers, s = Shells;
        if (f > 0 || s > 0)
        {
            bool night = WorldStateManager.Instance != null &&
                         WorldStateManager.Instance.CurrentTime == WorldStateManager.TimeOfDay.Night;
            UiTheme.PillLabel(UiTheme.CounterRect(night ? 2 : 0), Loc.F("Flowers {0}   ·   Shells {1}", f, s));
        }

        // Give buttons appear next to Hug / Kiss when you're close and have something to give
        if (!PartnerClose(out _)) return;
        if (f >= flowersPerBouquet && UiTheme.RoundButton(UiTheme.DockSlot(2, 1), "flower", true)) Give(GiftType.Bouquet);
        if (s >= 1 && UiTheme.RoundButton(UiTheme.DockSlot(3, 1), "shell", true)) Give(GiftType.Shell);
    }
}