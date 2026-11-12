using UnityEngine;

/// <summary>
/// Correct sound: plays when the car passes a sign / situation correctly.
/// Drop this file anywhere inside Assets and press Play. It attaches itself to "main car".
/// </summary>
public class CorrectSound : MonoBehaviour
{
    [Header("Sound")]
    [Tooltip("Drag the sound here, or leave empty and put the file in a 'Resources' folder")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.9f;

    AudioSource src;
    CarSignSystem car;

    // Adds itself to the car ("main car") when the scene starts
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetup()
    {
        if (FindObjectOfType<CorrectSound>() != null) return;
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            string n = t.name.Replace(" ", "").Replace("_", "").ToLower();
            if (n == "maincar") { t.gameObject.AddComponent<CorrectSound>(); return; }
        }
    }

    void Awake()
    {
        if (clip == null) clip = FindClip();

        src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = false;
        src.playOnAwake = false;
        src.spatialBlend = 0f;   // 2D sound
        src.volume = volume;

        if (clip == null)
            Debug.LogWarning("CorrectSound: sound not found. Drag it into 'Clip', or put the file (name contains 'correct') inside a Resources folder.");
    }

    // Looks for the file in any "Resources" folder by its name
    AudioClip FindClip()
    {
        string[] keys = { "correct", "success", "right" };
        foreach (AudioClip c in Resources.LoadAll<AudioClip>(""))
        {
            string n = c.name.ToLower();
            foreach (string k in keys)
                if (n.Contains(k)) return c;
        }
        return null;
    }

    bool HasCar()
    {
        if (car == null) car = CarSignSystem.Instance;
        return car != null;
    }


    void OnEnable() { CarSignSystem.Correct += Play; }
    void OnDisable() { CarSignSystem.Correct -= Play; }

    void Play()
    {
        if (src == null || clip == null) return;
        src.Stop();
        src.Play();
    }
}
