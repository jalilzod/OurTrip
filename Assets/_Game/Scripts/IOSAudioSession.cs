using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// iPhone sound fix. By default iOS silences the game when the silent switch is on,
/// and a WeChat/Telegram call can take the audio away from it.
/// This switches the game to "playback, mix with others" at startup and again whenever
/// the app comes back (after a call, notification or switching apps).
/// Works by itself: no need to put it on any object. Needs Plugins/iOS/AudioSessionFix.mm.
/// </summary>
public class IOSAudioSession : MonoBehaviour
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void _OurTrip_SetPlaybackAudio();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        Apply();
        var go = new GameObject("IOSAudioSession");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<IOSAudioSession>();
    }

    static void Apply()
    {
#if UNITY_IOS && !UNITY_EDITOR
        _OurTrip_SetPlaybackAudio();
#endif
    }

    // Coming back to the app (after a call, Control Center, another app): set it again.
    void OnApplicationPause(bool paused)
    {
        if (!paused) Apply();
    }

    void OnApplicationFocus(bool focus)
    {
        if (focus) Apply();
    }
}