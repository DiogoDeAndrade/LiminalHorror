using NaughtyAttributes;
using UnityEngine;
using UC;
public class TerrorEvent : MonoBehaviour
{
    public enum TriggerType { Time, DistanceWalked };

    [SerializeField]
    private Variable[]      preconditions;
    [SerializeField] 
    private TriggerType     type;
    [SerializeField] 
    private bool            retrigger = true;
    [SerializeField, ShowIf("type", TriggerType.DistanceWalked)]
    private float           distanceWalked;
    [SerializeField, MinMaxSlider(1.0f, 240.0f), ShowIf("type", TriggerType.Time)] 
    private Vector2         initialInterval = new Vector2(10.0f, 10.0f);
    [SerializeField, MinMaxSlider(1.0f, 240.0f), ShowIf("type", TriggerType.Time)] 
    private Vector2         repeatInterval = new Vector2(10.0f, 10.0f);
    [SerializeField]
    private TerrorObject    eventObjectPrefab;
    [SerializeField]
    private KeyCode         cheatKey = KeyCode.None;


    private float           timer = 0.0f;
    private float           currentDistanceWalked = 0.0f;  
    private TerrorObject    currentEvent;
    private int             triggerCount = 0;
    private FPSController   player; 
    private Vector3         prevPlayerPos;

    void Start()
    {
        timer = initialInterval.Random();
        player = FindAnyObjectByType<FPSController>();
        prevPlayerPos = player.transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        if (currentEvent == null)
        {
            bool canRun = true;
            if ((preconditions != null) && (preconditions.Length > 0))
            {
                foreach (var condition in preconditions)
                {
                    if (condition.currentValue < 1)
                    {
                        canRun = false;
                    }
                }
            }

            if (cheatKey != KeyCode.None)
            {
                if (Input.GetKeyDown(cheatKey))
                {
                    TriggerEvent();
                    return;
                }
            }

            if (canRun)
            {
                switch (type)
                {
                    case TriggerType.Time:
                        timer -= Time.deltaTime;
                        if (timer < 0.0f)
                        {
                            if (TriggerEvent())
                            {
                                timer = repeatInterval.Random();
                            }
                        }
                        break;
                    case TriggerType.DistanceWalked:
                        currentDistanceWalked += Vector3.Distance(player.transform.position, prevPlayerPos);
                        if (currentDistanceWalked > distanceWalked)
                        {
                            if (TriggerEvent())
                            {
                                currentDistanceWalked = 0.0f;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        prevPlayerPos = player.transform.position;
    }

    [Button("Trigger Now")]
    bool TriggerEvent()
    {
        triggerCount++;

        if (eventObjectPrefab)
        {
            currentEvent = Instantiate(eventObjectPrefab, transform);
            if (!currentEvent.Init()) return false;
        }
        if (!retrigger)
        {
            Destroy(this);
        }

        return true;
    }
}
