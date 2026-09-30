using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 2026-09-30: 피해 숫자에 FEEL이 못 하는 부분을 더한다. 글자별 정점 연출(갈라짐·떨림·일렁임·가라앉음)과
// 작은 장식(불씨·금·번개·흘러내림·빛줄기). 움직임 전체(오르기·크기·투명도)는 FEEL 곡선이 그대로 맡는다.
// 정점은 TMP가 메시를 다시 만들 때(OnPreRenderText) 원본을 받아 두고, 매 프레임 원본에서 다시 계산해 올린다.
[DisallowMultipleComponent]
public sealed class DamageNumberFx : MonoBehaviour
{
    private const int MaxAccents = 6;
    private const int MaxShakeSlots = 24;
    private const float ShatterAt = .3f;

    private DamageNumberPopup popup;
    private TMP_Text text;
    private RectTransform rect;
    private DamageNumberFxKind kind;
    private float seed;

    private Vector3[][] source = new Vector3[0][];
    private bool cached;
    private int numberLine;
    private int numberCount;
    private Vector2 numberMin, numberMax;

    private readonly Vector2[] shake = new Vector2[MaxShakeSlots];
    private float nextShakeAt;

    private Image[] accents;
    private RectTransform[] accentRects;
    private readonly Vector2[] accentStart = new Vector2[MaxAccents];
    private readonly Vector2[] accentVelocity = new Vector2[MaxAccents];
    private readonly float[] accentSize = new float[MaxAccents];
    private readonly float[] accentLife = new float[MaxAccents];
    private readonly Color[] accentTint = new Color[MaxAccents];
    private int accentCount;

    public DamageNumberFxKind Kind => kind;

    public void Bind(DamageNumberPopup owner, TMP_Text target)
    {
        if (text == target && popup == owner)
            return;
        if (text != null)
            text.OnPreRenderText -= HandlePreRender;
        popup = owner;
        text = target;
        rect = transform as RectTransform;
        if (text != null)
            text.OnPreRenderText += HandlePreRender;
        enabled = false;
    }

    private void OnDestroy()
    {
        if (text != null)
            text.OnPreRenderText -= HandlePreRender;
    }

    public void Begin(DamageNumberFxKind fxKind)
    {
        kind = fxKind;
        seed = Random.Range(0f, 100f);
        cached = false;
        nextShakeAt = 0f;
        HideAccents();
        enabled = kind != DamageNumberFxKind.None;
        if (!enabled || text == null)
            return;
        SetupAccents();
        text.ForceMeshUpdate(); // Caches the source geometry through HandlePreRender right away.
    }

    public void Stop()
    {
        kind = DamageNumberFxKind.None;
        cached = false;
        HideAccents();
        enabled = false;
    }

    private void LateUpdate()
    {
        if (kind == DamageNumberFxKind.None || text == null || !cached)
            return;
        TMP_TextInfo info = text.textInfo;
        if (info == null || !Apply(info))
            return;
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        UpdateAccents();
    }

    private void HandlePreRender(TMP_TextInfo info)
    {
        if (kind == DamageNumberFxKind.None || info == null)
            return;
        Cache(info);
        Apply(info); // The mesh TMP is about to upload already carries this frame's offsets.
    }

    private void Cache(TMP_TextInfo info)
    {
        int meshCount = info.meshInfo.Length;
        if (source.Length < meshCount)
            System.Array.Resize(ref source, meshCount);
        for (int i = 0; i < meshCount; i++)
        {
            Vector3[] vertices = info.meshInfo[i].vertices;
            if (vertices == null)
                continue;
            if (source[i] == null || source[i].Length < vertices.Length)
                source[i] = new Vector3[vertices.Length];
            System.Array.Copy(vertices, source[i], vertices.Length);
        }

        numberLine = Mathf.Max(0, info.lineCount - 1);
        numberCount = 0;
        numberMin = new Vector2(float.MaxValue, float.MaxValue);
        numberMax = new Vector2(float.MinValue, float.MinValue);
        for (int c = 0; c < info.characterCount; c++)
        {
            TMP_CharacterInfo character = info.characterInfo[c];
            if (!character.isVisible || character.lineNumber != numberLine)
                continue;
            numberCount++;
            numberMin = Vector2.Min(numberMin, new Vector2(character.bottomLeft.x, character.descender));
            numberMax = Vector2.Max(numberMax, new Vector2(character.topRight.x, character.ascender));
        }
        if (numberCount == 0)
        {
            numberMin = numberMax = Vector2.zero;
        }
        cached = true;
    }

