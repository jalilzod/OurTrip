using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Day 1: owner-controlled movement. Keyboard (WASD/arrows) in the editor,
/// floating touch joystick on the LEFT half of the screen on iPhone.
/// Needs: CharacterController, NetworkObject, NetworkTransform (Authority Mode = Owner).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [SerializeField] float walkSpeed = 3.5f;
    [SerializeField] float turnSpeed = 12f;
    [SerializeField] float gravity = -20f;
    [Tooltip("How far you drag for full speed, as a fraction of screen height.")]
    [SerializeField] float joystickRadiusFraction = 0.12f;

    float JoystickRadius => Screen.height * joystickRadiusFraction;
    static Texture2D circleTex;

    static readonly Color BoyColor = new Color(0.30f, 0.55f, 1.00f);
    static readonly Color GirlColor = new Color(1.00f, 0.45f, 0.70f);

    CharacterController cc;
    float verticalVelocity;

    int stickTouchId = -1;
    Vector2 stickStart;
    Vector2 stickVector;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        // Same color logic runs on every device, so no extra syncing needed.
        var rend = GetComponentInChildren<Renderer>();
        if (rend != null)
            rend.material.color = OwnerClientId == NetworkManager.ServerClientId ? BoyColor : GirlColor;

        if (IsOwner)
        {
            // Spawn side by side instead of inside each other.
            cc.enabled = false;
            transform.position = new Vector3(OwnerClientId == NetworkManager.ServerClientId ? -1.5f : 1.5f, 1f, 0f);
            cc.enabled = true;

            var cam = Camera.main != null ? Camera.main.GetComponent<SimpleFollowCamera>() : null;
            if (cam != null) cam.target = transform;
        }
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        Vector2 input = ReadMoveInput();

        // Move relative to the camera direction.
        Vector3 move;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
            move = fwd * input.y + right * input.x;
        }
        else
        {
            move = new Vector3(input.x, 0f, input.y);
        }
        if (move.sqrMagnitude > 1f) move.Normalize();

        if (move.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move), turnSpeed * Time.deltaTime);

        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        cc.Move((move * walkSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    Vector2 ReadMoveInput()
    {
        Vector2 v = Vector2.zero;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
        }

        var ts = Touchscreen.current;
        if (ts != null)
        {
            UpdateTouchStick(ts);
            v += stickVector;
        }

        return Vector2.ClampMagnitude(v, 1f);
    }

    // Floating joystick: the touch that started on the left half becomes the stick.
    void UpdateTouchStick(Touchscreen ts)
    {
        bool found = false;

        foreach (var t in ts.touches)
        {
            if (!t.isInProgress) continue;

            int id = t.touchId.ReadValue();
            Vector2 start = t.startPosition.ReadValue();

            if (stickTouchId == -1 && start.x < Screen.width * 0.5f)
            {
                stickTouchId = id;
                stickStart = start;
            }

            if (id == stickTouchId)
            {
                Vector2 pos = t.position.ReadValue();
                stickVector = Vector2.ClampMagnitude((pos - stickStart) / JoystickRadius, 1f);
                found = true;
            }
        }

        if (!found)
        {
            stickTouchId = -1;
            stickVector = Vector2.zero;
        }
    }

    // ---------- Visible joystick (only on touch devices, only for your own player) ----------
    void OnGUI()
    {
        if (!IsSpawned || !IsOwner || Touchscreen.current == null) return;
        if (Event.current.type != EventType.Repaint) return;

        if (circleTex == null) circleTex = MakeCircleTexture(128);
        GUI.matrix = Matrix4x4.identity;

        float r = JoystickRadius;
        Color old = GUI.color;

        if (stickTouchId == -1)
        {
            // Hint: faint ring where the joystick usually is.
            Vector2 hint = new Vector2(Screen.width * 0.15f, Screen.height * 0.25f);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            DrawCircle(hint, r);
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            DrawCircle(stickStart, r);                               // base
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            DrawCircle(stickStart + stickVector * r, r * 0.4f);      // knob
        }

        GUI.color = old;
    }

    // Input System uses bottom-left origin; IMGUI uses top-left.
    static void DrawCircle(Vector2 screenCenter, float radius)
    {
        var rect = new Rect(screenCenter.x - radius, Screen.height - screenCenter.y - radius, radius * 2f, radius * 2f);
        GUI.DrawTexture(rect, circleTex);
    }

    static Texture2D MakeCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float a = Mathf.Clamp01((1f - d) * size * 0.5f); // soft edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }
}