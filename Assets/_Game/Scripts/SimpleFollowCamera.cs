using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person camera that follows the local player.
/// Drag on the RIGHT half of the screen (iPhone) or hold the right mouse button (editor):
/// left/right turns around the character, up/down tilts to look down at the ground or up at the sky.
/// The camera never goes below the ground and moves closer if something is in the way.
/// Put this on Main Camera. PlayerController assigns the target automatically.
/// </summary>
public class SimpleFollowCamera : MonoBehaviour
{
    public Transform target;

    [SerializeField] float distance = 6f;
    [Tooltip("Starting tilt in degrees (positive = looking down at the character).")]
    [SerializeField] float pitch = 12f;
    [Tooltip("Lowest tilt: negative values let you look up at the sky.")]
    [SerializeField] float minPitch = -35f;
    [SerializeField] float maxPitch = 60f;
    [SerializeField] float lookHeight = 1.5f;
    [SerializeField] float followSmooth = 10f;
    [SerializeField] float orbitSensitivity = 0.2f;
    [SerializeField] float tiltSensitivity = 0.15f;
    [Tooltip("Keep the camera at least this high above the ground.")]
    [SerializeField] float groundClearance = 0.4f;

    float yaw;

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 drag = ReadDrag();
        yaw += drag.x * orbitSensitivity;
        pitch = Mathf.Clamp(pitch - drag.y * tiltSensitivity, minPitch, maxPitch);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = target.position + Vector3.up * lookHeight;
        Vector3 desired = focus - rot * Vector3.forward * distance;

        // Move closer if a wall, tree or the mountain is between the camera and the character.
        Vector3 toCam = desired - focus;
        if (Physics.SphereCast(focus, 0.25f, toCam.normalized, out RaycastHit hit, toCam.magnitude,
                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            desired = focus + toCam.normalized * Mathf.Max(0.8f, hit.distance);

        // Never below the ground (important when looking up at the sky).
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float ground = terrain.SampleHeight(desired) + terrain.transform.position.y + groundClearance;
            if (desired.y < ground) desired.y = ground;
        }

        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
        transform.rotation = Quaternion.LookRotation(focus - transform.position);
    }

    Vector2 ReadDrag()
    {
        Vector2 d = Vector2.zero;

        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var t in ts.touches)
            {
                if (t.isInProgress && t.startPosition.ReadValue().x >= Screen.width * 0.5f)
                    d += t.delta.ReadValue();
            }
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
            d += mouse.delta.ReadValue();

        return d;
    }
}