    private bool Apply(TMP_TextInfo info)
    {
        if (!cached || popup == null)
            return false;
        float elapsed = popup.FxElapsed;
        float progress = popup.FxProgress;
        RefreshShake(elapsed);

        int numberIndex = 0;
        for (int c = 0; c < info.characterCount; c++)
        {
            TMP_CharacterInfo character = info.characterInfo[c];
            if (!character.isVisible)
                continue;
            int mesh = character.materialReferenceIndex;
            int vertex = character.vertexIndex;
            if (mesh < 0 || mesh >= source.Length || source[mesh] == null || mesh >= info.meshInfo.Length)
                continue;
            Vector3[] from = source[mesh];
            Vector3[] to = info.meshInfo[mesh].vertices;
            if (to == null || vertex + 3 >= from.Length || vertex + 3 >= to.Length)
                continue;

            bool isNumber = character.lineNumber == numberLine;
            Evaluate(isNumber, isNumber ? numberIndex : -1, elapsed, progress,
                out Vector2 move, out float angle, out Vector2 scale, out bool rigid, out Vector2 pivot);
            if (isNumber)
                numberIndex++;

            // A rigid piece (ice halves) turns around the split point; everything else around its own glyph center.
            Vector3 center = rigid ? new Vector3(pivot.x, pivot.y, 0f) : (from[vertex] + from[vertex + 2]) * .5f;
            float radians = angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            for (int k = 0; k < 4; k++)
            {
                Vector3 d = from[vertex + k] - center;
                d.x *= scale.x;
                d.y *= scale.y;
                to[vertex + k] = new Vector3(center.x + d.x * cos - d.y * sin + move.x,
                    center.y + d.x * sin + d.y * cos + move.y, from[vertex + k].z);
            }
        }
        return true;
    }

    private void Evaluate(bool isNumber, int index, float e, float t,
        out Vector2 move, out float angle, out Vector2 scale, out bool rigid, out Vector2 pivot)
    {
        move = Vector2.zero;
        angle = 0f;
        scale = Vector2.one;
        rigid = false;
        pivot = Vector2.zero;
        if (!isNumber)
            return;

        switch (kind)
        {
            case DamageNumberFxKind.Critical:
                if (e < .14f)
                    move.x = Mathf.Sin(e * 80f) * 1f * (1f - e / .14f);
                break;

            case DamageNumberFxKind.PlayerHit:
                if (e < .28f)
                    move.x = Mathf.Sin(e * 60f) * 2.4f * (1f - e / .28f);
                break;

            case DamageNumberFxKind.Ember:
                // 연소: 글자마다 다른 박자로 일렁인다.
                // v2: 젤리처럼 보이지 않게 아주 작게만 일렁인다.
                move.y = Mathf.Sin(e * 20f + index * 1.4f + seed) * .7f * (1f - t);
                break;

            case DamageNumberFxKind.Shatter:
            {
                // 쇄빙: 멈춘 동안 잘게 떨다가 가운데에서 둘로 갈라진다.
                if (e < ShatterAt)
                {
                    if (e > .1f) move.x = Mathf.Sin(e * 90f + index) * .7f;
                    break;
                }
                float split = EaseOutCubic(Mathf.Clamp01((e - ShatterAt) / .18f));
                float fall = Mathf.Clamp01((e - ShatterAt - .12f) / .45f);
                fall *= fall;
                bool left = index < numberCount * .5f;
                move = left ? new Vector2(-7f * split, -2f * split - 9f * fall) : new Vector2(7f * split, 1f * split - 13f * fall);
                angle = left ? 8f * split : -10f * split;
                rigid = true;
                pivot = new Vector2((numberMin.x + numberMax.x) * .5f, numberMin.y);
                break;
            }

            case DamageNumberFxKind.Shock:
            {
                // 감전: 글자마다 따로 튀는 떨림, 시간이 갈수록 약해진다.
                float amplitude = 2.2f * Mathf.Pow(1f - t, 1.2f);
                Vector2 jitter = shake[Mathf.Clamp(index, 0, MaxShakeSlots - 1)];
                move = new Vector2(jitter.x * amplitude, jitter.y * amplitude * .35f);
                break;
            }

            case DamageNumberFxKind.Sink:
            {
                // 잠식: 글자가 하나씩 늦게 아래로 흘러내린다.
                float drip = Mathf.Max(0f, e - (.16f + index * .07f));
                move.y = -Mathf.Min(12f, drip * drip * 50f);
                scale.y = 1f + Mathf.Min(.12f, drip * .4f);
                break;
            }

            case DamageNumberFxKind.Radiance:
                move.y = Mathf.Sin(e * 5f + index * .8f) * .5f;
                break;
        }
    }

