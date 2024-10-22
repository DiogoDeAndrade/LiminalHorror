using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using System.IO;
using System;


#if UNITY_EDITOR
using UnityEditor;
#endif

public class WFCTilemap : MonoBehaviour
{
    struct Tile
    {
        public byte tileId;
        public byte rotation;

        // Overload the == operator
        public static bool operator ==(Tile lhs, Tile rhs)
        {
            // Compare each field for equality
            return lhs.tileId == rhs.tileId && lhs.rotation == rhs.rotation;
        }

        // Overload the != operator
        public static bool operator !=(Tile lhs, Tile rhs)
        {
            // Use the == operator to define !=
            return !(lhs == rhs);
        }

        // Override Equals for structural equality comparison
        public override bool Equals(object obj)
        {
            if (obj is Tile)
            {
                return this == (Tile)obj;
            }
            return false;
        }

        // Override GetHashCode for use in collections
        public override int GetHashCode()
        {
            // Combine the hash codes of the fields
            return tileId.GetHashCode() ^ rotation.GetHashCode();
        }
    }

    class TileProbability
    {
        public Tile tile;
        public int  count;
    }

    enum Direction { PX = 0, PY = 1, PZ = 2, NX = 3, NY = 4, NZ = 5 };
    const int maxDirection = 6;

    class WFCData
    {
        public WFCData() 
        { 
            adjacency = new List<TileProbability>[maxDirection];
            for (int i = 0; i < maxDirection; i++) adjacency[i] = new();
        }

        List<TileProbability>[] adjacency;

        public void Add(Direction direction, Tile tile)
        {
            Find(direction, tile).count++;
        }

        TileProbability Find(Direction dir, Tile tile)
        {
            foreach (var t in adjacency[(int)dir])
            {
                if (t.tile == tile)
                {
                    return t;
                }
            }

            var tmp = new TileProbability() { tile = tile, count = 0 };
            adjacency[(int)dir].Add(tmp);
            return tmp;
        }

        internal void Save(BinaryWriter writer)
        {
            // For each direction (PX, PY, PZ, NX, NY, NZ)
            for (int dir = 0; dir < maxDirection; dir++)
            {
                List<TileProbability> tileProbs = adjacency[dir];

                // Write the number of TileProbability entries for this direction
                writer.Write(tileProbs.Count);

                // Write each TileProbability's tileId and count
                foreach (var tileProb in tileProbs)
                {
                    writer.Write(tileProb.tile.tileId);     // Write the tileId
                    writer.Write(tileProb.tile.rotation);   // Write the tileId
                    writer.Write(tileProb.count);           // Write the count
                }
            }
        }
    }

    [SerializeField, HideIf("hasAdjacencyData")]
    private TextAsset       tilemapData;
    [SerializeField, HideIf("hasData")]
    private TextAsset       adjacencyData;
    [SerializeField] 
    private Vector3         gridSize = Vector3.one;
    [SerializeField] 
    private Vector3Int      mapSize = Vector3Int.zero;
    [SerializeField, HideIf("hasAnyData")] 
    private Bounds          localBounds;
    [SerializeField]
    private WFCTileset      tileset;
    [SerializeField, HideInInspector]
    private Tile[]          map;

    [SerializeField]
    private bool            drawGrid = false;

    private WFCTile3d[]          tileGameObjects;

    bool hasData => tilemapData != null;
    bool hasAdjacencyData => adjacencyData != null;
    bool hasAnyData => hasData || hasAdjacencyData;

    private void Awake()
    {
    }

    private void Start()
    {
        LoadTilemap();
    }


    void InstantiateMap()
    {
        tileGameObjects = new WFCTile3d[map.Length];

        UpdateMap(new Vector3Int(0, 0, 0), mapSize);
    }

    void UpdateMap(Vector3Int startPos, Vector3Int size)
    {
        for (int z = startPos.z; z < startPos.z + size.z; z++)
        {
            for (int y = startPos.y; y < startPos.y + size.y; y++)
            {
                for (int x = startPos.x; x < startPos.x + size.x; x++)
                {
                    int index = GetTilePosIndex(x, y, z);
                    DestroyTile(tileGameObjects[index]);
                    tileGameObjects[index] = CreateTileAt(x, y, z, map[index]);
                }
            }
        }
    }

