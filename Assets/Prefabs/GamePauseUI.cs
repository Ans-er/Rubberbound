using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class GamePauseUI : NetworkBehaviour
{


    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button checkpointButton;

    [SerializeField] private GameObject container;

    private bool isVisible;


    public override void OnNetworkSpawn()
    {
        if (container == null)
        {
            container = gameObject;
        }

        resumeButton.onClick.AddListener(() =>
        {
            Hide();
        });
        mainMenuButton.onClick.AddListener(() =>
        {
            Loader.LoadNetwork(GameScene.LobbyScene);
        });

        // Return to last checkpoint: respawns BOTH players at their stored checkpoints (the rope
        // ties them together, so RespawnBothPlayers resets both — same path as falling off the map).
        if (checkpointButton != null)
        {
            checkpointButton.onClick.AddListener(() =>
            {
                var nm = NetworkManager.Singleton;
                var playerObject = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
                if (playerObject != null)
                {
                    var respawn = playerObject.GetComponent<PlayerRespawn>();
                    if (respawn != null) respawn.RequestRespawnServerRpc();
                }
                Hide();
            });
        }

        if (optionsButton != null)
        {
            optionsButton.gameObject.SetActive(false);
        }

        DontDestroyOnLoad(gameObject);

    }

    private void Start()
    {
        Hide();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        if (isVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    private void Show()
    {
        container.SetActive(true);
        isVisible = true;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        resumeButton.Select();
    }

    private void Hide()
    {
        container.SetActive(false);
        isVisible = false;

        // If we're in gameplay and the local player controller locked the cursor,
        // allow it to re-lock after closing the menu.
        if (UnityEngine.Object.FindObjectOfType<PhysicsBasedCharacterController>() != null)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

}