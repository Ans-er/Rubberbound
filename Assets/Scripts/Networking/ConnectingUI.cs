using System;

using UnityEngine;

public class ConnectingUI : MonoBehaviour
{
    private void Start()
    {
        RubberBandMultiplayer.Instance.OnTryingToJoinGame += RubberBandMultiplayer_OnTryingToJoinGame;
        RubberBandMultiplayer.Instance.OnFailedToJoinGame += RubberBandMultiplayer_OnFailedToJoinGame;

        Hide();
    }

    private void RubberBandMultiplayer_OnFailedToJoinGame(object sender, EventArgs e)
    {
        Hide();
    }

    private void RubberBandMultiplayer_OnTryingToJoinGame(object sender, EventArgs e)
    {
        Show();
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
        RubberBandMultiplayer.Instance.OnTryingToJoinGame -= RubberBandMultiplayer_OnTryingToJoinGame;
        RubberBandMultiplayer.Instance.OnFailedToJoinGame -= RubberBandMultiplayer_OnFailedToJoinGame;
    }
}
