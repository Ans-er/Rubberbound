using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    [SerializeField] private Transform playerSingleTemplate;
    [SerializeField] private Transform container;
    [SerializeField] private TextMeshProUGUI lobbyNameText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private Button startGameButton;


    private void Awake()
    {
        Instance = this;

        playerSingleTemplate.gameObject.SetActive(false);

        leaveLobbyButton.onClick.AddListener(() =>
        {
            // Tell the lobby service we're leaving (fire-and-forget), then deterministically swap the
            // UI back to the main menu right here. We don't wait on the OnLeftLobby event because the
            // async leave call can be slow or throw, which would leave the menu hidden forever.
            RBGameLobby.Instance.LeaveLobby();
            ReturnToMenu();
        });

        startGameButton.onClick.AddListener(() =>
        {
            RBGameLobby.Instance.StartGame();
        });

        RBGameLobby.Instance.OnJoinedLobby += UpdateLobby_Event;
        RBGameLobby.Instance.OnJoinedLobbyUpdate += UpdateLobby_Event;
        RBGameLobby.Instance.OnLeftLobby += LobbyManager_OnLeftLobby;
        RBGameLobby.Instance.OnKickedFromLobby += LobbyManager_OnLeftLobby;

        Hide();
    }

    private void LobbyManager_OnLeftLobby(object sender, System.EventArgs e)
    {
        // Fired when we leave or get kicked: same swap back to the main menu as the leave button.
        ReturnToMenu();
    }

    // Deactivate this in-lobby UI and bring the create/join menu back.
    private void ReturnToMenu()
    {
        ClearLobby();
        Hide();
        if (LobbyMenuUI.Instance != null) LobbyMenuUI.Instance.Show();
    }

    private void UpdateLobby_Event(object sender, RBGameLobby.LobbyEventArgs e)
    {
        UpdateLobby(e.lobby);
    }

    private void UpdateLobby(Lobby lobby)
    {
        if (lobby == null)
        {
            Debug.LogWarning("LobbyUI: lobby is null, aborting update.");
            return;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        ClearLobby();

        foreach (Player player in lobby.Players)
        {
            Transform playerSingleTransform = Instantiate(playerSingleTemplate, container);
            playerSingleTransform.gameObject.SetActive(true);
            PlayerSingleTemplate lobbyPlayerSingleUI = playerSingleTransform.GetComponent<PlayerSingleTemplate>();

            lobbyPlayerSingleUI.SetKickPlayerButtonVisible(
                RBGameLobby.Instance.IsLobbyHost() &&
                player.Id != AuthenticationService.Instance.PlayerId
            );

            lobbyPlayerSingleUI.UpdatePlayer(player);
        }

        lobbyNameText.text = lobby.Name;
        playerCountText.text = lobby.Players.Count + "/" + lobby.MaxPlayers;
    }

    private void ClearLobby()
    {
        foreach (Transform child in container)
        {
            if (child == playerSingleTemplate) continue;
            Destroy(child.gameObject);
        }
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }
}