    WFCTile3d CreateTileAt(int x, int y, int z, Tile tile)
    {
        if (tile.tileId == 0) return null;
          
        var ret = Instantiate(tileset.GetTile(tile.tileId), transform);
        ret.transform.position = GetWorldPos(x, y, z);
        ret.transform.localRotation = Quaternion.Euler(0, 90 * tile.rotation, 0);

        return ret;
    }

    void DestroyTile(WFCTile3d tile)
    {
        if (tile == null) return;
#if UNITY_EDITOR
        if (Application.isPlaying) Destroy(tile.gameObject);
        else DestroyImmediate(tile.gameObject);
#else
            Destroy(tile.gameObject);
#endif
    }

#if UNITY_EDITOR
    void BuildTilemap()
    {
        var tiles = GetComponentsInChildren<WFCTile3d>();

        if (tileset == null)
        {
            tileset = ScriptableObject.CreateInstance<WFCTileset>();

            // Collect all tiles and initialize them
            foreach (var tile in tiles)
            {
                // Get prefab origin   
                var prefabTile = PrefabUtility.GetCorrespondingObjectFromSource(tile);
                if (prefabTile != null)
                {
                    tileset.Add(prefabTile);
                }
            }
        }

        if (tileset.Count > 255)
        {
            Debug.LogWarning("Only have support for 255 individual tiles - increase size of tile id!");
        }

        // Init bounds and map size
        localBounds = GetExtents(tiles);
        localBounds.center = localBounds.center - transform.position;

        mapSize.x = Mathf.Max(1, Mathf.FloorToInt((localBounds.max.x - localBounds.min.x) / gridSize.x));
        mapSize.y = Mathf.Max(1, Mathf.FloorToInt((localBounds.max.y - localBounds.min.y) / gridSize.y));
        mapSize.z = Mathf.Max(1, Mathf.FloorToInt((localBounds.max.z - localBounds.min.z) / gridSize.z));

        map = new Tile[mapSize.x * mapSize.y * mapSize.z];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = new Tile() { tileId = 0 };
        }

        // Collect all tiles and initialize them
        foreach (var tile in tiles)
        {
            // Get prefab origin   
            var prefabTile = PrefabUtility.GetCorrespondingObjectFromSource(tile);
            if (prefabTile != null)
            {
                Tile t = new Tile() { tileId = tileset.GetTileIndex(prefabTile), rotation = GetRotation(tile) };
                var  tilePos = WorldToTilePos(tile.GetExtents().center);   
                var  tilePosIndex = GetTilePosIndex(tilePos);
                map[tilePosIndex] = t;
            }
        }
    }
