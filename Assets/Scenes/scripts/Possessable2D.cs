using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ติดที่ "ตุ๊กตา/ของเล่น" ที่ผีสิงได้ (ติดสคริปต์นี้ตัวเดียวพอ — Rigidbody2D จะถูกเพิ่มให้เอง)
/// - ผีเข้าใกล้: neon outline สีขาวกระพริบ
/// - ถูกสิง: บังคับด้วย WASD / ลูกศร + neon outline สีเขียว
/// - ถ้า needsRepair: ต้องเก็บไอเทมให้ครบ requiredItems ชิ้นเพื่อซ่อม
///   พอซ่อมเสร็จ ภาพเปลี่ยนเป็น repairedSprite ผีออกจากร่างเอง และตุ๊กตาถูกปล่อยไว้ตรงนั้น
///
/// ไฟล์นี้มีตัววาด neon outline (NeonOutline) อยู่ด้วย ใช้ร่วมกับ ItemPickup2D
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Possessable2D : MonoBehaviour
{
    public enum MoveMode
    {
        [Tooltip("ขยับได้ทุกทิศ (top-down / ลอยได้) ปิดแรงโน้มถ่วงให้อัตโนมัติ")]
        Free,
        [Tooltip("เดินซ้าย-ขวา แรงโน้มถ่วงยังทำงานตามปกติ")]
        Horizontal
    }

    /// <summary>รายการของเล่นที่เปิดใช้งานอยู่ทั้งหมด (ผีใช้ค้นหาตัวที่ใกล้ที่สุด)</summary>
    public static readonly List<Possessable2D> All = new List<Possessable2D>();

    [Header("Movement (ตอนถูกสิง)")]
    public MoveMode moveMode = MoveMode.Free;
    public float moveSpeed = 4f;
    public float acceleration = 30f;
    public float deceleration = 25f;
    public bool freezeRotation = true;
    public bool flipByDirection = true;

    [Header("Prompt (ปุ่ม F / ตัวนับ)")]
    [Tooltip("ตำแหน่งปุ่ม F และตัวนับไอเทม เมื่อเทียบกับตุ๊กตา")]
    public Vector2 promptOffset = new Vector2(0f, 1f);

    [Header("Repair (ซ่อมตุ๊กตา)")]
    [Tooltip("ติ๊กออกถ้าเป็นของเล่นที่ไม่ต้องซ่อม")]
    public bool needsRepair = true;
    public int requiredItems = 3;
    [Tooltip("ซ่อมเสร็จแล้วให้สิงซ้ำได้ไหม")]
    public bool canPossessAfterRepair = false;
    [Tooltip("ภาพตุ๊กตาหลังซ่อมเสร็จ")]
    public Sprite repairedSprite;
    [Tooltip("SpriteRenderer ที่จะเปลี่ยนภาพ (เว้นว่าง = หาให้อัตโนมัติ)")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("เรียกตอนซ่อมเสร็จ (เล่นเสียง, เปิดประตู ฯลฯ)")]
    public UnityEvent onRepaired = new UnityEvent();

    [Header("Neon Outline")]
    public NeonOutlineSettings outlineStyle = new NeonOutlineSettings();

    public bool IsPossessed { get; private set; }
    public bool IsHighlighted { get; private set; }
    public bool IsRepaired { get; private set; }
    public int ItemsCollected { get; private set; }

    public bool CanCollectItems => needsRepair && !IsRepaired;
    public bool CanBePossessed => !IsPossessed && (canPossessAfterRepair || !IsRepaired);

    Rigidbody2D rb;
    NeonOutline outline;
    Vector2 input;

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

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        rb.freezeRotation = freezeRotation;
        if (moveMode == MoveMode.Free) rb.gravityScale = 0f;

        outline = new NeonOutline(transform, outlineStyle);
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy() { if (outline != null) outline.Dispose(); }

    // ------------------------------------------------------------ possession

    public void Possess()
    {
        IsPossessed = true;
    }

    public void Release()
    {
        IsPossessed = false;
        input = Vector2.zero;

        // หยุดของเล่นเมื่อผีออก
        Vector2 v = Velocity;
        v.x = 0f;
        if (moveMode == MoveMode.Free) v.y = 0f;
        Velocity = v;
    }

    /// <summary>ผีเรียกตอนเข้า/ออกจากระยะสิง เพื่อให้ outline สีขาวกระพริบ</summary>
    public void SetHighlight(bool on)
    {
        IsHighlighted = on;
    }

    /// <summary>เก็บไอเทมได้ 1 ชิ้น ครบตามกำหนดแล้วตุ๊กตาจะซ่อมเสร็จ</summary>
    public void AddItem()
    {
        if (!CanCollectItems) return;

        ItemsCollected++;
        if (ItemsCollected >= requiredItems)
        {
            IsRepaired = true;
            ApplyRepairedSprite();
            onRepaired.Invoke();
        }
    }

    // เปลี่ยนเป็นภาพหลังซ่อม แล้วให้ outline สร้างรูปทรงใหม่ตามภาพนั้น
    void ApplyRepairedSprite()
    {
        if (repairedSprite == null || spriteRenderer == null) return;

        spriteRenderer.sprite = repairedSprite;
        outline.Rebuild();
    }

    // ----------------------------------------------------------------- loop

    void Update()
    {
        input = IsPossessed ? ReadInput() : Vector2.zero;

        if (IsPossessed && flipByDirection && Mathf.Abs(Velocity.x) > 0.1f)
        {
            Vector3 s = transform.localScale;
            s.x = Mathf.Abs(s.x) * Mathf.Sign(Velocity.x);
            transform.localScale = s;
        }

        outline.mode = IsPossessed ? NeonMode.Solid
                     : IsHighlighted ? NeonMode.Blink
                     : NeonMode.Off;
    }

    void FixedUpdate()
    {
        if (!IsPossessed) return;

        float dt = Time.fixedDeltaTime;
        Vector2 vel = Velocity;

        if (moveMode == MoveMode.Free)
        {
            Vector2 target = input * moveSpeed;
            float rate = input.sqrMagnitude > 0.0001f ? acceleration : deceleration;
            vel = Vector2.MoveTowards(vel, target, rate * dt);
        }
        else
        {
            float targetX = input.x * moveSpeed;
            float rate = Mathf.Abs(input.x) > 0.01f ? acceleration : deceleration;
            vel.x = Mathf.MoveTowards(vel.x, targetX, rate * dt); // y ปล่อยให้ฟิสิกส์จัดการ
        }

        Velocity = vel;
    }

    void LateUpdate()
    {
        outline.Update();
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

// =====================================================================
//  Neon outline (ใช้ร่วมกันระหว่าง Possessable2D และ ItemPickup2D)
//  ไม่ใช่ component — ไม่ต้องติดที่ object ไหน
// =====================================================================

public enum NeonMode { Off, Blink, Solid }

/// <summary>ค่าปรับแต่ง outline (โชว์ใน Inspector ของ Possessable2D / ItemPickup2D)</summary>
[System.Serializable]
public class NeonOutlineSettings
{
    [Tooltip("สีตอนถูกสิง")]
    public Color solidColor = new Color(0.15f, 1f, 0.35f, 1f);
    [Tooltip("สีตอนกระพริบ (ผีอยู่ใกล้ / ไอเทม)")]
    public Color blinkColor = Color.white;
    [Tooltip("ความหนาของเส้นแกนกลาง (เส้นเรืองแสงรอบๆ จะหนาเป็นทวีคูณ)")]
    public float width = 0.04f;
    [Tooltip("จำนวนครั้งที่กระพริบต่อวินาที")]
    public float blinkSpeed = 2.5f;
    public float fadeSpeed = 8f;
    [Tooltip("จำนวนเส้นรอบรูปสูงสุด (กรณี sprite มีหลายชิ้น)")]
    public int maxLoops = 4;
}

/// <summary>
/// Neon outline รอบรูปทรงของ object
/// รูปทรงใช้ตามลำดับ: PolygonCollider2D → Physics Shape ของ Sprite → กรอบสี่เหลี่ยมของ Sprite
/// </summary>
public sealed class NeonOutline
{
    sealed class Loop
    {
        public Vector2[] pts;
        public Transform space;
        public Vector2 offset;
        public Vector3[] world;
        public LineRenderer[] layers;
    }

    // เส้นเรืองแสง 3 ชั้น: นอกสุด (จาง/กว้าง) → กลาง → แกนสว่าง (บาง)
    static readonly float[] LayerWidthMul = { 5f, 2.5f, 1f };
    static readonly float[] LayerAlpha = { 0.16f, 0.4f, 1f };

    /// <summary>โหมดปัจจุบัน (Off / Blink ขาวกระพริบ / Solid เขียวค้าง)</summary>
    public NeonMode mode = NeonMode.Off;

    readonly Transform owner;
    readonly NeonOutlineSettings s;
    readonly List<Loop> loops = new List<Loop>();
    SpriteRenderer sprite;
    GameObject root;
    Material mat;
    Color cur;
    float alpha;
    bool visible;

    public NeonOutline(Transform owner, NeonOutlineSettings settings)
    {
        this.owner = owner;
        s = settings;
        cur = s.blinkColor;
        Build();
    }

    /// <summary>เรียกทุกเฟรม (ใน LateUpdate ของเจ้าของ)</summary>
    public void Update()
    {
        if (loops.Count == 0) return;

        float target = mode == NeonMode.Off ? 0f : 1f;
        alpha = Mathf.MoveTowards(alpha, target, s.fadeSpeed * Time.deltaTime);

        bool show = alpha > 0.001f;
        if (show != visible) SetEnabled(show);
        if (!show) return;

        // สี: เขียว / ขาว เปลี่ยนแบบนุ่มๆ
        Color targetCol = mode == NeonMode.Solid ? s.solidColor : s.blinkColor;
        cur = Color.Lerp(cur, targetCol, 1f - Mathf.Exp(-12f * Time.deltaTime));

        // ความสว่าง: Solid = เต้นเบาๆ / Blink = กระพริบชัด
        float intensity;
        if (mode == NeonMode.Solid)
        {
            intensity = 0.88f + 0.12f * Mathf.Sin(Time.time * 3f);
        }
        else
        {
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * s.blinkSpeed);
            intensity = Mathf.Lerp(0.12f, 1f, blink);
        }

        float a = alpha * intensity;
        Color hot = Color.Lerp(cur, Color.white, 0.55f); // แกนกลางสว่างจัดแบบ neon

        foreach (var loop in loops)
        {
            Transform t = loop.space;
            for (int i = 0; i < loop.pts.Length; i++)
                loop.world[i] = t.TransformPoint(loop.pts[i] + loop.offset);

            for (int k = 0; k < loop.layers.Length; k++)
            {
                var lr = loop.layers[k];
                lr.SetPositions(loop.world);

                Color c = (k == loop.layers.Length - 1) ? hot : cur;
                c.a = LayerAlpha[k] * a;
                lr.startColor = c;
                lr.endColor = c;
                lr.widthMultiplier = s.width * LayerWidthMul[k];
            }
        }
    }

    /// <summary>สร้างเส้นใหม่ตามรูปทรงปัจจุบัน (เรียกหลังเปลี่ยน Sprite)</summary>
    public void Rebuild()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        loops.Clear();
        Build();
    }

    /// <summary>ลบเส้นและวัสดุทิ้ง (เรียกตอน object ถูกทำลาย)</summary>
    public void Dispose()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        if (mat != null) UnityEngine.Object.Destroy(mat);
    }

    // ---------------------------------------------------------------- build

    void Build()
    {
        sprite = owner.GetComponentInChildren<SpriteRenderer>();
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));

        root = new GameObject("NeonOutline");
        root.transform.SetParent(owner, false);

        var poly = owner.GetComponentInChildren<PolygonCollider2D>();
        if (poly != null && poly.pathCount > 0)
        {
            for (int i = 0; i < poly.pathCount && loops.Count < s.maxLoops; i++)
                AddLoop(poly.GetPath(i), poly.transform, poly.offset);
        }
        else if (sprite != null && sprite.sprite != null)
        {
            Sprite sp = sprite.sprite;
            int count = sp.GetPhysicsShapeCount();
            var tmp = new List<Vector2>();
            for (int i = 0; i < count && loops.Count < s.maxLoops; i++)
            {
                tmp.Clear();
                sp.GetPhysicsShape(i, tmp);
                if (tmp.Count >= 3)
                    AddLoop(tmp.ToArray(), sprite.transform, Vector2.zero);
            }

            if (loops.Count == 0)
            {
                Bounds b = sp.bounds;
                var rect = new[]
                {
                    new Vector2(b.min.x, b.min.y), new Vector2(b.min.x, b.max.y),
                    new Vector2(b.max.x, b.max.y), new Vector2(b.max.x, b.min.y)
                };
                AddLoop(rect, sprite.transform, Vector2.zero);
            }
        }

        SetEnabled(false);
    }

    void AddLoop(Vector2[] pts, Transform space, Vector2 offset)
    {
        var loop = new Loop
        {
            pts = pts,
            space = space,
            offset = offset,
            world = new Vector3[pts.Length],
            layers = new LineRenderer[LayerWidthMul.Length]
        };

        for (int k = 0; k < loop.layers.Length; k++)
        {
            var go = new GameObject("Loop" + loops.Count + "_Layer" + k);
            go.transform.SetParent(root.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = pts.Length;
            lr.numCornerVertices = 4;
            lr.numCapVertices = 4;
            lr.material = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            if (sprite != null)
            {
                lr.sortingLayerID = sprite.sortingLayerID;
                lr.sortingOrder = sprite.sortingOrder + 1 + k; // อยู่หน้าวัตถุ แกนสว่างบนสุด
            }
            else
            {
                lr.sortingOrder = 1 + k;
            }

            loop.layers[k] = lr;
        }

        loops.Add(loop);
    }

    void SetEnabled(bool on)
    {
        visible = on;
        foreach (var loop in loops)
            foreach (var lr in loop.layers)
                lr.enabled = on;
    }
}
