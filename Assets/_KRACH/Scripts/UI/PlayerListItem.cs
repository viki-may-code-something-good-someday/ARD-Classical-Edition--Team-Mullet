using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerListItem : MonoBehaviour
{
    public string playerName;
    public int connectionID;
    public ulong playerSteamID;
    public bool avatarReceived;

    public TextMeshProUGUI playerNameText;
    public PlayerRole role;
    public TextMeshProUGUI playerRole;
    public RawImage playerIcon;

    public TextMeshProUGUI readyStatusText;
    public Color colorForReadyText;
    public Color colorForGettingReadyText;
    public Image isReadiedImage;
    public bool isReady;

    protected Callback<AvatarImageLoaded_t> imageLoaded;

    private void Start()
    {
        imageLoaded = Callback<AvatarImageLoaded_t>.Create(OnImageLoaded);

    }

    void GetPlayerIcon()
    {
        int imageID = SteamFriends.GetLargeFriendAvatar((CSteamID)playerSteamID);
        if (imageID == -1) { return; }
        playerIcon.texture = GetSteamImageAsTexture(imageID);
    }

    public void SetPlayerValues(PlayerRole role)
    {
        this.role = role; // Rolle speichern
        playerNameText.text = playerName;
        playerRole.text = role.ToString() + " �";
        playerIcon.uvRect = new Rect(0, 1, 1, -1);
        if (!avatarReceived) { GetPlayerIcon(); }
        UpdateReadyStatusText();
    }

    public void UpdateReadyStatusText()
    {
        if (isReady)
        {
            readyStatusText.text = "Bereit!";
            isReadiedImage.enabled = true;
            // pop up anim here
            // sound for readied here
        }
        else
        {
            readyStatusText.text = "Sucht Bereit Button...";
            isReadiedImage.enabled = false;
            // away anim here
            // sound for unreadied here
        }
    }

    private void OnImageLoaded(AvatarImageLoaded_t callback)
    {
        if (callback.m_steamID.m_SteamID == playerSteamID)
        {
            playerIcon.texture = GetSteamImageAsTexture(callback.m_iImage);
        }
        else
        {
            return;
        }
    }

    private Texture2D GetSteamImageAsTexture(int iImage)
    {
        Texture2D texture = null;
        bool isValid = SteamUtils.GetImageSize(iImage, out uint width, out uint height);

        if (isValid)
        {
            byte[] image = new byte[width * height * 4];
            isValid = SteamUtils.GetImageRGBA(iImage, image, (int)(width * height * 4));

            if (isValid)
            {
                texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false, true);
                texture.LoadRawTextureData(image);
                texture.Apply();

                // Steam liefert Textur vertikal gespiegelt � hier flippen:
                //texture = FlipTexture(texture);
            }
        }

        avatarReceived = true;
        return texture;
    }

    private Texture2D FlipTexture(Texture2D original)
    {
        int width = original.width;
        int height = original.height;

        Texture2D flipped = new Texture2D(width, height, original.format, false);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                flipped.SetPixel(x, height - 1 - y, original.GetPixel(x, y));
            }
        }

        flipped.Apply();
        Destroy(original); // Original aufr�umen um Memory Leaks zu vermeiden
        return flipped;
    }

}
