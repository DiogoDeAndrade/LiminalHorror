using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using Unity.AI.Navigation;
using NaughtyAttributes;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

[ExecuteInEditMode]
public class WFCTilemap : MonoBehaviour
{
    public delegate void MapStartupFunction();

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
    private float           maxGenerationDistance = 50;
    [SerializeField, ShowIf("isDynamic")]
    private float           fadeOutDistance = 10;
    [SerializeField, ShowIf("isDynamic")]
    private float           maxTimePerFrameMS = 15.0f;
    [SerializeField, ShowIf("isDynamic")]
    private bool            buildNavmesh = false;
    [SerializeField, ShowIf("isDynamic")]
    private Vector3Int      minMapLimit = new Vector3Int(-10000, 0, -10000);
    [SerializeField, ShowIf("isDynamic")]
    private Vector3Int      maxMapLimit = new Vector3Int(-10000, 1, -10000);
    [SerializeField, ShowIf("isDynamic")]
    private bool            usePooling;
    [SerializeField, ShowIf("isDynamic")]
    private Transform       poolingContainer;

    bool isDynamic => hasAdjacencyData && dynamicUpdate;

    [SerializeField]
    private bool            drawGrid = false;
    [SerializeField, ShowIf("hasAdjacencyData")]
    private bool            debugWFC = false;
    [SerializeField, ShowIf("hasDebugOptions")]
    private bool            drawDebugWFC = false;
    [SerializeField, ShowIf("hasDebugOptions")]
    private bool            cellInfo = false;
    [SerializeField, ShowIf("hasDebugOptions"), ReadOnly, ResizableTextArea]
    private string          debugInfo = "";
    [SerializeField, ShowIf("hasDebugOptions"), ReadOnly]
    private float           debuglastGenerationTimeMS;

    bool hasData => tilemapData != null;
    bool hasAdjacencyData => adjacencyData != null;
    bool hasAnyData => hasData || hasAdjacencyData;
    bool hasDebugOptions => hasAdjacencyData && debugWFC;

    private WFCTileData     tilemap;

    private void Awake()
    {
        if (!Application.isPlaying) return;
    }

    private IEnumerator Start()
    {
        if (!Application.isPlaying) yield break;

        if (initOnStart)
        {
            StartTilemap();
            yield return null;
            if (buildNavmesh) UpdateNavMesh();
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
        tilemap.DisableClusterObject();
        tilemap.SetPooling(usePooling, poolingContainer);

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
            GenerateMap();
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
            tilemap.SetPooling(usePooling, poolingContainer);

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
        if (buildNavmesh) UpdateNavMesh();
        if (!LoadWFCData()) return;

        startedWFC = true;
    }

    bool startedWFC = false;

    [Button("Generate Full Map"), ShowIf("hasAdjacencyData")]
    public void GenerateMap(MapStartupFunction startupFunction = null)
    {
        SetupTilemap();
        startupFunction?.Invoke();

        switch (GenerateTilemap(Vector3Int.zero, mapSize))
        {
            case GenResult.Ok:
                break;
            case GenResult.Conflict:
                Debug.LogWarning("Conflict detected, halted!");
                break;
            case GenResult.Complete:
                break;
        }
    }

    [Button("Generate Step"), ShowIf(EConditionOperator.And, "hasAdjacencyData", "startedWFC")]
    void GenerateStep()
    {
        tilemap.onTileCreate -= Debug_OnTileCreated;
        tilemap.onConflict -= Debug_OnConflict;
        tilemap.onPropagate -= Debug_OnPropagate;
        if (debugWFC)
        {
            tilemap.onTileCreate += Debug_OnTileCreated;
            tilemap.onConflict += Debug_OnConflict;
            tilemap.onPropagate += Debug_OnPropagate;

            debugPropagations = new();
        }

        var err = tilemap.GenerateTile(Vector3Int.zero, mapSize);
        switch (err)
        {
            case GenResult.Ok:
                break;
            case GenResult.Conflict:
                Debug.LogWarning("Conflict detected, halted!");
                startedWFC = false;
                break;
            case GenResult.Complete:
                Debug.LogWarning("Generation completed, halted!");
                startedWFC = false;
                break;
            default:
                break;
        }
    }

    private void Debug_OnPropagate(Vector3Int prevPos, Vector3Int nextPos, ProbList<Tile> allowedTiles, int depth)
    {
        Debug.Log($"[WFC]: Propagating {allowedTiles.ToSimpleString()} from {prevPos} to ({nextPos}) - Depth = {depth}");
        debugPropagations.Add((prevPos, nextPos));
    }

    private void Debug_OnConflict(Vector3Int worldPos)
    {
        Debug.Log($"[WFC]: Conflict at ({worldPos})");
    }

    private void Debug_OnTileCreated(Vector3Int worldPos, WFCTileData.Cluster cluster, Vector3Int clusterPos, Tile tile, ProbList<Tile> possibilities)
    {
        Debug.Log($"[WFC]: Create tile at ({worldPos}), cluster = {cluster.basePos}, localPos = {clusterPos}, Tile = {tile}, CurrentSet = {possibilities.ToSimpleString()}");
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
                float count = reader.ReadSingle();
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
            tilemap.SetPooling(usePooling, poolingContainer);
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
                writer.Write(ut.weight);
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
        if (!Application.isPlaying) return;

        if ((isDynamic) && (dynamicCamera))
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            bool updated = false;
            int currentDistance = Mathf.CeilToInt(Mathf.Min(2, maxGenerationDistance * 0.1f));
            int incDistance = currentDistance;

            while (currentDistance <= maxGenerationDistance)
            {
                Vector3[] corners = new Vector3[4];
                dynamicCamera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), currentDistance, Camera.MonoOrStereoscopicEye.Mono, corners);

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

                for (int i = 0; i < 5; i++)
                {
                    var err = tilemap.GenerateTile(start, end - start);
                    if ((err == GenResult.Ok) || (err == GenResult.Conflict))
                    {
                        updated = true;
                    }
                    else
                    {
                        currentDistance += incDistance;
                    }

                    if (sw.ElapsedMilliseconds > maxTimePerFrameMS) break;
                }

                if (sw.ElapsedMilliseconds > maxTimePerFrameMS) break;
            }

