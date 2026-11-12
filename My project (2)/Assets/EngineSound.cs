using UnityEngine;

/// <summary>
/// Engine sound (loop): plays while the car is moving. Pitch and volume follow the speed.
/// Drop this file anywhere inside Assets and press Play. It attaches itself to "main car".
/// </summary>
public class EngineSound : MonoBehaviour
{
    [Header("Sound")]
    [Tooltip("Drag the sound here, or leave empty and put the file in a 'Resources' folder")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.5f;

    AudioSource src;
    CarSignSystem car;

    // Adds itself to the car ("main car") when the scene starts
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetup()
    {
        if (FindObjectOfType<EngineSound>() != null) return;
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            string n = t.name.Replace(" ", "").Replace("_", "").ToLower();
            if (n == "maincar") { t.gameObject.AddComponent<EngineSound>(); return; }
        }
    }

    void Awake()
    {
        if (clip == null) clip = FindClip();

        src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;   // 2D sound
        src.volume = 0f;

        if (clip == null)
            Debug.LogWarning("EngineSound: sound not found. Drag it into 'Clip', or put the file (name contains 'engine') inside a Resources folder.");
    }

    // Looks for the file in any "Resources" folder by its name
    AudioClip FindClip()
    {
        string[] keys = { "engine", "motor" };
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

    [Header("Engine")]
    public float minPitch = 0.8f;   // pitch when moving slowly
    public float maxPitch = 1.8f;   // pitch at top speed
    [Tooltip("ON = silent while turning (only straight driving)")]
    public bool onlyWhenStraight = false;


    void Update()
    {
        if (src == null || clip == null || !HasCar()) return;

        float dt = Time.unscaledDeltaTime;
        float speed = Mathf.Abs(car.SpeedMs);
        float t = Mathf.Clamp01(speed / Mathf.Max(0.1f, car.MaxSpeedMs));

        bool moving = !car.IsPaused && speed > 0.3f;
        bool steering = moving && speed > 1f && car.SteerInput != 0;
        bool on = moving && !(onlyWhenStraight && steering);

        float target = on ? volume * Mathf.Lerp(0.5f, 1f, t) : 0f;
        src.volume = Mathf.MoveTowards(src.volume, target, 3f * dt);
        src.pitch = Mathf.Lerp(src.pitch, Mathf.Lerp(minPitch, maxPitch, t), 6f * dt);

        if (src.volume > 0.001f && !src.isPlaying) src.Play();
        else if (src.volume <= 0.001f && src.isPlaying) src.Stop();
    }
}
