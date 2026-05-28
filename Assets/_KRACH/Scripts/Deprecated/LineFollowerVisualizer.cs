using DG.Tweening;
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(LineRenderer))]
[ExecuteAlways]
public class LineFollowerVisualizer : MonoBehaviour
{
    [Header("Line Settings")]
    [SerializeField] private int pointCount = 100;
    [SerializeField] private float lengthX = 0.5f;
    [SerializeField] private float heightMultiplier = 0.8f;

    public RMF_State rmfState;
    private float rmfHighValue;

    public GameObject rmfIndicator;

    [Header("Animation")]
    [SerializeField] private float followSpeed = 100f; // Wie schnell die Punkte folgen
    [SerializeField] private bool useUnscaledTime = false;

    private LineRenderer line;
    private float[] yValues;
    private float xSpacing;

    public float targetValue01; // Extern gesetzter Wert (0�1)

    // interne Sine-Variablen
    private float sinePhase;
    private float sineCenter;
    private float sineAmplitude;
    private Tween phaseTween;
    private Coroutine sineCoroutine;


    // Sine / DOTween Steuerung
    [Header("Sine DOTween Settings")]
    [SerializeField] private float lowMin = 0f;
    [SerializeField] private float lowMax = 0.4f;
    [SerializeField] private float highMin = 0.6f;
    [SerializeField] private float highMax = 1f;
    [SerializeField] private float minAmplitude = 0.02f;
    [SerializeField] private float maxAmplitude = 0.1f;// minimale Sinusamplitude
    [SerializeField] private float minPeriodLow = 0.5f;     // minimale Periode (s)
    [SerializeField] private float maxPeriodLow = 1f;     // maximale Periode (s)
    [SerializeField] private float minPeriodHigh = 0.05f;
    [SerializeField] private float maxPeriodHigh = 0.5f;
    [SerializeField] private float changeIntervalMin = 2f; // wie oft neue Zufallswerte
    [SerializeField] private float changeIntervalMax = 5f;

