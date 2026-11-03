using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// สคริปต์ผีตัวเดียวจบ (ติดที่ Player ที่มี Rigidbody2D)
///
/// 1) ลอยแบบ "หัวนำ ตัวไหลตาม"
///    - หัว (Rigidbody) พุ่งนำ / ตัวผี (Visual) ถูกสปริงดึงตามหลัง
///    - ตอนหยุดตัวไหลเลยหัวนิดแล้วแกว่งกลับ, ตอนอยู่เฉยๆ ลอยขึ้นลง + มีควันตรงปลายตัว
/// 2) สิงของเล่น
///    - เข้าใกล้ของเล่น (Possessable2D) → ของเล่นมี outline ขาวกระพริบ + ตัว F → กด F เพื่อสิง
///    - ตอนสิง: คุมของเล่นด้วย WASD / ลูกศร + outline เขียว
///    - ตอนสิงของเล่นที่ต้องซ่อน: เข้าใกล้ไอเทม (ItemPickup2D) กด F เก็บ ครบแล้วซ่อมเสร็จ ผีออกเอง
///    - ไม่มีไอเทมอยู่ใกล้ → กด F เพื่อออกจากร่างเอง
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class GhostController2D : MonoBehaviour
{
    // ============================================================ ลอย

    [Header("Visual (child ที่มี SpriteRenderer — เว้นว่างให้หาเอง)")]
    public Transform visual;

    [Header("Movement (หัวนำ)")]
    public float maxSpeed = 5f;
    [Tooltip("ความไวในการออกตัว (หัวพาไปก่อน)")]
    public float acceleration = 16f;
    [Tooltip("ค่าน้อย = ไหลไกลตอนหยุด / ค่ามาก = หยุดเร็ว")]
    public float deceleration = 3f;

    [Header("Idle Drift (แรงลอยตอนหยุด)")]
    public float driftStrength = 1.5f;
    public float driftSpeed = 0.7f;

    [Header("Body Follow (ตัวไหลตามหัว)")]
    [Tooltip("ความถี่สปริงของตัว: น้อย = ตัวหน่วง/ลากยาว, มาก = ตามติด")]
    public float followFrequency = 2.2f;
    [Tooltip("ความหน่วงสปริง: น้อย = แกว่งเลยหัวหลายรอบ, 1 = ไม่แกว่งเลย")]
    [Range(0.1f, 1f)] public float followDamping = 0.4f;
    [Tooltip("ระยะที่ตัวลากตามหลังได้สูงสุด")]
    public float maxLag = 0.8f;

    [Header("Lean / Stretch (หัวเอนนำ ตัวยืด)")]
    [Tooltip("จุดหมุนของตัว เทียบกับกลางตัว (ต่ำลงมา = หัวแกว่งไกลขึ้น)")]
    public Vector2 pivotOffset = new Vector2(0f, -0.4f);
    [Tooltip("องศาที่หัวเอนต่อระยะที่ตัวลากตามหลัง 1 หน่วย")]
    public float leanPerUnit = 55f;
    public float maxLean = 35f;
    [Tooltip("ยืดตัวตามระยะที่ลากตามหลัง")]
    public float stretchPerUnit = 0.3f;
    public bool flipByDirection = true;

    [Header("Idle Bob / Sway (ลอยขึ้นลง แกว่งเบาๆ)")]
    public float bobAmplitude = 0.12f;
    public float bobFrequency = 1.2f;
    [Range(0f, 1f)] public float bobWhileMoving = 0.25f;
    public float idleSwayAngle = 3f;
    public float idleSwayFrequency = 0.8f;

    [Header("Smoke (ควันตรงปลายตัว)")]
    public ParticleSystem smoke;
    [Tooltip("ตำแหน่งควัน เทียบกับจุดหมุนของตัว")]
    public Vector2 smokeOffset = new Vector2(0f, -0.1f);
    public float smokeMaxRate = 28f;
    public float smokeMinSpeed = 0.4f;
    public int smokeSortingOrder = -1;

    // ============================================================ สิง

    [Header("Possess (สิงของเล่น)")]
    [Tooltip("ระยะที่เข้าใกล้ของเล่นแล้วกด F สิงได้ (ดูเป็นวงกลมเหลืองใน Scene เมื่อเลือกผี)")]
    public float possessRange = 1.6f;
    [Tooltip("ตำแหน่งที่ผีโผล่ตอนออกจากร่าง (เทียบกับของเล่น)")]
    public Vector2 exitOffset = new Vector2(0f, 0.8f);
    [Tooltip("ระยะที่ของเล่นต้องเข้าใกล้ไอเทมถึงจะกด F เก็บได้")]
    public float itemRange = 1.3f;
    [Tooltip("หลังสิง/ออกจากร่าง จะไม่รับปุ่ม F ซ้ำภายในกี่วินาที (กันกดเบิ้ล)")]
    public float actionCooldown = 0.25f;

    [Header("Prompt (ตัว F / ตัวนับ)")]
    public float promptSize = 0.14f;
    public Color promptColor = Color.white;
    public Color counterColor = new Color(1f, 0.92f, 0.45f, 1f);

    [Header("Debug")]
    [Tooltip("แสดงข้อความสิง/ออก และจำนวนของที่เจอใน Console")]
    public bool debugLog = true;

    /// <summary>ของเล่นที่กำลังสิงอยู่ (null = ยังเป็นผีอิสระ)</summary>
    public Possessable2D Current { get; private set; }

    // ---- ภายใน ----
    Rigidbody2D rb;
    Vector2 input;
    float noiseSeed;
    float idleBlend = 1f;
    Vector3 baseScale = Vector3.one;
    ParticleSystem.EmissionModule smokeEmission;

    Transform bodyPivot;
    Vector3 visualRestLocal;
    Vector2 bodyPos;
    Vector2 bodyVel;

    TextMesh prompt;
    TextMesh counter;
    Possessable2D candidate;
    Possessable2D lastCandidate;
    float lockUntil;

    Vector2 Velocity
    {
#if UNITY_6000_0_OR_NEWER
        get => rb.linearVelocity;
        set => rb.linearVelocity = value;
#else
        get => rb.velocity;
        set => rb.velocity = value;
#endif
    }

    // ================================================================ setup

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        noiseSeed = Random.value * 100f;
        bodyPos = transform.position;

        if (visual == null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.transform != transform) visual = sr.transform;
            else if (sr != null) visual = MoveSpriteToChild(sr); // SpriteRenderer อยู่บน Player เอง → ย้ายไป child ให้
        }

        if (visual != null && visual != transform) BuildBodyPivot();
        else Debug.LogWarning("GhostController2D: ไม่เจอ SpriteRenderer ของผี เอฟเฟกต์หัวนำ-ตัวไหลจะไม่ทำงาน", this);

        if (smoke == null) smoke = CreateDefaultSmoke();
        smokeEmission = smoke.emission;

        prompt = MakeText("PossessPrompt", promptSize, promptColor);
        counter = MakeText("RepairCounter", promptSize, counterColor);
    }

    void Start()
    {
        if (!debugLog) return;

        Debug.Log("[Ghost] พบของเล่นที่สิงได้ " + Possessable2D.All.Count + " ชิ้น, ไอเทม " + ItemPickup2D.All.Count + " ชิ้น");
        if (Possessable2D.All.Count == 0)
            Debug.LogWarning("[Ghost] ยังไม่มีของเล่นตัวไหนมี Possessable2D (หรือถูกปิดอยู่) — เลือกหมีแล้ว Add Component > Possessable2D", this);
    }

    void OnEnable()
    {
        bodyPos = transform.position;
        bodyVel = Vector2.zero;
    }

    void OnDestroy()
    {
        if (prompt != null) Destroy(prompt.gameObject);
        if (counter != null) Destroy(counter.gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, possessRange);
    }

    // ถ้า SpriteRenderer อยู่บน Player ตรงๆ: สร้าง child "Visual" แล้วคัดลอกค่าไปให้ จากนั้นลบตัวเดิมทิ้ง
    // (ถ้าใช้ Animator เล่นภาพผีบน Player ให้ย้าย Animator + SpriteRenderer ไปไว้ที่ child ด้วยมือแทน)
    Transform MoveSpriteToChild(SpriteRenderer src)
    {
        var go = new GameObject("Visual");
        go.transform.SetParent(transform, false);

        var dst = go.AddComponent<SpriteRenderer>();
        dst.sprite = src.sprite;
        dst.color = src.color;
        dst.flipX = src.flipX;
        dst.flipY = src.flipY;
        dst.sharedMaterial = src.sharedMaterial;
        dst.sortingLayerID = src.sortingLayerID;
        dst.sortingOrder = src.sortingOrder;
        dst.maskInteraction = src.maskInteraction;

        Destroy(src);
        return go.transform;
    }

    void BuildBodyPivot()
    {
        Vector3 visLocal = transform.InverseTransformPoint(visual.position);

        bodyPivot = new GameObject("BodyPivot").transform;
        bodyPivot.SetParent(transform, false);
        bodyPivot.localPosition = visLocal + (Vector3)pivotOffset;

        visual.SetParent(bodyPivot, true);
        visualRestLocal = visual.localPosition;
        baseScale = visual.localScale;
    }

    // ================================================================ loop

    void Update()
    {
        if (Current != null)
        {
            // ตอนสิงอยู่ ให้ผีตามของเล่นไปเสมอ (กล้องที่ตามผีจะตามของเล่นด้วย)
            input = Vector2.zero;
            transform.position = Current.transform.position;
            UpdatePossessed();
            return;
        }

        input = ReadInput();
        UpdateNearbyToy();
    }

    void FixedUpdate()
    {
        if (Current != null) return;

        Vector2 vel = Velocity;
        float dt = Time.fixedDeltaTime;

        if (input.sqrMagnitude > 0.0001f)
        {
            // กำลังบังคับ: หัวพุ่งนำเข้าหาความเร็วเป้าหมาย
            vel = Vector2.MoveTowards(vel, input * maxSpeed, acceleration * dt);
        }
        else
        {
            // ปล่อยปุ่ม: ไหลต่อแล้วค่อยๆ ช้าลง
            vel = Vector2.MoveTowards(vel, Vector2.zero, deceleration * dt);

            // แรงลอยเบาๆ (Perlin noise)
            float t = Time.time * driftSpeed + noiseSeed;
            Vector2 drift = new Vector2(
                Mathf.PerlinNoise(t, 0f) - 0.5f,
                Mathf.PerlinNoise(0f, t + 17f) - 0.5f) * 2f;
            vel += drift * (driftStrength * dt);
        }

        Velocity = vel;
    }

    void LateUpdate()
    {
        if (Current != null) return; // ซ่อนตัวอยู่ ไม่ต้องคำนวณ

        if (bodyPivot == null)
        {
            UpdateSmoke(null);
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        Vector2 headPos = transform.position;
        Vector2 vel = Velocity;

        // ---- สปริงดึงตัวตามหัว (underdamped → ตัวไหลเลยแล้วแกว่งกลับตอนหยุด) ----
        float omega = 2f * Mathf.PI * Mathf.Max(0.1f, followFrequency);
        Vector2 acc = omega * omega * (headPos - bodyPos) - 2f * followDamping * omega * bodyVel;
        bodyVel += acc * dt;
        bodyPos += bodyVel * dt;

        Vector2 lag = bodyPos - headPos; // ชี้จากหัว "ไปทางตัว" (ตรงข้ามทิศที่หัวนำ)
        if (lag.magnitude > maxLag)
        {
            lag = lag.normalized * maxLag;
            bodyPos = headPos + lag;
        }

        // ---- สถานะนิ่ง/เคลื่อนที่ ----
        float speedRatio = Mathf.Clamp01(vel.magnitude / Mathf.Max(0.01f, maxSpeed));
        float targetIdle = 1f - Mathf.Clamp01(speedRatio * 2f);
        idleBlend = Mathf.Lerp(idleBlend, targetIdle, 1f - Mathf.Exp(-4f * Time.deltaTime));

        // ---- หัวเอนนำ + แกว่งเบาๆ ตอนนิ่ง ----
        float lean = Mathf.Clamp(lag.x * leanPerUnit, -maxLean, maxLean);
        float sway = Mathf.Sin(Time.time * Mathf.PI * 2f * idleSwayFrequency) * idleSwayAngle * idleBlend;

        // ---- ยืดตามแนวหัว-หาง ----
        float stretch = lag.magnitude * stretchPerUnit;

        bodyPivot.position = new Vector3(
            bodyPos.x + pivotOffset.x,
            bodyPos.y + pivotOffset.y,
            transform.position.z);
        bodyPivot.rotation = Quaternion.Euler(0f, 0f, lean + sway);
        bodyPivot.localScale = new Vector3(1f / (1f + stretch * 0.5f), 1f + stretch, 1f);

        // ---- ลอยขึ้นลง ----
        float bobScale = Mathf.Lerp(bobWhileMoving, 1f, idleBlend);
        float bob = Mathf.Sin(Time.time * Mathf.PI * 2f * bobFrequency) * bobAmplitude * bobScale;
        visual.localPosition = visualRestLocal + new Vector3(0f, bob, 0f);

        // ---- กลับด้านตามทิศ (รูปผีหันขวาเป็นค่าเริ่มต้น) ----
        if (flipByDirection && Mathf.Abs(vel.x) > 0.15f)
        {
            Vector3 s = baseScale;
            s.x = Mathf.Abs(baseScale.x) * Mathf.Sign(vel.x);
            visual.localScale = s;
        }

        UpdateSmoke(bodyPivot);
    }

    // ================================================================ สิง

    // ตอนเป็นผีอิสระ: หาของเล่นที่ใกล้ที่สุด → outline ขาวกระพริบ + F
    void UpdateNearbyToy()
    {
        candidate = FindNearestToy();

        if (candidate != lastCandidate)
        {
            if (lastCandidate != null) lastCandidate.SetHighlight(false);
            if (candidate != null) candidate.SetHighlight(true);
            lastCandidate = candidate;
        }

        if (candidate != null) PlacePrompt(candidate.transform.position + (Vector3)candidate.promptOffset);
        else ShowText(prompt, false);

        if (candidate != null && FPressed) Possess(candidate);
    }

    // ตอนสิงอยู่: เก็บไอเทม / ออกจากร่าง
    void UpdatePossessed()
    {
        // ซ่อมเสร็จแล้ว → ออกจากร่าง
        if (Current.IsRepaired)
        {
            Release();
            return;
        }

        bool repairing = Current.CanCollectItems;
        UpdateCounter(repairing);

        ItemPickup2D item = repairing ? FindNearestItem(Current.transform.position) : null;

        if (item != null)
        {
            PlacePrompt(item.transform.position + (Vector3)item.promptOffset);

            if (FPressed)
            {
                item.Collect();
                Current.AddItem();
                ShowText(prompt, false);

                if (Current.IsRepaired) Release(); // ครบแล้ว ออกจากร่างทันที
            }
        }
        else
        {
            ShowText(prompt, false);
            if (FPressed) Release();
        }
    }

    void UpdateCounter(bool show)
    {
        if (!show)
        {
            ShowText(counter, false);
            return;
        }

        ShowText(counter, true);
        counter.text = Current.ItemsCollected + "/" + Current.requiredItems;
        counter.transform.position = Current.transform.position
                                     + (Vector3)Current.promptOffset
                                     + new Vector3(0f, 0.35f, 0f);
    }

    void Possess(Possessable2D target)
    {
        // เลิกกระพริบสีขาว เปลี่ยนเป็น outline เขียวของตอนสิง
        if (lastCandidate != null) lastCandidate.SetHighlight(false);
        lastCandidate = null;

        Current = target;
        Current.Possess();

        lockUntil = Time.unscaledTime + actionCooldown;
        if (debugLog) Debug.Log("[Ghost] POSSESS " + target.name + " (frame " + Time.frameCount + ")", target);

        ZeroVelocity();
        rb.simulated = false;
        SetGhostVisible(false);

        transform.position = target.transform.position;
        ShowText(prompt, false);
    }

    void Release()
    {
        Vector3 exitPos = Current.transform.position + (Vector3)exitOffset;

        lockUntil = Time.unscaledTime + actionCooldown;
        if (debugLog) Debug.Log("[Ghost] RELEASE " + Current.name + " (frame " + Time.frameCount + ", repaired=" + Current.IsRepaired + ")", Current);

        Current.Release();
        Current = null;

        transform.position = exitPos;
        rb.simulated = true;
        Physics2D.SyncTransforms();
        ZeroVelocity();

        // ให้ตัวอยู่ที่หัวทันที ไม่ไหลข้ามฉาก
        bodyPos = exitPos;
        bodyVel = Vector2.zero;

        SetGhostVisible(true);

        ShowText(prompt, false);
        ShowText(counter, false);
    }

    void SetGhostVisible(bool visible)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;

        // หยุด/เล่นควัน ไม่ให้ปล่อยค้างตอนผีหาย
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (visible) ps.Play();
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    void ZeroVelocity()
    {
        Velocity = Vector2.zero;
    }

    Possessable2D FindNearestToy()
    {
        Possessable2D best = null;
        float bestSqr = possessRange * possessRange;
        Vector2 p = transform.position;

        foreach (var toy in Possessable2D.All)
        {
            if (toy == null || !toy.CanBePossessed) continue;
            float d = ((Vector2)toy.transform.position - p).sqrMagnitude;
            if (d <= bestSqr)
            {
                bestSqr = d;
                best = toy;
            }
        }
        return best;
    }

    ItemPickup2D FindNearestItem(Vector2 from)
    {
        ItemPickup2D best = null;
        float bestSqr = itemRange * itemRange;

        foreach (var item in ItemPickup2D.All)
        {
            if (item == null || item.IsCollected) continue;
            float d = ((Vector2)item.transform.position - from).sqrMagnitude;
            if (d <= bestSqr)
            {
                bestSqr = d;
                best = item;
            }
        }
        return best;
    }

    // ================================================================ ตัวหนังสือ (F / x/3)

    TextMesh MakeText(string objName, float charSize, Color color)
    {
        var go = new GameObject(objName); // ไม่ผูกกับผี เพราะผีจะถูกซ่อนตอนสิง
        var tm = go.AddComponent<TextMesh>();
        tm.text = "F";
        tm.fontSize = 64;
        tm.characterSize = charSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;

#if UNITY_2022_2_OR_NEWER
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        tm.font = font;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = font.material;
        mr.sortingOrder = 100;

        go.SetActive(false);
        return tm;
    }

    void PlacePrompt(Vector3 pos)
    {
        ShowText(prompt, true);

        float bob = Mathf.Sin(Time.time * 4f) * 0.06f;
        float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);

        prompt.transform.position = pos + new Vector3(0f, bob, 0f);
        prompt.transform.localScale = Vector3.one * pulse;
    }

    static void ShowText(TextMesh tm, bool show)
    {
        if (tm != null && tm.gameObject.activeSelf != show)
            tm.gameObject.SetActive(show);
    }

    // ================================================================ ควัน

    void UpdateSmoke(Transform follow)
    {
        if (smoke == null) return;

        // ควันออกจากปลายตัว (ตามตัวที่ไหลอยู่)
        if (follow != null)
            smoke.transform.position = follow.TransformPoint(smokeOffset);

        float speed = Mathf.Max(Velocity.magnitude, bodyVel.magnitude * 0.6f);
        float ratio = Mathf.Clamp01(speed / Mathf.Max(0.01f, maxSpeed));
        smokeEmission.rateOverTime = speed > smokeMinSpeed ? ratio * smokeMaxRate : 0f;
    }

    ParticleSystem CreateDefaultSmoke()
    {
        var go = new GameObject("GhostSmoke");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = (Vector3)pivotOffset + (Vector3)smokeOffset;

        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // ให้ควันค้างเป็นทางตามหลัง
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.85f, 0.92f, 1f, 0.55f);
        main.gravityModifier = -0.05f; // ลอยขึ้นนิดๆ
        main.maxParticles = 250;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.12f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.9f, 0.95f, 1f), 0f),
                    new GradientColorKey(new Color(0.6f, 0.7f, 0.85f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.6f, 0.2f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.7f));

        var rend = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.mainTexture = CreateSoftCircle();
        rend.material = mat;
        rend.sortingOrder = smokeSortingOrder;

        return ps;
    }

    static Texture2D CreateSoftCircle()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    // ================================================================ input

    // ปุ่ม F ที่ผ่านตัวกันกดซ้ำแล้ว
    bool FPressed => Time.unscaledTime >= lockUntil && PressedF();

    static bool PressedF()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F);
#endif
    }

    static Vector2 ReadInput()
    {
        Vector2 v;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        v.x = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1f : 0f)
            - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1f : 0f);
        v.y = ((k.wKey.isPressed || k.upArrowKey.isPressed) ? 1f : 0f)
            - ((k.sKey.isPressed || k.downArrowKey.isPressed) ? 1f : 0f);
#else
        v.x = Input.GetAxisRaw("Horizontal");
        v.y = Input.GetAxisRaw("Vertical");
#endif
        return Vector2.ClampMagnitude(v, 1f);
    }
}
