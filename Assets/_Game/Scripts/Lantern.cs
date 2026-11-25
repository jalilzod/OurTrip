using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One lantern in the world. Put this on each lantern (best: on the lantern prefab).
/// LanternChallenge turns it on and off. Not networked by itself: both phones have the same
/// lanterns in the scene and get the lit/unlit state from LanternChallenge.
/// </summary>
public class Lantern : MonoBehaviour
{
    static readonly List<Lantern> all = new List<Lantern>();
    public static IReadOnlyList<Lantern> All => all;

    [Tooltip("A Point Light inside the lantern (shadows off).")]
    [SerializeField] Light lampLight;
    [SerializeField] float lightIntensity = 2f;
    [Tooltip("The lantern's mesh renderer whose material should glow (material needs Emission ticked).")]
    [SerializeField] Renderer glowRenderer;
    [SerializeField] Color glowColor = new Color(1f, 0.6f, 0.25f);
    [SerializeField] float glowIntensity = 3f;
    [Tooltip("Optional: flame particles, shown while lit.")]
    [SerializeField] GameObject litEffect;
    [Tooltip("How close (meters) you must be to light it.")]
    public float useDistance = 2.2f;

    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock block;
    float current;  // 0 = off, 1 = fully lit (fades smoothly)
    bool lit;

    public bool IsLit => lit;

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
        Apply(0f);
    }

    void OnDisable() => all.Remove(this);

    public static int IndexOf(Lantern l) => all.IndexOf(l);

    public void SetLit(bool on)
    {
        if (lit == on) return;
        lit = on;
        if (litEffect != null) litEffect.SetActive(on);
    }

    void Update()
    {
        float target = lit ? 1f : 0f;
        if (Mathf.Approximately(current, target)) return;
        current = Mathf.MoveTowards(current, target, Time.deltaTime * 1.5f); // ~0.7 s fade
        Apply(current);
    }

    void Apply(float k)
    {
        if (lampLight != null)
        {
            lampLight.enabled = k > 0.001f;
            lampLight.intensity = lightIntensity * k;
        }
        if (glowRenderer != null)
        {
            if (block == null) block = new MaterialPropertyBlock();
            glowRenderer.GetPropertyBlock(block);
            block.SetColor(EmissionColor, glowColor * glowIntensity * k);
            glowRenderer.SetPropertyBlock(block);
        }
        if (litEffect != null) litEffect.SetActive(lit);
    }
}