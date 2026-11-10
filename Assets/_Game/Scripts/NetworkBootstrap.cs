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
    string status = "Starting...";
    bool busy;

    UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();
    string ConnType => connection == RelayConnection.WSS ? "wss" : "dtls";

    async void Start()
    {
        Application.targetFrameRate = 60;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnect;
        await InitServices();
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnect;
    }

    async Task InitServices()
    {
        try
        {
            status = "Signing in...";
            var options = new InitializationOptions();
            // Unique profile per launch so two editor instances (Multiplayer Play Mode) don't share one account.
            options.SetProfile("p" + UnityEngine.Random.Range(0, 1000000));
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            status = "Ready";
        }
        catch (Exception e)
        {
            status = "Sign-in failed: " + e.Message;
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
            status = "Creating trip...";
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(1); // 1 other player
            joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            Transport.UseWebSockets = connection == RelayConnection.WSS;
            Transport.SetRelayServerData(AllocationUtils.ToRelayServerData(alloc, ConnType));

            if (NetworkManager.Singleton.StartHost())
                status = "Hosting (" + ConnType + ")";
            else
                status = "StartHost failed";
        }
        catch (Exception e)
        {
            status = "Host failed: " + e.Message;
            Debug.LogException(e);
        }
        busy = false;
    }

    async void Join(string code)
    {
        busy = true;
        try
        {
            status = "Joining " + code + "...";
            JoinAllocation alloc = await RelayService.Instance.JoinAllocationAsync(code);

            Transport.UseWebSockets = connection == RelayConnection.WSS;
            Transport.SetRelayServerData(AllocationUtils.ToRelayServerData(alloc, ConnType));

            if (NetworkManager.Singleton.StartClient())
                status = "Connecting (" + ConnType + ")...";
            else
                status = "StartClient failed";
        }
        catch (Exception e)
        {
            status = "Join failed: " + e.Message;
            Debug.LogException(e);
        }
        busy = false;
    }

    void OnDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
            status = "Partner left";
        else
            status = "Disconnected" + (string.IsNullOrEmpty(nm.DisconnectReason) ? "" : ": " + nm.DisconnectReason);
    }

    // ---------- Temporary debug UI (top-right corner, so the left half stays free for the joystick) ----------
    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        float s = Screen.height / 540f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = 300f;
        GUILayout.BeginArea(new Rect(Screen.width / s - w - 10f, 10f, w, 380f), GUI.skin.box);

        GUILayout.Label(status);

        if (!nm.IsClient && !nm.IsServer)
        {
            GUI.enabled = !busy && ServicesReady;

            if (GUILayout.Button("Connection: " + connection, GUILayout.Height(36)))
                connection = connection == RelayConnection.DTLS ? RelayConnection.WSS : RelayConnection.DTLS;

            if (GUILayout.Button("Create Trip (Host)", GUILayout.Height(50)))
                Host();

            GUILayout.Space(8);
            GUILayout.Label("Join code:");
            inputCode = GUILayout.TextField(inputCode, 6, GUILayout.Height(40)).ToUpperInvariant();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Paste", GUILayout.Height(44)))
                inputCode = GUIUtility.systemCopyBuffer.Trim().ToUpperInvariant();
            if (GUILayout.Button("Join Trip", GUILayout.Height(44)) && inputCode.Length == 6)
                Join(inputCode);
            GUILayout.EndHorizontal();

            GUI.enabled = true;
        }
        else
        {
            if (nm.IsHost && !string.IsNullOrEmpty(joinCode))
            {
                GUILayout.Label("Join code: " + joinCode);
                if (GUILayout.Button("Copy code", GUILayout.Height(40)))
                    GUIUtility.systemCopyBuffer = joinCode;
                GUILayout.Label("Players: " + nm.ConnectedClientsIds.Count + "/2");
            }

            GUILayout.Label("Ping: " + PingText(nm));

            if (GUILayout.Button("Leave", GUILayout.Height(40)))
            {
                nm.Shutdown();
                joinCode = "";
                status = "Ready";
            }
        }

        GUILayout.EndArea();
    }

    string PingText(NetworkManager nm)
    {
        if (nm.IsServer)
        {
            foreach (var id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId)
                    return Transport.GetCurrentRtt(id) + " ms";
            return "waiting for partner";
        }
        return nm.IsConnectedClient ? Transport.GetCurrentRtt(NetworkManager.ServerClientId) + " ms" : "-";
    }
}