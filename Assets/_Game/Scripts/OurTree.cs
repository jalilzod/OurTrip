using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// "Our tree": plant it together, water it every day, and it grows over real days.
/// - It only grows when BOTH of you watered it on the same day (once per day).
/// - Progress is saved on both phones; when you join, the host keeps the most grown version,
///   so it doesn't matter who hosts.
/// - "Today" is the host's calendar date.
///
/// Setup: put this on the tree spot object in the scene, together with a NetworkObject.
/// Assign the stage objects (child models, smallest first).
/// </summary>
public class OurTree : NetworkBehaviour
{
    const string SaveKey = "ourTree";

    [Serializable]
    class SaveData { public bool planted; public int points; public int day; public int mask; }

    [Header("Looks")]
    [Tooltip("Child objects for each stage, smallest first (e.g. Seed, Sprout, Little, Young, Big, Blossom).")]
    [SerializeField] GameObject[] stages;
    [Tooltip("How many days of growing (both watered) are needed to reach each stage. Same length as Stages.")]
    [SerializeField] int[] daysForStage = { 0, 1, 3, 5, 8, 12 };
    [SerializeField] string[] stageNames = { "Seed", "Sprout", "Little tree", "Young tree", "Big tree", "Blossoming tree" };
    [Tooltip("Optional: shown where the seed will be planted before it's planted (e.g. a small dirt patch).")]
    [SerializeField] GameObject emptySpot;

    [Header("Effects (optional)")]
    [SerializeField] ParticleSystem waterEffectPrefab;
    [SerializeField] ParticleSystem growEffectPrefab;
    [SerializeField] AudioClip waterSound;
    [SerializeField] AudioClip growSound;

    [Header("Use")]
    [SerializeField] float useDistance = 4f;

    readonly NetworkVariable<bool> planted = new NetworkVariable<bool>(false);
    readonly NetworkVariable<int> points = new NetworkVariable<int>(0);   // days grown
    readonly NetworkVariable<int> day = new NetworkVariable<int>(0);      // yyyyMMdd of "today" on the host
    readonly NetworkVariable<int> mask = new NetworkVariable<int>(0);     // bit 0 = Boy watered today, bit 1 = Girl

    int shownStage = -1;
    float nextDayCheck;

    static int Today => int.Parse(DateTime.Now.ToString("yyyyMMdd"));
    static int Me => CharacterVisuals.LocalChoice == 1 ? 1 : 0; // 0 = Boy, 1 = Girl

    void Awake()
    {
        // Before anyone connects: hide all stages, show only the empty spot.
        if (stages != null)
            foreach (var s in stages) if (s != null) s.SetActive(false);
        if (emptySpot != null) emptySpot.SetActive(true);
        if (stages == null || stages.Length == 0)
            Debug.LogWarning("[OurTree] The Stages list is empty: drag the stage children into it.");
    }

    // ---------- Start: share my saved copy with the host ----------
    public override void OnNetworkSpawn()
    {
        planted.OnValueChanged += (_, __) => OnChanged();
        points.OnValueChanged += (_, __) => OnChanged();
        day.OnValueChanged += (_, __) => OnChanged();
        mask.OnValueChanged += (_, __) => OnChanged();

        var s = Load();
        ShareSaveRpc(s.planted, s.points, s.day, s.mask);
        ShowStage(instant: true);
    }

    [Rpc(SendTo.Server)]
    void ShareSaveRpc(bool p, int pts, int d, int m)
    {
        int today = Today;
        if (day.Value != today) { day.Value = today; mask.Value = 0; }

        if (p && !planted.Value) planted.Value = true;
        if (pts > points.Value) points.Value = pts;
        if (d == today) mask.Value |= m; // someone already watered today
    }

    // ---------- Actions ----------
    [Rpc(SendTo.Server)]
    void PlantRpc()
    {
        if (planted.Value) return;
        planted.Value = true;
        EffectRpc(false, true);
    }

    [Rpc(SendTo.Server)]
    void WaterRpc(int who)
    {
        if (!planted.Value) return;
        int today = Today;
        if (day.Value != today) { day.Value = today; mask.Value = 0; }

        int bit = 1 << Mathf.Clamp(who, 0, 1);
        if ((mask.Value & bit) != 0) return;

        mask.Value |= bit;
        bool grew = mask.Value == 3; // both watered today -> grow once
        if (grew) points.Value += 1;
        EffectRpc(true, grew);
    }

