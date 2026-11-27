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
    bool greeted;

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
                    ShowToast(Loc.T("A letter is waiting for you somewhere..."));
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
        ShowToast(Loc.F("Your note is waiting here for {0}", NameOf(1 - Me)));
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

    void ShowToast(string t) => UiTheme.Toast(t, 4f);

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
            ? dt.ToString("d MMMM yyyy", Loc.Culture) : "";
    }

    // ---------- UI ----------
    void OnGUI()
    {
        if (PhotoMode.HideUI || !IsSpawned) return;
        UiTheme.Begin();
        float u = UiTheme.U;

        // Menu column: Write (slot 4) and Letters (slot 5)
        if (UiTheme.MenuOpen)
        {
            if (UiTheme.RoundButton(UiTheme.MenuSlot(4), "write", writing))
            { writing = !writing; showLetters = false; openIndex = -1; UiTheme.OpenPanel = null; }
            if (UiTheme.RoundButton(UiTheme.MenuSlot(5), "letters", showLetters))
            { showLetters = !showLetters; writing = false; openIndex = -1; UiTheme.OpenPanel = null; }
        }

        // A letter for me is right here
        int near = NearestLetterForMe();
        if (near >= 0 && openIndex < 0 && !writing &&
            UiTheme.PillButton(UiTheme.ActionRect(3), Loc.T("Open the letter"), "letter_open", UiTheme.Kind.Active))
            Open(near);

        if (!writing && openIndex < 0 && !showLetters) return;

        UiTheme.Dim();
        var p = UiTheme.CenterRect(560f, 380f);
        UiTheme.PanelBox(p);
        float x = p.x + 24f * u, w = p.width - 48f * u;
        var closeRect = new Rect(p.xMax - 60f * u, p.y + 12f * u, 46f * u, 46f * u);

        if (writing)
        {
            GUI.Label(new Rect(p.x, p.y + 16f * u, p.width, 34f * u), Loc.F("A note for {0}", NameOf(1 - Me)), UiTheme.Title);
            if (UiTheme.RoundButton(closeRect, "close")) writing = false;
            var area = new GUIStyle(UiTheme.Field) { alignment = TextAnchor.UpperLeft, wordWrap = true };
            draft = GUI.TextArea(new Rect(x, p.y + 64f * u, w, 210f * u), draft, MaxChars, area);
            GUI.Label(new Rect(x, p.y + 280f * u, 200f * u, 24f * u), draft.Length + " / " + MaxChars, new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleLeft });
            if (UiTheme.PillButton(new Rect(x, p.yMax - 70f * u, w, 54f * u), Loc.T("Leave it here"), "heart",
                                   UiTheme.Kind.Accent, draft.Trim().Length > 0))
                LeaveNote();
        }
        else if (openIndex >= 0 && openIndex < notes.Count)
        {
            var n = notes[openIndex];
            GUI.Label(new Rect(p.x, p.y + 16f * u, p.width, 34f * u), Loc.F("From {0}", NameOf(n.author)), UiTheme.Title);
            if (UiTheme.RoundButton(closeRect, "close")) openIndex = -1;
            GUI.Label(new Rect(x, p.y + 66f * u, w, 240f * u), n.text.ToString(), UiTheme.Body);
            GUI.Label(new Rect(x, p.yMax - 40f * u, w, 24f * u), FormatDate(n.date), new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleRight });
        }
        else if (showLetters)
        {
            GUI.Label(new Rect(p.x, p.y + 16f * u, p.width, 34f * u), Loc.T("Our letters"), UiTheme.Title);
            if (UiTheme.RoundButton(closeRect, "close")) showLetters = false;

            var view = new Rect(x, p.y + 64f * u, w, p.height - 84f * u);
            float rowH = 54f * u, gap = 8f * u;
            var content = new Rect(0, 0, view.width - 18f * u, Mathf.Max(view.height, notes.Count * (rowH + gap)));
            lettersScroll = GUI.BeginScrollView(view, lettersScroll, content);
            float y = 0f;
            for (int i = notes.Count - 1; i >= 0; i--) // newest first
            {
                var n = notes[i];
                bool mine = n.author == Me;
                string status = Loc.T(mine ? (n.read ? "read" : "not read yet") : (n.read ? "" : "NEW, find it!"));
                string label = (mine ? Loc.F("To {0}", NameOf(1 - Me)) : Loc.F("From {0}", NameOf(n.author))) + "  ·  " + FormatDate(n.date) +
                               (status.Length > 0 ? "  ·  " + status : "");
                bool canOpen = mine || n.read; // new letters must be found in the world first
                bool isNew = !mine && !n.read;
                if (UiTheme.PillButton(new Rect(0, y, content.width, rowH), label, mine ? "write" : "letters",
                                       isNew ? UiTheme.Kind.Active : UiTheme.Kind.Normal, canOpen))
                    Open(i);
                y += rowH + gap;
            }
            if (notes.Count == 0)
                GUI.Label(new Rect(0, 0, content.width, 60f * u), Loc.T("No letters yet. Write the first one!"), UiTheme.BodyCenter);
            GUI.EndScrollView();
        }
    }
}