#endif

    byte GetRotation(WFCTile3d tile)
    {
        float angle = tile.transform.rotation.eulerAngles.y;
        while (angle < 0) angle += 360.0f;
        while (angle > 360) angle -= 360.0f;

        return (byte)Mathf.RoundToInt(angle / 90.0f);
    }

    Vector3Int WorldToTilePos(Vector3 worldPos)
    {
        Vector3 localPos = transform.worldToLocalMatrix.MultiplyPoint3x4(worldPos);

        Vector3Int tilePos = Vector3Int.zero;
        tilePos.x = Mathf.FloorToInt((localPos.x - localBounds.min.x) / gridSize.x);
        tilePos.y = Mathf.FloorToInt((localPos.y - localBounds.min.y) / gridSize.y);
        tilePos.z = Mathf.FloorToInt((localPos.z - localBounds.min.z) / gridSize.z);

        return tilePos;
    }

    Vector3 GetWorldPos(Vector3Int tilePos)
    {
        return GetWorldPos(tilePos.x, tilePos.y, tilePos.z);
    }

    Vector3 GetWorldPos(int x, int y, int z)
    {        
        return transform.localToWorldMatrix.MultiplyPoint3x4(GetLocalPos(x, y, z));
    }

    Vector3 GetLocalPos(int x, int y, int z)
    {
        Vector3 pos = Vector3.zero;
        pos.x = (x - mapSize.x * 0.5f) * gridSize.x;
        pos.y = y * gridSize.y;
        pos.z = (z - mapSize.z * 0.5f) * gridSize.z;

        return pos;
    }

    int GetTilePosIndex(Vector3Int tilePos)
    {
        return GetTilePosIndex(tilePos.x, tilePos.y, tilePos.z);
    }
    int GetTilePosIndex(int x, int y, int z)
    {
        return x + y * mapSize.x + z * (mapSize.x * mapSize.y);
    }

    Bounds GetExtents(WFCTile3d[] tiles)
    {
        if (tiles.Length == 0) return new Bounds();

        Bounds ret = tiles[0].GetExtents();

        foreach (var tile in tiles)
        {
            Bounds bounds = tile.GetExtents();
            ret.Encapsulate(bounds);
        }

        return ret;
    }

    void ClearTilemap()
    {
        // Delete all subobjects
        var tiles = GetComponentsInChildren<WFCTile3d>();
        foreach (var tile in tiles)
        {
            DestroyTile(tile);
        }
    }

    [Button("Load Tilemap"), ShowIf("hasData")]
    void LoadTilemap()
    {
        ClearTilemap();

        if (tilemapData == null)
        {
            Debug.LogWarning("No file specified for tile data!");
            return;
        }

        // Read the file's binary content
        byte[] fileData = tilemapData.bytes;

        using (MemoryStream stream = new MemoryStream(fileData))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            // Read the magic number and validate it
            int magicNumber = reader.ReadInt32();
            if (magicNumber != 0x0D1060)
            {
                Debug.LogError("Invalid file format!");
                return;
            }

            // Read grid size
            gridSize.x = reader.ReadSingle();
            gridSize.y = reader.ReadSingle();
            gridSize.z = reader.ReadSingle();

            // Read map size
            mapSize.x = reader.ReadInt32();
            mapSize.y = reader.ReadInt32();
            mapSize.z = reader.ReadInt32();

            // Initialize the map array with the correct size
            map = new Tile[mapSize.x * mapSize.y * mapSize.z];

            // Read the tile data
            for (int i = 0; i < map.Length; i++)
            {
                map[i].tileId = reader.ReadByte();
                map[i].rotation = reader.ReadByte();
            }
        }

        InstantiateMap();
    }

    [Button("Generate Tilemap"), ShowIf("hasAdjacencyData")]
    void GenerateTilemap()
    {
        ClearTilemap();

        if (adjacencyData == null)
        {
            Debug.LogWarning("No file specified for adjacency data!");
            return;
        }

        // Read the file's binary content
        byte[] fileData = adjacencyData.bytes;

        using (MemoryStream stream = new MemoryStream(fileData))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            // Read the magic number and validate it
            int magicNumber = reader.ReadInt32();
            if (magicNumber != 0x0D1061)
            {
                Debug.LogError("Invalid file format!");
                return;
            }

            // Read grid size
            gridSize.x = reader.ReadSingle();
            gridSize.y = reader.ReadSingle();
            gridSize.z = reader.ReadSingle();

            // Initialize the map array with the correct size
            map = new Tile[mapSize.x * mapSize.y * mapSize.z];
        }
    }

