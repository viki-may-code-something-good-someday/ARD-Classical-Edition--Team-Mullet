using UnityEngine;
using UnityEngine.UI;

public enum RMF_State
{
    Low,   // Wert: 0
    High   // Wert: 1
}

public class RMF_Script : MonoBehaviour
{
    private Toggle toggle;
    private float rmfCurrentValue = 0f;
    public float rmfValueTriggeringHigh = 0.6f;
    public float waitingToleranceTimeAfterFallingBelowThreshold = 0.5f;
    public RMF_State currentRMFstate;

    [Range(0,1)]public float testRMFValue;

    public LineFollowerVisualizer lineAnimator;
    public static RMF_Script Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        lineAnimator.SetRmfHighValue(rmfValueTriggeringHigh);
    }

    void Start()
    {
        toggle = GetComponent<Toggle>();
    }

    // Update is called once per frame
    void Update()
    {
        SetRMFValue(testRMFValue);
        
    }

    public void ActivateFakeSinus(RMF_State ForState)
    {
        lineAnimator.StartSine(ForState);
        
        /*if (ForState == RMF_State.High)
        {
            lineAnimator.StartSine(ForState);
        }
        else
        {
            lineAnimator.StartSine(ForState);
        }
        */
    }

    public bool IsRMFHigh()
    {
        return currentRMFstate == RMF_State.High;
    }

    public void SetRMFValue(float value)
    {
       rmfCurrentValue = Mathf.Clamp01(value);
    
       if(rmfCurrentValue >= rmfValueTriggeringHigh)
       {
                currentRMFstate = RMF_State.High;
            ActivateFakeSinus(RMF_State.High);
        }
       else
       {
                currentRMFstate = RMF_State.Low;
            ActivateFakeSinus(RMF_State.Low);
        }
    }
}