    public SpriteRenderer upperHalf;
    public float upperHalfInitialAlpha;
    public SpriteRenderer lowerHalf;
    public float lowerHalfInitialAlpha;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        Initialize();
    }

    void Update()
    {
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (phaseTween != null && phaseTween.IsActive())
        {
            float sineValue = Mathf.Clamp01(sineCenter + sineAmplitude * Mathf.Sin(sinePhase));
            SetValue(sineValue); // <-- hier wird das Ergebnis in die vorhandene Methode �bertragen
            SetTransparencyOfHalf(rmfState, sineValue); 
        }

        float targetHeight = targetValue01 * heightMultiplier;

        // Punkt 0 bewegt sich zum Zielwert
        yValues[0] = Mathf.Lerp(
            yValues[0],
            targetHeight,
            delta * followSpeed
        );

        // Rest folgt jeweils dem Vorg�nger
        for (int i = 1; i < pointCount; i++)
        {
            float lerpFactor = 1f - Mathf.Exp(-followSpeed * delta);
            yValues[i] = Mathf.Lerp(yValues[i], yValues[i - 1], lerpFactor);
        }

        UpdateLine();
    }

    void Initialize()
    {
        line.positionCount = pointCount;

        xSpacing = lengthX / (pointCount - 1);
        yValues = new float[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            float x = i * xSpacing;
            line.SetPosition(i, new Vector3(x, 0, 0));
        }

        SetValue(0f);
        StartSine(RMF_State.Low);
    }

    public void SetRmfHighValue(float value)
    {
        Debug.Log("Setting RMF High Value to: " + value);
        rmfHighValue = value;

        //vorher: 1 * value * heightMultiplikator
        Vector3 indicatorPos = new Vector3(rmfIndicator.transform.localPosition.x, 1, rmfIndicator.transform.localPosition.z);
        rmfIndicator.gameObject.transform.SetLocalPositionAndRotation(indicatorPos, Quaternion.identity);
    }

    public void SetValue(float value01)
    {
        targetValue01 = Mathf.Clamp01(value01);

        if(targetValue01 >= rmfHighValue)
        {
            rmfState = RMF_State.High;
        }
        else
        {
            rmfState = RMF_State.Low;
        }
    }

    public void StartSine(RMF_State forState)
    {
        // stoppe bestehende Sine-Animation falls vorhanden
        if (forState != rmfState)
        {
            Debug.Log("Starting sine for state: " + forState);
            StopSine();
            rmfState = forState;
            sineCoroutine = StartCoroutine(SineControllerCoroutine(forState));
        }

        // starte neue Coroutine, die in Intervallen neue Zufallswerte w�hlt
        
    }

    public void StopSine()
    {
        if (sineCoroutine != null)
        {
            StopCoroutine(sineCoroutine);
            sineCoroutine = null;
        }

        if (phaseTween != null && phaseTween.IsActive())
        {
            phaseTween.Kill();
            phaseTween = null;
        }
    }

    public void SetTransparencyOfHalf(RMF_State state, float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (state == RMF_State.Low)
        {
            alpha *= lowerHalfInitialAlpha;
            Color c = lowerHalf.color;
            c.a = alpha;
            lowerHalf.color = c;

            alpha *= upperHalfInitialAlpha;
            Color d = upperHalf.color;
            d.a = upperHalfInitialAlpha;
            upperHalf.color = d;
        }
        else
        {
            alpha *= upperHalfInitialAlpha;
            Color c = upperHalf.color;
            c.a = alpha;
            upperHalf.color = c;

            alpha *= lowerHalfInitialAlpha;
            Color d = lowerHalf.color;
            d.a = lowerHalfInitialAlpha;
            lowerHalf.color = d;
        }
    }

    private IEnumerator SineControllerCoroutine(RMF_State forState)
    {
        Debug.Log("Sine Coroutine started for state: " + forState);
        ChooseNewSineParameters(forState);

        while (true)
        {
            // Erzeuge einen periodischen Tween, der sinePhase kontinuierlich erh�ht
            if (phaseTween != null && phaseTween.IsActive())
            {
                phaseTween.Kill();
                phaseTween = null;
            }


            // periodische Drehung: increase phase by 2PI over 'period' seconds, loopet unendlich inkrementell
            float period = 0f;
            if(forState == RMF_State.High)
            {
                period = Random.Range(minPeriodHigh, maxPeriodHigh);
            }
            else
            {
                period = Random.Range(minPeriodLow, maxPeriodLow);
            }
            
            phaseTween = DOTween.To(() => sinePhase, x => sinePhase = x, sinePhase + Mathf.PI * 2f, period)
                                .SetEase(Ease.Linear)
                                .SetLoops(-1, LoopType.Incremental);

            // Warte ein zuf�lliges Intervall bevor neue Parameter gew�hlt werden (stilistische Variation)
            float wait = Random.Range(changeIntervalMin, changeIntervalMax);
            yield return new WaitForSeconds(wait);

            // W�hle neue Parameter (Center/Amplitude/evtl. schneller/langsamer)
            ChooseNewSineParameters(forState);
            // PhaseTween wird im n�chsten Loop neu erstellt mit neuem Periodenwert
        }
    }

    private void ChooseNewSineParameters(RMF_State state)
    {
        float min, max;
        if (state == RMF_State.Low)
        {
            min = lowMin;
            max = lowMax;
        }
        else
        {
            min = highMin;
            max = highMax;
        }

        // maximale m�gliche halbe Bandbreite
        float maxHalfRange = (max - min) * 0.5f;
        float ampMax = Mathf.Max(minAmplitude, maxHalfRange);

        // amplitude zuf�llig, aber nicht gr��er als die halbe Bandbreite
        float amp = Random.Range(minAmplitude, maxAmplitude);
        // center so w�hlen, dass center +/- amp innerhalb [min,max] bleibt
        float centerMin = min + amp;
        float centerMax = max - amp;
        if (centerMax < centerMin) centerMax = centerMin; // Fallback
        float center = Random.Range(centerMin, centerMax);

        sineAmplitude = amp;
        sineCenter = center;

        // Debug.Log($"Sine new params: center={sineCenter:F2}, amp={sineAmplitude:F2}");
    }

   

    void UpdateLine()
    {
        for (int i = 0; i < pointCount; i++)
        {
            float x = i * xSpacing;
            line.SetPosition(i, new Vector3(x, yValues[i], 0));
        }
    }
}