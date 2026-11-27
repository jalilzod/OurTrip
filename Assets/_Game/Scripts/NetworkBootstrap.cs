using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Day 1: signs in to Unity Gaming Services, creates/joins a Relay session with a join code,
/// and starts NGO as host or client. Includes a temporary on-screen debug UI (replaced by real UI later).
/// Put this on the same GameObject as NetworkManager + UnityTransport.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    public enum RelayConnection { DTLS, WSS }

    [Tooltip("DTLS = encrypted UDP (fastest). WSS = WebSocket over TCP (use if your VPN blocks UDP).")]
    [SerializeField] RelayConnection connection = RelayConnection.DTLS;

    string joinCode = "";
    string inputCode = "";
    string status = "Starting...";   // stored in English, shown with Loc.T
    bool busy;

    UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();
    string ConnType => connection == RelayConnection.WSS ? "wss" : "dtls";

    async void Start()
    {
        Application.targetFrameRate = 60;
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        NetworkManager.Singleton.OnClientConnectedCallback += OnConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnect;
        await InitServices();
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnect;
        }
    }

    void OnConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            if (clientId != nm.LocalClientId) status = Loc.T("Partner joined");
        }
        else if (clientId == nm.LocalClientId)
        {
            status = Loc.F("Connected ({0})", ConnType);
        }
    }

    async Task InitServices()
    {
        try
        {
            status = Loc.T("Signing in...");
            var options = new InitializationOptions();
            // Unique profile per launch so two editor instances (Multiplayer Play Mode) don't share one account.
            options.SetProfile("p" + UnityEngine.Random.Range(0, 1000000));
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            status = Loc.T("Ready");
        }
        catch (Exception e)
        {
            status = Loc.F("Sign-in failed: {0}", e.Message);
            Debug.LogException(e);
        }
    }

    bool ServicesReady =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    async void Host()
    {
        busy = true;
        try
        {
            status = Loc.T("Creating trip...");
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(1); // 1 other player
            joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            Transport.UseWebSockets = connection == RelayConnection.WSS;
            Transport.SetRelayServerData(AllocationUtils.ToRelayServerData(alloc, ConnType));

            if (NetworkManager.Singleton.StartHost())
                status = Loc.F("Hosting ({0})", ConnType);
            else
                status = Loc.T("Could not create the trip");
        }
        catch (Exception e)
        {
            status = Loc.F("Host failed: {0}", e.Message);
            Debug.LogException(e);
        }
        busy = false;
    }

    async void Join(string code)
    {
        busy = true;
        try
        {
            status = Loc.F("Joining {0}...", code);
            JoinAllocation alloc = await RelayService.Instance.JoinAllocationAsync(code);

            Transport.UseWebSockets = connection == RelayConnection.WSS;
            Transport.SetRelayServerData(AllocationUtils.ToRelayServerData(alloc, ConnType));

            if (NetworkManager.Singleton.StartClient())
                status = Loc.F("Connecting ({0})...", ConnType);
            else
                status = Loc.T("Could not connect");
        }
        catch (Exception e)
        {
            status = Loc.F("Join failed: {0}", e.Message);
            Debug.LogException(e);
        }
        busy = false;
    }

    void OnDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
            status = Loc.T("Partner left");
        else
            status = string.IsNullOrEmpty(nm.DisconnectReason) ? Loc.T("Disconnected") : Loc.F("Disconnected: {0}", nm.DisconnectReason);
    }

    // ---------- Temporary debug UI (top-right corner, so the left half stays free for the joystick) ----------
    bool sessionOpen;

    void OnGUI()
    {
        UiTheme.Begin();
        var nm = NetworkManager.Singleton;
        if (nm == null || PhotoMode.HideUI) return;

        UiTheme.DrawToast();
        float u = UiTheme.U;

        if (!nm.IsClient && !nm.IsServer)
        {
            DrawLobby(u);
            return;
        }

        // Menu toggle (top-left). The other scripts draw their round buttons in the column under it.
        if (UiTheme.RoundButton(UiTheme.MenuButtonRect, UiTheme.MenuOpen ? "close" : "menu", UiTheme.MenuOpen))
        {
            UiTheme.MenuOpen = !UiTheme.MenuOpen;
            if (!UiTheme.MenuOpen) UiTheme.OpenPanel = null;
        }

        // Session (top-right): trip code, ping, leave.
        var sb = UiTheme.TopRightButton;
        if (UiTheme.RoundButton(sb, "wifi", sessionOpen)) sessionOpen = !sessionOpen;
        if (!sessionOpen) return;

        var p = new Rect(sb.xMax - 300f * u, sb.yMax + 10f * u, 300f * u, 270f * u);
        UiTheme.PanelBox(p);
        float x = p.x + 20f * u, w = p.width - 40f * u, y = p.y + 16f * u;

        if (nm.IsHost && !string.IsNullOrEmpty(joinCode))
        {
            GUI.Label(new Rect(x, y, w, 18f * u), Loc.T("TRIP CODE"), UiTheme.Caption);
            GUI.Label(new Rect(x, y + 18f * u, w - 60f * u, 44f * u), joinCode, new GUIStyle(UiTheme.Big) { alignment = TextAnchor.MiddleLeft });
            if (UiTheme.RoundButton(new Rect(p.xMax - 20f * u - 48f * u, y + 16f * u, 48f * u, 48f * u), "copy"))
            {
                GUIUtility.systemCopyBuffer = joinCode;
                UiTheme.Toast(Loc.T("Code copied. Send it to your love"));
            }
            y += 70f * u;
        }
        else y += 10f * u;

        GUI.Label(new Rect(x, y, w, 24f * u), Loc.F("Players {0}/2   ·   Ping {1}", nm.ConnectedClientsIds.Count, PingText(nm)), new GUIStyle(UiTheme.Body) { alignment = TextAnchor.MiddleLeft });
        GUI.Label(new Rect(x, y + 26f * u, w, 20f * u), Loc.T(status), new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleLeft });
        y += 58f * u;

        if (UiTheme.PillButton(new Rect(x, y, w, 50f * u), Loc.T("Leave trip"), "leave"))
        {
            nm.Shutdown();
            joinCode = "";
            status = Loc.T("Ready");
            sessionOpen = false;
            UiTheme.MenuOpen = false;
            UiTheme.OpenPanel = null;
        }
        LanguageSwitch(new Rect(x, p.yMax - 56f * u, 110f * u, 44f * u));
        GUI.Label(new Rect(x + 116f * u, p.yMax - 46f * u, w - 116f * u, 24f * u), "Our Trip v" + Application.version, new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleRight });
    }

    /// <summary>Two small pills: EN | RU. Switches the whole UI immediately.</summary>
    static void LanguageSwitch(Rect r)
    {
        float half = (r.width - 4f) * 0.5f;
        bool ru = Loc.IsRussian;
        if (UiTheme.PillButton(new Rect(r.x, r.y, half, r.height), "EN", null, ru ? UiTheme.Kind.Normal : UiTheme.Kind.Active)) Loc.Current = Loc.Lang.EN;
        if (UiTheme.PillButton(new Rect(r.x + half + 4f, r.y, half, r.height), "RU", null, ru ? UiTheme.Kind.Active : UiTheme.Kind.Normal)) Loc.Current = Loc.Lang.RU;
    }

    void DrawLobby(float u)
    {
        var p = UiTheme.CenterRect(440f, 420f);
        UiTheme.PanelBox(p);
        float x = p.x + 30f * u, w = p.width - 60f * u, y = p.y + 18f * u;

        LanguageSwitch(new Rect(p.xMax - 124f * u, p.y + 14f * u, 110f * u, 44f * u));
        GUI.Label(new Rect(p.x, y, p.width, 44f * u), "Our Trip", UiTheme.Big);
        GUI.Label(new Rect(p.x, y + 44f * u, p.width, 22f * u), Loc.T(status), UiTheme.Small);
        y += 80f * u;

        bool ready = !busy && ServicesReady;
        bool was = GUI.enabled;
        GUI.enabled = ready;

        GUI.Label(new Rect(x, y, w, 18f * u), Loc.T("I AM"), UiTheme.Caption);
        y += 22f * u;
        int choice = CharacterVisuals.LocalChoice;
        float half = (w - 10f * u) * 0.5f;
        if (UiTheme.PillButton(new Rect(x, y, half, 48f * u), Loc.T("Boy"), null, choice == 0 ? UiTheme.Kind.Active : UiTheme.Kind.Normal)) CharacterVisuals.LocalChoice = 0;
        if (UiTheme.PillButton(new Rect(x + half + 10f * u, y, half, 48f * u), Loc.T("Girl"), null, choice == 1 ? UiTheme.Kind.Active : UiTheme.Kind.Normal)) CharacterVisuals.LocalChoice = 1;
        y += 62f * u;

        if (UiTheme.PillButton(new Rect(x, y, w, 56f * u), Loc.T("Create a trip"), "heart", UiTheme.Kind.Accent)) Host();
        y += 74f * u;

        GUI.Label(new Rect(x, y, w, 18f * u), Loc.T("OR JOIN WITH A CODE"), UiTheme.Caption);
        y += 22f * u;
        float joinW = 110f * u, pasteW = 50f * u;
        inputCode = GUI.TextField(new Rect(x, y, w - joinW - pasteW - 20f * u, 50f * u), inputCode, 6, UiTheme.Field).ToUpperInvariant();
        if (UiTheme.RoundButton(new Rect(x + w - joinW - pasteW - 10f * u, y, pasteW, 50f * u), "paste"))
            inputCode = GUIUtility.systemCopyBuffer.Trim().ToUpperInvariant();
        if (UiTheme.PillButton(new Rect(x + w - joinW, y, joinW, 50f * u), Loc.T("Join"), null, UiTheme.Kind.Active, inputCode.Length == 6))
            Join(inputCode);
        y += 64f * u;

        GUI.enabled = was && !busy;
        var small = new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleLeft };
        if (GUI.Button(new Rect(x, y, w * 0.6f, 24f * u), Loc.F("Connection: {0} (tap to change)", connection), small))
            connection = connection == RelayConnection.DTLS ? RelayConnection.WSS : RelayConnection.DTLS;
        GUI.Label(new Rect(x + w * 0.6f, y, w * 0.4f, 24f * u), "v" + Application.version, new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleRight });
        GUI.enabled = was;
    }

    string PingText(NetworkManager nm)
    {
        if (nm.IsServer)
        {
            foreach (var id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId)
                    return Transport.GetCurrentRtt(id) + " ms";
            return Loc.T("waiting for partner");
        }
        return nm.IsConnectedClient ? Transport.GetCurrentRtt(NetworkManager.ServerClientId) + " ms" : "-";
    }
}