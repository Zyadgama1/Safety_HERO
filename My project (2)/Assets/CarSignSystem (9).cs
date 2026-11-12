using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop this file anywhere inside Assets and press Play.
/// It finds the object named "main car" automatically and attaches itself.
/// Controls: Arrow keys = drive, Space = brake, Enter (or OK button) = close sign message.
/// </summary>
public class CarSignSystem : MonoBehaviour
{
    [Header("Engine / Brakes")]
    public float maxSpeedKmh = 80f;
    public float reverseMaxKmh = 20f;
    public float acceleration = 6f;       // m/s^2 at low speed (fades near top speed)
    public float brakePower = 28f;        // m/s^2
    public float naturalDrag = 1.5f;      // m/s^2 when no key is pressed

    [Header("Steering")]
    public float wheelBase = 2.6f;            // meters between front and rear axle
    public float maxSteerAngle = 32f;         // front wheel angle at low speed
    public float highSpeedSteerAngle = 6f;    // front wheel angle at top speed
    public float steerRate = 70f;             // deg/s when turning the wheel
    public float steerCenterRate = 120f;      // deg/s when releasing the wheel
    public float maxLateralAccel = 9f;        // grip limit, stops impossible turns at speed

    [Header("Body motion")]
    public float rollPerAccel = 0.5f;
    public float maxRoll = 5f;
    public float pitchPerAccel = 0.5f;
    public float maxPitch = 4f;

    [Header("Model (change if the car looks wrong)")]
    [Tooltip("Which local axis is the front of the car body")]
    public Vector3 localForward = Vector3.forward;
    [Tooltip("Local axis the wheels spin around (usually X)")]
    public Vector3 wheelSpinAxis = Vector3.right;
    [Tooltip("Local axis the front wheels steer around (usually Y)")]
    public Vector3 wheelSteerAxis = Vector3.up;

    [Header("World")]
    public bool collideWithWorld = true;   // car stops at walls / props
    public bool followGround = true;       // keeps car on the road height

    [Header("Camera")]
    public bool followCamera = true;
    public float camDistance = 7f;      // how far behind the car
    public float camHeight = 3f;        // how high above the car
    public float camLookHeight = 1.2f;  // point on the car the camera looks at
    public float camPosSmooth = 10f;
    public float camRotSmooth = 8f;

    [Header("Signs")]
    [Tooltip("Distance (meters) from a sign at which its message appears")]
    public float triggerRadius = 12f;
    public float exitExtra = 3f;

    [Header("Finish")]
    [Tooltip("Victory when the car gets this close to an object named 'Finish'")]
    public float finishRadius = 6f;

    // ------------------------------------------------------------------
    class SignRule
    {
        public string keyword, title, message;
        public float maxKmh;
        public bool mustStop;
        public int forbidSteer;   // -1 = left forbidden, 1 = right forbidden
        public int mustTurn;      // -1 = must turn left, 1 = must turn right
        public float blockRadius; // > 0 = road is closed, you may not get this close
        public SignRule(string k, string t, string m, float max, bool stop, int steer, int turn = 0, float block = 0f)
        { keyword = k; title = t; message = m; maxKmh = max; mustStop = stop; forbidSteer = steer; mustTurn = turn; blockRadius = block; }
    }

    class SignInstance
    {
        public Transform t;
        public SignRule rule;
        public float limit;
        public string message;
        public bool stoppedOk;
        public Vector3 savePos;
        public float saveYaw;
        public Texture icon;   // picture of the sign shown in the message panel
        public bool done;      // passed correctly
    }

    class Wheel
    {
        public Transform t;
        public Quaternion baseRot;
        public bool front;
        public float radius;
    }

    // Edit / add rules here. keyword = part of the sign object's name (lowercase, no spaces).
    static readonly SignRule[] Rules = new SignRule[]
    {
        new SignRule("stopsign", "STOP SIGN", "Come to a complete stop near the sign before you continue.", 0f, true, 0),
        new SignRule("crosswalk", "CROSSWALK", "Pedestrians may be crossing. Slow down to 20 km/h or less.", 20f, false, 0),
        new SignRule("chevronleft", "TURN LEFT", "You must turn left at this sign. Slow down to 30 km/h and do not go straight or right.", 30f, false, 1, -1),
        new SignRule("chevronright", "TURN RIGHT", "You must turn right at this sign. Slow down to 30 km/h and do not go straight or left.", 30f, false, -1, 1),
        new SignRule("trafficcone", "ROAD CLOSED", "The road is blocked by traffic cones. Do not drive through them - stop and turn back.", 0f, false, 0, 0, 3f),
        new SignRule("sawhorse", "ROAD WORK / BARRICADE", "Road work ahead. Slow down to 25 km/h.", 25f, false, 0),
    };

