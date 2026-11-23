using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A pet cat shared by both players. The host decides what the cat does; both phones see the same.
/// - Follows the nearest player: runs if far away, walks if a bit away, stops next to you.
/// - Standing still for a while: sits down, later falls asleep on the spot.
/// - At night: walks to its bed (if assigned) and sleeps there.
/// - "Cat" button (or key C): the cat wakes up and comes to whoever called it.
///
/// Setup: Cat object in the scene with NetworkObject + NetworkTransform + this script.
/// The Animator needs states named: Idle, Walk, Run, Sit, Sleep (names editable below).
/// </summary>
public class CatAI : NetworkBehaviour
{
    enum State { Idle = 0, Walk = 1, Run = 2, Sit = 3, Sleep = 4 }

    [Header("Animation")]
    [SerializeField] Animator animator;
    [SerializeField] string idleState = "Idle";
    [SerializeField] string walkState = "Walk";
    [SerializeField] string runState = "Run";
    [SerializeField] string sitState = "Sit";
    [SerializeField] string sleepState = "Sleep";
    [SerializeField] float fade = 0.25f;

    [Header("Movement")]
    [SerializeField] float walkSpeed = 1.2f;
    [SerializeField] float runSpeed = 4f;
    [SerializeField] float turnSpeed = 6f;
    [Tooltip("Raise (+) or lower (-) the cat if its paws float above or sink into the ground.")]
    [SerializeField] float groundOffset = 0f;
    [Tooltip("The cat stops this far from you.")]
    [SerializeField] float stopDistance = 1.5f;
    [Tooltip("Further than this, the cat runs instead of walking.")]
    [SerializeField] float runDistance = 7f;

    [Header("Resting")]
    [SerializeField] float sitAfterSeconds = 8f;
    [SerializeField] float sleepAfterSeconds = 45f;
    [Tooltip("Optional: the cat bed. At night the cat goes there to sleep.")]
    [SerializeField] Transform bed;

    readonly NetworkVariable<int> state = new NetworkVariable<int>((int)State.Idle);

    Transform caller;      // player who called the cat (server only)
    float stillTime;       // how long the cat has been standing still (server only)

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
    }

    public override void OnNetworkSpawn()
    {
        state.OnValueChanged += OnStateChanged;
        PlayAnimation((State)state.Value, instant: true);

        // Put the cat exactly on the ground when the game starts.
        if (IsServer)
        {
            Vector3 p = transform.position;
            p.y = GroundHeight(p) + groundOffset;
            transform.position = p;
        }
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= OnStateChanged;
    }

    void OnStateChanged(int oldValue, int newValue) => PlayAnimation((State)newValue, instant: false);

    void PlayAnimation(State s, bool instant)
    {
        if (animator == null) return;
        string name = s switch
        {
            State.Walk => walkState,
            State.Run => runState,
            State.Sit => sitState,
            State.Sleep => sleepState,
            _ => idleState,
        };
        animator.CrossFadeInFixedTime(name, instant ? 0f : fade, 0);
    }

    // ---------------- Brain (host only) ----------------
    void Update()
    {
        if (IsSpawned && IsServer) Think();
        if (IsSpawned) ReadCallKey();
    }

    void Think()
    {
        var world = WorldStateManager.Instance;
        bool night = world != null && world.CurrentTime == WorldStateManager.TimeOfDay.Night;

        // 1. Someone called the cat: go to them.
        if (caller != null)
        {
            if (MoveTowards(caller.position, stopDistance)) { caller = null; SetState(State.Idle); }
            return;
        }

        // 2. Night and there is a bed: go to bed and sleep.
        if (night && bed != null)
        {
            if (MoveTowards(bed.position, 0.15f)) SetState(State.Sleep);
            return;
        }

        // 3. Follow the nearest player.
        Transform player = NearestPlayer(out float dist);
        if (player == null) { Rest(); return; }

        bool sleeping = (State)state.Value == State.Sleep;
        // A sleeping cat only gets up if you walk really far away.
        if (sleeping && dist < runDistance * 1.5f) return;

        if (dist > stopDistance + 0.75f)
            MoveTowards(player.position, stopDistance);
        else
            Rest();
    }

    void Rest()
    {
        stillTime += Time.deltaTime;
        if (stillTime > sleepAfterSeconds) SetState(State.Sleep);
        else if (stillTime > sitAfterSeconds) SetState(State.Sit);
        else SetState(State.Idle);
    }

    /// <summary>Moves toward a point. Returns true when arrived.</summary>
    bool MoveTowards(Vector3 target, float arriveDistance)
    {
        Vector3 to = target - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist <= arriveDistance) return true;

        stillTime = 0f;
        bool run = dist > runDistance;
        SetState(run ? State.Run : State.Walk);

        Vector3 dir = to / dist;
        float step = Mathf.Min((run ? runSpeed : walkSpeed) * Time.deltaTime, dist - arriveDistance);
        Vector3 pos = transform.position + dir * step;
        pos.y = GroundHeight(pos) + groundOffset;
        transform.position = pos;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
        return false;
    }

    void SetState(State s)
    {
        if (state.Value != (int)s) state.Value = (int)s;
    }

    Transform NearestPlayer(out float best)
    {
        best = float.MaxValue;
        Transform nearest = null;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (d < best) { best = d; nearest = client.PlayerObject.transform; }
        }
        return nearest;
    }

    static float GroundHeight(Vector3 pos)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(pos) + terrain.transform.position.y;
        return pos.y;
    }

    // ---------------- Calling the cat (either player) ----------------
    void ReadCallKey()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.cKey.wasPressedThisFrame) CallCat();
    }

    public void CallCat()
    {
        if (IsSpawned) CallRpc(NetworkManager.LocalClientId);
    }

    [Rpc(SendTo.Server)]
    void CallRpc(ulong clientId)
    {
        if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
            caller = client.PlayerObject.transform;
        stillTime = 0f;
    }

    // Temporary button (top-left, next to World and Music)
    void OnGUI()
    {
        if (PhotoMode.HideUI) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient && !nm.IsServer) return;

        float s = Screen.height / 540f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        if (GUI.Button(new Rect(250f, 10f, 110f, 44f), "Cat, come!")) CallCat();
    }
}