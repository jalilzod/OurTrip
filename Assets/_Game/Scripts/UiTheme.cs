using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One shared look for all on-screen UI: glassy purple buttons, pink "active" state, round icon buttons,
/// panels, pills and the screen layout (menu column top-left, emote dock bottom-right, action button
/// bottom-center, counters top-center, toasts).
/// Images live in Assets/_Game/Resources/UI/ (loaded by name). Optional font: Resources/UI/Font.
/// Every OnGUI calls UiTheme.Begin() first.
/// </summary>
public static class UiTheme
{
    // ---------- Scale + layout ----------
    /// <summary>1 unit = 1 px on a 540 px tall screen; everything scales with the screen height.</summary>
    public static float U => Screen.height / 540f;
    public static float SW => Screen.width;
    public static float SH => Screen.height;

    /// <summary>Is the top-left menu column open (World, Music, Cat, Photo, Write, Letters)?</summary>
    public static bool MenuOpen;
    /// <summary>Which menu panel is open next to the column (only one at a time).</summary>
    public static string OpenPanel;

    const float Margin = 16f, RoundSize = 56f, RoundGap = 10f;

    /// <summary>Respect the iPhone notch / rounded corners.</summary>
    static Rect Safe
    {
        get
        {
            var s = Screen.safeArea;
            return new Rect(s.x, Screen.height - s.yMax, s.width, s.height); // to GUI coordinates (top-left origin)
        }
    }

    public static Rect MenuButtonRect => new Rect(Safe.x + Margin * U, Safe.y + Margin * U, RoundSize * U, RoundSize * U);

    public static Rect TopRightButton => new Rect(Safe.xMax - (Margin + RoundSize) * U, Safe.y + Margin * U, RoundSize * U, RoundSize * U);

    /// <summary>Slot i in the menu column under the menu button.</summary>
    public static Rect MenuSlot(int i) =>
        new Rect(MenuButtonRect.x, MenuButtonRect.y + (i + 1) * (RoundSize + RoundGap) * U, RoundSize * U, RoundSize * U);

    /// <summary>Area for a panel that opens to the right of menu slot i.</summary>
    public static Rect MenuPanel(int i, float w, float h)
    {
        var slot = MenuSlot(i);
        float y = Mathf.Min(slot.y, SH - Safe.y - h * U - Margin * U);
        return new Rect(slot.xMax + RoundGap * U, Mathf.Max(MenuButtonRect.y, y), w * U, h * U);
    }

    /// <summary>Round buttons in the bottom-right dock. col 0 = rightmost, row 0 = bottom row.</summary>
    public static Rect DockSlot(int col, int row, float size = 60f)
    {
        float right = Safe.xMax - Margin * U, bottom = Safe.yMax - Margin * U;
        return new Rect(right - (col + 1) * size * U - col * RoundGap * U,
                        bottom - (row + 1) * size * U - row * RoundGap * U, size * U, size * U);
    }

    /// <summary>Big action button bottom-center (row 0 = lowest). Used by Sit, Light lantern, Water, Open letter.</summary>
    public static Rect ActionRect(int row = 0, float w = 260f, float h = 58f) =>
        new Rect(SW * 0.5f - w * 0.5f * U, Safe.yMax - Margin * U - h * U - row * (h + 10f) * U, w * U, h * U);

    /// <summary>Counter pills top-center (row 0 = top).</summary>
    public static Rect CounterRect(int row, float w = 260f) =>
        new Rect(SW * 0.5f - w * 0.5f * U, Safe.y + Margin * U + row * 46f * U, w * U, 38f * U);

    public static Rect CenterRect(float w, float h) =>
        new Rect(SW * 0.5f - w * 0.5f * U, SH * 0.5f - h * 0.5f * U, w * U, h * U);

    // ---------- Images + styles ----------
    static bool loaded;
    static bool hiRes;
    static Font font;
    static Texture2D roundTex, roundHover, roundActive, roundDisabled, dimTex;
    public static Texture2D JoyBase, JoyKnob;
    static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

    public static GUIStyle Button, ButtonActive, ButtonAccent, Round, RoundActive;
    public static GUIStyle Panel, Pill, Field, Title, Body, BodyCenter, Small, Caption, Big;

    public static readonly Color White = Color.white;
    public static readonly Color Soft = new Color(0.93f, 0.89f, 0.98f);
    public static readonly Color Muted = new Color(0.78f, 0.72f, 0.92f);
    public static readonly Color Peach = new Color(1f, 0.77f, 0.54f);
    public static readonly Color Pink = new Color(0.95f, 0.48f, 0.61f);
    public static readonly Color Ink = new Color(0.14f, 0.10f, 0.23f);

