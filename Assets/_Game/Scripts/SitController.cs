using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sitting on benches, synced for both players.
/// - Near a bench: "Sit" button (or key B) sits you on the nearest free seat.
/// - Push the joystick / WASD, tap "Stand up" or press B again to stand up.
/// Put this on the ROOT of the Player prefab. Animator needs a state named "Sit" (Sitting Idle clip).
/// </summary>
public class SitController : NetworkBehaviour
{
    static readonly List<SitController> All = new List<SitController>();

    [SerializeField] string sitState = "Sit";
    [SerializeField] float fade = 0.3f;
    [Tooltip("When standing up, step this far forward away from the bench.")]
    [SerializeField] float standUpStep = 0.7f;

    // -1 = standing. Otherwise benchIndex * 10 + seatIndex. Written by the owner, seen by everyone.
    readonly NetworkVariable<int> seat = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    PlayerController controller;
    CharacterVisuals visuals;
    EmoteController emotes;
    float satDownAt;

    public bool IsSitting => seat.Value >= 0;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        visuals = GetComponent<CharacterVisuals>();
        emotes = GetComponent<EmoteController>();
    }

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        seat.OnValueChanged += OnSeatChanged;
        if (seat.Value >= 0) Apply(-1, seat.Value);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        seat.OnValueChanged -= OnSeatChanged;
    }

    void OnSeatChanged(int oldValue, int newValue) => Apply(oldValue, newValue);

    static Transform SeatTransform(int value)
    {
        if (value < 0) return null;
        var bench = Bench.Get(value / 10);
        int i = value % 10;
        return bench != null && bench.seats != null && i < bench.seats.Length ? bench.seats[i] : null;
    }

    void Apply(int oldValue, int newValue)
    {
        var anim = visuals != null ? visuals.Animator : null;

        if (newValue >= 0)
        {
            var s = SeatTransform(newValue);
            if (s == null) return;
            if (emotes != null) emotes.ResetLocal();

            if (IsOwner && controller != null)
            {
                controller.MovementFrozen = true;
                controller.Teleport(s.position, s.rotation);
            }
            if (anim != null) anim.CrossFadeInFixedTime(sitState, fade, 0);
        }
        else
        {
            if (IsOwner && controller != null)
            {
                var s = SeatTransform(oldValue);
                if (s != null) controller.Teleport(s.position + s.forward * standUpStep, s.rotation);
                controller.MovementFrozen = false;
            }
            if (anim != null) anim.CrossFadeInFixedTime("Locomotion", fade, 0);
        }
    }

    // ---------- Owner input ----------
    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        var kb = Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame)
        {
            if (IsSitting) StandUp(); else TrySit();
        }

        // Pushing the joystick stands you up (small delay so the tap that sat you down doesn't count).
        if (IsSitting && controller != null && Time.time - satDownAt > 0.5f &&
            controller.RawInput.sqrMagnitude > 0.25f)
            StandUp();
    }

    /// <summary>Free seat on the nearest bench (one nobody else is sitting on), or -1.</summary>
    int FindSeat()
    {
        var bench = Bench.Nearest(transform.position);
        if (bench == null || bench.seats == null) return -1;
        int b = Bench.IndexOf(bench);

        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < bench.seats.Length && i < 10; i++)
        {
            if (bench.seats[i] == null) continue;
            int value = b * 10 + i;

            bool taken = false;
            foreach (var other in All)
                if (other != this && other.seat.Value == value) { taken = true; break; }
            if (taken) continue;

            float d = Vector3.Distance(transform.position, bench.seats[i].position);
            if (d < bestDist) { bestDist = d; best = value; }
        }
        return best;
    }

    public void TrySit()
    {
        if (!IsOwner || IsSitting) return;
        int s = FindSeat();
        if (s < 0) return;
        satDownAt = Time.time;
        seat.Value = s;
    }

    public void StandUp()
    {
        if (IsOwner && IsSitting) seat.Value = -1;
    }

    // ---------- Temporary button (bottom-right, above Hug/Kiss) ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI) return;
        if (!IsSpawned || !IsOwner) return;

        bool canSit = !IsSitting && FindSeat() >= 0;
        if (!IsSitting && !canSit) return;

        UiTheme.Begin();
        if (UiTheme.PillButton(UiTheme.ActionRect(0), Loc.T(IsSitting ? "Stand up" : "Sit together"), IsSitting ? "stand" : "sit",
                               IsSitting ? UiTheme.Kind.Normal : UiTheme.Kind.Accent))
        {
            if (IsSitting) StandUp(); else TrySit();
        }
    }
}