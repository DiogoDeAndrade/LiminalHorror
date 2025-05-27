using NaughtyAttributes;
using System.Collections;
using UnityEngine;
using UC;

public class Level0_EventCat : TerrorObject
{
    [SerializeField] 
    GameObject     actualObjects;
    [SerializeField] 
    AudioClip      initialMeowSnd;
    [SerializeField]
    AudioClip      catAttack;
    [SerializeField, MinMaxSlider(1.0f, 30.0f)] 
    Vector2        meowInterval;
    [SerializeField] 
    AudioSource    meowSound;
    [SerializeField]
    Variable       seenCat;
    [SerializeField]
    Transform      smileyCat;
    [SerializeField]
    Sprite         audioIconSprite;

    float                   meowTimer;
    WFCCluster              catCluster;
    Coroutine               deathCR;

    protected override void Start()
    {
        base.Start();

        actualObjects.SetActive(false);

        tilemap.onNewCluster += GenerateCatCluster;
    }

    private void OnDestroy()
    {
        if (catCluster != null) catCluster.persistent = false;
        tilemap.onNewCluster -= GenerateCatCluster;
    }

    private void Update()
    {
        if (!actualObjects.gameObject.activeSelf) return;

        if (meowTimer > 0)
        {
            meowTimer -= Time.deltaTime;
            if (meowTimer <= 0.0f)
            {
                if (initialMeowSnd) meowSound.Play();
                meowTimer = meowInterval.Random();
            }
        }

        // Check for LOS on cat
        Ray ray = new Ray();
        ray.origin = player.transform.position + Vector3.up * 0.5f + player.transform.forward * 0.5f;
        Vector3 toCat = (actualObjects.transform.position + Vector3.up * 0.5f) - ray.origin;
        ray.direction = toCat;
        float maxDist = toCat.magnitude;
        if (maxDist < 20.0f)
        {
            ray.direction.Normalize();
            if (!Physics.Raycast(ray, maxDist))
            {
                Debug.DrawLine(ray.origin, ray.origin + ray.direction * maxDist, Color.green, 0.1f);
                catCluster.persistent = false;
                seenCat.SetValue(1);
            }
            else
            {
                Debug.DrawLine(ray.origin, ray.origin + ray.direction * maxDist, Color.red, 0.1f);
            }

            if (maxDist < 2.0f)
            {
                if ((!terrorManager.isFading) && (deathCR == null))
                {
                    deathCR = StartCoroutine(DeathCR());
                }
            }
        }
        else
        {
            Debug.DrawLine(ray.origin, ray.origin + ray.direction * maxDist, Color.blue, 0.1f);
        }
    }

    public void GenerateCatCluster(WFCCluster cluster)
    {
        catCluster = cluster;
        catCluster.persistent = true;

        transform.SetParent(tilemap.GetClusterTransform(cluster), true);

        tilemap.onNewCluster -= GenerateCatCluster;

        Vector3Int centerPos = new Vector3Int(Mathf.FloorToInt((cluster.basePos.x + 0.5f) * cluster.config.clusterSize.x),
                                              cluster.basePos.y * cluster.config.clusterSize.y,
                                              Mathf.FloorToInt((cluster.basePos.z + 0.5f) * cluster.config.clusterSize.z));
        ClearArea(centerPos, 2);

        transform.position = new Vector3(centerPos.x * tilemap.gridSize.x,
                                         centerPos.y * tilemap.gridSize.y,
                                         centerPos.z * tilemap.gridSize.z);

        actualObjects.SetActive(true);

        if (initialMeowSnd)
        {
            SoundManager.PlaySound(SoundType.PrimaryFX, initialMeowSnd);
            meowSound.Play();
            meowTimer = meowInterval.Random();
        }
    }

    IEnumerator DeathCR()
    {
        // DEATH!
        SoundManager.PlaySound(SoundType.PrimaryFX, catAttack);

        Vector3 toPlayer = ((player.transform.position + Vector3.up * 1.2f) - smileyCat.transform.position).normalized;
        smileyCat.transform.rotation = Quaternion.LookRotation(toPlayer, Vector3.up);
        smileyCat.gameObject.SetActive(true);

        float t = 0.0f;
        while (t < 0.5f)
        {
            toPlayer = ((player.transform.position + Vector3.up * 1.2f) - smileyCat.transform.position).normalized;
            smileyCat.transform.rotation = Quaternion.LookRotation(toPlayer, Vector3.up);

            t += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        terrorManager.GameOver();
    }
}
