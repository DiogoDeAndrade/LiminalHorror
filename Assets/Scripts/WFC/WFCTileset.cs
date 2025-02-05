using System.Collections.Generic;
using UnityEngine;

// This holds a list of tiles to be used in a tilemap
// Currently, a maximum number of 256 tiles are supported, changes will have to be made in this class
// and others to support more than 256 tiles
[CreateAssetMenu(fileName = "WFCTileset", menuName = "WFC/Tileset")]
public class WFCTileset : ScriptableObject
{
    [SerializeField]
    private List<WFCTile3d>             tilePrefabs;
    private Dictionary<WFCTile3d, byte> tilePrefabIndex;

    void InitTilePrefabIndex()
    {
        tilePrefabIndex = new();
        for (int i = 0; i < tilePrefabs.Count; i++)
        {
            if (tilePrefabs[i] != null)
            {
                tilePrefabIndex.Add(tilePrefabs[i], (byte)i);
            }
        }

    }

    public WFCTile3d GetTile(int index)
    {
        return tilePrefabs[index];
    }

    public byte GetTileIndex(WFCTile3d tile)
    {
        if (tilePrefabIndex == null) InitTilePrefabIndex();

        if (!tilePrefabIndex.TryGetValue(tile, out byte tileId))
        {
            Debug.LogWarning($"Tileset {name} doesn't include {tile.name}!");
            return 255;
        }
        return tileId;
    }

    public void Add(WFCTile3d tile)
    {
        if (tilePrefabIndex == null) tilePrefabIndex = new();
        if (tilePrefabIndex.ContainsKey(tile)) return;

        if (tilePrefabs == null)
        {
            tilePrefabs = new();
            tilePrefabs.Add(null);
        }
        tilePrefabIndex.Add(tile, (byte)tilePrefabs.Count);
        tilePrefabs.Add(tile);
    }

    public int Count => tilePrefabs.Count;
}
