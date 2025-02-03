using OkapiKit;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Level0_PhoneEvent : TerrorObject
{
    [SerializeField]
    private GameObject  actualObjects;
    [SerializeField]
    private Transform   focusPoint;
    [SerializeField]
    private AudioSource ringSound;
    [SerializeField]
    private AudioClip   pickupPhoneSnd;
    [SerializeField]
    private AudioClip   voiceOnTheOtherSideSnd;
    [SerializeField]
    private AudioClip   splashSnd;
    [SerializeField]
    private LayerMask   layerMask;
    [SerializeField]
    private LayerMask   excludeLayers;    

    private WFCTileData.Cluster phoneCluster;
    private bool                firstLOS = false;
    private Coroutine           nextLevelCR;

    protected override void Start()
    {
        base.Start();

        actualObjects.SetActive(false);

        tilemap.onNewCluster += GenerateCluster;
    }

    private void Update()
    {
        if (!actualObjects.gameObject.activeSelf) return;

        // Check for LOS on phone
        Ray ray = new Ray();
        ray.origin = player.transform.position + Vector3.up * 0.5f + player.transform.forward * 0.5f;
        Vector3 toPhone = focusPoint.position - ray.origin;
        ray.direction = toPhone;
        float maxDist = toPhone.magnitude;
        if (maxDist < 20.0f)
        {
            ray.direction.Normalize();            
            if (!Physics.Raycast(ray, out RaycastHit hitInfo, maxDist, layerMask))
            {
                Debug.DrawLine(ray.origin, ray.origin + ray.direction * maxDist, Color.green, 0.1f);
                //phoneCluster.persistent = false;
                if (!firstLOS)
                {
                    // Rotate towards player
                    actualObjects.transform.rotation = Quaternion.LookRotation(-ray.direction.x0z().normalized, Vector3.up);
                    firstLOS = true;
                    ringSound.Stop();
                }
                else
                {
                    if (maxDist < 1.0f)
                    {
                        if (nextLevelCR == null)
                        {
                            nextLevelCR = StartCoroutine(NextLevelCR());
                        }
                    }
                }
            }
            else
            {
                Debug.DrawLine(ray.origin, ray.origin + ray.direction * maxDist, Color.red, 0.1f);
            }
        }
        else
        {
            Debug.DrawLine(ray.origin, ray.origin + ray.direction * maxDist, Color.blue, 0.1f);
        }
    }

    public void GenerateCluster(WFCCluster cluster)
    {
        phoneCluster = cluster.cluster;
        phoneCluster.persistent = true;

        transform.SetParent(cluster.transform, true);

        tilemap.onNewCluster -= GenerateCluster;

        Vector3Int centerPos = new Vector3Int(Mathf.FloorToInt((phoneCluster.basePos.x + 0.5f) * phoneCluster.clusterSize.x),
                                              phoneCluster.basePos.y * phoneCluster.clusterSize.y,
                                              Mathf.FloorToInt((phoneCluster.basePos.z + 0.5f) * phoneCluster.clusterSize.z));
        ClearArea(centerPos, 2);

        transform.position = new Vector3(centerPos.x * tilemap.gridSize.x,
                                         centerPos.y * tilemap.gridSize.y,
                                         centerPos.z * tilemap.gridSize.z);
        actualObjects.SetActive(true);

        ringSound.Play();
    }

    IEnumerator NextLevelCR()
    {
        var snd = SoundManager.PlaySound(SoundType.PrimaryFX, pickupPhoneSnd);

        yield return new WaitForSound(snd);
        yield return new WaitForSeconds(0.5f);

        snd = SoundManager.PlaySound(SoundType.PrimaryFX, voiceOnTheOtherSideSnd);

        yield return new WaitForSound(snd);
        yield return new WaitForSeconds(0.5f);

        snd = SoundManager.PlaySound(SoundType.PrimaryFX, pickupPhoneSnd);

        yield return new WaitForSound(snd);
        yield return new WaitForSeconds(0.5f);

        var ctrl = player.GetComponent<CharacterController>();
        ctrl.excludeLayers = excludeLayers;

        yield return new WaitForSeconds(2.0f);

        SoundManager.PlaySound(SoundType.PrimaryFX, splashSnd);

        terrorManager.ToBeContinued();
    }
}
