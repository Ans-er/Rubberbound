using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RubberBandMultiplayer : NetworkBehaviour
{
    public static RubberBandMultiplayer Instance { get; private set; }

    public const int MAX_PLAYER_COUNT = 2;
    private const string PLAYER_PREFS_PLAYER_NAME = "PlayerName";

    public event EventHandler OnTryingToJoinGame;
    public event EventHandler OnFailedToJoinGame;

    private string playerName;

    [Header("UI")]
    [SerializeField] private GamePauseUI gamePauseUI;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        playerName = PlayerPrefs.GetString(PLAYER_PREFS_PLAYER_NAME, "PlayerName" + UnityEngine.Random.Range(100, 1000));
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (gamePauseUI == null)
            {
                gamePauseUI = FindObjectOfType<GamePauseUI>(true);
            }

            if (gamePauseUI != null)
            {
                gamePauseUI.Toggle();
            }
        }
    }

    public string GetPlayerName()
    {
        return playerName;
    }

    public void SetPlayerName(string playerName)
    {
        this.playerName = playerName;
        PlayerPrefs.SetString(PLAYER_PREFS_PLAYER_NAME, playerName);
    }

    public void StartHost()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback += NetworkManager_ConnectionApprovalCallback;
        NetworkManager.Singleton.OnClientConnectedCallback += NetworkManager_OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        OnTryingToJoinGame?.Invoke(this, EventArgs.Empty);

        NetworkManager.Singleton.OnClientConnectedCallback += NetworkManager_OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_Client_OnClientDisconnectCallback;
        NetworkManager.Singleton.StartClient();
    }

    // Tears down the current connection and removes the callbacks StartHost/StartClient added, so a
    // later Create/Join starts clean. Netcode never clears these itself, and leaving them subscribed
    // would double-fire on the next connection. Safe to call when nothing is running.
    public void Shutdown()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.ConnectionApprovalCallback -= NetworkManager_ConnectionApprovalCallback;
        nm.OnClientConnectedCallback -= NetworkManager_OnClientConnectedCallback;
        nm.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
        nm.OnClientDisconnectCallback -= NetworkManager_Client_OnClientDisconnectCallback;

        if (nm.IsListening || nm.IsClient || nm.IsServer)
            nm.Shutdown();
    }

    private void NetworkManager_ConnectionApprovalCallback(NetworkManager.ConnectionApprovalRequest connectionApprovalRequest, NetworkManager.ConnectionApprovalResponse connectionApprovalResponse)
    {
        if (SceneManager.GetActiveScene().name != GameScene.LobbyScene.ToString())
        {
            connectionApprovalResponse.Approved = false;
            connectionApprovalResponse.Reason = "Game in progress";
            return;
        }

        if (NetworkManager.Singleton.ConnectedClientsIds.Count >= MAX_PLAYER_COUNT)
        {
            connectionApprovalResponse.Approved = false;
            connectionApprovalResponse.Reason = "Game is full";
            return;
        }

        connectionApprovalResponse.Approved = true;
        connectionApprovalResponse.CreatePlayerObject = true;
    }

    private void NetworkManager_OnClientConnectedCallback(ulong clientId)
    {
        if (IsHost)
            RBGameLobby.Instance.OnNetworkClientConnected();
    }

    private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
    {
        OnFailedToJoinGame?.Invoke(this, EventArgs.Empty);
    }

    private void NetworkManager_Client_OnClientDisconnectCallback(ulong clientId)
    {
        OnFailedToJoinGame?.Invoke(this, EventArgs.Empty);
    }
}
