using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RBGameLobby : NetworkBehaviour
{
    public static RBGameLobby Instance { get; private set; }

    public const string KEY_PLAYER_NAME = "PlayerName";
    private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";

    public event EventHandler OnCreateLobbyStarted;
    public event EventHandler OnCreateLobbyFailed;
    public event EventHandler OnJoinStarted;
    public event EventHandler OnQuickJoinFailed;
    public event EventHandler OnJoinFailed;

    public event EventHandler OnLeftLobby;
    public event EventHandler<LobbyEventArgs> OnJoinedLobby;
    public event EventHandler<LobbyEventArgs> OnJoinedLobbyUpdate;
    public event EventHandler<LobbyEventArgs> OnKickedFromLobby;

    public event EventHandler OnLoadingStart;
    public event EventHandler OnLoadingEnd;

    public class LobbyEventArgs : EventArgs
    {
        public Lobby lobby;
    }

    private Lobby joinedLobby;
    private float heartbeatTimer;

    [SerializeField] private LobbyUI lobbyUI;

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeUnityAuthentication();
    }

    private void Update()
    {
        HandleHeartbeat();
    }

    private async void InitializeUnityAuthentication()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            InitializationOptions initializationOptions = new InitializationOptions();
            initializationOptions.SetProfile(UnityEngine.Random.Range(0, 1000000).ToString());

            await UnityServices.InitializeAsync(initializationOptions);

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private void HandleHeartbeat()
    {
        if (!IsLobbyHost()) return;

        heartbeatTimer -= Time.deltaTime;
        if (heartbeatTimer <= 0f)
        {
            heartbeatTimer = 15f;
            LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
        }
    }

    public bool IsLobbyHost()
    {
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    private async Task<Allocation> AllocateRelay()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(RubberBandMultiplayer.MAX_PLAYER_COUNT - 1);

            return allocation;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to allocate relay: {e}");
            return default;
        }
    }

    private async Task<string> GetRelayJoinCode(Allocation allocation)
    {
        try
        {
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return relayJoinCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to get relay join code: {e}");
            return default;
        }
    }

    private async Task<JoinAllocation> JoinRelay(string relayJoinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            return joinAllocation;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join relay: {e}");
            return default;
        }
    }

    public async void CreateLobby(string lobbyName, bool isPrivate)
    {
        OnCreateLobbyStarted?.Invoke(this, EventArgs.Empty);
        OnLoadingStart?.Invoke(this, EventArgs.Empty);

        try
        {
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, RubberBandMultiplayer.MAX_PLAYER_COUNT, new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = GetPlayer()
            });

            Allocation allocation = await AllocateRelay();

            string relayJoinCode = await GetRelayJoinCode(allocation);

            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    {
                        KEY_RELAY_JOIN_CODE,
                        new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)
                    }
                }
            });
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            lobbyUI.Show();
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });

            RubberBandMultiplayer.Instance.StartHost();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to create lobby: {e}");
            OnCreateLobbyFailed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            OnLoadingEnd?.Invoke(this, EventArgs.Empty);
        }
    }

    /*public async void QuickJoin()
    {
        OnJoinStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions
            {
                Player = GetPlayer()
            });

            lobbyUI.Show();
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });

            RubberBandMultiplayer.Instance.StartClient();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to quick join lobby: {e}");
            OnQuickJoinFailed?.Invoke(this, EventArgs.Empty);
        }
    } */

    // Code-less quick join: grab the newest open lobby and connect. Keeps testing to two clicks
    // (Create Game / Join Game) with no join code to copy around.
    public async void JoinNewestLobby()
    {
        OnJoinStarted?.Invoke(this, EventArgs.Empty);
        OnLoadingStart?.Invoke(this, EventArgs.Empty);

        try
        {
            var queryResponse = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
            {
                Count = 1,
                Order = new List<QueryOrder>
            {
                new QueryOrder(asc: false, field: QueryOrder.FieldOptions.Created)
            },
                Filters = new List<QueryFilter>
            {
                new QueryFilter(
                    field: QueryFilter.FieldOptions.AvailableSlots,
                    op: QueryFilter.OpOptions.GT,
                    value: "0"
                )
            }
            });

            if (queryResponse.Results.Count == 0)
            {
                Debug.LogError("No lobbies found.");
                OnQuickJoinFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            string lobbyId = queryResponse.Results[0].Id;

            try
            {
                joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions
                {
                    Player = GetPlayer()
                });

                string relayJoinCode = joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value;

                JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));
            }


            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyConflict)
            {
                // Already a member from a previous session, just fetch it directly.
                Debug.LogWarning("Already in lobby, reconnecting...");
                joinedLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);
            }

            if (joinedLobby == null)
            {
                Debug.LogError("joinedLobby is null after join attempt.");
                OnQuickJoinFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            lobbyUI.Show();
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
            RubberBandMultiplayer.Instance.StartClient();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"JoinNewestLobby failed. Reason: {e.Reason} | Message: {e.Message}");
            OnQuickJoinFailed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            OnLoadingEnd?.Invoke(this, EventArgs.Empty);
        }
    }

    public async void OnNetworkClientConnected()
    {
        joinedLobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
        NotifyLobbyUpdatedClientRpc();
    }

    [ClientRpc]
    private void NotifyLobbyUpdatedClientRpc()
    {
        OnJoinedLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
    }

    private Player GetPlayer()
    {
        string name = RubberBandMultiplayer.Instance != null ? RubberBandMultiplayer.Instance.GetPlayerName() : "Player";

        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {
                    KEY_PLAYER_NAME,
                    new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, name)
                }
            }
        };
    }

    public async void LeaveLobby()
    {
        // Disconnect Netcode first so the next Create/Join starts from a clean state (this also
        // unsubscribes the host/client callbacks that StartHost/StartClient added).
        if (RubberBandMultiplayer.Instance != null)
            RubberBandMultiplayer.Instance.Shutdown();

        // Reset lobby state unconditionally, even if the service call below fails, so we can never
        // get stuck "in" a lobby we've already left.
        Lobby lobby = joinedLobby;
        joinedLobby = null;

        if (lobby != null)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
            }
            catch (LobbyServiceException e)
            {
                Debug.Log(e);
            }
        }

        OnLeftLobby?.Invoke(this, EventArgs.Empty);
    }

    public async void KickPlayer(string playerId)
    {
        if (!IsLobbyHost()) return;

        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, playerId);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public Lobby GetJoinedLobby()
    {
        return joinedLobby;
    }

    public Lobby GetLobby()
    {
        return joinedLobby;
    }

    public async void UpdatePlayerName(string playerName)
    {
        if (RubberBandMultiplayer.Instance != null)
        {
            RubberBandMultiplayer.Instance.SetPlayerName(playerName);
        }

        if (joinedLobby == null) return;

        try
        {
            var options = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {
                        KEY_PLAYER_NAME,
                        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName)
                    }
                }
            };

            string playerId = AuthenticationService.Instance.PlayerId;
            joinedLobby = await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, playerId, options);

            OnJoinedLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public async void StartGame()
    {
        if (!IsLobbyHost()) return;

        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
            {
                IsLocked = true
            });
            //Loader.LoadNetwork(GameScene.GameScene);
            Loader.LoadNetwork(GameScene.RBDemo);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private void OnApplicationQuit()
    {
        if (joinedLobby == null) return;
        if (!AuthenticationService.Instance.IsSignedIn) return;

        if (IsLobbyHost())
            _ = LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
        else
            _ = LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
    }
}
