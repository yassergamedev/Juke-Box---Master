using UnityEngine;
using UnityEngine.UI;

public class DeviceIconManager : MonoBehaviour
{
    public string iconPath;

    public void LoadIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        iconPath = path;

        var image = GetComponent<Image>();
        if (image == null)
        {
            image = GetComponentInChildren<Image>();
        }
        if (image == null) return;

        if (path.StartsWith("Resources/"))
        {
            string resourcePath = path.Replace("Resources/", "").Split('.')[0];
            image.sprite = Resources.Load<Sprite>(resourcePath);
        }
        else
        {

            byte[] fileData = System.IO.File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(fileData);
            image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);

        }
    }

    public string GetIconPath() => iconPath;
}