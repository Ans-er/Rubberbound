using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyMenuUI : MonoBehaviour
{
    // Set in Awake and kept alive for the whole scene (hiding only SetActive(false)s the object,
    // it isn't destroyed), so LobbyUI can reactivate this menu when the player leaves a lobby.
    public static LobbyMenuUI Instance { get; private set; }

    [SerializeField] private Button createLobbyButton;
    [SerializeField] private Button quickJoinButton;
    [SerializeField] private LobbyCreateUI lobbyCreateUI;
    [SerializeField] private TMP_InputField playerNameInputField;

    private void Awake()
    {
        Instance = this;
        Show();

        createLobbyButton.onClick.AddListener(() =>
        {
            string playerName;
            if (string.IsNullOrEmpty(playerNameInputField.text))
            {
                playerName = "Player" + Random.Range(100, 1000);
            }
            else
            {
                playerName = playerNameInputField.text;
            }
            RubberBandMultiplayer.Instance.SetPlayerName(playerName);
            RBGameLobby.Instance.CreateLobby("Wajoe", false);
            //lobbyCreateUI.Show();
            Hide();
        });

        quickJoinButton.onClick.AddListener(() =>
        {
            string playerName;
            if (string.IsNullOrEmpty(playerNameInputField.text))
            {
                playerName = "Player" + Random.Range(100, 1000);
            }
            else
            {
                playerName = playerNameInputField.text;
            }
            RubberBandMultiplayer.Instance.SetPlayerName(playerName);
            RBGameLobby.Instance.JoinNewestLobby();
            Hide();
        });
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
