using UnityEngine;

/// <summary>
/// Brake sound: plays when you press Space while the car is moving.
/// Drop this file anywhere inside Assets and press Play. It attaches itself to "main car".
/// </summary>
public class BrakeSound : MonoBehaviour
{
    [Header("Sound")]
    [Tooltip("Drag the sound here, or leave empty and put the file in a 'Resources' folder")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.8f;

    AudioSource src;
    CarSignSystem car;

    // Adds itself to the car ("main car") when the scene starts
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetup()
    {
        if (FindObjectOfType<BrakeSound>() != null) return;
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            string n = t.name.Replace(" ", "").Replace("_", "").ToLower();
            if (n == "maincar") { t.gameObject.AddComponent<BrakeSound>(); return; }
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
            Debug.LogWarning("BrakeSound: sound not found. Drag it into 'Clip', or put the file (name contains 'stop') inside a Resources folder.");
    }

    // Looks for the file in any "Resources" folder by its name
    AudioClip FindClip()
    {
        string[] keys = { "stop", "brake", "skid" };
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

    [Header("Brake")]
    [Tooltip("Only plays if the car is faster than this (km/h)")]
    public float minSpeedKmh = 5f;
    bool wasBraking;


    void Update()
    {
        if (src == null || clip == null || !HasCar()) return;

        bool braking = car.Braking && Mathf.Abs(car.SpeedMs) * 3.6f > minSpeedKmh;

        if (braking && !wasBraking) src.Play();                 // key pressed while moving
        else if (!braking && src.isPlaying) src.Stop();         // released, or the car stopped

        wasBraking = braking;
    }
}
