using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lantern challenge, shared by both players.
/// At Night, walk to a lantern and tap "Light lantern" (or key L). Both phones see it glow.
/// When all lanterns are lit -> fireworks over the sea and your own message on screen.
/// Every new night the lanterns start dark again.
///
/// Setup: add to the WorldState object (has NetworkObject). Lanterns in the scene need the Lantern script.
/// </summary>
public class LanternChallenge : NetworkBehaviour
{
    [Header("Fireworks")]
    [Tooltip("One firework burst (a particle system that plays once).")]
    [SerializeField] ParticleSystem fireworkPrefab;
    [Tooltip("An empty object above the sea: fireworks burst around it.")]
    [SerializeField] Transform fireworksCenter;
    [SerializeField] Vector3 spread = new Vector3(30f, 10f, 15f);
    [SerializeField] float showSeconds = 25f;
    [SerializeField] Vector2 secondsBetweenBursts = new Vector2(0.3f, 1.1f);
    [SerializeField] AudioClip[] boomSounds;
    [Tooltip("Colors for the bursts (picked at random).")]
    [SerializeField] Color[] colors =
    {
        new Color(1f, 0.35f, 0.55f), new Color(1f, 0.8f, 0.3f), new Color(0.5f, 0.8f, 1f),
        new Color(0.7f, 1f, 0.5f), new Color(0.85f, 0.5f, 1f), Color.white,
    };

    [Header("Message")]
    [TextArea] [SerializeField] string finishMessage = "Every night with you is my favorite.";

    readonly NetworkVariable<int> litMask = new NetworkVariable<int>(0);
    WorldStateManager.TimeOfDay lastTime = WorldStateManager.TimeOfDay.Day;
    bool showing;
    float messageUntil;

    static bool IsNight =>
        WorldStateManager.Instance != null &&
        WorldStateManager.Instance.CurrentTime == WorldStateManager.TimeOfDay.Night;

    int Count => Mathf.Min(Lantern.All.Count, 31);
    int AllMask => (1 << Count) - 1;
    int LitCount
    {
        get { int c = 0, v = litMask.Value; while (v != 0) { c += v & 1; v >>= 1; } return c; }
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsServer) ServerCheckNightStart();

        // Show the lit state on this phone (lanterns only glow at night).
        bool night = IsNight;
        for (int i = 0; i < Count; i++)
            Lantern.All[i].SetLit(night && (litMask.Value & (1 << i)) != 0);

        var kb = Keyboard.current;
        if (kb != null && kb.lKey.wasPressedThisFrame) TryLightNearest();
    }

    // ---------- Lighting ----------
    int NearestUnlit()
    {
        var nm = NetworkManager.Singleton;
        var me = nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null
            ? nm.LocalClient.PlayerObject.transform : null;
        if (me == null || !IsNight) return -1;

        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < Count; i++)
        {
            if ((litMask.Value & (1 << i)) != 0) continue;
            var l = Lantern.All[i];
            Vector3 d = l.transform.position - me.position; d.y = 0f;
            float dist = d.magnitude;
            if (dist <= l.useDistance && dist < bestDist) { best = i; bestDist = dist; }
        }
        return best;
    }

    void TryLightNearest()
    {
        int i = NearestUnlit();
        if (i >= 0) LightRpc(i);
    }

    [Rpc(SendTo.Server)]
    void LightRpc(int index)
    {
        if (index < 0 || index >= Count || !IsNight) return;
        int before = litMask.Value;
        litMask.Value = before | (1 << index);
        if (before != AllMask && litMask.Value == AllMask) CelebrateRpc();
    }

    void ServerCheckNightStart()
    {
        var world = WorldStateManager.Instance;
        if (world == null) return;
        var now = world.CurrentTime;
        if (now == WorldStateManager.TimeOfDay.Night && lastTime != WorldStateManager.TimeOfDay.Night)
            litMask.Value = 0; // new night, dark lanterns
        lastTime = now;
    }

    // ---------- Fireworks (each phone plays its own show) ----------
    [Rpc(SendTo.Everyone)]
    void CelebrateRpc()
    {
        messageUntil = Time.time + showSeconds;
        if (!showing) StartCoroutine(Fireworks());
    }

    IEnumerator Fireworks()
    {
        showing = true;
        float end = Time.time + showSeconds;
        Vector3 center = fireworksCenter != null ? fireworksCenter.position : transform.position + Vector3.up * 30f;

        while (Time.time < end)
        {
            if (fireworkPrefab != null)
            {
                Vector3 pos = center + new Vector3(
                    Random.Range(-spread.x, spread.x), Random.Range(-spread.y, spread.y), Random.Range(-spread.z, spread.z));
                var fw = Instantiate(fireworkPrefab, pos, Quaternion.identity);
                var main = fw.main;
                if (colors != null && colors.Length > 0) main.startColor = colors[Random.Range(0, colors.Length)];
                fw.Play();
                Destroy(fw.gameObject, 5f);

                if (boomSounds != null && boomSounds.Length > 0)
                    StartCoroutine(Boom(pos));
            }
            yield return new WaitForSeconds(Random.Range(secondsBetweenBursts.x, secondsBetweenBursts.y));
        }
        showing = false;
    }

    IEnumerator Boom(Vector3 pos)
    {
        // Sound arrives a moment after the flash, like real fireworks far away.
        yield return new WaitForSeconds(Random.Range(0.2f, 0.6f));
        var clip = boomSounds[Random.Range(0, boomSounds.Length)];
        var cam = Camera.main;
        // Played near the camera so it's clearly heard, but quieter for variety.
        AudioSource.PlayClipAtPoint(clip, cam != null ? cam.transform.position : pos, Random.Range(0.5f, 0.9f));
    }

    // ---------- UI ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI || !IsSpawned) return;
        UiTheme.Begin();
        float u = UiTheme.U;

        if (IsNight && Count > 0)
        {
            UiTheme.PillLabel(UiTheme.CounterRect(1),
                LitCount >= Count ? Loc.T("All lanterns lit  <3") : Loc.F("Lanterns  {0} / {1}", LitCount, Count));

            if (NearestUnlit() >= 0 &&
                UiTheme.PillButton(UiTheme.ActionRect(2), Loc.T("Light the lantern"), "lantern", UiTheme.Kind.Accent))
                TryLightNearest();
        }

        if (Time.time < messageUntil && !string.IsNullOrEmpty(finishMessage))
        {
            var style = new GUIStyle(UiTheme.Big) { wordWrap = true, normal = { textColor = new Color(1f, 0.92f, 0.8f) } };
            UiTheme.Shadowed(new Rect(UiTheme.SW * 0.1f, UiTheme.SH * 0.16f, UiTheme.SW * 0.8f, 120f * u), finishMessage, style);
        }
    }
}