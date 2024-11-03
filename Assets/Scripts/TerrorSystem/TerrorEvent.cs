using NaughtyAttributes;
using UnityEngine;

public class TerrorEvent : MonoBehaviour
{
    public enum TriggerType { Time };

    [SerializeField] 
    private TriggerType     type;
    [SerializeField] 
    private bool            retrigger = true;
    [SerializeField, MinMaxSlider(1.0f, 240.0f), ShowIf("type", TriggerType.Time)] 
    private Vector2         initialInterval = new Vector2(10.0f, 10.0f);
    [SerializeField, MinMaxSlider(1.0f, 240.0f), ShowIf("type", TriggerType.Time)] 
    private Vector2         repeatInterval = new Vector2(10.0f, 10.0f);
    [SerializeField]
    private TerrorObject    eventObjectPrefab;
    [SerializeField]
    private KeyCode         cheatKey = KeyCode.None;


    private float           timer = 0.0f;
    private TerrorObject    currentEvent;

    void Start()
    {
        timer = initialInterval.Random(); 
    }

    // Update is called once per frame
    void Update()
    {
        if (currentEvent != null) return;

        if (cheatKey != KeyCode.None)
        {
            if (Input.GetKeyDown(cheatKey))
            {
                TriggerEvent();
                return;
            }
        }

        switch (type)
        {
            case TriggerType.Time:
                timer -= Time.deltaTime;
                if (timer < 0.0f)
                {
                    TriggerEvent();
                    timer = repeatInterval.Random();
                }
                break;
            default:
                break;
        }
    }

    [Button("Trigger Now")]
    void TriggerEvent()
    {
        if (eventObjectPrefab)
        {
            currentEvent = Instantiate(eventObjectPrefab, transform);
        }
        if (!retrigger)
        {
            Destroy(this);
        }
    }
}
