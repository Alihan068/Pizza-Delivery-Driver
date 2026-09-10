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
    [Min(0.01f)] [SerializeField] float flightDuration = 0.22f;
    [Min(0f)] [SerializeField] float arcHeight = 0.35f;
    [Min(0.01f)] [SerializeField] float startScale = 0.85f;
    [Min(0.01f)] [SerializeField] float endScale = 1f;
    [Min(0f)] [SerializeField] float launchStagger = 0.035f;
    [Min(1)] [SerializeField] int maximumVisualPizzas = 3;
    [SerializeField] int sortingOrderOffset = 2;

    SpriteRenderer sourceRenderer;

    /// <summary>Assigns the sprite used by the vehicle's carried-pizza visual.</summary>
    /// <param name="source">Source renderer, or null when no pizza visual is authored.</param>
    public void Configure(SpriteRenderer source) {
        sourceRenderer = source;
    }

    /// <summary>Launches one short visual arc for each accepted pizza up to the authored visual cap.</summary>
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
        if (sourceRenderer == null || sourceRenderer.sprite == null) {
            onComplete?.Invoke();
            return;
        }

        int visualCount = Mathf.Clamp(pizzaCount, 1, Mathf.Max(1, maximumVisualPizzas));
        FlightBatch batch = new FlightBatch(visualCount, onComplete);
        for (int i = 0; i < visualCount; i++) {
            GameObject visualObject = new GameObject();
            SpriteRenderer visualRenderer = visualObject.AddComponent<SpriteRenderer>();
            CopySourceRenderer(visualRenderer);
            StartCoroutine(AnimatePizza(visualObject.transform, visualRenderer, start, target,
                targetTransform, Mathf.Max(0f, launchStagger) * i, batch));
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
        Vector3 target, Transform targetTransform, float delay, FlightBatch batch) {
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
            Vector3 position = Vector3.Lerp(start, currentTarget, normalized);
            position += Vector3.up * (Mathf.Sin(normalized * Mathf.PI) * Mathf.Max(0f, arcHeight));
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
