using System.Collections.Generic;
using UnityEngine;

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