            sw.Stop();
            debuglastGenerationTimeMS = sw.ElapsedMilliseconds;

            Vector3 localCameraPos = transform.worldToLocalMatrix.MultiplyPoint(dynamicCamera.transform.position);
            Vector3 localCameraDir = transform.worldToLocalMatrix.MultiplyVector(dynamicCamera.transform.forward); localCameraDir.y = 0; localCameraDir.Normalize();
            var activeClusters = new List<WFCTileData.Cluster>(tilemap.currentClusters);
            foreach (var cluster in tilemap.currentClusters)
            {
                // Get cluster world position
                Vector3 clusterCenter = cluster.basePos;
                clusterCenter.x = (clusterCenter.x + 0.5f ) * gridSize.x * tilemap.clusterSize.x;
                clusterCenter.y = (clusterCenter.y + 0.5f ) * gridSize.y * tilemap.clusterSize.y;
                clusterCenter.z = (clusterCenter.z + 0.5f ) * gridSize.z * tilemap.clusterSize.z;

                Vector3 toClusterCenter = clusterCenter - localCameraPos;
                float distance = toClusterCenter.magnitude;
                toClusterCenter /= distance;
                if (distance > fadeOutDistance)
                {
                    if (Vector3.Dot(toClusterCenter, localCameraDir) < -0.25f)
                    {
                        // Remove this cluster
                        tilemap.RemoveCluster(cluster);
                    }
                }
            }

