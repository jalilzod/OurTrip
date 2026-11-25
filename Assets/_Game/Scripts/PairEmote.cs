 using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Paired emotes: Hug and Cheek Kiss.
/// Flow: A taps "Hug" (only when close) -> B sees "Hug?" with Accept/No -> B accepts ->
/// both characters line up facing each other -> both animations play at the same time -> hearts.
/// Editor keys: H = Hug, K = Kiss, Y = accept, N = decline.
///
/// Put this on the ROOT of the Player prefab (next to PlayerController, CharacterVisuals, EmoteController).
/// Animator states used: "Hug" (both players), "Kiss" (the one who kisses), "Locomotion".
/// </summary>
public class PairEmote : NetworkBehaviour
{
    public enum PairType { Hug = 0, Kiss = 1 }

    static readonly string[] Labels = { "Hug", "Kiss" };
    // Animator state for the one who asked, and for the one who accepted (null = stay idle).
    static readonly string[] InitiatorState = { "Hug", "Kiss" };
    static readonly string[] AccepterState = { "Hug", null };

    static readonly List<PairEmote> All = new List<PairEmote>();

    [Tooltip("How close (meters) you must be to start a paired emote.")]
    [SerializeField] float maxDistance = 2.5f;
    [Tooltip("Distance between the two characters' feet during the emote. Tune for your models.")]
    [SerializeField] float[] gap = { 0.45f, 0.5f };
    [Tooltip("Sideways correction (meters) for each emote: Hug, Kiss. If the animation makes them " +
             "end up beside each other, try 0.2 or -0.2 until they line up face to face.")]
    [SerializeField] float[] sideOffset = { 0f, 0f };
    [SerializeField] float fade = 0.25f;
    [Tooltip("Optional: hearts particle prefab spawned above you both.")]
    [SerializeField] ParticleSystem heartsPrefab;
    [SerializeField] float inviteTimeout = 10f;

    // The invite I received (shown on MY screen): who asked and what.
    static PairEmote pendingFrom;
    static PairType pendingType;
    static float pendingUntil;

    // Invite I sent and am waiting for.
    float waitingUntil;

