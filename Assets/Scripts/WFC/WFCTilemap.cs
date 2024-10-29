using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using Unity.AI.Navigation;
using NaughtyAttributes;

public class WFCTilemap : MonoBehaviour
{
    [SerializeField] 
    private bool            initOnStart;
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
    [SerializeField, ShowIf("hasAdjacencyData")]
    private List<WFCTile3d> conflictTiles;

    [SerializeField, ShowIf("hasAdjacencyData")]
    private int             maxDepth = 25;
    [SerializeField, ShowIf("hasAdjacencyData")]
    private bool            dynamicUpdate;
    [SerializeField, ShowIf("isDynamic")]
    private Camera          dynamicCamera;
    [SerializeField, ShowIf("isDynamic")]
    private float           dynamicRadius;
    [SerializeField, ShowIf("isDynamic")]
    private Vector3Int      minMapLimit = new Vector3Int(-10000, 0, -10000);
    [SerializeField, ShowIf("isDynamic")]
    private Vector3Int      maxMapLimit = new Vector3Int(-10000, 1, -10000);

    bool isDynamic => hasAdjacencyData && dynamicUpdate;

    [SerializeField]
    private bool            drawGrid = false;
    [SerializeField]
    private bool            drawDebugWFC = false;

    bool hasData => tilemapData != null;
    bool hasAdjacencyData => adjacencyData != null;
    bool hasAnyData => hasData || hasAdjacencyData;

    private WFCTileData     tilemap;

    private void Awake()
    {
    }

