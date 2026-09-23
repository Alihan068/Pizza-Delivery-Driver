using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Shows the accepted pizzas travelling from the driver to the customer.
/// </summary>
/// <remarks>
/// The effect is created by <see cref="Delivery"/> when a customer accepts an offer. It copies the
/// authored carried-pizza sprite, affects no gameplay state, and safely does nothing when the source
/// sprite is unavailable. The effect is intentionally short lived because delivery events are rare
/// compared with the vehicle's physics loop.
/// </remarks>
public class PizzaDeliveryEffect : MonoBehaviour {

    [Header("Flight")]
    [Min(0.01f)] [SerializeField] float flightDuration = 0.32f;
    [Min(0f)] [SerializeField] float arcHeight = 0.85f;
    [Min(0.01f)] [SerializeField] float startScale = 1.7f;
    [Min(0.01f)] [SerializeField] float endScale = 1.4f;
    [Min(0f)] [SerializeField] float launchStagger = 0.12f;
    [Min(0f)] [SerializeField] float pizzaFanSpacing = 0.45f;
    [HideInInspector] [SerializeField] int maximumVisualPizzas = 3;
    [SerializeField] int sortingOrderOffset = 2;

    SpriteRenderer sourceRenderer;

    /// <summary>Assigns the sprite used by the vehicle's carried-pizza visual.</summary>
    /// <param name="source">Source renderer, or null when no pizza visual is authored.</param>
    public void Configure(SpriteRenderer source) {
        sourceRenderer = source;
    }

    /// <summary>Launches one short visual arc for every accepted pizza.</summary>
    /// <param name="start">World position where the pizza leaves the vehicle.</param>
    /// <param name="target">World position where the pizza arrives.</param>
    /// <param name="pizzaCount">Number of accepted pizzas represented by the effect.</param>
    /// <param name="onComplete">Callback invoked once all visual pizzas reach the target.</param>
    public void Play(Vector3 start, Vector3 target, int pizzaCount, Action onComplete) {
        PlayInternal(start, target, null, pizzaCount, onComplete);
    }

    /// <summary>
    /// Launches pizza visuals toward a moving transform, such as the delivery vehicle's pizza holder.
    /// </summary>
    /// <param name="start">World position where the pizza leaves the collection point.</param>
    /// <param name="targetTransform">Transform that should be followed until arrival.</param>
    /// <param name="pizzaCount">Number of pizzas represented by the effect.</param>
    /// <param name="onComplete">Callback invoked once all visual pizzas reach the target.</param>
    /// <remarks>
    /// A null target transform falls back to the start position. The overload keeps collection
    /// feedback readable when the vehicle is already moving while preserving the fixed-position
    /// overload used by customer delivery.
    /// </remarks>
    public void Play(Vector3 start, Transform targetTransform, int pizzaCount, Action onComplete) {
        Vector3 fallbackTarget = targetTransform != null ? targetTransform.position : start;
        PlayInternal(start, fallbackTarget, targetTransform, pizzaCount, onComplete);
    }

    void PlayInternal(Vector3 start, Vector3 target, Transform targetTransform, int pizzaCount,
        Action onComplete) {
        if (pizzaCount <= 0) {
            onComplete?.Invoke();
            return;
        }

        if (sourceRenderer == null || sourceRenderer.sprite == null) {
            onComplete?.Invoke();
            return;
        }

        int visualCount = pizzaCount;
        Vector2 travel = (Vector2)(target - start);
        Vector3 lateral = travel.sqrMagnitude > 0.0001f
            ? new Vector3(-travel.normalized.y, travel.normalized.x, 0f)
            : Vector3.right;
        FlightBatch batch = new FlightBatch(visualCount, onComplete);
        for (int i = 0; i < visualCount; i++) {
            GameObject visualObject = new GameObject();
            SpriteRenderer visualRenderer = visualObject.AddComponent<SpriteRenderer>();
            CopySourceRenderer(visualRenderer);
            float centeredIndex = i - (visualCount - 1) * 0.5f;
            StartCoroutine(AnimatePizza(visualObject.transform, visualRenderer, start, target,
                targetTransform, lateral, centeredIndex * Mathf.Max(0f, pizzaFanSpacing),
                Mathf.Max(0f, launchStagger) * i, batch));
        }
    }

    void CopySourceRenderer(SpriteRenderer targetRenderer) {
        targetRenderer.sprite = sourceRenderer.sprite;
        targetRenderer.color = sourceRenderer.color;
        targetRenderer.flipX = sourceRenderer.flipX;
        targetRenderer.flipY = sourceRenderer.flipY;
        targetRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        targetRenderer.sortingOrder = sourceRenderer.sortingOrder + sortingOrderOffset;
        targetRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
    }

    IEnumerator AnimatePizza(Transform visualTransform, SpriteRenderer visualRenderer, Vector3 start,
        Vector3 target, Transform targetTransform, Vector3 lateral, float lateralOffset,
        float delay, FlightBatch batch) {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, flightDuration);
        float sourceScale = Mathf.Abs(sourceRenderer.transform.lossyScale.x);

        while (elapsed < delay) {
            if (visualTransform == null) {
                CompleteFlight(batch);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < duration) {
            if (visualTransform == null) {
                CompleteFlight(batch);
                yield break;
            }
            float normalized = Mathf.Clamp01(elapsed / duration);
            Vector3 currentTarget = targetTransform != null ? targetTransform.position : target;
            Vector3 position = Vector3.Lerp(start, currentTarget, Mathf.SmoothStep(0f, 1f, normalized));
            position += Vector3.up * (Mathf.Sin(normalized * Mathf.PI) * Mathf.Max(0f, arcHeight));
            position += lateral * (Mathf.Sin(normalized * Mathf.PI) * lateralOffset);
            visualTransform.position = position;

            float scale = Mathf.Lerp(startScale, endScale, normalized) * sourceScale;
            visualTransform.localScale = Vector3.one * scale;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (visualRenderer != null) Destroy(visualRenderer.gameObject);
        CompleteFlight(batch);
    }

    void CompleteFlight(FlightBatch batch) {
        if (batch == null || batch.remaining <= 0) return;

        batch.remaining--;
        if (batch.remaining == 0) {
            Action callback = batch.onComplete;
            batch.onComplete = null;
            callback?.Invoke();
        }
    }

    sealed class FlightBatch {
        public int remaining;
        public Action onComplete;

        public FlightBatch(int count, Action callback) {
            remaining = count;
            onComplete = callback;
        }
    }
}
