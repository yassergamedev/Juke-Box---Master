using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class DeviceIconLoader : MonoBehaviour
{
    private void Start()
    {
        var iconManager = GetComponent<DeviceIconManager>();
        if (!string.IsNullOrEmpty(iconManager.iconPath))
        {
            if (iconManager.iconPath.StartsWith("Assets/Resources/"))
            {
                string resourcePath = iconManager.iconPath
                    .Replace("Assets/Resources/", "")
                    .Replace(Path.GetExtension(iconManager.iconPath), "");

                Sprite loadedSprite = Resources.Load<Sprite>(resourcePath);
                if (loadedSprite != null)
                {
                    GetComponent<Image>().sprite = loadedSprite;
                }
            }
            else 
            {
                iconManager.LoadIcon(iconManager.iconPath);
            }
        }
    }
}