            if ((updated) && (buildNavmesh))
            {
                UpdateNavMesh();
            }
        }
    }

    static Vector3Int? debugHoveredTile = null;
    static List<(Vector3Int p1, Vector3Int p2)> debugPropagations = new();

    private void OnEnable()
    {
        // Subscribe to the Scene view event
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        // Unsubscribe when disabled
        SceneView.duringSceneGui -= OnSceneGUI;
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
                Gizmos.DrawFrustum(Vector3.zero, dynamicCamera.fieldOfView, maxGenerationDistance, 1.0f, 16.0f / 9.0f);

                Vector3[] corners = new Vector3[4];
                dynamicCamera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), maxGenerationDistance, Camera.MonoOrStereoscopicEye.Mono, corners);

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
        if ((cellInfo) && (debugHoveredTile.HasValue))
        {
            Vector3 gSize = gridSize; gSize.y *= 0.05f;
            Vector3 worldPos = new Vector3((debugHoveredTile.Value.x + 0.5f) * gSize.x, (debugHoveredTile.Value.y + 0.5f) * gSize.y * 0.5f, (debugHoveredTile.Value.z + 0.5f) * gSize.z);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(worldPos, gSize);

            foreach (var p in debugPropagations)
            {
                if (p.p1 == debugHoveredTile)
                {
                    Vector3 p1 = new Vector3((p.p1.x + 0.5f) * gridSize.x, p.p1.y * gridSize.y, (p.p1.z + 0.5f) * gridSize.z);
                    Vector3 p2 = new Vector3((p.p2.x + 0.5f) * gridSize.x, p.p2.y * gridSize.y, (p.p2.z + 0.5f) * gridSize.z);

                    Vector3 delta = p2 - p1;
                    float headSize = delta.magnitude * 0.15f;
                    Vector3 rightHeadDir = Quaternion.LookRotation(delta) * Quaternion.Euler(0, 150, 0) * Vector3.forward;
                    Vector3 leftHeadDir = Quaternion.LookRotation(delta) * Quaternion.Euler(0, -150, 0) * Vector3.forward;

                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawLine(p2, p2 + rightHeadDir * headSize);
                    Gizmos.DrawLine(p2, p2 + leftHeadDir * headSize);
                }
            }
        }

        Gizmos.matrix = prevMatrix;
    }

    void OnSceneGUI(SceneView view)
    { 
        if ((cellInfo) && (tilemap != null))
        {
            // Get mouse position in Scene view
            Event e = Event.current;
            if (e != null)
            {
                // Only proceed if the mouse is moving in the scene view
                if (e.type == EventType.MouseMove || e.type == EventType.Repaint)
                {
                    debugHoveredTile = null;
                    debugInfo = "";

                    Vector3 clusterSize = tilemap.GetClusterWorldSize();
                    Vector3Int clusterSizeInTiles = tilemap.clusterSize;
                    Vector3 gSize = gridSize; gSize.y *= 0.05f;

                    var clusters = tilemap.currentClusters;

                    foreach (var cluster in clusters)
                    {
                        var tiles = cluster.map;
                        for (int i = 0; i < tiles.Length; i++)
                        {
                            // Compute position of this tile
                            int localX = i % clusterSizeInTiles.x;
                            int localY = (i / clusterSizeInTiles.x) % clusterSizeInTiles.y;
                            int localZ = i / (clusterSizeInTiles.x * clusterSizeInTiles.y);
                            Vector3Int tileWorldPos = new Vector3Int(localX + cluster.basePos.x * clusterSizeInTiles.x,
                                                                     localY + cluster.basePos.y * clusterSizeInTiles.y,
                                                                     localZ + cluster.basePos.z * clusterSizeInTiles.z);
                            Vector3 worldPos = new Vector3((tileWorldPos.x + 0.5f) * gSize.x, (tileWorldPos.y + 0.5f) * gSize.y, (tileWorldPos.z + 0.5f) * gSize.z);

                            Bounds boundingBox = new Bounds(worldPos, gSize);

                            // Create a ray from the mouse position, and change it to local coordinates
                            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                            ray.origin = transform.worldToLocalMatrix.MultiplyPoint(ray.origin);
                            ray.direction = transform.worldToLocalMatrix.MultiplyVector(ray.direction);

                            // Check if the ray intersects the bounding box
                            if (boundingBox.IntersectRay(ray))
                            {
                                debugHoveredTile = tileWorldPos;

                                var tileInfo = tilemap.GetWFCTile(tileWorldPos);

                                debugInfo = $"Tile Pos = {tileWorldPos}\n";
                                debugInfo += $"Tile = {tileInfo.tile}\n";
                                if (tileInfo.probMap == null)
                                {
                                    if (tileInfo.tile.tileId == 0) debugInfo += $"No solution possible (conflict will happen)\n";
                                    else debugInfo += $"Already observed\n";
                                }
                                else
                                {
                                    debugInfo += $"Allowed states:\n";
                                    float totalWeight = 0.0f;
                                    foreach (var prob in tileInfo.probMap)
                                    {
                                        totalWeight += prob.weight;
                                    }
                                    foreach (var prob in tileInfo.probMap)
                                    {
                                        debugInfo += $"  {prob.element} => {prob.weight * 100.0f / totalWeight}%\n";
                                    }
                                }

                                debugInfo += "Propagations:\n";
                                foreach (var p in debugPropagations)
                                {
                                    if (p.p1 == tileWorldPos)
                                    {
                                        debugInfo += $"  => {p.p2}\n";
                                    }
                                }

                                // Repaint the scene view to update the label
                                view.Repaint();
                            }
                        }
                    }
                }
            }
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

    private void DrawGridPos(Vector3Int pos)
    {
        Vector3 p = localBounds.min + gridSize * 0.5f + new Vector3(pos.x * gridSize.x, pos.y * gridSize.y, pos.z * gridSize.z);

        Gizmos.DrawWireCube(p, gridSize);
    }
}