    /// <summary>Call at the top of every OnGUI.</summary>
    public static void Begin()
    {
        GUI.matrix = Matrix4x4.identity;
        bool wantHi = U >= 2f; // sharper images on big screens (buttons are always at least 44 units tall)
        if (!loaded || wantHi != hiRes) Load(wantHi);
        UpdateSizes();
    }

    static Texture2D Tex(string name) => Resources.Load<Texture2D>("UI/" + name);

    static Texture2D Slice(string name, bool hi) => Tex(hi ? name + "_2x" : name) ?? Tex(name);

    static void Load(bool hi)
    {
        loaded = true;
        hiRes = hi;
        font = Resources.Load<Font>("UI/Font");

        roundTex = Tex("round"); roundHover = Tex("round_hover"); roundActive = Tex("round_active"); roundDisabled = Tex("round_disabled");
        JoyBase = Tex("joy_base"); JoyKnob = Tex("joy_knob"); dimTex = Tex("dim");

        int f = hi ? 2 : 1;
        var btnBorder = new RectOffset(22 * f, 22 * f, 22 * f, 22 * f);

        Button = MakeButton(Slice("btn", hi), Slice("btn_hover", hi), Slice("btn_active", hi), Slice("btn_disabled", hi), btnBorder, White);
        ButtonActive = MakeButton(Slice("btn_active", hi), Slice("btn_active", hi), Slice("btn_active", hi), Slice("btn_disabled", hi), btnBorder, Ink);
        ButtonAccent = MakeButton(Slice("btn_accent", hi), Slice("btn_accent", hi), Slice("btn_active", hi), Slice("btn_disabled", hi), btnBorder, Ink);

        Round = MakeButton(roundTex, roundHover, roundActive, roundDisabled, new RectOffset(), White);
        RoundActive = MakeButton(roundActive, roundActive, roundActive, roundDisabled, new RectOffset(), White);

        Panel = new GUIStyle { normal = { background = Slice("panel", hi) }, border = new RectOffset(26 * f, 26 * f, 26 * f, 26 * f) };
        Pill = new GUIStyle
        {
            normal = { background = Slice("pill", hi), textColor = White },
            border = new RectOffset(19 * f, 19 * f, 19 * f, 19 * f),
            alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, font = font,
        };
        Field = new GUIStyle(GUI.skin.textField)
        {
            normal = { background = Slice("field", hi), textColor = Ink },
            focused = { background = Slice("field", hi), textColor = Ink },
            hover = { background = Slice("field", hi), textColor = Ink },
            border = new RectOffset(14 * f, 14 * f, 14 * f, 14 * f),
            alignment = TextAnchor.MiddleCenter, font = font, wordWrap = true,
        };

        Title = Label(FontStyle.Bold, TextAnchor.MiddleCenter, White);
        Big = Label(FontStyle.Bold, TextAnchor.MiddleCenter, White);
        Body = Label(FontStyle.Normal, TextAnchor.UpperLeft, Soft);
        Body.wordWrap = true;
        BodyCenter = Label(FontStyle.Normal, TextAnchor.MiddleCenter, Soft);
        BodyCenter.wordWrap = true;
        Small = Label(FontStyle.Normal, TextAnchor.MiddleCenter, Muted);
        Caption = Label(FontStyle.Bold, TextAnchor.MiddleLeft, Peach);

        icons.Clear();
    }

    static GUIStyle MakeButton(Texture2D normal, Texture2D hover, Texture2D active, Texture2D disabled, RectOffset border, Color text)
    {
        var s = new GUIStyle
        {
            normal = { background = normal, textColor = text },
            hover = { background = hover, textColor = text },
            active = { background = active, textColor = Ink },
            focused = { background = normal, textColor = text },
            onNormal = { background = active, textColor = Ink },
            border = border,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            font = font,
            clipping = TextClipping.Clip,
        };
        s.padding = new RectOffset(12, 12, 4, 4);
        return s;
    }

    static GUIStyle Label(FontStyle fs, TextAnchor a, Color c) =>
        new GUIStyle { fontStyle = fs, alignment = a, normal = { textColor = c }, font = font, richText = true };

