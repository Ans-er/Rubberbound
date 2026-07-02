using System;
using UnityEngine;

public class LoadingUI : MonoBehaviour
{
    private void Start()
    {
        RBGameLobby.Instance.OnLoadingStart += RBGameLobby_OnLoadingStart;
        RBGameLobby.Instance.OnLoadingEnd += RBGameLobby_OnLoadingEnd;

        Hide();
    }

    private void RBGameLobby_OnLoadingStart(object sender, EventArgs e)
    {
        Show();
    }

    private void RBGameLobby_OnLoadingEnd(object sender, EventArgs e)
    {
        Hide();
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
        if (RBGameLobby.Instance == null) return;

        RBGameLobby.Instance.OnLoadingStart -= RBGameLobby_OnLoadingStart;
        RBGameLobby.Instance.OnLoadingEnd -= RBGameLobby_OnLoadingEnd;
    }
}