    PlayerController controller;
    CharacterVisuals visuals;
    EmoteController emotes;
    bool busy;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        visuals = GetComponent<CharacterVisuals>();
        emotes = GetComponent<EmoteController>();
    }

    public override void OnNetworkSpawn() => All.Add(this);

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (pendingFrom == this) pendingFrom = null;
    }

    PairEmote Partner()
    {
        foreach (var p in All)
            if (p != this && p != null && p.IsSpawned) return p;
        return null;
    }

    static PairEmote ByOwner(ulong clientId)
    {
        foreach (var p in All)
            if (p != null && p.OwnerClientId == clientId) return p;
        return null;
    }

    bool PartnerInRange(out PairEmote partner)
    {
        partner = Partner();
        if (partner == null) return false;
        Vector3 d = partner.transform.position - transform.position;
        d.y = 0f;
        return d.magnitude <= maxDistance;
    }

    // ---------- Local input (owner only) ----------
    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.hKey.wasPressedThisFrame) Ask(PairType.Hug);
            if (kb.kKey.wasPressedThisFrame) Ask(PairType.Kiss);
            if (kb.yKey.wasPressedThisFrame) AnswerInvite(true);
            if (kb.nKey.wasPressedThisFrame) AnswerInvite(false);
        }

        if (pendingFrom != null && Time.time > pendingUntil) pendingFrom = null;
    }

    public void Ask(PairType type)
    {
        if (!IsOwner || busy || Time.time < waitingUntil) return;
        if (!PartnerInRange(out _)) return;

        waitingUntil = Time.time + inviteTimeout;
        InviteRpc((int)type);
    }

    // Runs on my partner's phone, on the copy of MY player.
    [Rpc(SendTo.NotMe)]
    void InviteRpc(int type)
    {
        pendingFrom = this;
        pendingType = (PairType)type;
        pendingUntil = Time.time + inviteTimeout;
    }

    // Called on the phone that received the invite, on the LOCAL player.
    void AnswerInvite(bool accept)
    {
        if (pendingFrom == null) return;
        var from = pendingFrom;
        var type = pendingType;
        pendingFrom = null;

        if (!accept) { from.DeclinedRpc(); return; }
        if (busy || !PartnerInRange(out _)) return;

        // Decide the meeting point here, once, and send it to both phones.
        Vector3 a = from.transform.position;
        Vector3 b = transform.position;
        Vector3 dir = b - a; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = from.transform.forward;
        dir.Normalize();
        Vector3 center = (a + b) * 0.5f;

        StartPairRpc((int)type, from.OwnerClientId, OwnerClientId, center, dir);
    }

    [Rpc(SendTo.NotMe)]
    void DeclinedRpc()
    {
        // Runs on the asker's phone (on the asker's player): stop waiting.
        waitingUntil = 0f;
    }

    // ---------- Both phones ----------
    [Rpc(SendTo.Everyone)]
    void StartPairRpc(int type, ulong initiatorId, ulong accepterId, Vector3 center, Vector3 dir)
    {
        var initiator = ByOwner(initiatorId);
        var accepter = ByOwner(accepterId);
        if (initiator == null || accepter == null) return;

        float g = gap != null && type < gap.Length ? gap[type] : 0.5f;
        float side = sideOffset != null && type < sideOffset.Length ? sideOffset[type] : 0f;
        Vector3 right = Vector3.Cross(Vector3.up, dir); // the asker's right-hand side
        initiator.waitingUntil = 0f;

        // Each one is shifted to the opposite side, cancelling the sideways lean of the animation.
        initiator.StartCoroutine(initiator.Perform(InitiatorState[type],
            center - dir * (g * 0.5f) + right * side, Quaternion.LookRotation(dir)));
        accepter.StartCoroutine(accepter.Perform(AccepterState[type],
            center + dir * (g * 0.5f) - right * side, Quaternion.LookRotation(-dir)));

        if (heartsPrefab != null)
        {
            var hearts = Instantiate(heartsPrefab, center + Vector3.up * 1.8f, Quaternion.identity);
            Destroy(hearts.gameObject, 4f);
        }
    }

    IEnumerator Perform(string state, Vector3 pos, Quaternion rot)
    {
        busy = true;
        if (emotes != null) emotes.ResetLocal();

        // Only the owner moves their own character; the other phone sees it through NetworkTransform.
        if (IsOwner && controller != null)
        {
            controller.InputLocked = true;
            controller.Teleport(pos, rot);
        }

        var anim = visuals != null ? visuals.Animator : null;
        if (anim != null && !string.IsNullOrEmpty(state))
        {
            anim.CrossFadeInFixedTime(state, fade, 0);
            yield return new WaitForSeconds(fade + 0.1f);

            float timeout = Time.time + 8f;
            while (Time.time < timeout)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                if (!anim.IsInTransition(0) && (!st.IsName(state) || st.normalizedTime >= 0.95f)) break;
                yield return null;
            }
            anim.CrossFadeInFixedTime("Locomotion", fade, 0);
        }
        else
        {
            yield return new WaitForSeconds(2.5f); // the one being kissed stands still for a moment
        }

        if (IsOwner && controller != null) controller.InputLocked = false;
        busy = false;
    }

    // ---------- Temporary UI ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI) return;
        if (!IsSpawned || !IsOwner) return;

        float s = Screen.height / 540f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float sw = Screen.width / s;

        // Invite prompt in the middle of the screen
        if (pendingFrom != null)
        {
            var r = new Rect(sw * 0.5f - 150f, 150f, 300f, 130f);
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 20f, r.y + 15f, 260f, 30f), Labels[(int)pendingType] + "?  <3");
            if (GUI.Button(new Rect(r.x + 20f, r.y + 60f, 120f, 50f), "Accept")) AnswerInvite(true);
            if (GUI.Button(new Rect(r.x + 160f, r.y + 60f, 120f, 50f), "No")) AnswerInvite(false);
        }

        // Hug / Kiss buttons above the solo emote buttons (bottom-right)
        bool close = PartnerInRange(out _);
        bool waiting = Time.time < waitingUntil;
        float w = 90f, h = 50f, gapX = 8f;
        float x = sw - (w + gapX) * 4 - 10f;
        float y = 540f - h - 15f - h - 10f;

        GUI.enabled = close && !busy && !waiting;
        for (int i = 0; i < Labels.Length; i++)
        {
            string label = waiting ? "..." : Labels[i];
            if (GUI.Button(new Rect(x + i * (w + gapX), y, w, h), label)) Ask((PairType)i);
        }
        GUI.enabled = true;

        if (!close && Partner() != null)
            GUI.Label(new Rect(x + 2f * (w + gapX), y + 15f, 200f, 30f), "Get closer...");
    }
}