    private IEnumerator Start()
    {
        if (initOnStart)
        {
            StartTilemap();
            yield return null;
            UpdateNavMesh();
        }
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
                    tilemap.CreateTile(x, y, z);
                }
            }
        }
    }

    GenResult GenerateTilemap(Vector3Int startPos, Vector3Int size)
    {
        GenResult ret = GenResult.Ok;

        while (ret == GenResult.Ok)
        {
            // Select a tile, using a entropy measure
            ret = tilemap.GenerateTile(startPos, size);
        }

        return ret;
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

        tilemap = new WFCTileData(mapSize, gridSize, tileset, conflictTiles, transform);
        tilemap.SetLimits(minMapLimit, maxMapLimit);
        tilemap.SetMaxDepth(maxDepth);

        // Collect all tiles and initialize them
        foreach (var tile in tiles)
        {
            // Get prefab origin   
            var prefabTile = PrefabUtility.GetCorrespondingObjectFromSource(tile);
            if (prefabTile != null)
            {
                Tile t = new Tile() { tileId = tileset.GetTileIndex(prefabTile), rotation = GetRotation(tile) };
                var  tilePos = WorldToTilePos(tile.GetExtents().center);   
                tilemap.Set(tilePos, t);
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
        var clusters = GetComponentsInChildren<WFCCluster>();
        foreach (var c in clusters)
        {
#if UNITY_EDITOR
            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
#else
            Destroy(c.gameObject);
#endif
        }
    }

    void StartTilemap()
    {
        if (tilemapData != null)
        {
            LoadTilemap();
        }
        else if (adjacencyData != null)
        {
            GenerateFullMap();
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
            tilemap = new WFCTileData(mapSize, gridSize, tileset, conflictTiles, transform);
            tilemap.SetLimits(minMapLimit, maxMapLimit);
            tilemap.SetMaxDepth(maxDepth);

            // Read the tile data
            int tileCount = mapSize.x * mapSize.y * mapSize.z;
            for (int i = 0; i < tileCount; i++)
            {
                var tilePos = IndexToTilePos(i);
                tilemap.Set(tilePos, new Tile { tileId = reader.ReadByte(), rotation = reader.ReadByte() });
            }
        }

        InstantiateMap();
    }

    [Button("Setup Tilemap"), ShowIf("hasAdjacencyData")]
    void SetupTilemap()
    {
        ClearTilemap();
        if (!LoadWFCData()) return;
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
            var uniqueTiles = new ProbList<Tile>();
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
            var adjacencyInfo = new WFCData[adjacencyLength];
            for (int i = 0; i < adjacencyLength; i++)
            {
                adjacencyInfo[i] = new WFCData();
                adjacencyInfo[i].Load(reader);
            }

            // Initialize the map array with the correct size
            tilemap = new WFCTileData(mapSize, gridSize, tileset, conflictTiles, transform);
            tilemap.SetLimits(minMapLimit, maxMapLimit);
            tilemap.SetUniqueTiles(uniqueTiles);
            tilemap.SetAdjacencyInfo(adjacencyInfo);
            tilemap.SetMaxDepth(maxDepth);
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
            int tileCount = mapSize.x * mapSize.y * mapSize.z;
            for (int i = 0; i < tileCount; i++)
            {
                var tilePos = IndexToTilePos(i);
                var t = tilemap.GetTile(tilePos);
                writer.Write(t.tileId);   // Write tileId
                writer.Write(t.rotation); // Write rotation
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
        var uniqueTiles = new ProbList<Tile>();

        for (int z = 0; z < mapSize.z; z++)
        {
            for (int y = 0; y < mapSize.y; y++)
            {
                for (int x = 0; x < mapSize.x; x++)
                {
                    uniqueTiles.Add(tilemap.GetTile(x, y, z), 1);
                }
            }
        }

        // Build adjacency information
        var adjacencyInfo = new WFCData[uniqueTiles.Count];
        for (int i = 0; i < adjacencyInfo.Length; i++) adjacencyInfo[i] = new WFCData();

        for (int z = 0; z < mapSize.z; z++)
        {
            for (int y = 0; y < mapSize.y; y++)
            {
                for (int x = 0; x < mapSize.x; x++)
                {
                    int uniqueId = uniqueTiles.IndexOf(tilemap.GetTile(x, y, z));

                    if (x > 0) adjacencyInfo[uniqueId].Add(Direction.NX, tilemap.GetTile(x - 1, y, z));
                    if (y > 0) adjacencyInfo[uniqueId].Add(Direction.NY, tilemap.GetTile(x, y - 1, z));
                    if (z > 0) adjacencyInfo[uniqueId].Add(Direction.NZ, tilemap.GetTile(x, y, z - 1));
                    if (x < mapSize.x - 1) adjacencyInfo[uniqueId].Add(Direction.PX, tilemap.GetTile(x + 1, y, z));
                    if (y < mapSize.y - 1) adjacencyInfo[uniqueId].Add(Direction.PY, tilemap.GetTile(x, y + 1, z));
                    if (z < mapSize.z - 1) adjacencyInfo[uniqueId].Add(Direction.PZ, tilemap.GetTile(x, y, z + 1));
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

    [Button("Update Navmesh"), ShowIf("hasAdjacencyData")]
    void UpdateNavMesh()
    {
        NavMeshSurface navMeshSurface = GetComponent<NavMeshSurface>();
        if (navMeshSurface == null)
        {
            navMeshSurface = gameObject.AddComponent<NavMeshSurface>();
            navMeshSurface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        }
        navMeshSurface.BuildNavMesh();
    }

    [Button("Run update")]
    private void Update()
    {
        if ((isDynamic) && (dynamicCamera))
        {
            Vector3[] corners = new Vector3[4];
            dynamicCamera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), dynamicRadius, Camera.MonoOrStereoscopicEye.Mono, corners);

            Vector3 min = dynamicCamera.transform.position;
            Vector3 max = dynamicCamera.transform.position;
            for (int i = 0; i < 4; i++)
            {
                corners[i] = dynamicCamera.transform.localToWorldMatrix.MultiplyPoint3x4(corners[i]);
                if (corners[i].x < min.x) min.x = corners[i].x;
                if (corners[i].y < min.y) min.y = corners[i].y;
                if (corners[i].z < min.z) min.z = corners[i].z;
                if (corners[i].x > max.x) max.x = corners[i].x;
                if (corners[i].y > max.y) max.y = corners[i].y;
                if (corners[i].z > max.z) max.z = corners[i].z;
            }

            Vector3Int p1 = WorldToTilePos(min);
            Vector3Int p2 = WorldToTilePos(max);
            Vector3Int start = new Vector3Int(Mathf.Min(p1.x, p2.x), Mathf.Min(p1.y, p2.y), Mathf.Min(p1.z, p2.z));
            Vector3Int end = new Vector3Int(Mathf.Max(p1.x, p2.x), Mathf.Max(p1.y, p2.y), Mathf.Max(p1.z, p2.z));
            if (start.x < minMapLimit.x) start.x = minMapLimit.x;
            if (start.y < minMapLimit.y) start.y = minMapLimit.y;
            if (start.z < minMapLimit.z) start.z = minMapLimit.z;
            if (end.x > maxMapLimit.x) end.x = maxMapLimit.x;
            if (end.y > maxMapLimit.y) end.y = maxMapLimit.y;
            if (end.z > maxMapLimit.z) end.z = maxMapLimit.z;

            int maxTilesPerFrame = 25;
            bool updated = false;
            for (int i = 0; i < maxTilesPerFrame; i++)
            {
                var err = tilemap.GenerateTile(start, end - start);
                if ((err == GenResult.Ok) || (err == GenResult.Conflict))
                {
                    updated = true;
                }
                else
                {
                    break;
                }
            }

            if (updated)
            {
                UpdateNavMesh();
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (hasAnyData)
        {
            // Bounds are not loaded, so we need to compute them
            localBounds.SetMinMax(Vector3.zero,
                                  new Vector3(mapSize.x * gridSize.x, gridSize.y, mapSize.z * gridSize.z));
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
            if ((isDynamic) && (dynamicCamera))
            {
                Gizmos.color = Color.green;
                Gizmos.matrix = dynamicCamera.transform.localToWorldMatrix;
                Gizmos.DrawFrustum(Vector3.zero, dynamicCamera.fieldOfView, dynamicRadius, 1.0f, 16.0f / 9.0f);

                Vector3[] corners = new Vector3[4];
                dynamicCamera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), dynamicRadius, Camera.MonoOrStereoscopicEye.Mono, corners);

                Vector3 min = dynamicCamera.transform.position;
                Vector3 max = dynamicCamera.transform.position;
                for (int i = 0; i < 4; i++)
                {
                    corners[i] = dynamicCamera.transform.localToWorldMatrix.MultiplyPoint3x4(corners[i]);
                    if (corners[i].x < min.x) min.x = corners[i].x;
                    if (corners[i].y < min.y) min.y = corners[i].y;
                    if (corners[i].z < min.z) min.z = corners[i].z;
                    if (corners[i].x > max.x) max.x = corners[i].x;
                    if (corners[i].y > max.y) max.y = corners[i].y;
                    if (corners[i].z > max.z) max.z = corners[i].z;
                }

                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = new Color(1.0f, 0.5f, 0.2f, 1.0f);
                Gizmos.DrawWireCube((min + max) * 0.5f, (max - min));

                Gizmos.color = Color.red;
                Vector3Int p1 = WorldToTilePos(min);
                Vector3Int p2 = WorldToTilePos(max);
                Vector3Int start = new Vector3Int(Mathf.Min(p1.x, p2.x), Mathf.Min(p1.y, p2.y), Mathf.Min(p1.z, p2.z));
                Vector3Int end = new Vector3Int(Mathf.Max(p1.x, p2.x), Mathf.Max(p1.y, p2.y), Mathf.Max(p1.z, p2.z));

                Gizmos.matrix = transform.localToWorldMatrix;

                for (int z = start.z; z <= end.z; z++)
                {
                    for (int x = start.x; x <= end.x; x++)
                    {
                        DrawGridPos(new Vector3Int(x, 0, z));
                    }
                }
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