    static void UpdateSizes()
    {
        float u = U;
        Button.fontSize = ButtonActive.fontSize = ButtonAccent.fontSize = Mathf.RoundToInt(18 * u);
        Pill.fontSize = Mathf.RoundToInt(17 * u);
        Field.fontSize = Mathf.RoundToInt(20 * u);
        Title.fontSize = Mathf.RoundToInt(24 * u);
        Big.fontSize = Mathf.RoundToInt(34 * u);
        Body.fontSize = BodyCenter.fontSize = Mathf.RoundToInt(18 * u);
        Small.fontSize = Mathf.RoundToInt(14 * u);
        Caption.fontSize = Mathf.RoundToInt(13 * u);
        int pad = Mathf.RoundToInt(10 * u);
        Field.padding = new RectOffset(pad, pad, pad / 2, pad / 2);
    }

    public static Texture2D Icon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!icons.TryGetValue(name, out var t)) { t = Tex("icon_" + name); icons[name] = t; }
        return t;
    }

    // ---------- Widgets ----------
    /// <summary>Round icon button. Returns true when tapped.</summary>
    public static bool RoundButton(Rect r, string icon, bool active = false, bool enabled = true, string caption = null)
    {
        bool was = GUI.enabled;
        GUI.enabled = was && enabled;
        bool clicked = GUI.Button(r, GUIContent.none, active ? RoundActive : Round);
        var tex = Icon(icon);
        if (tex != null)
        {
            float inset = r.width * 0.24f;
            var old = GUI.color;
            GUI.color = enabled ? (active ? White : White) : new Color(1f, 1f, 1f, 0.4f);
            GUI.DrawTexture(new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset * 2f), tex, ScaleMode.ScaleToFit);
            GUI.color = old;
        }
        if (!string.IsNullOrEmpty(caption))
            Shadowed(new Rect(r.x - 20f * U, r.yMax + 1f * U, r.width + 40f * U, 18f * U), caption, Small);
        GUI.enabled = was;
        return clicked;
    }

    public enum Kind { Normal, Active, Accent }

    /// <summary>Pill button with optional icon on the left.</summary>
    public static bool PillButton(Rect r, string text, string icon = null, Kind kind = Kind.Normal, bool enabled = true)
    {
        bool was = GUI.enabled;
        GUI.enabled = was && enabled;
        var style = kind == Kind.Active ? ButtonActive : kind == Kind.Accent ? ButtonAccent : Button;
        var tex = Icon(icon);
        bool clicked;
        if (tex == null)
        {
            clicked = GUI.Button(r, text, style);
        }
        else
        {
            clicked = GUI.Button(r, GUIContent.none, style);
            float s = r.height * 0.46f;
            float total = s + 8f * U + style.CalcSize(new GUIContent(text)).x - style.padding.horizontal;
            float x = r.x + Mathf.Max(14f * U, (r.width - total) * 0.5f);
            var old = GUI.color;
            GUI.color = kind == Kind.Normal ? White : Ink;
            GUI.DrawTexture(new Rect(x, r.y + (r.height - s) * 0.5f, s, s), tex, ScaleMode.ScaleToFit);
            GUI.color = old;
            var label = new GUIStyle(style) { normal = { background = null, textColor = kind == Kind.Normal ? White : Ink },
                                              hover = { background = null }, active = { background = null },
                                              alignment = TextAnchor.MiddleLeft, padding = new RectOffset() };
            GUI.Label(new Rect(x + s + 8f * U, r.y, r.xMax - (x + s + 8f * U), r.height), text, label);
        }
        GUI.enabled = was;
        return clicked;
    }

    public static void PanelBox(Rect r) => GUI.Box(r, GUIContent.none, Panel);

    public static void PillLabel(Rect r, string text) => GUI.Box(r, text, Pill);

    /// <summary>Soft dark layer behind a centered window.</summary>
    public static void Dim()
    {
        if (dimTex != null) GUI.DrawTexture(new Rect(0, 0, SW, SH), dimTex);
    }

    /// <summary>White text with a soft shadow, for text drawn directly over the world.</summary>
    public static void Shadowed(Rect r, string text, GUIStyle style)
    {
        var old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
        float o = Mathf.Max(1f, 1.5f * U);
        GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
        style.normal.textColor = old;
        GUI.Label(r, text, style);
    }

    // ---------- Toasts (one at a time, any script can show one) ----------
    static string toast;
    static float toastUntil;

    public static void Toast(string text, float seconds = 3f)
    {
        toast = text;
        toastUntil = Time.time + seconds;
    }

    /// <summary>Drawn by one script only (NetworkBootstrap).</summary>
    public static void DrawToast()
    {
        if (toast == null || Time.time > toastUntil) return;
        float w = Mathf.Min(520f, 40f + toast.Length * 10f);
        var r = new Rect(SW * 0.5f - w * 0.5f * U, SH * 0.24f, w * U, 50f * U);
        GUI.Box(r, toast, Pill);
    }
}