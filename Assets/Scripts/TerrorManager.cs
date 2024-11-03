using System.Collections;
using UnityEngine;

public class TerrorManager : MonoBehaviour
{
    [SerializeField] private FPSController player;

    WFCTilemap          tilemap;
    CharacterController charCtrl;

    void Start()
    {
        tilemap = FindAnyObjectByType<WFCTilemap>();
        charCtrl = player.GetComponent<CharacterController>();

        StartCoroutine(StartCR());
    }

    IEnumerator StartCR()
    {
        tilemap.GenerateMap(() =>
        {
            // Clear the area in the middle
            var worldTilePos = tilemap.WorldToTilePos(player.transform.position);
            for (int z = -3; z <= 3; z++)
            {
                for (int x = -3; x <= 3; x++)
                {
                    tilemap.ObserveTile(new Vector3Int(worldTilePos.x + x, worldTilePos.y, worldTilePos.z + z), (byte)(((x == 0) && (z == 0)) ? (2) : (1)), 0);
                }
            }
        });

        yield return new WaitForSeconds(1.0f);
        charCtrl.enabled = true;
        player.enabled = true;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