    [Rpc(SendTo.Everyone)]
    void EffectRpc(bool water, bool grew)
    {
        Vector3 pos = transform.position + Vector3.up * 0.5f;
        if (water)
        {
            if (waterEffectPrefab != null) Destroy(Instantiate(waterEffectPrefab, pos + Vector3.up, Quaternion.identity).gameObject, 4f);
            if (waterSound != null) AudioSource.PlayClipAtPoint(waterSound, pos, 0.8f);
        }
        if (grew)
        {
            if (growEffectPrefab != null) Destroy(Instantiate(growEffectPrefab, pos, Quaternion.identity).gameObject, 5f);
            if (growSound != null) AudioSource.PlayClipAtPoint(growSound, pos, 0.9f);
            ShowToast(Loc.T(planted.Value && points.Value > 0 ? "Our tree grew!" : "We planted our seed!"));
        }
        else if (water)
        {
            ShowToast(Loc.T("Watered! Waiting for your partner to water too."));
        }
    }

    // ---------- Saving + showing ----------
    void OnChanged()
    {
        Save(new SaveData { planted = planted.Value, points = points.Value, day = day.Value, mask = mask.Value });
        ShowStage(instant: false);
    }

    static SaveData Load()
    {
        try
        {
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception e) { Debug.LogException(e); }
        return new SaveData();
    }

    static void Save(SaveData s)
    {
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(s));
        PlayerPrefs.Save();
    }

    int StageIndex()
    {
        if (!planted.Value || stages == null || stages.Length == 0) return -1;
        int stage = 0;
        for (int i = 0; i < stages.Length && i < daysForStage.Length; i++)
            if (points.Value >= daysForStage[i]) stage = i;
        return stage;
    }

    void ShowStage(bool instant)
    {
        int stage = StageIndex();
        if (emptySpot != null) emptySpot.SetActive(stage < 0);
        if (stage == shownStage) return;

        if (stages != null)
            for (int i = 0; i < stages.Length; i++)
                if (stages[i] != null) stages[i].SetActive(i == stage);

        if (!instant && stage >= 0 && stages[stage] != null) StartCoroutine(Pop(stages[stage].transform));
        shownStage = stage;
    }

    static IEnumerator Pop(Transform t)
    {
        Vector3 full = t.localScale;
        float k = 0f;
        while (k < 1f)
        {
            k += Time.deltaTime / 1.2f;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f); // ease out
            t.localScale = full * Mathf.Lerp(0.2f, 1f, e);
            yield return null;
        }
        t.localScale = full;
    }

    // ---------- Per frame ----------
    void Update()
    {
        if (!IsSpawned) return;

        // Midnight on the host: a new day starts while you're playing.
        if (IsServer && Time.time > nextDayCheck)
        {
            nextDayCheck = Time.time + 30f;
            if (day.Value != Today) { day.Value = Today; mask.Value = 0; }
        }

        var kb = Keyboard.current;
        if (kb != null && kb.tKey.wasPressedThisFrame && Near()) Use();
    }

    bool Near()
    {
        var nm = NetworkManager.Singleton;
        var me = nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null
            ? nm.LocalClient.PlayerObject.transform : null;
        if (me == null) return false;
        Vector3 d = me.position - transform.position; d.y = 0f;
        return d.magnitude <= useDistance;
    }

    bool IWateredToday => (mask.Value & (1 << Me)) != 0;

    void Use()
    {
        if (!planted.Value) PlantRpc();
        else if (!IWateredToday) WaterRpc(Me);
    }

    void ShowToast(string text) => UiTheme.Toast(text, 3f);

    void OnGUI()
    {
        if (PhotoMode.HideUI || !IsSpawned || !Near()) return;
        UiTheme.Begin();
        float u = UiTheme.U;

        int stage = StageIndex();
        string info = stage < 0
            ? Loc.T("A perfect spot for our tree")
            : Loc.F("{0}  ·  grown {1} days", Loc.T(stage < stageNames.Length ? stageNames[stage] : "Our tree"), points.Value);
        var btn = UiTheme.ActionRect(1);
        UiTheme.PillLabel(new Rect(btn.center.x - 170f * u, btn.y - 46f * u, 340f * u, 38f * u), info);

        string label = Loc.T(!planted.Value ? "Plant our seed"
                     : IWateredToday ? (mask.Value == 3 ? "Watered together today  <3" : "Watered, waiting for you two")
                     : "Water our tree");
        string icon = !planted.Value ? "seed" : "water";
        bool can = !planted.Value || !IWateredToday;
        if (UiTheme.PillButton(new Rect(btn.center.x - 160f * u, btn.y, 320f * u, btn.height), label, icon,
                               can ? UiTheme.Kind.Accent : UiTheme.Kind.Normal, can))
            Use();
    }
}