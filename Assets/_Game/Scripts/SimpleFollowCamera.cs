using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Day 1 temporary camera (Cinemachine replaces it later).
/// Follows the local player. Orbit: drag on the RIGHT half of the screen (iPhone) or hold right mouse (editor).
/// Put this on Main Camera. PlayerController assigns the target automatically.
/// </summary>
public class SimpleFollowCamera : MonoBehaviour
{
    public Transform target;

    [SerializeField] float distance = 6f;
    [SerializeField] float pitch = 20f;
    [SerializeField] float lookHeight = 1.2f;
    [SerializeField] float followSmooth = 10f;
    [SerializeField] float orbitSensitivity = 0.2f;

    float yaw;

    void LateUpdate()
    {
        if (target == null) return;

        yaw += ReadOrbitDelta() * orbitSensitivity;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = target.position + Vector3.up * lookHeight;
        Vector3 desired = focus - rot * Vector3.forward * distance;

        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
        transform.LookAt(focus);
    }

    float ReadOrbitDelta()
    {
        float d = 0f;

        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var t in ts.touches)
            {
                if (t.isInProgress && t.startPosition.ReadValue().x >= Screen.width * 0.5f)
                    d += t.delta.ReadValue().x;
            }
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
            d += mouse.delta.ReadValue().x;

        return d;
    }
}