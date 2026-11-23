using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A place to sit. Put this on the bench (prefab) and assign two child empty objects as seats.
/// Each seat's position = where the character's feet/root go; its blue arrow (forward) = the way
/// the sitting person faces. Not networked: both phones have the same benches in the scene.
/// </summary>
public class Bench : MonoBehaviour
{
    static readonly List<Bench> all = new List<Bench>();

    [Tooltip("Seat points (empty child objects). Usually 2: left and right.")]
    public Transform[] seats;
    [Tooltip("How close (meters) you must be to sit down.")]
    public float useDistance = 2.5f;

    public static int Count => all.Count;

    void OnEnable()
    {
        all.Add(this);
        // Same order on both phones: sort by position.
        all.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position, pb = b.transform.position;
            int c = pa.x.CompareTo(pb.x);
            return c != 0 ? c : pa.z.CompareTo(pb.z);
        });
    }

    void OnDisable() => all.Remove(this);

    public static Bench Get(int index) => index >= 0 && index < all.Count ? all[index] : null;
    public static int IndexOf(Bench b) => all.IndexOf(b);

    /// <summary>Nearest bench within its use distance, or null.</summary>
    public static Bench Nearest(Vector3 pos)
    {
        Bench best = null;
        float bestDist = float.MaxValue;
        foreach (var b in all)
        {
            Vector3 d = b.transform.position - pos; d.y = 0f;
            float dist = d.magnitude;
            if (dist <= b.useDistance && dist < bestDist) { best = b; bestDist = dist; }
        }
        return best;
    }

    void OnDrawGizmos()
    {
        if (seats == null) return;
        Gizmos.color = Color.magenta;
        foreach (var s in seats)
        {
            if (s == null) continue;
            Gizmos.DrawWireSphere(s.position, 0.12f);
            Gizmos.DrawLine(s.position, s.position + s.forward * 0.5f);
        }
    }
}