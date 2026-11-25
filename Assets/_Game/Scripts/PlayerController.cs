using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owner-controlled movement. Keyboard (WASD/arrows, hold Shift to run) in the editor,
/// floating touch joystick on the LEFT half of the screen on iPhone (push far = run).
/// Needs: CharacterController, NetworkObject, NetworkTransform (Authority Mode = Owner).
/// Animation is handled by CharacterVisuals.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Tooltip("Speed at full joystick. Walk/Run blend thresholds in the Animator should match (Walk 1.8, Run 4.5).")]
    [SerializeField] float runSpeed = 4.5f;
    [Tooltip("Keyboard speed without Shift, as a fraction of runSpeed.")]
    [SerializeField] float keyboardWalkFraction = 0.4f;
    [SerializeField] float turnSpeed = 12f;
    [SerializeField] float gravity = -20f;
    [Tooltip("How far you drag for full speed, as a fraction of screen height.")]
    [SerializeField] float joystickRadiusFraction = 0.12f;

    float JoystickRadius => Screen.height * joystickRadiusFraction;
    static Texture2D circleTex;

    CharacterController cc;
    float verticalVelocity;

    /// <summary>While true, joystick/keys are ignored (used during hugs, kisses, sitting).</summary>
    public bool InputLocked { get; set; }

    /// <summary>While true, the character doesn't move at all (not even gravity). Used for sitting.</summary>
    public bool MovementFrozen { get; set; }

    /// <summary>The joystick/keys input this frame, even while locked (used to stand up).</summary>
    public Vector2 RawInput { get; private set; }

    /// <summary>Moves this player instantly (owner only), e.g. to line up for a hug.</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        verticalVelocity = 0f;
        cc.enabled = true;
    }

    int stickTouchId = -1;
    Vector2 stickStart;
    Vector2 stickVector;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Spawn side by side instead of inside each other.
            float side = OwnerClientId == NetworkManager.ServerClientId ? -1.0f : 1.0f;
            spawnXZ = new Vector3(spawnPoint.x + side, 0f, spawnPoint.z);
            Respawn();

            var cam = Camera.main != null ? Camera.main.GetComponent<SimpleFollowCamera>() : null;
            if (cam != null) cam.target = transform;
        }
    }

    [Tooltip("Where players appear (X and Z). Height is found automatically from the ground.")]
    [SerializeField] Vector3 spawnPoint = new Vector3(-78.46f, 0f, -124.17f);

    Vector3 spawnXZ;

    /// <summary>Places the player on top of whatever ground is below the spawn point
    /// (works no matter how high the terrain is sculpted).</summary>
    public void Respawn()
    {
        cc.enabled = false; // a disabled controller is ignored by the raycast

        Vector3 pos = spawnXZ + Vector3.up * 0.1f; // fallback: ground at y = 0
        if (Physics.Raycast(spawnXZ + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 1000f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            pos = hit.point + Vector3.up * 0.1f;
            Debug.Log($"[Player] Spawned on '{hit.collider.name}' at height {hit.point.y:F1}");
        }
        else
        {
            Debug.LogWarning("[Player] No ground found under the spawn point! Is the Terrain (with Terrain Collider) in this scene and saved?");
        }

        transform.position = pos;
        verticalVelocity = 0f;
        cc.enabled = true;
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        // Safety net: fell off the world (edge of the map, hole, glitch) -> back to the spawn point.
        if (transform.position.y < -50f)
        {
            Debug.LogWarning($"[Player] Fell out of the world at {transform.position}, respawning.");
            Respawn();
            return;
        }

        RawInput = ReadMoveInput();
        if (MovementFrozen) return; // sitting: no movement, no gravity

        Vector2 input = InputLocked ? Vector2.zero : RawInput;

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

        cc.Move((move * runSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    Vector2 ReadMoveInput()
    {
        Vector2 v = Vector2.zero;

        var kb = Keyboard.current;
        if (kb != null)
        {
            Vector2 k = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) k.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) k.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) k.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) k.x -= 1f;
            k = Vector2.ClampMagnitude(k, 1f);
            if (!kb.leftShiftKey.isPressed) k *= keyboardWalkFraction; // walk unless Shift
            v += k;
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
        if (PhotoMode.HideUI) return;
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