using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class GameOverUI : MonoBehaviour {


    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private Button playAgainButton;


    private void Awake() {
        playAgainButton.onClick.AddListener(() => {
            // Replay from the initial spawn (server-authoritative): route through the local player.
            // The win screen hides itself once the server confirms the reset (OnLevelReset, below).
            var nm = NetworkManager.Singleton;
            var playerObject = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (playerObject != null)
            {
                var respawn = playerObject.GetComponent<PlayerRespawn>();
                if (respawn != null) respawn.RequestReplayFromSpawnServerRpc();
            }
        });
    }

    private void Start() {
        if (RBGameManager.Instance != null)
        {
            RBGameManager.Instance.OnLevelCompleted += RBGameManager_OnLevelCompleted;
            RBGameManager.Instance.OnLevelReset += RBGameManager_OnLevelReset;
        }

        Hide();
    }

    private void RBGameManager_OnLevelReset(object sender, System.EventArgs e)
    {
        Hide();
    }

    private void RBGameManager_OnLevelCompleted(object sender, RBGameManager.LevelCompletedEventArgs e)
    {
        Show();
        if (timeText != null)
        {
            timeText.text = e.FormattedTime;
        }
    }

    private void Show() {
        gameObject.SetActive(true);

        // Free the cursor so the player can click Play Again. This also stops the camera, which
        // only looks while the cursor is locked.
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        playAgainButton.Select();
    }

    private void Hide() {
        gameObject.SetActive(false);

        // Back to gameplay: re-lock the cursor so look control resumes for the next run.
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void OnDestroy()
    {
        if (RBGameManager.Instance != null)
        {
            RBGameManager.Instance.OnLevelCompleted -= RBGameManager_OnLevelCompleted;
            RBGameManager.Instance.OnLevelReset -= RBGameManager_OnLevelReset;
        }
    }


}