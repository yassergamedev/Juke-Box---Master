using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

public class Song : MonoBehaviour
{
    public TMP_Text songNameText;
    public TMP_Text artistText;
    public TMP_Text numberText;
    public Button playButton;
    public Button stopButton;

    public string SongName { get; private set; }
    public string Artist { get; private set; }
    public int Number { get; private set; }
    public string NumberString { get; private set; }
    public string AudioClipPath { get; private set; }

    public AudioClip AudioClip;
    private AudioSource audioSource;

    public float SongLength { get; set; }

    public string KeypadInput { get; set; }
    public void Initialize(string songName, string artist, string audioClipPath, int number)
    {
        SongName = songName;
        Artist = artist;
        AudioClipPath = audioClipPath;
        Number = number;
        NumberString = number.ToString();
        UpdateUI();
        audioSource = GetComponent<AudioSource>();

        if (playButton != null)
        {
            playButton.onClick.AddListener(PlayAudio);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopPlayback);
        }
    }

    public void Initialize(string songName, string artist, string audioClipPath, string number)
    {
        SongName = songName;
        Artist = artist;
        AudioClipPath = audioClipPath;
        NumberString = number;

        UpdateUI();
        audioSource = GetComponent<AudioSource>();

        if (playButton != null)
        {
            playButton.onClick.AddListener(PlayAudio);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopPlayback);
        }
    }

    private void UpdateUI()
    {
        SetTextAndAdjustSize(songNameText, SongName);
        SetTextAndAdjustSize(artistText, Artist);
        SetTextAndAdjustSize(numberText, NumberString);
    }

    private void SetTextAndAdjustSize(TMP_Text textComponent, string content)
    {
        if (textComponent == null || string.IsNullOrEmpty(content)) return;

        textComponent.text = content;
    }

    public void PlayAudio()
    {
        if (audioSource != null)
        {
            if (AudioClip != null)
            {
                audioSource.clip = AudioClip;
                audioSource.Play();
            }
            else
            {
                StartCoroutine(LoadAudioClipFromPath());
            }
        }
        else
        {
            Debug.LogWarning("AudioSource is missing.");
        }
    }

    public IEnumerator LoadAudioClipFromPath()
    {
        Debug.Log($"Attempting to load audio from: {AudioClipPath}");

        AudioClipPath = AudioClipPath.Replace("\\", "/");

        if (!System.IO.File.Exists(AudioClipPath))
        {
            Debug.Log($"Error: File does not exist at {AudioClipPath}");
            yield break;
        }

        string formattedPath = "file://" + AudioClipPath.Replace("\\", "/");

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(formattedPath, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"Error: Failed to load audio. {www.error}");
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

            if (clip == null)
            {
                Debug.Log("Error: DownloadHandler returned NULL.");
                yield break;
            }

            Debug.Log($"Success: Loaded {AudioClipPath}");
            this.AudioClip = clip;

            SongLength = AudioClip.length;

            Debug.Log($"Song Length: {SongLength} seconds");
        }
    }

    public void StopPlayback()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        else
        {
            Debug.LogWarning("AudioSource is not playing or is missing.");
        }
    }

    public AudioClip GetAudioClip()
    {
        if (AudioClip == null)
        {
            Debug.LogWarning($"AudioClip for {SongName} is not loaded yet.");
        }

        return AudioClip;
    }
}
