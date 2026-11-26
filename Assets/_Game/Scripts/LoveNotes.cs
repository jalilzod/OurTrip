using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Love notes: write a short message, it's left as a glowing envelope where you stand.
/// Your partner walks to it and opens it. Opened letters leave the world but stay in the "Letters" box.
/// All notes are saved on both phones; when you join, both phones share their notes with the host,
/// so a note written while the other was offline appears the next time you're together.
///
/// Setup: add to the WorldState object (has NetworkObject). Assign the Envelope prefab and your names.
/// </summary>
public class LoveNotes : NetworkBehaviour
{
    const string SaveKey = "loveNotes";
    const int MaxChars = 200;   // fits safely in the network message, also for Cyrillic text
    const int MaxNotes = 40;

    public struct Note : INetworkSerializable, IEquatable<Note>
    {
        public FixedString64Bytes id;
        public byte author;                 // 0 = Boy, 1 = Girl
        public FixedString512Bytes text;
        public Vector3 pos;
        public int date;                    // yyyyMMdd
        public bool read;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref id);
            s.SerializeValue(ref author);
            s.SerializeValue(ref text);
            s.SerializeValue(ref pos);
            s.SerializeValue(ref date);
            s.SerializeValue(ref read);
        }

        public bool Equals(Note o) =>
            id.Equals(o.id) && author == o.author && text.Equals(o.text) &&
            pos == o.pos && date == o.date && read == o.read;
    }

    [Serializable] class SavedNote { public string id; public int author; public string text; public Vector3 pos; public int date; public bool read; }
    [Serializable] class SaveFile { public List<SavedNote> notes = new List<SavedNote>(); }

    [SerializeField] GameObject envelopePrefab;
    [SerializeField] string boyName = "Derik";
    [SerializeField] string girlName = "My love";
    [SerializeField] float openDistance = 2f;
    [SerializeField] AudioClip openSound;

    NetworkList<Note> notes;
    readonly Dictionary<string, GameObject> envelopes = new Dictionary<string, GameObject>();

    // UI state
    bool writing, showLetters;
    string draft = "";
    int openIndex = -1;
    Vector2 lettersScroll;
    string toast;
    float toastUntil;
    bool greeted;
    GUIStyle box, text, title, button, area;

    static int Me => CharacterVisuals.LocalChoice == 1 ? 1 : 0;
    string NameOf(int author) => author == 1 ? girlName : boyName;

    void Awake()
    {
        notes = new NetworkList<Note>();
    }

    // ---------- Sharing saved notes when joining ----------
    public override void OnNetworkSpawn()
    {
        notes.OnListChanged += OnListChanged;
        foreach (var s in Load().notes)
            ShareRpc(new Note
            {
                id = s.id, author = (byte)s.author, text = Trim(s.text),
                pos = s.pos, date = s.date, read = s.read
            });
        Refresh();
    }

    public override void OnNetworkDespawn()
    {
        notes.OnListChanged -= OnListChanged;
        foreach (var e in envelopes.Values) if (e != null) Destroy(e);
        envelopes.Clear();
    }

    [Rpc(SendTo.Server)]
    void ShareRpc(Note n)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            if (!notes[i].id.Equals(n.id)) continue;
            if (n.read && !notes[i].read) { var x = notes[i]; x.read = true; notes[i] = x; }
            return;
        }
        if (notes.Count < MaxNotes) notes.Add(n);
    }

    [Rpc(SendTo.Server)]
    void ReadRpc(FixedString64Bytes id)
    {
        for (int i = 0; i < notes.Count; i++)
            if (notes[i].id.Equals(id) && !notes[i].read) { var x = notes[i]; x.read = true; notes[i] = x; }
    }

    void OnListChanged(NetworkListEvent<Note> e) => Refresh();

    void Refresh()
    {
        Save();

        // Envelopes in the world: every note nobody has opened yet.
        var keep = new HashSet<string>();
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.read) continue;
            string id = n.id.ToString();
            keep.Add(id);
            if (!envelopes.ContainsKey(id) && envelopePrefab != null)
                envelopes[id] = Instantiate(envelopePrefab, n.pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), transform);
        }
        var remove = new List<string>();
        foreach (var kv in envelopes) if (!keep.Contains(kv.Key)) remove.Add(kv.Key);
        foreach (var id in remove) { if (envelopes[id] != null) Destroy(envelopes[id]); envelopes.Remove(id); }

        // Hello message once: a letter for me is waiting.
        if (!greeted && IsSpawned)
        {
            for (int i = 0; i < notes.Count; i++)
                if (!notes[i].read && notes[i].author != Me)
                {
                    ShowToast("A letter is waiting for you somewhere...");
                    greeted = true;
                    break;
                }
        }
    }

    // ---------- Writing ----------
    void LeaveNote()
    {
        string t = draft.Trim();
        if (t.Length == 0) return;

        var nm = NetworkManager.Singleton;
        var me = nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
        if (me == null) return;

        Vector3 p = me.position + me.forward * 0.8f;
        var terrain = Terrain.activeTerrain;
        if (terrain != null) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;

        ShareRpc(new Note
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 16),
            author = (byte)Me,
            text = Trim(t),
            pos = p,
            date = int.Parse(DateTime.Now.ToString("yyyyMMdd")),
            read = false,
        });
        draft = "";
        writing = false;
        ShowToast("Your note is waiting here for " + NameOf(1 - Me));
    }

    static FixedString512Bytes Trim(string s)
    {
        if (s == null) return default;
        if (s.Length > MaxChars) s = s.Substring(0, MaxChars);
        // Cut further if the UTF-8 bytes don't fit (Cyrillic letters take 2 bytes each).
        while (System.Text.Encoding.UTF8.GetByteCount(s) > 500) s = s.Substring(0, s.Length - 1);
        return new FixedString512Bytes(s);
    }

    // ---------- Opening ----------
    int NearestLetterForMe()
    {
        var nm = NetworkManager.Singleton;
        var me = nm != null && nm.LocalClient != null && nm.LocalClient.PlayerObject != null ? nm.LocalClient.PlayerObject.transform : null;
        if (me == null) return -1;
        int best = -1; float bestDist = openDistance;
        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].read || notes[i].author == Me) continue;
            float d = Vector3.Distance(me.position, notes[i].pos);
            if (d < bestDist) { best = i; bestDist = d; }
        }
        return best;
    }

    void Open(int i)
    {
        openIndex = i;
        if (!notes[i].read && notes[i].author != Me)
        {
            if (openSound != null && Camera.main != null)
                AudioSource.PlayClipAtPoint(openSound, Camera.main.transform.position, 0.8f);
            ReadRpc(notes[i].id);
        }
    }

    // ---------- Saving ----------
    void Save()
    {
        var f = new SaveFile();
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            f.notes.Add(new SavedNote
            {
                id = n.id.ToString(), author = n.author, text = n.text.ToString(),
                pos = n.pos, date = n.date, read = n.read
            });
        }
        // Don't overwrite a saved list with an empty one before the host sent everything.
        if (f.notes.Count == 0 && Load().notes.Count > 0) return;
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(f));
        PlayerPrefs.Save();
    }

    static SaveFile Load()
    {
        try
        {
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<SaveFile>(json) ?? new SaveFile();
        }
        catch (Exception e) { Debug.LogException(e); }
        return new SaveFile();
    }

    void ShowToast(string t) { toast = t; toastUntil = Time.time + 4f; }

    // Don't walk around while typing (WASD would move the character in the editor).
    bool lockedByMe;
    void Update()
    {
        if (!IsSpawned) return;
        var nm = NetworkManager.Singleton;
        var pc = nm.LocalClient != null && nm.LocalClient.PlayerObject != null
            ? nm.LocalClient.PlayerObject.GetComponent<PlayerController>() : null;
        if (pc == null) return;
        if (writing && !lockedByMe) { pc.InputLocked = true; lockedByMe = true; }
        else if (!writing && lockedByMe) { pc.InputLocked = false; lockedByMe = false; }
    }

    static string FormatDate(int d)
    {
        return DateTime.TryParseExact(d.ToString(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) : "";
    }

    // ---------- UI ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI || !IsSpawned) return;

        float u = Screen.height / 540f;
        if (box == null)
        {
            box = new GUIStyle(GUI.skin.box);
            text = new GUIStyle(GUI.skin.label) { wordWrap = true };
            title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button);
            area = new GUIStyle(GUI.skin.textArea) { wordWrap = true };
        }
        text.fontSize = Mathf.RoundToInt(20 * u);
        title.fontSize = Mathf.RoundToInt(24 * u);
        button.fontSize = Mathf.RoundToInt(18 * u);
        area.fontSize = Mathf.RoundToInt(20 * u);
        box.fontSize = Mathf.RoundToInt(18 * u);
        GUI.matrix = Matrix4x4.identity;
        float sw = Screen.width, sh = Screen.height;

        // Top-left row: next to World / Music / Cat / Photo
        if (GUI.Button(new Rect(490f * u, 10f * u, 110f * u, 44f * u), "Write", button)) { writing = !writing; showLetters = false; openIndex = -1; }
        if (GUI.Button(new Rect(610f * u, 10f * u, 110f * u, 44f * u), "Letters", button)) { showLetters = !showLetters; writing = false; openIndex = -1; }

        // Open a letter nearby
        int near = NearestLetterForMe();
        if (near >= 0 && openIndex < 0 && !writing &&
            GUI.Button(new Rect(sw * 0.5f - 120f * u, sh - 205f * u, 240f * u, 55f * u), "Open letter", button))
            Open(near);

        var panel = new Rect(sw * 0.5f - 280f * u, sh * 0.5f - 190f * u, 560f * u, 380f * u);

        if (writing)
        {
            GUI.Box(panel, "", box);
            GUI.Label(new Rect(panel.x, panel.y + 10f * u, panel.width, 36f * u), "A note for " + NameOf(1 - Me), title);
            draft = GUI.TextArea(new Rect(panel.x + 20f * u, panel.y + 55f * u, panel.width - 40f * u, 220f * u), draft, MaxChars, area);
            GUI.Label(new Rect(panel.x + 20f * u, panel.y + 280f * u, 200f * u, 30f * u), draft.Length + " / " + MaxChars, text);
            if (GUI.Button(new Rect(panel.x + 20f * u, panel.y + 315f * u, 250f * u, 50f * u), "Leave it here", button)) LeaveNote();
            if (GUI.Button(new Rect(panel.xMax - 190f * u, panel.y + 315f * u, 170f * u, 50f * u), "Cancel", button)) writing = false;
        }
        else if (openIndex >= 0 && openIndex < notes.Count)
        {
            var n = notes[openIndex];
            GUI.Box(panel, "", box);
            GUI.Label(new Rect(panel.x, panel.y + 10f * u, panel.width, 36f * u), "From " + NameOf(n.author), title);
            GUI.Label(new Rect(panel.x + 25f * u, panel.y + 55f * u, panel.width - 50f * u, 230f * u), n.text.ToString(), text);
            GUI.Label(new Rect(panel.x + 25f * u, panel.y + 285f * u, panel.width - 50f * u, 30f * u), FormatDate(n.date), text);
            if (GUI.Button(new Rect(panel.xMax - 190f * u, panel.y + 315f * u, 170f * u, 50f * u), "Close", button)) openIndex = -1;
        }
        else if (showLetters)
        {
            GUI.Box(panel, "", box);
            GUI.Label(new Rect(panel.x, panel.y + 10f * u, panel.width, 36f * u), "Our letters", title);

            var view = new Rect(panel.x + 15f * u, panel.y + 55f * u, panel.width - 30f * u, 250f * u);
            float rowH = 50f * u;
            var content = new Rect(0, 0, view.width - 20f * u, Mathf.Max(view.height, notes.Count * (rowH + 6f * u)));
            lettersScroll = GUI.BeginScrollView(view, lettersScroll, content);
            float y = 0f;
            for (int i = notes.Count - 1; i >= 0; i--) // newest first
            {
                var n = notes[i];
                bool mine = n.author == Me;
                string status = mine ? (n.read ? "read" : "not read yet") : (n.read ? "" : "NEW - find it!");
                string label = (mine ? "To " + NameOf(1 - Me) : "From " + NameOf(n.author)) + "  ·  " + FormatDate(n.date) +
                               (status.Length > 0 ? "  ·  " + status : "");
                // Unopened letters for me must be found in the world first.
                bool canOpen = mine || n.read;
                GUI.enabled = canOpen;
                if (GUI.Button(new Rect(0, y, content.width, rowH), label, button)) Open(i);
                GUI.enabled = true;
                y += rowH + 6f * u;
            }
            if (notes.Count == 0) GUI.Label(new Rect(0, 0, content.width, 40f * u), "No letters yet. Write the first one!", text);
            GUI.EndScrollView();

            if (GUI.Button(new Rect(panel.xMax - 190f * u, panel.y + 315f * u, 170f * u, 50f * u), "Close", button)) showLetters = false;
        }

        if (toast != null && Time.time < toastUntil)
            GUI.Box(new Rect(sw * 0.5f - 230f * u, sh * 0.22f, 460f * u, 50f * u), toast, box);
    }
}