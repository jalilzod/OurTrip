using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 2: shows the Boy or Girl model for each player and drives the Idle/Walk/Run animation.
/// - Each player picks Boy/Girl in the menu; the choice is synced to the other phone.
/// - The animation speed is calculated from movement on every device,
///   so walking/running looks right on both phones without sending extra data.
/// Put this on the ROOT of the Player prefab. Assign the two child models in the Inspector.
/// </summary>
public class CharacterVisuals : NetworkBehaviour
{
    public const string PrefKey = "character";   // 0 = Boy, 1 = Girl

    /// <summary>The local player's choice from the menu (saved on the phone).</summary>
    public static int LocalChoice
    {
        get => PlayerPrefs.GetInt(PrefKey, 0);
        set { PlayerPrefs.SetInt(PrefKey, value); PlayerPrefs.Save(); }
    }

    [SerializeField] GameObject boyModel;
    [SerializeField] GameObject girlModel;
    [Tooltip("Smoothing for the animation speed (seconds).")]
    [SerializeField] float speedDamp = 0.1f;

    static readonly int SpeedHash = Animator.StringToHash("Speed");

    readonly NetworkVariable<int> character = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    Animator animator;
    Vector3 lastPos;

    /// <summary>The Animator of the visible model (Boy or Girl).</summary>
    public Animator Animator => animator;

    /// <summary>Smoothed movement speed in m/s, the same value the Animator uses.</summary>
    public float CurrentSpeed { get; private set; }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) character.Value = LocalChoice;

        character.OnValueChanged += OnCharacterChanged;
        Apply(character.Value);
        lastPos = transform.position;
    }

    public override void OnNetworkDespawn()
    {
        character.OnValueChanged -= OnCharacterChanged;
    }

    void OnCharacterChanged(int oldValue, int newValue) => Apply(newValue);

    void Apply(int index)
    {
        bool girl = index == 1;
        if (boyModel != null) boyModel.SetActive(!girl);
        if (girlModel != null) girlModel.SetActive(girl);

        var active = girl ? girlModel : boyModel;
        animator = active != null ? active.GetComponent<Animator>() : null;
        if (animator != null) animator.applyRootMotion = false;
    }

    void Update()
    {
        if (!IsSpawned || animator == null || Time.deltaTime <= 0f) return;

        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;
        lastPos = transform.position;

        float speed = delta.magnitude / Time.deltaTime;
        if (speed > 20f) speed = 0f; // ignore teleports / respawns

        animator.SetFloat(SpeedHash, speed, speedDamp, Time.deltaTime);
        CurrentSpeed = animator.GetFloat(SpeedHash);
    }
}