using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a font-independent row of filled and outline difficulty stars.
/// </summary>
/// <remarks>
/// The graphic is generated as UI geometry, so it does not depend on a particular TMP font
/// containing Unicode star glyphs. It is safe to configure in the Inspector and only rebuilds its
/// mesh when a displayed value or authored setting changes.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public class DifficultyStarGraphic : MaskableGraphic {

    [Min(1)] [SerializeField] int starCount = 6;
    [Min(0)] [SerializeField] int filledStarCount = 1;
    [Min(1f)] [SerializeField] float starSize = 28f;
    [Min(0f)] [SerializeField] float starSpacing = 8f;
    [Range(0.1f, 0.9f)] [SerializeField] float innerRadiusRatio = 0.45f;
    [Range(0.01f, 0.35f)] [SerializeField] float outlineThickness = 0.12f;
    [SerializeField] Color filledStarColor = new Color(1f, 0.78f, 0.08f, 1f);
    [SerializeField] Color emptyStarColor = Color.black;

    /// <summary>Sets the number of stars and the number currently filled.</summary>
    /// <param name="totalStars">Total number of stars shown.</param>
    /// <param name="filledStars">Number of stars shown as filled.</param>
    public void SetStars(int totalStars, int filledStars) {
        starCount = Mathf.Max(1, totalStars);
        filledStarCount = Mathf.Clamp(filledStars, 0, starCount);
        SetVerticesDirty();
    }

    /// <summary>Sets the colors used for filled and outline stars.</summary>
    /// <param name="filledColor">Color of completed difficulty stars.</param>
    /// <param name="emptyColor">Color of unfilled star outlines.</param>
    public void SetColors(Color filledColor, Color emptyColor) {
        filledStarColor = filledColor;
        emptyStarColor = emptyColor;
        SetVerticesDirty();
    }

    /// <summary>Sets the visual size and gap between stars.</summary>
    /// <param name="size">Diameter of each star in canvas units.</param>
    /// <param name="spacing">Gap between neighboring stars in canvas units.</param>
    public void SetLayout(float size, float spacing) {
        starSize = Mathf.Max(1f, size);
        starSpacing = Mathf.Max(0f, spacing);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper) {
        vertexHelper.Clear();
        int safeStarCount = Mathf.Max(1, starCount);
        float diameter = Mathf.Max(1f, starSize);
        float step = diameter + Mathf.Max(0f, starSpacing);
        float totalWidth = safeStarCount * diameter + (safeStarCount - 1) * Mathf.Max(0f, starSpacing);
        float startX = -totalWidth * 0.5f + diameter * 0.5f;
        int safeFilledCount = Mathf.Clamp(filledStarCount, 0, safeStarCount);

        for (int i = 0; i < safeStarCount; i++) {
            bool filled = i < safeFilledCount;
            if (filled) AddFilledStar(vertexHelper, new Vector2(startX + step * i, 0f), diameter);
            else AddOutlineStar(vertexHelper, new Vector2(startX + step * i, 0f), diameter);
        }
    }

    void AddFilledStar(VertexHelper vertexHelper, Vector2 center, float diameter) {
        int centerIndex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, center, filledStarColor);
        float outerRadius = diameter * 0.5f;
        float innerRadius = outerRadius * Mathf.Clamp(innerRadiusRatio, 0.1f, 0.9f);
        for (int i = 0; i < 10; i++) {
            float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float radius = i % 2 == 0 ? outerRadius : innerRadius;
            AddVertex(vertexHelper, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                filledStarColor);
        }

        for (int i = 0; i < 10; i++) {
            int next = (i + 1) % 10;
            vertexHelper.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + next);
        }
    }

    void AddOutlineStar(VertexHelper vertexHelper, Vector2 center, float diameter) {
        int startIndex = vertexHelper.currentVertCount;
        float outerRadius = diameter * 0.5f;
        float innerRadius = outerRadius * Mathf.Clamp(innerRadiusRatio, 0.1f, 0.9f);
        float outlineScale = 1f - Mathf.Clamp(outlineThickness, 0.01f, 0.35f);
        for (int i = 0; i < 10; i++) {
            float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float radius = i % 2 == 0 ? outerRadius : innerRadius;
            AddVertex(vertexHelper, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                emptyStarColor);
            AddVertex(vertexHelper, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * outlineScale,
                emptyStarColor);
        }

        for (int i = 0; i < 10; i++) {
            int next = (i + 1) % 10;
            int outer = startIndex + i * 2;
            int inner = outer + 1;
            int nextOuter = startIndex + next * 2;
            int nextInner = nextOuter + 1;
            vertexHelper.AddTriangle(outer, nextOuter, nextInner);
            vertexHelper.AddTriangle(outer, nextInner, inner);
        }
    }

    void AddVertex(VertexHelper vertexHelper, Vector2 position, Color vertexColor) {
        vertexHelper.AddVert(position, vertexColor, Vector2.zero);
    }
}
