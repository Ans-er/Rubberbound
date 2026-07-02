using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class PlayerSingleTemplate : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private Button kickPlayerButton;

    private Player player;

    private void Awake()
    {
        kickPlayerButton.onClick.AddListener(KickPlayer);
    }

    public void SetKickPlayerButtonVisible(bool visible)
    {
        kickPlayerButton.gameObject.SetActive(visible);
    }

    public void UpdatePlayer(Player player)
    {
        this.player = player;

        if (playerNameText == null) return;

        if (player != null && player.Data != null && player.Data.TryGetValue(RBGameLobby.KEY_PLAYER_NAME, out PlayerDataObject nameData))
        {
            playerNameText.text = nameData.Value;
        }
        else
        {
            playerNameText.text = "Unnamed";
        }
    }

    private void KickPlayer()
    {
        if (player != null)
        {
            RBGameLobby.Instance.KickPlayer(player.Id);
        }
    }
}
