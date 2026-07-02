using TMPro;

using UnityEngine;
using UnityEngine.UI;

public class LobbyCreateUI : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    [SerializeField] private Button createPublicButton;
    [SerializeField] private Button createPrivateButton;
    [SerializeField] private TMP_InputField lobbyNameInputField;
    [SerializeField] private LobbyMenuUI lobbyMenuUI;

    private void Awake()
    {
        createPublicButton.onClick.AddListener(() =>
        {
            RBGameLobby.Instance.CreateLobby(lobbyNameInputField.text, false);
            Hide();
        });
        createPrivateButton.onClick.AddListener(() =>
        {
            RBGameLobby.Instance.CreateLobby(lobbyNameInputField.text, true);
            Hide();
        });
        closeButton.onClick.AddListener(() =>
        {
            Hide();
            lobbyMenuUI.Show();
        });
    }

    private void Start()
    {
        Hide();
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
