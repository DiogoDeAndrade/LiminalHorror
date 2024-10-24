using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class WFCTilemap : MonoBehaviour
{
    enum GenResult { Ok, Conflict, Complete };

    [System.Serializable]
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

    enum Direction { PX = 0, PY = 1, PZ = 2, NX = 3, NY = 4, NZ = 5 };
    const int maxDirection = 6;

    class WFCData
    {
        public WFCData() 
        { 
            adjacency = new ProbList<Tile>[maxDirection];
            for (int i = 0; i < maxDirection; i++) adjacency[i] = new();
        }

        ProbList<Tile>[] adjacency;

        public void Add(Direction direction, Tile tile)
        {
            adjacency[(int)direction].Add(tile, 1);
        }

        internal void Save(BinaryWriter writer)
        {
            // For each direction (PX, PY, PZ, NX, NY, NZ)
            for (int dir = 0; dir < maxDirection; dir++)
            {
                ProbList<Tile> tileProbs = adjacency[dir];

                // Write the number of TileProbability entries for this direction
                writer.Write(tileProbs.Count);

                // Write each TileProbability's tileId and count
                foreach (var tileProb in tileProbs)
                {
                    writer.Write(tileProb.element.tileId);     // Write the tileId
                    writer.Write(tileProb.element.rotation);   // Write the rotation
                    writer.Write(tileProb.count);              // Write the count
                }
            }
        }

        internal void Load(BinaryReader reader)
        {
            // For each direction (PX, PY, PZ, NX, NY, NZ)
            for (int dir = 0; dir < maxDirection; dir++)
            {
                // Read the number of TileProbability entries for this direction
                int tileProbCount = reader.ReadInt32();

                // Clear the current list for this direction to prepare for new data
                adjacency[dir] = new ProbList<Tile>();

                // Read each TileProbability entry
                for (int i = 0; i < tileProbCount; i++)
                {
                    // Read the tile's tileId and rotation
                    Tile tile = new Tile
                    {
                        tileId = reader.ReadByte(),      // Read the tileId
                        rotation = reader.ReadByte()     // Read the rotation
                    };

                    // Read the count
                    int count = reader.ReadInt32();  // Read the count

                    // Add the TileProbability to the list for this direction
                    adjacency[dir].Add(tile, count);
                }
            }
        }

        internal ProbList<Tile> Get(Direction direction)
        {
            return adjacency[(int)direction];
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
    [SerializeField, ShowIf("hasAdjacencyData")]
    private List<WFCTile3d> conflictTiles;

    [SerializeField]
    private bool            drawGrid = false;
    [SerializeField]
    private bool            drawDebugWFC = false;

    private WFCTile3d[]     tileGameObjects;
    ProbList<Tile>          uniqueTiles;
    WFCData[]               adjacencyInfo;
    ProbList<Tile>[]        probabilityMap;

    bool hasData => tilemapData != null;
    bool hasAdjacencyData => adjacencyData != null;
    bool hasAnyData => hasData || hasAdjacencyData;

    Vector3Int              lastObservedPos = new Vector3Int(-1, -1, -1);
    List<Vector3Int>        lastPropagatedPos = new List<Vector3Int>();
    List<Vector3Int>        lastConflict = new List<Vector3Int>();

    private void Awake()
    {
    }

    private void Start()
    {
        LoadTilemap();
    }


    void InstantiateMap()
    {
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
                    int index = TilePosToIndex(x, y, z);
                    DestroyTile(tileGameObjects[index]);
                    tileGameObjects[index] = CreateTileAt(x, y, z, map[index]);
                }
            }
        }
    }

    void UpdateMap(int mapIndex)
    {
        DestroyTile(tileGameObjects[mapIndex]);
        tileGameObjects[mapIndex] = CreateTileAt(IndexToTilePos(mapIndex), map[mapIndex]);
    }

    GenResult GenerateTilemap(Vector3Int startPos, Vector3Int size)
    {
        GenResult ret = GenResult.Ok;

        while (ret == GenResult.Ok)
        {
            // Select a tile, using a entropy measure
            int mapIndex = GetLowestEntropyIndex(startPos, size);
            if (mapIndex >= 0)
            {
                ret = GenerateTile(mapIndex);
            }
            else
            {
                ret = GenResult.Complete;
            }
        }

        return ret;
    }

    GenResult GenerateTile(int index)
    {
        Vector3Int tilePos = IndexToTilePos(index);
        lastObservedPos = tilePos;

        Tile newTile = probabilityMap[index].Get();
        if (newTile == null) return GenResult.Ok;

        // Observe this map position
        map[index] = newTile;

        // Remove this, no need to keep the probability list
        probabilityMap[index] = null;

        // Now propagate
        var ret = Propagate(tilePos, new ProbList<Tile>(newTile), true);

        UpdateMap(index);

        return ret;
    }

    private GenResult Propagate(Vector3Int tilePos, ProbList<Tile> tiles, bool force)
    {
        GenResult PropagateDir(Direction direction, Vector3Int delta)
        {
            ProbList<Tile> allowed = new();
            foreach (var tile in tiles)
            {
                int uniqueId = uniqueTiles.IndexOf(tile.element);
                allowed.Add(adjacencyInfo[uniqueId].Get(direction));
            }
            return Propagate(tilePos + delta, allowed, false);
        }

        GenResult   ret = GenResult.Ok;
        bool        propagateFurther = false;

        int thisIndex = TilePosToIndex(tilePos);
        var pm = probabilityMap[thisIndex];
        if (pm != null)
        {
            lastPropagatedPos.Add(tilePos);

            // Check if something should be removed
            foreach (var tc in pm)
            {
                if (tiles.IndexOf(tc.element) == -1)
                {
                    // This should be removed
                    pm.Set(tc.element, 0);
                    // We changed the list, need to propagate the change
                    propagateFurther = true;
                }
                else
                {
                    pm.Set(tc.element, Mathf.Min(tc.count, pm.GetCount(tc.element)));
                }
            }

            if (propagateFurther)
            {
                pm.Cleanup();
            }

            if (pm.Count == 0)
            {
                // this isn't collapsed, but there's no options, log conflict (for now)
                Debug.LogWarning("Conflict found in propagation!");
                probabilityMap[thisIndex] = null;

                if ((conflictTiles != null) && (conflictTiles.Count > 0))
                {
                    // Resolve this one - set map to one of the conflict tiles
                    WFCTile3d tile = conflictTiles.Random();
                    map[thisIndex] = new Tile() { tileId = tileset.GetTileIndex(tile), rotation = (byte)Random.Range(0, 3) };
                }
                else
                {
                    lastConflict.Add(tilePos);
                    ret = GenResult.Conflict;
                }
            }
        }
        else
        {
            propagateFurther = force;
        }

        if (propagateFurther)
        {
            // Get all tiles allowed to the right (X+), and propagate that
            if (tilePos.x < mapSize.x - 1)
            {
                if (PropagateDir(Direction.PX, Vector3Int.right) == GenResult.Conflict)
                {
                    ret = GenResult.Conflict;
                }
            }
            // Left (X-)
            if (tilePos.x > 0)
            {
                if (PropagateDir(Direction.NX, Vector3Int.left) == GenResult.Conflict)
                {
                    ret = GenResult.Conflict;
                }
            }
            // Forward (Z+)
            if (tilePos.z < mapSize.z - 1)
            {
                if (PropagateDir(Direction.PZ, Vector3Int.forward) == GenResult.Conflict)
                {
                    ret = GenResult.Conflict;
                }
            }
            // Back (Z-)
            if (tilePos.z > 0)
            {
                if (PropagateDir(Direction.NZ, Vector3Int.back) == GenResult.Conflict)
                {
                    ret = GenResult.Conflict;
                }
            }
            // Up (Y+)
            if (tilePos.y < mapSize.y - 1)
            {
                if (PropagateDir(Direction.PY, Vector3Int.up) == GenResult.Conflict)
                {
                    ret = GenResult.Conflict;
                }
            }
            // Down (Y-)
            if (tilePos.y > 0)
            {
                if (PropagateDir(Direction.NY, Vector3Int.down) == GenResult.Conflict)
                {
                    ret = GenResult.Conflict;
                }
            }
        }

        return ret;
    }

    int GetLowestEntropyIndex(Vector3Int startPos, Vector3Int size)
    {
        List<int>   possibilities = new List<int>();
        int         minOptions = int.MaxValue;

        for (int z = startPos.z; z < startPos.z + size.z; z++)
        {
            for (int y = startPos.y; y < startPos.y + size.y; y++)
            {
                for (int x = startPos.x; x < startPos.x + size.x; x++)
                {
                    int i = TilePosToIndex(x, y, z);
                    if (probabilityMap[i] == null) continue;

                    if (probabilityMap[i].Count < minOptions)
                    {
                        possibilities.Clear();
                        possibilities.Add(i);
                        minOptions = probabilityMap[i].Count;
                    }
                    else if (probabilityMap[i].Count == minOptions)
                    {
                        possibilities.Add(i);
                    }
                }
            }
        }

        return (possibilities.Count > 0) ? (possibilities.Random()) : (-1);
    }

    WFCTile3d CreateTileAt(Vector3Int tilePos, Tile tile)
    {
        if (tile.tileId == 0) return null;

        return CreateTileAt(tilePos.x, tilePos.y, tilePos.z, tile);
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
                var  tilePosIndex = TilePosToIndex(tilePos);
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

    int TilePosToIndex(Vector3Int tilePos)
    {
        return TilePosToIndex(tilePos.x, tilePos.y, tilePos.z);
    }
    int TilePosToIndex(int x, int y, int z)
    {
        return x + y * mapSize.x + z * (mapSize.x * mapSize.y);
    }

    Vector3Int IndexToTilePos(int index)
    {
        int x = index % mapSize.x;
        int y = (index / mapSize.x) % mapSize.y;
        int z = index / (mapSize.x * mapSize.y);

        return new Vector3Int(x, y, z);
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
            probabilityMap = null;
            tileGameObjects = new WFCTile3d[map.Length];

            // Read the tile data
            for (int i = 0; i < map.Length; i++)
            {
                map[i].tileId = reader.ReadByte();
                map[i].rotation = reader.ReadByte();
            }
        }

        InstantiateMap();
    }

    [Button("Setup Tilemap"), ShowIf("hasAdjacencyData")]
    void SetupTilemap()
    {
        lastObservedPos = new Vector3Int(-1, -1, -1);
        lastPropagatedPos = new();
        lastConflict = new();

        ClearTilemap();
        if (!LoadWFCData()) return;

        // Create the probability map, if it doesn't exist
        if (probabilityMap == null)
        {
            probabilityMap = new ProbList<Tile>[mapSize.x * mapSize.y * mapSize.z];
            for (int i = 0; i < probabilityMap.Length; i++)
            {
                probabilityMap[i] = new ProbList<Tile>(uniqueTiles);
            }
        }
    }


    [Button("Generate Full Map"), ShowIf("hasAdjacencyData")]
    void GenerateFullMap()
    {
        SetupTilemap();

        // Select a tile, using a entropy measure
        switch (GenerateTilemap(Vector3Int.zero, mapSize))
        {
            case GenResult.Ok:
                break;
            case GenResult.Conflict:
                Debug.LogWarning("Conflict detected, halting!");
                break;
            case GenResult.Complete:
                break;
        }
    }

    [Button("Generate next step"), ShowIf("hasAdjacencyData")]
    void GenerateNextStep()
    {
        lastObservedPos = new Vector3Int(-1, -1, -1);
        lastPropagatedPos = new();
        lastConflict = new();

        // Select a tile, using a entropy measure
        int mapIndex = GetLowestEntropyIndex(Vector3Int.zero, mapSize);
        if (mapIndex >= 0)
        {
            GenerateTile(mapIndex);
        }
    }

    [SerializeField] private Tile       debugHardcodedTile;
    [SerializeField] private Vector3Int debugHardcodedPos;

    [Button("Generate hardcoded step"), ShowIf("hasAdjacencyData")]
    void HardCodedStep()
    {
        SetupTilemap();

        lastObservedPos = debugHardcodedPos;

        int index = TilePosToIndex(debugHardcodedPos);

        // Observe this map position
        map[index] = debugHardcodedTile;

        // Remove this, no need to keep the probability list
        probabilityMap[index] = null;

        // Now propagate
        Propagate(debugHardcodedPos, new ProbList<Tile>(debugHardcodedTile), true);

        UpdateMap(index);
    }

    bool LoadWFCData()
    { 
        if (adjacencyData == null)
        {
            Debug.LogWarning("No file specified for adjacency data!");
            return false;
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
                return false;
            }

            // Read grid size
            gridSize.x = reader.ReadSingle();
            gridSize.y = reader.ReadSingle();
            gridSize.z = reader.ReadSingle();

            // Read unique tiles
            int uniqueTileCount = reader.ReadInt32();
            uniqueTiles = new ProbList<Tile>();
            for (int i = 0; i < uniqueTileCount; i++)
            {
                Tile tile = new Tile();
                tile.tileId = reader.ReadByte();
                tile.rotation = reader.ReadByte();
                int count = reader.ReadInt32();
                uniqueTiles.Add(tile, count);
            }

            // Read adjacency array
            int adjacencyLength = reader.ReadInt32();
            adjacencyInfo = new WFCData[adjacencyLength];
            for (int i = 0; i < adjacencyLength; i++)
            {
                adjacencyInfo[i] = new WFCData();
                adjacencyInfo[i].Load(reader);
            }

            // Initialize the map array with the correct size
            map = new Tile[mapSize.x * mapSize.y * mapSize.z];
            probabilityMap = null;
            tileGameObjects = new WFCTile3d[map.Length];
        }

        Debug.Log("Adjacency data loaded successfully!");

        return true;
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

        // Get unique tiles
        uniqueTiles = new ProbList<Tile>();

        int strideX = 1;
        int strideY = mapSize.x;
        int strideZ = mapSize.x * mapSize.y;

        for (int z = 0; z < mapSize.z; z++)
        {
            for (int y = 0; y < mapSize.y; y++)
            {
                for (int x = 0; x < mapSize.x; x++)
                {
                    int index = TilePosToIndex(x, y, z);
                    uniqueTiles.Add(map[index], 1);
                }
            }
        }

        // Build adjacency information
        adjacencyInfo = new WFCData[uniqueTiles.Count];
        for (int i = 0; i < adjacencyInfo.Length; i++) adjacencyInfo[i] = new WFCData();

        for (int z = 0; z < mapSize.z; z++)
        {
            for (int y = 0; y < mapSize.y; y++)
            {
                for (int x = 0; x < mapSize.x; x++)
                {
                    int index = TilePosToIndex(x, y, z);
                    int uniqueId = uniqueTiles.IndexOf(map[index]);

                    if (x > 0) adjacencyInfo[uniqueId].Add(Direction.NX, map[index - strideX]);
                    if (y > 0) adjacencyInfo[uniqueId].Add(Direction.NY, map[index - strideY]);
                    if (z > 0) adjacencyInfo[uniqueId].Add(Direction.NZ, map[index - strideZ]);
                    if (x < mapSize.x - 1) adjacencyInfo[uniqueId].Add(Direction.PX, map[index + strideX]);
                    if (y < mapSize.y - 1) adjacencyInfo[uniqueId].Add(Direction.PY, map[index + strideY]);
                    if (z < mapSize.z - 1) adjacencyInfo[uniqueId].Add(Direction.PZ, map[index + strideZ]);
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
                writer.Write(ut.element.tileId);
                writer.Write(ut.element.rotation);
                writer.Write(ut.count);
            }

            // Write the number of tiles (adjacency.Length)
            writer.Write(adjacencyInfo.Length);

            // For each tile in the adjacency array
            for (int i = 0; i < adjacencyInfo.Length; i++)
            {
                adjacencyInfo[i].Save(writer);
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

        var prevMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (drawGrid)
        {
            Gizmos.color = Color.yellow;
            DrawXZGrid(localBounds.min.y);
        }
        if (drawDebugWFC)
        {
            if (lastPropagatedPos != null)
            {
                Gizmos.color = new Color(0.0f, 0.25f, 0.0f, 1.0f);
                foreach (var p in lastPropagatedPos)
                {
                    DrawGridPos(p);
                }
            }
            if (lastConflict != null)
            {
                Gizmos.color = Color.red;
                foreach (var p in lastConflict)
                {
                    DrawGridPos(p);
                }
            }
            if (lastObservedPos.x != -1)
            {
                Gizmos.color = Color.green;
                DrawGridPos(lastObservedPos);
            }
        }

        Gizmos.matrix = prevMatrix;
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

    private void DrawGridPos(Vector3Int pos)
    {
        Vector3 p = localBounds.min + gridSize * 0.5f + new Vector3(pos.x * gridSize.x, pos.y * gridSize.y, pos.z * gridSize.z);

        Gizmos.DrawWireCube(p, gridSize);
    }
}
