using System.Collections;
using UnityEngine;

/// <summary>
/// Background sounds that follow the world mood (from WorldStateManager):
/// - Birds: day and sunset, when it's not raining
/// - Crickets: at night
/// - Rain: while it rains
/// - Thunder + lightning flash: now and then while it rains
/// Sounds fade in and out smoothly. Runs locally on each phone, so no networking is needed:
/// both phones already share the same time of day and rain.
///
/// Waves at the beach and wind on the mountain are NOT here: they are normal 3D AudioSources
/// placed in the world, so they get louder as you walk closer.
///
/// Setup: put this on an empty GameObject "Ambience" with 4 child AudioSources (see Inspector fields).
/// </summary>
public class AmbientAudio : MonoBehaviour
{
    [Header("Looping sounds (2D AudioSources with their clips assigned)")]
    [SerializeField] AudioSource birds;
    [SerializeField] AudioSource crickets;
    [SerializeField] AudioSource rain;

    [Header("Volumes")]
    [Range(0f, 1f)][SerializeField] float birdsVolume = 0.35f;
    [Range(0f, 1f)][SerializeField] float cricketsVolume = 0.35f;
    [Range(0f, 1f)][SerializeField] float rainVolume = 0.6f;
    [Tooltip("How fast sounds fade in/out (volume per second).")]
    [SerializeField] float fadeSpeed = 0.4f;

    [Header("Thunder (only while raining)")]
    [SerializeField] AudioSource thunderSource;
    [SerializeField] AudioClip[] thunderClips;
    [SerializeField] Vector2 thunderEverySeconds = new Vector2(12f, 35f);
    [Tooltip("Optional: a second Directional Light (intensity 0) used for the lightning flash.")]
    [SerializeField] Light lightningLight;
    [SerializeField] float lightningIntensity = 2.5f;

    float nextThunder;

    void Start()
    {
        foreach (var s in new[] { birds, crickets, rain })
        {
            if (s == null) continue;
            s.loop = true;
            s.spatialBlend = 0f;
            s.volume = 0f;
            if (s.clip != null) s.Play();
        }
        if (thunderSource != null) thunderSource.spatialBlend = 0f;
        if (lightningLight != null) lightningLight.intensity = 0f;
        ScheduleThunder();
    }

    void Update()
    {
        var world = WorldStateManager.Instance;
        var time = world != null ? world.CurrentTime : WorldStateManager.TimeOfDay.Day;
        bool raining = world != null && world.IsRaining;

        bool daylight = time != WorldStateManager.TimeOfDay.Night;
        float birdsTarget = daylight && !raining
            ? birdsVolume * (time == WorldStateManager.TimeOfDay.Sunset ? 0.6f : 1f)
            : 0f;
        float cricketsTarget = time == WorldStateManager.TimeOfDay.Night
            ? cricketsVolume * (raining ? 0.3f : 1f)
            : 0f;
        float rainTarget = raining ? rainVolume : 0f;

        Fade(birds, birdsTarget);
        Fade(crickets, cricketsTarget);
        Fade(rain, rainTarget);

        if (raining && Time.time >= nextThunder)
        {
            StartCoroutine(Thunder());
            ScheduleThunder();
        }
    }

    void Fade(AudioSource s, float target)
    {
        if (s == null) return;
        s.volume = Mathf.MoveTowards(s.volume, target, fadeSpeed * Time.deltaTime);
    }

    void ScheduleThunder()
    {
        nextThunder = Time.time + Random.Range(thunderEverySeconds.x, thunderEverySeconds.y);
    }

    IEnumerator Thunder()
    {
        // Lightning first (light is faster than sound), with a quick double flash.
        if (lightningLight != null)
        {
            yield return Flash(0.08f);
            yield return new WaitForSeconds(0.08f);
            yield return Flash(0.15f);
        }

        // Thunder arrives a little later; the further away, the longer the gap.
        yield return new WaitForSeconds(Random.Range(0.3f, 1.8f));

        if (thunderSource != null && thunderClips != null && thunderClips.Length > 0)
        {
            thunderSource.pitch = Random.Range(0.85f, 1.1f);
            thunderSource.PlayOneShot(thunderClips[Random.Range(0, thunderClips.Length)], Random.Range(0.6f, 1f));
        }
    }

    IEnumerator Flash(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            lightningLight.intensity = Mathf.Lerp(lightningIntensity, 0f, t / seconds);
            yield return null;
        }
        lightningLight.intensity = 0f;
    }
}