using TMPro;
using UnityEngine;

// Manager cho Main Menu - hien thi ten nguoi choi
public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text playerNameText;  // Text hien thi "Hello, [name]"

    private void Start()
    {
        // Hien thi ten nguoi choi tu GameManager
        DisplayPlayerName();
        
        Debug.Log("[MainMenuManager] Main Menu da san sang");
    }

    // Hien thi ten nguoi choi
    private void DisplayPlayerName()
    {
        if (GameManager.Instant == null)
        {
            Debug.LogError("[MainMenuManager] GameManager khong ton tai!");
            
            // Hien thi text mac dinh
            if (playerNameText != null)
            {
                playerNameText.text = "Hello, Player";
            }
            return;
        }

        if (string.IsNullOrEmpty(GameManager.Instant.Nickname))
        {
            Debug.LogWarning("[MainMenuManager] Nickname trong GameManager rong!");
            
            // Hien thi text mac dinh
            if (playerNameText != null)
            {
                playerNameText.text = "Hello, Player";
            }
            return;
        }

        // Hien thi ten nguoi choi
        if (playerNameText != null)
        {
            playerNameText.text = "Hello, " + GameManager.Instant.Nickname;
            Debug.Log("[MainMenuManager] Da hien thi ten: " + GameManager.Instant.Nickname);
        }
        else
        {
            Debug.LogError("[MainMenuManager] PlayerNameText chua duoc gan trong Inspector!");
        }
    }
}
