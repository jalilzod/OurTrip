using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Photo mode, synced: either player taps "Photo" (or key P) -> both phones count down 3-2-1,
/// the camera frames both characters, the UI hides, a flash, and the picture (with a date caption)
/// is saved to each phone's Photos app. A copy is also kept in the game's folder for a future gallery.
///
/// Setup: add this to the WorldState object (it already has a NetworkObject).
/// iPhone saving needs Plugins/iOS/PhotoSaver.mm and the editor script IOSPostBuild.cs.
/// </summary>
public class PhotoMode : NetworkBehaviour
{
    /// <summary>All other on-screen buttons check this and hide while a photo is taken.</summary>
    public static bool HideUI { get; private set; }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void _OurTrip_SaveToPhotos(string path);
#endif

    [SerializeField] float countdownSeconds = 3f;
    [Tooltip("Minimum camera distance from the two of you.")]
    [SerializeField] float distance = 4f;
    [SerializeField] float cameraHeight = 1.5f;
    [SerializeField] float lookHeight = 1.1f;
    [SerializeField] string caption = "Our Trip";

    bool busy;
    string countdownText;
    bool showCaption;
    float flashAlpha;
    string toast;
    float toastUntil;
    float serverLastStart = -100f;

    static Texture2D white;
    GUIStyle bigStyle, captionStyle, toastStyle;

    // ---------- Starting a photo ----------
    void Update()
    {
        if (!IsSpawned) return;
        var kb = Keyboard.current;
        if (kb != null && kb.pKey.wasPressedThisFrame) RequestPhoto();
        if (flashAlpha > 0f) flashAlpha = Mathf.Max(0f, flashAlpha - Time.deltaTime * 2.5f);
    }

    public void RequestPhoto()
    {
        if (IsSpawned && !busy) RequestRpc();
    }

    [Rpc(SendTo.Server)]
    void RequestRpc()
    {
        // Ignore double taps from both players at the same moment.
        if (Time.time - serverLastStart < countdownSeconds + 2f) return;
        serverLastStart = Time.time;
        StartPhotoRpc();
    }

    [Rpc(SendTo.Everyone)]
    void StartPhotoRpc()
    {
        if (!busy) StartCoroutine(TakePhoto());
    }

    // ---------- Taking the photo (on each phone) ----------
    IEnumerator TakePhoto()
    {
        busy = true;
        HideUI = true;

        var cam = Camera.main;
        var follow = cam != null ? cam.GetComponent<SimpleFollowCamera>() : null;
        if (follow != null) follow.enabled = false;

        // Frame everyone: center between the players, camera on the side it's already on.
        var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        Vector3 center = Vector3.zero;
        foreach (var p in players) center += p.transform.position;
        if (players.Length > 0) center /= players.Length;
        float spread = 0f;
        foreach (var p in players) spread = Mathf.Max(spread, Vector3.Distance(p.transform.position, center));

        Vector3 dir = cam != null ? cam.transform.position - center : Vector3.back;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
        dir.Normalize();

        float dist = Mathf.Max(distance, spread * 2.2f + 2f);
        Vector3 targetPos = center + dir * dist + Vector3.up * cameraHeight;
        Quaternion targetRot = Quaternion.LookRotation((center + Vector3.up * lookHeight) - targetPos);

        // Countdown while the camera glides into place.
        float t = countdownSeconds;
        while (t > 0f)
        {
            countdownText = Mathf.CeilToInt(t).ToString();
            if (cam != null)
            {
                float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
                cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, k);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, k);
            }
            t -= Time.deltaTime;
            yield return null;
        }
        if (cam != null) cam.transform.SetPositionAndRotation(targetPos, targetRot);

        // Capture: only the world + the date caption.
        countdownText = null;
        showCaption = true;
        yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        showCaption = false;
        flashAlpha = 1f;

        string path = null;
        try
        {
            string dir2 = Path.Combine(Application.persistentDataPath, "Photos");
            Directory.CreateDirectory(dir2);
            path = Path.Combine(dir2, "OurTrip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            SaveToGallery(path);
            ShowToast("Saved to Photos");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ShowToast("Photo could not be saved");
        }
        finally
        {
            Destroy(tex);
        }
        Debug.Log("[Photo] Saved " + path);

        yield return new WaitForSeconds(1f);
        if (follow != null) follow.enabled = true; // glides back behind you
        HideUI = false;
        busy = false;
    }

    static void SaveToGallery(string path)
    {
#if UNITY_IOS && !UNITY_EDITOR
        _OurTrip_SaveToPhotos(path);
#endif
    }

    void ShowToast(string text)
    {
        toast = text;
        toastUntil = Time.time + 2.5f;
    }

    // ---------- Drawing ----------
    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient && !nm.IsServer) return;

        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
            toastStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
        }

        GUI.matrix = Matrix4x4.identity;
        float sw = Screen.width, sh = Screen.height, u = sh / 540f;
        bigStyle.fontSize = Mathf.RoundToInt(120 * u);
        captionStyle.fontSize = Mathf.RoundToInt(26 * u);
        toastStyle.fontSize = Mathf.RoundToInt(20 * u);

        if (!busy && !HideUI)
        {
            if (GUI.Button(new Rect(370f * u, 10f * u, 110f * u, 44f * u), "Photo")) RequestPhoto();
        }

        if (countdownText != null)
        {
            Shadowed(new Rect(0, 0, sw, sh), countdownText, bigStyle);
        }

        if (showCaption)
        {
            string text = caption + "  ·  " + DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            Shadowed(new Rect(0, 0, sw - 30f * u, sh - 20f * u), text, captionStyle);
        }

        if (flashAlpha > 0f)
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, flashAlpha);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), white);
            GUI.color = old;
        }

        if (toast != null && Time.time < toastUntil && !showCaption)
        {
            GUI.Box(new Rect(sw * 0.5f - 160f * u, sh - 90f * u, 320f * u, 50f * u), toast, toastStyle);
        }
    }

    static void Shadowed(Rect r, string text, GUIStyle style)
    {
        var old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), text, style);
        style.normal.textColor = Color.white;
        GUI.Label(r, text, style);
        style.normal.textColor = old;
    }
}