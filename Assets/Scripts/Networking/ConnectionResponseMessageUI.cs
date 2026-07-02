using System;

using TMPro;

using Unity.Netcode;

using UnityEngine;
using UnityEngine.UI;

public class ConnectionResponseMessageUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button closeButton;

    private void Awake()
    {
        closeButton.onClick.AddListener(Hide);
    }

    private void Start()
    {
        RubberBandMultiplayer.Instance.OnFailedToJoinGame += RubberBandMultiplayer_OnFailedToJoinGame;

        Hide();
    }

    private void RubberBandMultiplayer_OnFailedToJoinGame(object sender, EventArgs e)
    {
        Show();

        messageText.text = NetworkManager.Singleton.DisconnectReason;

        if( messageText.text == string.Empty )
        {
            messageText.text = "Failed to join game.";
        }
    }

    private void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
           RubberBandMultiplayer.Instance.OnFailedToJoinGame -= RubberBandMultiplayer_OnFailedToJoinGame;
    }
}
