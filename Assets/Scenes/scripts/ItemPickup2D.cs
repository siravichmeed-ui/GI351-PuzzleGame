using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ไอเทมที่เก็บเพื่อซ่อมตุ๊กตา (ติดสคริปต์นี้ตัวเดียวพอ ที่ object ที่มี SpriteRenderer)
/// - มี neon outline สีขาวกระพริบตลอดเวลา
/// - ตอนตุ๊กตาที่ผีสิงอยู่เข้าใกล้ จะขึ้นตัว F ให้กดเก็บ (GhostController2D เป็นคนจัดการ)
/// </summary>
public class ItemPickup2D : MonoBehaviour
{
    /// <summary>ไอเทมที่ยังอยู่ในฉากทั้งหมด</summary>
    public static readonly List<ItemPickup2D> All = new List<ItemPickup2D>();

    [Tooltip("ตำแหน่งปุ่ม F เมื่อเทียบกับไอเทม")]
    public Vector2 promptOffset = new Vector2(0f, 0.7f);

    [Tooltip("เวลาเอฟเฟกต์ตอนเก็บ (วินาที)")]
    public float popDuration = 0.2f;

    [Header("Neon Outline")]
    public NeonOutlineSettings outlineStyle = new NeonOutlineSettings();

    public bool IsCollected { get; private set; }

    NeonOutline outline;

    void Awake()
    {
        outline = new NeonOutline(transform, outlineStyle);
        outline.mode = NeonMode.Blink; // ขาวกระพริบตลอด
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy() { if (outline != null) outline.Dispose(); }

    void LateUpdate()
    {
        outline.Update();
    }

    public void Collect()
    {
        if (IsCollected) return;
        IsCollected = true;
        outline.mode = NeonMode.Off;
        StartCoroutine(Pop());
    }

    // ขยายนิดหนึ่งแล้วหดหายไป
    IEnumerator Pop()
    {
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / popDuration);
            float s = k < 0.3f ? Mathf.Lerp(1f, 1.3f, k / 0.3f) : Mathf.Lerp(1.3f, 0f, (k - 0.3f) / 0.7f);
            transform.localScale = start * s;
            yield return null;
        }
        Destroy(gameObject);
    }
}