    private void RefreshShake(float elapsed)
    {
        if (kind != DamageNumberFxKind.Shock || elapsed < nextShakeAt)
            return;
        nextShakeAt = elapsed + .035f;
        for (int i = 0; i < MaxShakeSlots; i++)
            shake[i] = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f));
    }

    private static float EaseOutCubic(float x)
    {
        float inv = 1f - x;
        return 1f - inv * inv * inv;
    }

    // ---- Accents -------------------------------------------------------------------------------------------

    private void SetupAccents()
    {
        accentCount = 0;
        switch (kind)
        {
            case DamageNumberFxKind.Ember:
                for (int i = 0; i < 4; i++)
                    AddAccent(DamageNumberFxSprites.Dot, new Vector2(Random.Range(-14f, 14f), Random.Range(-4f, 6f)),
                        new Vector2(Random.Range(-55f, 55f), Random.Range(60f, 120f)), Random.Range(3f, 5.5f),
                        Random.Range(.45f, .7f), Color.Lerp(new Color(.95f, .72f, .4f, .85f), new Color(.85f, .3f, .12f, .85f), Random.value));
                break;
            case DamageNumberFxKind.Shatter:
                AddAccent(DamageNumberFxSprites.Crack, Vector2.zero, Vector2.zero, 40f, .26f, Color.white);
                for (int i = 0; i < 3; i++)
                    AddAccent(DamageNumberFxSprites.Dot, Vector2.zero,
                        new Vector2(Random.Range(-90f, 90f), Random.Range(10f, 80f)), Random.Range(3f, 5f), .42f,
                        Color.Lerp(Color.white, new Color(.55f, .88f, 1f), Random.value));
                break;
            case DamageNumberFxKind.Shock:
                AddAccent(DamageNumberFxSprites.Bolt, Vector2.zero, new Vector2(-1f, 0f), 24f, .36f, new Color(.82f, .76f, .95f, .75f));
                AddAccent(DamageNumberFxSprites.Bolt, Vector2.zero, new Vector2(1f, 0f), 20f, .36f, new Color(.7f, .6f, .9f, .75f));
                break;
            case DamageNumberFxKind.Sink:
                for (int i = 0; i < 3; i++)
                    AddAccent(DamageNumberFxSprites.Streak, new Vector2(.22f + i * .28f, 0f), Vector2.zero,
                        Random.Range(18f, 28f), .45f, new Color(.32f, .14f, .48f, .7f));
                break;
            case DamageNumberFxKind.Radiance:
                AddAccent(DamageNumberFxSprites.Rays, Vector2.zero, Vector2.zero, 74f, .7f, new Color(.95f, .82f, .5f, .5f));
                for (int i = 0; i < 2; i++)
                    AddAccent(DamageNumberFxSprites.Dot, new Vector2(i == 0 ? -24f : 26f, Random.Range(6f, 14f)), Vector2.zero,
                        Random.Range(3f, 4.5f), .7f, new Color(.95f, .9f, .74f, .8f));
                break;
        }
    }

    private void AddAccent(Sprite sprite, Vector2 start, Vector2 velocity, float size, float life, Color tint)
    {
        if (accentCount >= MaxAccents)
            return;
        EnsureAccentObjects();
        int i = accentCount++;
        accents[i].sprite = sprite;
        accentStart[i] = start;
        accentVelocity[i] = velocity;
        accentSize[i] = size;
        accentLife[i] = life;
        accentTint[i] = tint;
        accents[i].color = new Color(tint.r, tint.g, tint.b, 0f);
        accentRects[i].localRotation = Quaternion.identity;
        accentRects[i].pivot = new Vector2(.5f, .5f);
        accents[i].gameObject.SetActive(true);
    }

    private void EnsureAccentObjects()
    {
        if (accents != null)
            return;
        accents = new Image[MaxAccents];
        accentRects = new RectTransform[MaxAccents];
        Vector2 anchor = rect != null ? rect.pivot : new Vector2(.5f, .5f);
        for (int i = 0; i < MaxAccents; i++)
        {
            var go = new GameObject("FxAccent" + i, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = anchor;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.maskable = false;
            accents[i] = image;
            accentRects[i] = r;
            go.SetActive(false);
        }
    }

    private void HideAccents()
    {
        if (accents == null)
            return;
        for (int i = 0; i < accents.Length; i++)
            if (accents[i] != null && accents[i].gameObject.activeSelf)
                accents[i].gameObject.SetActive(false);
        accentCount = 0;
    }

    private void UpdateAccents()
    {
        if (accentCount == 0 || popup == null)
            return;
        float e = popup.FxElapsed;
        Vector2 center = (numberMin + numberMax) * .5f;
        float width = Mathf.Max(8f, numberMax.x - numberMin.x);
        for (int i = 0; i < accentCount; i++)
        {
            RectTransform r = accentRects[i];
            float alpha = 0f;
            Vector2 position = center;
            Vector2 size = new Vector2(accentSize[i], accentSize[i]);
            float rotation = 0f;
            float life = accentLife[i];

            switch (kind)
            {
                case DamageNumberFxKind.Ember:
                {
                    float age = e;
                    position = center + accentStart[i] + accentVelocity[i] * age + new Vector2(0f, -80f) * age * age;
                    float left = Mathf.Clamp01(1f - age / life);
                    alpha = left * accentTint[i].a * (.8f + .2f * Mathf.Sin(e * 40f + i * 2.1f));
                    size *= .6f + .4f * left;
                    break;
                }
                case DamageNumberFxKind.Shatter:
                {
                    float age = e - ShatterAt;
                    if (age < 0f) break;
                    if (i == 0)
                    {
                        // 금: 갈라지는 순간 번쩍 그어졌다가 사라진다.
                        size = new Vector2(5f, 40f * Mathf.Clamp01(age / .05f));
                        rotation = -12f;
                        alpha = Mathf.Clamp01(1f - age / life);
                    }
                    else
                    {
                        position = center + accentVelocity[i] * age + new Vector2(0f, -200f) * age * age;
                        alpha = Mathf.Clamp01(1f - age / life);
                    }
                    break;
                }
                case DamageNumberFxKind.Shock:
                {
                    // 번개: 숫자 양옆에서 깜빡인다.
                    float side = accentVelocity[i].x;
                    position = new Vector2(side < 0f ? numberMin.x - 8f : numberMax.x + 8f, center.y + (side < 0f ? 3f : -3f));
                    size = new Vector2(accentSize[i] * .5f, accentSize[i]);
                    rotation = side < 0f ? 14f : -18f;
                    bool on = ((int)(e / .05f) + i) % 2 == 0;
                    alpha = e < life ? (on ? 1f : .15f) * (1f - e / life * .5f) * accentTint[i].a : 0f;
                    break;
                }
                case DamageNumberFxKind.Sink:
                {
                    // 흘러내림: 숫자 아래로 자라났다가 흐려진다.
                    float grow = EaseOutCubic(Mathf.Clamp01((e - .2f - i * .08f) / life));
                    float height = accentSize[i] * grow;
                    r.pivot = new Vector2(.5f, 1f);
                    position = new Vector2(numberMin.x + width * accentStart[i].x, numberMin.y + 4f);
                    size = new Vector2(2.5f, height);
                    alpha = grow > 0f ? accentTint[i].a * Mathf.Clamp01((.95f - popup.FxProgress) / .3f) : 0f;
                    break;
                }
                case DamageNumberFxKind.Radiance:
                {
                    if (i == 0)
                    {
                        // 빛줄기: 번쩍 퍼지며 천천히 돈다. 가운데가 비어 있어 숫자를 가리지 않는다.
                        float grow = EaseOutCubic(Mathf.Clamp01(e / .5f));
                        size = Vector2.one * accentSize[i] * (.55f + .7f * grow);
                        rotation = e * 25f;
                        alpha = (e < .08f ? e / .08f : Mathf.Clamp01(1f - (e - .08f) / (life - .08f))) * accentTint[i].a;
                    }
                    else
                    {
                        position = center + accentStart[i];
                        alpha = Mathf.Clamp01(Mathf.Sin((e + i * .17f) * 9f)) * Mathf.Clamp01(1f - e / life) * accentTint[i].a;
                    }
                    break;
                }
            }

            r.anchoredPosition = position;
            r.sizeDelta = size;
            r.localRotation = Quaternion.Euler(0f, 0f, rotation);
            Color tint = accentTint[i];
            accents[i].color = new Color(tint.r, tint.g, tint.b, alpha);
        }
    }
}