#if UNITY_EDITOR
        [Button("Save Tilemap"), HideIf("hasAnyData")]
    void SaveTilemap()
    {
        BuildTilemap();

        string filename = EditorUtility.SaveFilePanel("Save Tilemap", "", "Tilemap", "tilemap.bytes");
        if (string.IsNullOrEmpty(filename)) return;

        using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create)))
        {
            // Magic number
            writer.Write(0x0D1060);
            // Grid size
            writer.Write(gridSize.x);
            writer.Write(gridSize.y);
            writer.Write(gridSize.z);
            // Map size
            writer.Write(mapSize.x);
            writer.Write(mapSize.y);
            writer.Write(mapSize.z);

            // Loop through the map array and write each tile's data
            for (int i = 0; i < map.Length; i++)
            {
                writer.Write(map[i].tileId);   // Write tileId
                writer.Write(map[i].rotation); // Write rotation
            }
        }

        Debug.Log($"Tilemap data saved to {filename}...");

        AssetDatabase.Refresh();
    }

    [Button("Save WFC Data"), HideIf("hasAnyData")]
    void SaveWFC()
    {
        BuildTilemap();

        // Build adjacency information
        WFCData[] adjacency = new WFCData[tileset.Count];
        for (int i = 0; i < adjacency.Length; i++) adjacency[i] = new WFCData();

        HashSet<Tile> uniqueTiles = new HashSet<Tile>();

        int strideX = 1;
        int strideY = mapSize.x;
        int strideZ = mapSize.x * mapSize.y;

        for (int z = 0; z < mapSize.z; z++)
        {
            for (int y = 0; y < mapSize.y; y++)
            {
                for (int x = 0; x < mapSize.x; x++)
                {
                    int index = GetTilePosIndex(x, y, z);

                    uniqueTiles.Add(map[index]);

                    if (x > 0) adjacency[map[index].tileId].Add(Direction.NX, map[index - strideX]);
                    if (y > 0) adjacency[map[index].tileId].Add(Direction.NY, map[index - strideY]);
                    if (z > 0) adjacency[map[index].tileId].Add(Direction.NZ, map[index - strideZ]);
                    if (x < mapSize.x - 1) adjacency[map[index].tileId].Add(Direction.PX, map[index + strideX]);
                    if (y < mapSize.y - 1) adjacency[map[index].tileId].Add(Direction.PY, map[index + strideY]);
                    if (z < mapSize.z - 1) adjacency[map[index].tileId].Add(Direction.PZ, map[index + strideZ]);
                }
            }
        }

        string filename = EditorUtility.SaveFilePanel("Save adjacency information", "", "Adjacency", "adjacency.bytes");
        if (string.IsNullOrEmpty(filename)) return;

        using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create)))
        {
            // Magic number
            writer.Write(0x0D1061);

            // Grid size
            writer.Write(gridSize.x);
            writer.Write(gridSize.y);
            writer.Write(gridSize.z);

            writer.Write(uniqueTiles.Count);
            foreach (var ut in uniqueTiles)
            {
                writer.Write(ut.tileId);
                writer.Write(ut.rotation);
            }

            // Write the number of tiles (adjacency.Length)
            writer.Write(adjacency.Length);

            // For each tile in the adjacency array
            for (int i = 0; i < adjacency.Length; i++)
            {
                adjacency[i].Save(writer);
            }
        }

        Debug.Log($"Adjacency information saved to {filename}");

        AssetDatabase.Refresh();
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (hasAnyData)
        {
            // Bounds are not loaded, so we need to compute them
            localBounds.SetMinMax(new Vector3((-mapSize.x - 1.0f) * gridSize.x * 0.5f, 0.0f, (-mapSize.z - 1.0f) * gridSize.z * 0.5f),
                                  new Vector3((mapSize.x - 1.0f) * gridSize.x * 0.5f, gridSize.y, (mapSize.z - 1.0f) * gridSize.z * 0.5f));
        }

        if (drawGrid)
        {
            var prevMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            DrawXZGrid(localBounds.min.y);
            Gizmos.matrix = prevMatrix;
        }

    }

    private void DrawXZGrid(float y)
    {
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        for (int z = 0; z <= mapSize.z; z++)
        {
            Gizmos.DrawLine(new Vector3(min.x, y, min.z + gridSize.z * z),
                            new Vector3(max.x, y, min.z + gridSize.z * z));
        }
        for (int x = 0; x <= mapSize.x; x++)
        {
            Gizmos.DrawLine(new Vector3(min.x + gridSize.x * x, y, min.z),
                            new Vector3(min.x + gridSize.x * x, y, max.z));
        }
    }
}
