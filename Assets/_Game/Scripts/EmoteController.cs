using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Solo emotes (Wave, Dance, Kiss), synced to both phones.
/// - Editor: keys 1 = Wave, 2 = Dance, 3 = Kiss, 4 = Dance 2.
/// - iPhone: buttons at the bottom-right of the screen.
/// - Walking cancels any emote. Wave and Kiss end by themselves; Dance loops until you
///   walk or tap Dance again.
/// The Animator must have states named exactly: Locomotion, Wave, Dance, Kiss, Dance2.
/// Put this on the ROOT of the Player prefab, next to CharacterVisuals.
/// </summary>
[RequireComponent(typeof(CharacterVisuals))]
public class EmoteController : NetworkBehaviour
{
    // Animator state names (must match exactly), button labels, and whether they loop.
    static readonly string[] States = { "Wave", "Dance", "Kiss", "Dance2" };
    static readonly string[] Labels = { "Wave", "Dance", "Kiss", "Dance 2" };
    static readonly bool[] Loops = { false, true, false, true };
    const string Locomotion = "Locomotion";

    [Tooltip("Blend time in seconds when starting or stopping an emote.")]
    [SerializeField] float fade = 0.2f;
    [Tooltip("Moving faster than this (m/s) cancels the emote.")]
    [SerializeField] float cancelSpeed = 0.4f;

    CharacterVisuals visuals;
    int current = -1;
    float startTime;

    void Awake()
    {
        visuals = GetComponent<CharacterVisuals>();
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner) ReadKeys();

        if (current < 0) return;

        var anim = visuals.Animator;
        if (anim == null) { current = -1; return; }

        // Walking cancels the emote (short grace period so the start isn't cancelled instantly).
        if (visuals.CurrentSpeed > cancelSpeed && Time.time - startTime > 0.3f)
        {
            Stop();
            return;
        }

        // One-shot emotes return to Idle/Walk when their clip ends.
        if (!Loops[current] && !anim.IsInTransition(0))
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.IsName(States[current]) && st.normalizedTime >= 0.95f)
                Stop();
        }
    }

    void ReadKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame) Request(0);
        if (kb.digit2Key.wasPressedThisFrame) Request(1);
        if (kb.digit3Key.wasPressedThisFrame) Request(2);
        if (kb.digit4Key.wasPressedThisFrame) Request(3);
    }

    /// <summary>Called by the local player (keys or buttons).</summary>
    public void Request(int emote)
    {
        if (!IsOwner || emote < 0 || emote >= States.Length) return;

        if (current == emote && Loops[emote])
            StopEmoteRpc();          // tap Dance again to stop
        else
            PlayEmoteRpc(emote);
    }

    [Rpc(SendTo.Everyone)]
    void PlayEmoteRpc(int emote)
    {
        var anim = visuals.Animator;
        if (anim == null) return;

        anim.CrossFadeInFixedTime(States[emote], fade, 0);
        current = emote;
        startTime = Time.time;
    }

    [Rpc(SendTo.Everyone)]
    void StopEmoteRpc() => Stop();

    /// <summary>Forget the current solo emote without changing the animation (a paired emote takes over).</summary>
    public void ResetLocal() => current = -1;

    void Stop()
    {
        var anim = visuals.Animator;
        if (anim != null) anim.CrossFadeInFixedTime(Locomotion, fade, 0);
        current = -1;
    }

    // ---------- Temporary on-screen buttons (bottom-right). Replaced by designed UI on Day 6-7. ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI) return;
        if (!IsSpawned || !IsOwner) return;

        float s = Screen.height / 540f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        float w = 90f, h = 50f, gap = 8f;
        float x = Screen.width / s - (w + gap) * States.Length - 10f;
        float y = 540f - h - 15f;

        for (int i = 0; i < States.Length; i++)
        {
            string label = Labels[i];
            if (i == current && Loops[i]) label = "Stop";
            if (GUI.Button(new Rect(x + i * (w + gap), y, w, h), label))
                Request(i);
        }
    }
}