    // ------------------------------------------------------------------
    readonly List<SignInstance> signs = new List<SignInstance>();
    readonly HashSet<Transform> signRoots = new HashSet<Transform>();
    readonly List<Wheel> wheels = new List<Wheel>();
    SignInstance current;
    Rigidbody rb;
    Camera cam;
    bool snapCamera = true;
    Vector3 startPos;

    public static CarSignSystem Instance;

    // Sound events (the *Sound scripts listen to them)
    public static event System.Action Lost;
    public static event System.Action Correct;
    public static event System.Action Won;

    public void NotifyLost() { if (Lost != null) Lost(); }

    // Read-only state for the sound scripts
    public float SpeedMs { get { return speed; } }
    public float MaxSpeedMs { get { return maxSpeedKmh / 3.6f; } }
    public int SteerInput { get { return steerInput; } }
    public bool IsPaused { get { return showing || GameUI.WaitingForStart; } }
    public bool Braking { get { return !IsPaused && Input.GetKey(KeyCode.Space); } }

    Transform finish;
    bool won;

    // Called by the Restart button: back to the very first state
    public void ResetToStart()
    {
        showing = false;
        SignMessagePanel.Get().Hide();
        Time.timeScale = 1f;
        current = null;
        distance = 0f;
        won = false;
        foreach (var sg in signs) sg.done = false;
        TeleportTo(startPos, 0f);
    }

    // Read-only access to the heading, used by PedestrianCrossing to save the retry position
    public float Yaw { get { return yaw; } }

    // car state
    float speed;          // m/s (negative = reverse)
    float yaw;            // degrees turned since start (positive = right)
    float steerAngle;     // front wheel angle (deg)
    float roll, pitch, accelSmooth;
    float distance;       // total rolled distance (for wheel spin)
    int steerInput;
    Quaternion initRot;
    Vector3 fwd0;

    // world interaction
    bool hasBox;
    Vector3 boxHalf, boxOffset;
    bool hasGround;
    float groundOffset;

    // ui
    bool showing;
    string title = "", body = "";
    float prevScale = 1f;
    Texture2D bg;

