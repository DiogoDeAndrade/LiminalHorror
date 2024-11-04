using UnityEngine;

public class TerrorObject : MonoBehaviour
{
    protected WFCTilemap tilemap;
    protected FPSController player;
    protected TerrorManager terrorManager;

    protected virtual void Start()
    {
        tilemap = FindAnyObjectByType<WFCTilemap>();
        player = FindAnyObjectByType<FPSController>();
        terrorManager = FindAnyObjectByType<TerrorManager>();
    }

    public virtual bool Init()
    {
        return true;
    }

    protected void ClearArea(Vector3Int pos, int radius)
    {
        // Clear the area in the middle
        var worldTilePos = tilemap.WorldToTilePos(pos);
        for (int z = -radius; z <= radius; z++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                tilemap.ObserveTile(new Vector3Int(worldTilePos.x + x, worldTilePos.y, worldTilePos.z + z), (byte)(((x == 0) && (z == 0)) ? (2) : (1)), 0);
            }
        }
    }
}
