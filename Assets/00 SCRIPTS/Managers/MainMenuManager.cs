using TMPro;
using UnityEngine;

// Manager cho Main Menu - hien thi ten nguoi choi
public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text playerNameText;  // Text hien thi "Hello, [name]"
    [SerializeField] private GameObject JoinPanel; // Panel nhap code phong

    private void Start()
    {
        // Hien thi ten nguoi choi tu GameManager
        JoinPanel.SetActive(false);
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

    public void DisplayJoinPanel()
    {
        // Hien thi panel nhap code phong
        JoinPanel.SetActive(true);
    }

    public void HideJoinPanel()
    {
        // An panel nhap code phong
        JoinPanel.SetActive(false);
    }

    public void OnExitButtonClicked()
    {
        // Thoat game
        Debug.Log("[MainMenuManager] Exit button clicked, thoat game");
        Application.Quit();
    }

}