    // Auto-attach to the object called "main car" when the scene starts.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoSetup()
    {
        if (FindObjectOfType<CarSignSystem>() != null) return;
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            string n = t.name.Replace(" ", "").Replace("_", "").ToLower();
            if (n == "maincar")
            {
                t.gameObject.AddComponent<CarSignSystem>();
                return;
            }
        }
        Debug.LogWarning("CarSignSystem: could not find an object named 'main car' in the scene.");
    }

    void Start()
    {
        Time.timeScale = 1f;

        rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true; // we move by transform

        Instance = this;
        startPos = transform.position;
        initRot = transform.rotation;
        fwd0 = Vector3.ProjectOnPlane(initRot * localForward, Vector3.up);
        fwd0 = fwd0.sqrMagnitude > 0.0001f ? fwd0.normalized : Vector3.forward;

        ScanSigns();
        FindFinish();
        SetupWheels();
        SetupBox();
        if (followGround && RaycastGround(out float gy))
        {
            hasGround = true;
            groundOffset = transform.position.y - gy;
        }

        Debug.Log("CarSignSystem: ready. Signs: " + signs.Count + ", wheels: " + wheels.Count);
    }

    void SetupWheels()
    {
        wheels.Clear();
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            if (t == transform) continue;
            string n = t.name.ToLower();
            if (!n.Contains("wheel")) continue;
            var w = new Wheel { t = t, baseRot = t.localRotation, front = n.Contains("front"), radius = 0.35f };
            Renderer r = t.GetComponentInChildren<Renderer>();
            if (r != null) w.radius = Mathf.Clamp(r.bounds.extents.y, 0.15f, 1f);
            wheels.Add(w);
        }
    }

    void SetupBox()
    {
        Renderer[] rs = GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        boxHalf = new Vector3(b.extents.x * 0.9f, Mathf.Max(0.15f, b.extents.y * 0.5f), b.extents.z * 0.9f);
        boxOffset = b.center - transform.position;
        hasBox = true;
    }

    // ------------------------------------------------------------------
    void ScanSigns()
    {
        signs.Clear();
        signRoots.Clear();
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            if (t == transform || t.IsChildOf(transform)) continue;
            SignRule r = Match(t.name);
            if (r == null) continue;
            if (t.parent != null && Match(t.parent.name) != null) continue; // avoid duplicates

            var s = new SignInstance { t = t, rule = r, limit = r.maxKmh, message = r.message };
            if (r.keyword == "speedlimit")
            {
                s.limit = FirstNumber(t.name, 55f);
                s.message = "The speed limit here is " + s.limit + " km/h. Do not exceed it.";
            }
            signs.Add(s);
            signRoots.Add(t);
        }
    }

    static SignRule Match(string objName)
    {
        string n = objName.ToLower().Replace(" ", "").Replace("_", "");
        foreach (var r in Rules) if (n.Contains(r.keyword)) return r;
        return null;
    }

    static float FirstNumber(string s, float fallback)
    {
        string d = "";
        foreach (char c in s)
        {
            if (char.IsDigit(c)) d += c;
            else if (d.Length > 0) break;
        }
        float v;
        return float.TryParse(d, out v) ? v : fallback;
    }

    void FindFinish()
    {
        finish = null;
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            if (t == transform || t.IsChildOf(transform)) continue;
            string n = t.name.ToLower().Replace(" ", "").Replace("_", "");
            if (n.Contains("finish")) { finish = t; break; }
        }
        if (finish == null)
            Debug.Log("CarSignSystem: no object named 'Finish' - victory happens after every sign was passed correctly. Create an Empty named 'Finish' to choose the end point.");
    }

    bool IsSignPart(Transform t)
    {
        while (t != null)
        {
            if (signRoots.Contains(t)) return true;
            t = t.parent;
        }
        return false;
    }

    // ------------------------------------------------------------------
    void Update()
    {
        if (GameUI.WaitingForStart) return;   // waiting for the Start button

        if (showing)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.E))
                CloseMessage();
            return;
        }

        Drive(Time.deltaTime);
        CheckSigns();
        CheckFinish();
    }

    void CheckFinish()
    {
        if (won || showing) return;

        bool reached;
        if (finish != null)
        {
            reached = Flat(transform.position, finish.position) < finishRadius;
        }
        else
        {
            if (signs.Count == 0) return;
            reached = true;
            foreach (var s in signs) if (!s.done) { reached = false; break; }
        }
        if (!reached) return;

        won = true;
        if (Won != null) Won();
        ShowPanel("YOU WIN", "Congratulations! You finished the route and followed the signs correctly.", false);
    }

    void Drive(float dt)
    {
        if (dt <= 0f) return;

        float v = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        steerInput = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        bool brake = Input.GetKey(KeyCode.Space);

        float max = maxSpeedKmh / 3.6f;
        float maxRev = reverseMaxKmh / 3.6f;
        float prevSpeed = speed;

        // ---- speed ----
        if (brake)
        {
            speed = Mathf.MoveTowards(speed, 0f, brakePower * dt);
        }
        else if (v > 0f)
        {
            if (speed < 0f) speed += brakePower * dt;
            else
            {
                float k = Mathf.Clamp01(1f - (speed * speed) / (max * max)); // engine fades near top speed
                speed += acceleration * k * dt;
            }
        }
        else if (v < 0f)
        {
            if (speed > 0f) speed -= brakePower * dt;
            else
            {
                float k = Mathf.Clamp01(1f - (speed * speed) / (maxRev * maxRev));
                speed -= acceleration * 0.6f * k * dt;
            }
        }
        else
        {
            float drag = naturalDrag + speed * speed * 0.004f;
            speed = Mathf.MoveTowards(speed, 0f, drag * dt);
        }
        speed = Mathf.Clamp(speed, -maxRev, max);

        // ---- steering (bicycle model) ----
        float speedT = Mathf.Clamp01(Mathf.Abs(speed) / max);
        float steerLimit = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, speedT);
        float target = steerInput * steerLimit;
        float rate = steerInput == 0 ? steerCenterRate : steerRate;
        steerAngle = Mathf.MoveTowards(steerAngle, target, rate * dt);

        float yawRate = speed / Mathf.Max(0.5f, wheelBase) * Mathf.Tan(steerAngle * Mathf.Deg2Rad); // rad/s
        float grip = maxLateralAccel / Mathf.Max(Mathf.Abs(speed), 0.1f);
        yawRate = Mathf.Clamp(yawRate, -grip, grip);
        yaw += yawRate * Mathf.Rad2Deg * dt;

        // ---- body roll / pitch ----
        float longAccel = (speed - prevSpeed) / dt;
        accelSmooth = Mathf.Lerp(accelSmooth, longAccel, 8f * dt);
        float lateral = speed * yawRate;
        float rollTarget = Mathf.Clamp(lateral * rollPerAccel, -maxRoll, maxRoll);
        float pitchTarget = Mathf.Clamp(-accelSmooth * pitchPerAccel, -maxPitch, maxPitch);
        roll = Mathf.Lerp(roll, rollTarget, 6f * dt);
        pitch = Mathf.Lerp(pitch, pitchTarget, 6f * dt);

        // ---- move ----
        Move(Forward() * speed * dt);
        distance += speed * dt;
        SnapToGround(dt);
        ApplyRotation();
        UpdateWheels();
    }

    Vector3 Forward()
    {
        return Quaternion.AngleAxis(yaw, Vector3.up) * fwd0;
    }

    // Chase camera: stays behind the car and follows its heading smoothly
    void LateUpdate()
    {
        if (!followCamera) return;
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) cam = FindObjectOfType<Camera>();
            if (cam == null) return;
        }

        Vector3 pos = transform.position;
        Vector3 look = pos + Vector3.up * camLookHeight;
        Vector3 desired = pos - Forward() * camDistance + Vector3.up * camHeight;
        desired = AvoidWalls(look, desired);

        float dt = Time.deltaTime;
        Transform ct = cam.transform;
        if (snapCamera)
        {
            ct.position = desired;
            ct.rotation = Quaternion.LookRotation(look - desired);
            snapCamera = false;
            return;
        }

        ct.position = Vector3.Lerp(ct.position, desired, 1f - Mathf.Exp(-camPosSmooth * dt));
        Vector3 toLook = look - ct.position;
        if (toLook.sqrMagnitude > 0.0001f)
            ct.rotation = Quaternion.Slerp(ct.rotation, Quaternion.LookRotation(toLook), 1f - Mathf.Exp(-camRotSmooth * dt));
    }

    // Pulls the camera closer if a wall is between it and the car
    Vector3 AvoidWalls(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return to;
        d /= len;
        RaycastHit[] hits = Physics.RaycastAll(from, d, len, ~0, QueryTriggerInteraction.Ignore);
        float best = len;
        bool hit = false;
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.isTrigger) continue;
            if (h.transform.IsChildOf(transform) || IsSignPart(h.transform)) continue;
            if (h.distance < best) { best = h.distance; hit = true; }
        }
        return hit ? from + d * Mathf.Max(0.5f, best - 0.3f) : to;
    }

    void ApplyRotation()
    {
        Quaternion yawQ = Quaternion.AngleAxis(yaw, Vector3.up);
        Vector3 f = yawQ * fwd0;
        Vector3 r = Vector3.Cross(Vector3.up, f);
        transform.rotation = Quaternion.AngleAxis(roll, f) * Quaternion.AngleAxis(pitch, r) * yawQ * initRot;
    }

    void UpdateWheels()
    {
        foreach (var w in wheels)
        {
            if (w.t == null) continue;
            float spin = (distance / w.radius) * Mathf.Rad2Deg % 360f;
            Quaternion q = w.baseRot;
            if (w.front) q *= Quaternion.AngleAxis(steerAngle, wheelSteerAxis);
            q *= Quaternion.AngleAxis(spin, wheelSpinAxis);
            w.t.localRotation = q;
        }
    }

    // ------------------------------------------------------------------
    void Move(Vector3 delta)
    {
        float dist = delta.magnitude;
        if (dist < 1e-5f) return;
        Vector3 dir = delta / dist;

        if (collideWithWorld && hasBox)
        {
            const float skin = 0.05f;
            float hitDist;
            if (Blocked(dir, dist + skin, out hitDist))
            {
                float allowed = Mathf.Clamp(hitDist - skin, 0f, dist);
                transform.position += dir * allowed;
                speed = Mathf.Abs(speed) > 2f ? -speed * 0.2f : 0f; // small bounce
                return;
            }
        }
        transform.position += delta;
    }

    bool Blocked(Vector3 dir, float maxDist, out float hitDist)
    {
        hitDist = float.MaxValue;
        Quaternion yq = Quaternion.AngleAxis(yaw, Vector3.up);
        Vector3 center = transform.position + yq * boxOffset;
        RaycastHit[] hits = Physics.BoxCastAll(center, boxHalf, dir, yq, maxDist, ~0, QueryTriggerInteraction.Ignore);
        bool blocked = false;
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.isTrigger) continue;
            if (h.transform.IsChildOf(transform)) continue;
            if (h.distance < 0.001f) continue;              // already overlapping, ignore
            if (Mathf.Abs(h.normal.y) > 0.6f) continue;     // floor / roof
            if (IsSignPart(h.transform)) continue;          // signs & cones are handled by the rules
            if (h.transform.GetComponentInParent<PedestrianCrossing>() != null) continue; // pedestrian hit is judged by PedestrianCrossing
            if (h.distance < hitDist) { hitDist = h.distance; blocked = true; }
        }
        return blocked;
    }

    bool RaycastGround(out float y)
    {
        y = 0f;
        Vector3 origin = transform.position + Vector3.up * 3f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.isTrigger) continue;
            if (h.transform.IsChildOf(transform)) continue;
            if (IsSignPart(h.transform)) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; found = true; }
        }
        return found;
    }

    void SnapToGround(float dt)
    {
        if (!followGround || !hasGround) return;
        float gy;
        if (!RaycastGround(out gy)) return;
        Vector3 p = transform.position;
        p.y = Mathf.Lerp(p.y, gy + groundOffset, 12f * dt);
        transform.position = p;
    }

    // ------------------------------------------------------------------
    static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void CheckSigns()
    {
        Vector3 p = transform.position;

        // Looking for a new sign
        if (current == null)
        {
            SignInstance best = null;
            float bestD = triggerRadius;
            foreach (var s in signs)
            {
                if (s.t == null) continue;
                float d = Flat(p, s.t.position);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best != null)
            {
                current = best;
                current.stoppedOk = false;
                current.savePos = transform.position;   // the point before the sign
                current.saveYaw = yaw;
                ShowMessage(current.rule.title, current.message);
            }
            return;
        }

        // Inside a sign zone: check the rules
        SignInstance c = current;
        if (c.t == null) { current = null; return; }

        float dist = Flat(p, c.t.position);
        float kmh = Mathf.Abs(speed) * 3.6f;

        // Road closed (cones): may not get close to any of them
        foreach (var s in signs)
        {
            if (s.t == null || s.rule.blockRadius <= 0f) continue;
            if (Flat(p, s.t.position) < s.rule.blockRadius)
            {
                if (s != c) { s.savePos = c.savePos; s.saveYaw = c.saveYaw; current = s; }
                Violation("The road is closed. You can't drive through the cones.");
                return;
            }
        }

        if (dist > triggerRadius + exitExtra)
        {
            Vector3 a = c.savePos - c.t.position; a.y = 0f;
            Vector3 b = p - c.t.position; b.y = 0f;
            bool passed = Vector3.Dot(a, b) < 0f; // car ended up on the other side of the sign

            if (c.rule.mustStop && !c.stoppedOk && passed)
            {
                Violation("You did not stop at the stop sign.");
                return;
            }

            if (c.rule.mustTurn != 0 && passed)
            {
                float turned = Mathf.DeltaAngle(c.saveYaw, yaw); // + = right, - = left
                if (c.rule.mustTurn * turned < 40f)
                {
                    Violation("You had to turn " + (c.rule.mustTurn < 0 ? "left" : "right") + " at this sign.");
                    return;
                }
            }
            c.done = true;
            if (Correct != null) Correct();
            current = null;
            return;
        }

        if (c.rule.mustStop && kmh < 1f && dist < triggerRadius * 0.8f) c.stoppedOk = true;

        if (c.limit > 0f && kmh > c.limit + 2f)
        {
            Violation("You were driving too fast (" + Mathf.RoundToInt(kmh) + " km/h, limit " + c.limit + ").");
            return;
        }

        if (c.rule.forbidSteer != 0 && kmh > 3f && steerInput == c.rule.forbidSteer)
        {
            Violation("You steered the wrong way for this sign.");
        }
    }

    void Violation(string reason)
    {
        NotifyLost();
        SignInstance s = current;
        TeleportTo(s.savePos, s.saveYaw);
        s.stoppedOk = false;
        ShowMessage(s.rule.title, "WRONG MOVE: " + reason + "\nYou were sent back to before the sign.\n\n" + s.message);
    }

    public void TeleportTo(Vector3 p, float yawValue)   // was private; made public for PedestrianCrossing
    {
        transform.position = p;
        yaw = yawValue;
        speed = 0f;
        steerAngle = 0f;
        roll = 0f; pitch = 0f; accelSmooth = 0f;
        snapCamera = true;
        ApplyRotation();
        UpdateWheels();
        if (rb != null) { rb.position = p; rb.rotation = transform.rotation; }
    }

    // ------------------------------------------------------------------
    public void ShowMessage(string t, string b)   // public: also used by PedestrianCrossing
    {
        ShowPanel(t, b, true);
    }

    void ShowPanel(string t, string b, bool withIcon)
    {
        title = t;
        body = b;
        showing = true;
        prevScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        speed = 0f;

        // picture: the sign we are at, or the nearest sign (e.g. pedestrian hit -> crosswalk sign)
        SignInstance si = !withIcon ? null : (current != null ? current : NearestSign(30f));
        Texture icon = null;
        if (si != null)
        {
            if (si.icon == null) si.icon = RenderIcon(si);
            icon = si.icon;
        }
        SignMessagePanel.Get().Show(b, icon, CloseMessage);

        Time.timeScale = 0f;   // pause the whole game
    }

    void CloseMessage()
    {
        if (!showing) return;
        showing = false;
        SignMessagePanel.Get().Hide();
        Time.timeScale = prevScale;   // resume, control is back
    }

    SignInstance NearestSign(float maxDist)
    {
        SignInstance best = null;
        float bestD = maxDist;
        foreach (var s in signs)
        {
            if (s.t == null) continue;
            float d = Flat(transform.position, s.t.position);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    // Takes a picture of the real sign (seen from the side the car is coming from)
    Texture RenderIcon(SignInstance s)
    {
        if (s == null || s.t == null) return null;
        Renderer[] rs = s.t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return null;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        float horiz = Mathf.Max(b.size.x, b.size.z);
        Vector3 focus;
        float focusSize;
        if (b.size.y > 1.5f && b.size.y > horiz * 1.4f)
        {
            focus = new Vector3(b.center.x, b.max.y - horiz * 0.5f, b.center.z);   // sign on a tall post: top part only
            focusSize = horiz * 1.3f;
        }
        else
        {
            focus = b.center;
            focusSize = Mathf.Max(b.size.y, horiz) * 1.2f;
        }
        focusSize = Mathf.Max(focusSize, 0.3f);

        Vector3 dir = transform.position - focus;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : -Forward();

        float ext = b.extents.magnitude;
        float camDist = ext + 2f;

        GameObject go = new GameObject("SignIconCam");
        Camera c = go.AddComponent<Camera>();
        c.enabled = false;
        c.orthographic = true;
        c.orthographicSize = focusSize * 0.5f;
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.16f, 0.17f, 0.2f, 1f);
        c.nearClipPlane = Mathf.Max(0.05f, camDist - ext - 0.2f);
        c.farClipPlane = camDist + ext + 0.2f;
        go.transform.position = focus + dir * camDist;
        go.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);

        RenderTexture rt = new RenderTexture(256, 256, 16);
        c.targetTexture = rt;
        c.Render();
        c.targetTexture = null;
        Destroy(go);
        return rt;
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    void OnGUI()
    {
        if (bg == null)
        {
            bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.88f));
            bg.Apply();
        }

        int fs = Mathf.Max(14, Screen.height / 32);
        GUIStyle label = new GUIStyle(GUI.skin.label) { fontSize = fs, wordWrap = true };
        label.normal.textColor = Color.white;

        GUI.Label(new Rect(15f, Screen.height - fs * 3.4f, Screen.width * 0.7f, fs * 3.2f),
            "Speed: " + Mathf.RoundToInt(Mathf.Abs(speed) * 3.6f) + " km/h\nArrows: drive   Space: brake", label);
    }
}
