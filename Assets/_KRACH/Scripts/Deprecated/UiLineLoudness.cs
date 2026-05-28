using UnityEngine;
using UnityEngine.UI;

public class UiLineLoudness : MonoBehaviour
{
    [SerializeField] private Transform upPoint;
    [SerializeField] private Transform downPoint;

    [SerializeField] private Transform line;

    [SerializeField] private float currentValue = 0f;
    private float currentY;
    private float targetY;

    // Lerp Parameter
    [SerializeField] private float slowLerpSpeed = 0.3f;  // Geschwindigkeit für Value 0 (langsam)
    [SerializeField] private float fastLerpSpeed = 1f;  // Geschwindigkeit für Value 1 (schneller)

    // Sinus Schwankung Parameter
    [SerializeField] private float swingAmplitude = 0.2f;  // Amplitude der Schwankung
    [SerializeField] private float swingFrequency = 2f;    // Aktuelle Frequenz
    private float swingTimer = 0f;
    private float frequencyChangeTimer = 0f;
    [SerializeField] private float frequencyChangeInterval = 3f;  // Frequenz alle 3 Sekunden ändern

    // Color Lerp
    [SerializeField] private Color startColor = new Color(0.8431373f, 0.38039216f, 1f); // #D761FF
    [SerializeField] private Color endColor = new Color(0.5137255f, 1f, 0.3843137f);   // #83FF62
    [SerializeField] private float colorLerpSpeed = 5f;
    private Color currentColor;
    private SpriteRenderer spriteRenderer;
    private Image uiImage;
    private Renderer meshRenderer;
    private LineRenderer lineRenderer;

    // Rotation wiggle (left-right)
    [SerializeField] private float rotationAmplitude = 5f; // degrees
    [SerializeField] private float rotationFrequency = 1.5f; // Hz
    [SerializeField] private float rotationLerpSpeed = 8f;
    private float rotationTimer = 0f;
    private float currentRotationAngle = 0f;

    void Start()
    {
        if (line != null)
        {
            currentY = line.localPosition.y;
            targetY = currentY;
        }
        RandomizeSwingFrequency();

        // Cache renderer components for color updates
        if (line != null)
        {
            spriteRenderer = line.GetComponent<SpriteRenderer>();
            uiImage = line.GetComponent<Image>();
            meshRenderer = line.GetComponent<Renderer>();
            lineRenderer = line.GetComponent<LineRenderer>();
        }

        currentColor = startColor;
        ApplyColor(currentColor);
    }

    void Update()
    {
        if (line == null)
            return;

        // Zielposition basierend auf currentValue setzen
        targetY = Mathf.Lerp(
            downPoint.localPosition.y,
            upPoint.localPosition.y,
            currentValue
        );


        // Lerpen basierend auf Wert
        float lerpSpeed = Mathf.Lerp(slowLerpSpeed, fastLerpSpeed, currentValue);
        currentY = Mathf.Lerp(currentY, targetY, lerpSpeed * Time.deltaTime);

        // Sinus-Schwankung hinzufügen
        float swing = Mathf.Sin(swingTimer * swingFrequency) * swingAmplitude;
        float finalY = currentY + swing;

        // Position aktualisieren (nur Y-Achse)
        Vector3 newPos = line.localPosition;
        newPos.y = finalY;
        line.localPosition = newPos;

        // Color lerp: Ziel basierend auf currentValue
        Color targetColor = Color.Lerp(startColor, endColor, currentValue);
        currentColor = Color.Lerp(currentColor, targetColor, colorLerpSpeed * Time.deltaTime);
        ApplyColor(currentColor);

        // Timer für Sinus-Schwankung erhöhen
        swingTimer += Time.deltaTime;

        // Rotation wiggle
        rotationTimer += Time.deltaTime;
        float targetAngle = Mathf.Sin(rotationTimer * rotationFrequency * Mathf.PI * 2f) * rotationAmplitude;
        currentRotationAngle = Mathf.LerpAngle(currentRotationAngle, targetAngle, rotationLerpSpeed * Time.deltaTime);
        line.localRotation = Quaternion.Euler(0f, 0f, currentRotationAngle);

        // Frequenz zufällig ändern
        frequencyChangeTimer += Time.deltaTime;
        if (frequencyChangeTimer >= frequencyChangeInterval)
        {
            RandomizeSwingFrequency();
            frequencyChangeTimer = 0f;
        }
    }

    /// <summary>
    /// Setzt das Value auf 0 - Linie lerpt langsam zum downPoint
    /// </summary>
    public void SetValueToZero()
    {
        currentValue = 0f;
    }

    /// <summary>
    /// Setzt das Value auf 1 - Linie lerpt schneller zum upPoint
    /// </summary>
    public void SetValueToOne()
    {
        currentValue = 1f;
    }

    /// <summary>
    /// Zufällige Frequenz für die Sinus-Schwankung setzen
    /// </summary>
    private void RandomizeSwingFrequency()
    {
        swingFrequency = Random.Range(1f, 4f);  // Frequenz zwischen 1 und 4 Hz
    }

    private void ApplyColor(Color color)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = color;
        }

        if (uiImage != null)
        {
            uiImage.color = color;
        }

        if (meshRenderer != null && spriteRenderer == null && uiImage == null)
        {
            meshRenderer.material.color = color;
        }

        if (lineRenderer != null)
        {
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
        }
    }
}
