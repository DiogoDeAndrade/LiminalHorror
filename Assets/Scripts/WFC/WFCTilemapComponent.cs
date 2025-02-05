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
using static UnityEngine.Rendering.STP;
using UnityEngine.UI;
using OkapiKit;
using System;
using System.Threading;

// This class bridges the generic WFC classes with Unity's system
// Although the WFC classes use some Unity data structures, like Vector3, etc
// if it is required it's easy to remove.

[ExecuteInEditMode]
public class WFCTilemapComponent : MonoBehaviour
{
    public enum Mode { LoadData, ExtractData, GenerateData };
    public delegate void MapStartupFunction();
    public delegate void OnNewCluster(WFCCluster cluster);

    [SerializeField]
    private Mode                mode;
    [SerializeField] 
    private bool                initOnStart;
    [SerializeField]
    private WFCTilemapConfig    config;
    [SerializeField, ShowIf(nameof(hasAdjacencyData)), Tooltip("List of elements to use when can't find a tile to place")]
    private List<WFCTile3d>     conflictTiles;

    [SerializeField, ShowIf(nameof(isDynamic))]
    private Camera              dynamicCamera;
    [SerializeField, ShowIf(nameof(isDynamic))]
    private bool                usePooling;
    [SerializeField, ShowIf(nameof(usesPooling))]
    private Transform           poolingContainer;

    bool isDynamic => hasAdjacencyData && (config != null) && (config.dynamicUpdate);
    bool usesPooling => isDynamic && usePooling;

    [SerializeField]
    private bool            drawGrid = false;
    [SerializeField, ShowIf(nameof(hasAdjacencyData))]
    private bool            debugWFC = false;
    [SerializeField, ShowIf(nameof(hasDebugOptions))]
    private bool            drawDebugWFC = false;
    [SerializeField, ShowIf(nameof(hasDebugOptions))]
    private bool            cellInfo = false;
    [SerializeField, ShowIf(nameof(hasDebugOptions)), ReadOnly, ResizableTextArea]
    private string          debugInfo = "";
    [SerializeField, ShowIf(nameof(hasDebugOptions)), ReadOnly]
    private float           debuglastGenerationTimeMS;

    bool hasTilemapData => (config != null) && (config.tilemapData != null);
    bool hasAdjacencyData => (config != null) && (config.adjacencyData != null);
    bool hasAnyData => hasTilemapData || hasAdjacencyData;

    bool hasDebugOptions => hasAdjacencyData && debugWFC;

    private bool                                    startedWFC = false;
    private WFCTilemap                              tilemap;
    private Dictionary<WFCCluster, Transform>       clusterObjects = new();
    private Dictionary<WFCTile3d, List<WFCTile3d>>  objectPool;
    private WFCThread                               thread;

    public event OnNewCluster onNewCluster;

    public Vector3 gridSize => config.gridSize;

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
            if (config.buildNavmesh) UpdateNavMesh();
        }
    }

    void InstantiateMap()
    {
        UpdateMap(new Vector3Int(0, 0, 0), config.mapSize);
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

        if (config == null)
        {
            config = ScriptableObject.CreateInstance<WFCTilemapConfig>();

            foreach (var tile in tiles)
            {
                config.gridSize = tile.GetExtents().size;
                break;
            }

            // Save this scriptable object
            string fileName = "WFCTilemapConfig";
            fileName = EditorUtility.SaveFilePanelInProject(
                "Save WFCTilemapConfig",
                fileName,
                "asset",
                "Enter a name for the tilemap config"
            );

            if (!string.IsNullOrEmpty(fileName))
            {
                config = SaveScriptableObject(config, fileName);
                EditorUtility.SetDirty(config);
            }
            else
            {
                return;
            }
        }

        if (config.tileset == null)
        {
            config.tileset = ScriptableObject.CreateInstance<WFCTileset>();

            // Collect all tiles and initialize them
            foreach (var tile in tiles)
            {
                // Get prefab origin   
                var prefabTile = PrefabUtility.GetCorrespondingObjectFromSource(tile);
                if (prefabTile != null)
                {
                    config.tileset.Add(prefabTile);
                }
            }

            // Save this scriptable object
            string fileName = "WFCTileset";
            fileName = EditorUtility.SaveFilePanelInProject(
                "Save WFCTileset",
                fileName,
                "asset",
                "Enter a name for the tileset"
            );

            if (!string.IsNullOrEmpty(fileName))
            {
                config.tileset = SaveScriptableObject(config.tileset, fileName);
                EditorUtility.SetDirty(config);
                EditorUtility.SetDirty(config.tileset);
            }
            else
            {
                return;
            }
        }

        if (config.tileset.Count > 255)
        {
            Debug.LogWarning("Only have support for 255 individual tiles - increase size of tile id!");
        }

        // Init bounds and map size
        config.localBounds = GetExtents(tiles);
        config.localBounds.center = config.localBounds.center - transform.position;

        config.mapSize.x = Mathf.Max(1, Mathf.FloorToInt((config.localBounds.max.x - config.localBounds.min.x) / config.gridSize.x));
        config.mapSize.y = Mathf.Max(1, Mathf.FloorToInt((config.localBounds.max.y - config.localBounds.min.y) / config.gridSize.y));
        config.mapSize.z = Mathf.Max(1, Mathf.FloorToInt((config.localBounds.max.z - config.localBounds.min.z) / config.gridSize.z));

        tilemap = new WFCTilemap(config, conflictTiles);
        SetupHandlers();

        // Collect all tiles and initialize them
        foreach (var tile in tiles)
        {
            // Get prefab origin   
            var prefabTile = PrefabUtility.GetCorrespondingObjectFromSource(tile);
            if (prefabTile != null)
            {
                Tile t = new Tile() { tileId = config.tileset.GetTileIndex(prefabTile), rotation = GetRotation(tile) };
                var  tilePos = WorldToTilePos(tile.GetExtents().center);   
                tilemap.Set(tilePos, t);
            }
        }

    }

    private T SaveScriptableObject<T>(T scriptableObject, string path) where T : ScriptableObject
    {
        // Ensure the path is inside "Assets/"
        if (!path.StartsWith("Assets/"))
        {
            Debug.LogError("Invalid path! Must be inside the Assets folder.");
            return null;
        }

        // Save the ScriptableObject as an asset
        AssetDatabase.CreateAsset(scriptableObject, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        T savedAsset = AssetDatabase.LoadAssetAtPath<T>(path);

        return savedAsset;
    }
#endif

    byte GetRotation(WFCTile3d tile)
    {
        float angle = tile.transform.rotation.eulerAngles.y;
        while (angle < 0) angle += 360.0f;
        while (angle > 360) angle -= 360.0f;

        return (byte)Mathf.RoundToInt(angle / 90.0f);
    }

    public Vector3Int WorldToTilePos(Vector3 worldPos)
    {
        Vector3 localPos = transform.worldToLocalMatrix.MultiplyPoint3x4(worldPos);

        Vector3Int tilePos = Vector3Int.zero;
        tilePos.x = Mathf.FloorToInt((localPos.x - config.localBounds.min.x) / config.gridSize.x);
        tilePos.y = Mathf.FloorToInt((localPos.y - config.localBounds.min.y) / config.gridSize.y);
        tilePos.z = Mathf.FloorToInt((localPos.z - config.localBounds.min.z) / config.gridSize.z);

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
        pos.x = (x - config.mapSize.x * 0.5f) * config.gridSize.x;
        pos.y = y * config.gridSize.y;
        pos.z = (z - config.mapSize.z * 0.5f) * config.gridSize.z;

        return pos;
    }

    int TilePosToIndex(Vector3Int tilePos)
    {
        return TilePosToIndex(tilePos.x, tilePos.y, tilePos.z);
    }
    int TilePosToIndex(int x, int y, int z)
    {
        return x + y * config.mapSize.x + z * (config.mapSize.x * config.mapSize.y);
    }

    Vector3Int IndexToTilePos(int index)
    {
        int x = index % config.mapSize.x;
        int y = (index / config.mapSize.x) % config.mapSize.y;
        int z = index / (config.mapSize.x * config.mapSize.y);

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

    bool canClear => transform.childCount > 0;

    [Button("Clear"), ShowIf(nameof(canClear))]
    void ClearTilemap()
    {
        var clusters = GetComponentsInChildren<WFCClusterComponent>();
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
        if (config.tilemapData != null)
        {
            LoadTilemap();
        }
        else if (config.adjacencyData != null)
        {
            GenerateMap();
        }
    }

    bool canLoad => mode == Mode.LoadData;

    [Button("Load Tilemap"), ShowIf("canLoad")]
    void LoadTilemap()
    {
        if (config.tileset == null)
        {
            Debug.LogWarning("No tileset defined!");
            return;
        }

        ClearTilemap();

        if (config.tilemapData == null)
        {
            Debug.LogWarning("No file specified for tile data!");
            return;
        }

        // Read the file's binary content
        byte[] fileData = config.tilemapData.bytes;

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
            config.gridSize.x = reader.ReadSingle();
            config.gridSize.y = reader.ReadSingle();
            config.gridSize.z = reader.ReadSingle();

            // Read map size
            config.mapSize.x = reader.ReadInt32();
            config.mapSize.y = reader.ReadInt32();
            config.mapSize.z = reader.ReadInt32();

            // Initialize the map array with the correct size
            tilemap = new WFCTilemap(config, conflictTiles);
            SetupHandlers();

            // Read the tile data
            int tileCount = config.mapSize.x * config.mapSize.y * config.mapSize.z;
            for (int i = 0; i < tileCount; i++)
            {
                var tilePos = IndexToTilePos(i);
                tilemap.Set(tilePos, new Tile { tileId = reader.ReadByte(), rotation = reader.ReadByte() });
            }
        }

        InstantiateMap();
    }

    bool canGenerate => mode == Mode.GenerateData;
    [Button("Setup Tilemap"), ShowIf(nameof(canGenerate))]
    void SetupTilemap()
    {
        if (config == null)
        {
            Debug.LogWarning("Configuration is needed to setup map!");
            return;
        }

        ClearTilemap();
        if (config.buildNavmesh) UpdateNavMesh();
        if (!LoadWFCData()) return;

        ResetObjectPool();

        startedWFC = true;
    }

    [Button("Generate Full Map"), ShowIf(nameof(canGenerate))]
    public void GenerateMap(MapStartupFunction startupFunction = null)
    {
        SetupTilemap();
        startupFunction?.Invoke();

        switch (GenerateTilemap(Vector3Int.zero, config.mapSize))
        {
            case GenResult.Ok:
                break;
            case GenResult.Conflict:
                Debug.LogWarning("Conflict detected, halted!");
                startedWFC = false;
                break;
            case GenResult.Complete:
                startedWFC = false;
                break;
        }

        if (config.multithreaded)
        {
            thread = new WFCThread(tilemap, this);
            SetupHandlers();
            thread.Start();
        }
    }

    bool canGenerateStep => (mode == Mode.GenerateData) && (startedWFC);
    [Button("Generate Step"), ShowIf(nameof(canGenerateStep))]
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

        var err = tilemap.GenerateTile(Vector3Int.zero, config.mapSize);
        switch (err)
        {
            case GenResult.Ok:
                break;
            case GenResult.Conflict:
                Debug.LogWarning("Conflict detected, halted!");
                break;
            case GenResult.Complete:
                Debug.LogWarning("Generation completed, halted!");
                break;
            default:
                break;
        }
    }

    [Button("Debug Step"), ShowIf(nameof(canGenerateStep))]
    void DebugStep()
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

        var err = tilemap.GenerateTile(3, 0, 3, new Tile { tileId = 2, rotation = 0 });
        switch (err)
        {
            case GenResult.Ok:
                break;
            case GenResult.Conflict:
                Debug.LogWarning("Conflict detected, halted!");
                break;
            case GenResult.Complete:
                Debug.LogWarning("Generation completed, halted!");
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

    private void Debug_OnTileCreated(Vector3Int worldPos, WFCCluster cluster, Vector3Int clusterPos, Tile tile, ProbList<Tile> possibilities)
    {
        Debug.Log($"[WFC]: Create tile at ({worldPos}), cluster = {cluster.basePos}, localPos = {clusterPos}, Tile = {tile}, CurrentSet = {possibilities.ToSimpleString()}");
    }

    bool LoadWFCData()
    { 
        if (!config.Load())
        {
            return false;
        }

        // Add conflict tiles to tileset and unique tiles
        var uniqueTiles = config.GetUniqueTiles();

        List<int> conflictTilesIds = new();
        foreach (var tile in conflictTiles)
        {
            var tileId = config.tileset.GetTileIndex(tile);
            if (tileId == 255) config.tileset.Add(tile);

            // Add to unique tiles
            for (byte i = 0; i < 4; i++)
            {
                Tile t = new Tile { tileId = tileId, rotation = i };
                if (uniqueTiles.IndexOf(t) == -1)
                {
                    uniqueTiles.Add(t, 0.001f);
                }
                conflictTilesIds.Add(uniqueTiles.IndexOf(t));
            }
        }
        // Create adjacency lists for the conflict tiles
        var adjacencyInfo = config.GetAdjacencyInfo();
        for (int i = adjacencyInfo.Count; i < uniqueTiles.Count; i++) adjacencyInfo.Add(null);
        foreach (var uniqueId in conflictTilesIds)
        {
            adjacencyInfo[uniqueId] = new WFCAdjacencyData();
            adjacencyInfo[uniqueId].Set(Direction.PX, uniqueTiles);
            adjacencyInfo[uniqueId].Set(Direction.NX, uniqueTiles);
            adjacencyInfo[uniqueId].Set(Direction.PY, uniqueTiles);
            adjacencyInfo[uniqueId].Set(Direction.NY, uniqueTiles);
            adjacencyInfo[uniqueId].Set(Direction.PZ, uniqueTiles);
            adjacencyInfo[uniqueId].Set(Direction.NZ, uniqueTiles);
        }

        // Initialize the map array with the correct size
        tilemap = new WFCTilemap(config, conflictTiles);
        SetupHandlers();

        return true;
    }

#if UNITY_EDITOR
    bool isModeExtract => mode == Mode.ExtractData;
    [Button("Save Tilemap"), ShowIf(nameof(isModeExtract))]
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
            writer.Write(config.gridSize.x);
            writer.Write(config.gridSize.y);
            writer.Write(config.gridSize.z);
            // Map size
            writer.Write(config.mapSize.x);
            writer.Write(config.mapSize.y);
            writer.Write(config.mapSize.z);

            // Loop through the map array and write each tile's data
            int tileCount = config.mapSize.x * config.mapSize.y * config.mapSize.z;
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

    [Button("Save WFC Data"), ShowIf(nameof(isModeExtract))]
    void SaveWFC()
    {
        BuildTilemap();

        // Get unique tiles
        var uniqueTiles = new ProbList<Tile>();

        for (int z = 0; z < config.mapSize.z; z++)
        {
            for (int y = 0; y < config.mapSize.y; y++)
            {
                for (int x = 0; x < config.mapSize.x; x++)
                {
                    uniqueTiles.Add(tilemap.GetTile(x, y, z), 1);
                }
            }
        }

        // Build adjacency information
        var adjacencyInfo = new List<WFCAdjacencyData>(); for (int i = 0; i < uniqueTiles.Count; i++) adjacencyInfo.Add(null);
        for (int i = 0; i < adjacencyInfo.Count; i++) adjacencyInfo[i] = new WFCAdjacencyData();

        for (int z = 0; z < config.mapSize.z; z++)
        {
            for (int y = 0; y < config.mapSize.y; y++)
            {
                for (int x = 0; x < config.mapSize.x; x++)
                {
                    int uniqueId = uniqueTiles.IndexOf(tilemap.GetTile(x, y, z));

                    if (x > 0) adjacencyInfo[uniqueId].Add(Direction.NX, tilemap.GetTile(x - 1, y, z));
                    if (y > 0) adjacencyInfo[uniqueId].Add(Direction.NY, tilemap.GetTile(x, y - 1, z));
                    if (z > 0) adjacencyInfo[uniqueId].Add(Direction.NZ, tilemap.GetTile(x, y, z - 1));
                    if (x < config.mapSize.x - 1) adjacencyInfo[uniqueId].Add(Direction.PX, tilemap.GetTile(x + 1, y, z));
                    if (y < config.mapSize.y - 1) adjacencyInfo[uniqueId].Add(Direction.PY, tilemap.GetTile(x, y + 1, z));
                    if (z < config.mapSize.z - 1) adjacencyInfo[uniqueId].Add(Direction.PZ, tilemap.GetTile(x, y, z + 1));
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
            writer.Write(config.gridSize.x);
            writer.Write(config.gridSize.y);
            writer.Write(config.gridSize.z);

            writer.Write(uniqueTiles.Count);
            foreach (var ut in uniqueTiles)
            {
                writer.Write(ut.element.tileId);
                writer.Write(ut.element.rotation);
                writer.Write(ut.weight);
            }

            // Write the number of tiles (adjacency.Length)
            writer.Write(adjacencyInfo.Count);

            // For each tile in the adjacency array
            for (int i = 0; i < adjacencyInfo.Count; i++)
            {
                adjacencyInfo[i].Save(writer);
            }
        }

        Debug.Log($"Adjacency information saved to {filename}");

        AssetDatabase.Refresh();
    }
#endif

    bool isModeGenerate => mode == Mode.GenerateData;
    [Button("Update Navmesh"), ShowIf(nameof(isModeGenerate))]
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

    private void Update()
    {
        if (!Application.isPlaying) return;

        UpdateStep();
    }

    [Button("Run update"), ShowIf(nameof(isModeGenerate))]
    private void UpdateStep()
    {
        if ((isDynamic) && (dynamicCamera))
        {
            bool updated = false;

            if (thread != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Setup data required for this update tick
                thread.SetCamera(dynamicCamera);
                // Create/Remove clusters/tiles
                thread.UpdateClusters();
                thread.UpdateTiles();
                // Show debug log information
                thread.FlushLog();

                sw.Stop();
            }
            else
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                int currentDistance = Mathf.CeilToInt(Mathf.Min(2, config.maxGenerationDistance * 0.1f));
                int incDistance = currentDistance;

                while (currentDistance <= config.maxGenerationDistance)
                {
                    (Vector3[] corners, Vector3 min, Vector3 max) = WFCHelpers.CalculateFrustumCorners(
                        dynamicCamera.transform.localToWorldMatrix,
                        0.0f, 0.0f, 1.0f, 1.0f, 
                        currentDistance, 
                        dynamicCamera.fieldOfView, dynamicCamera.aspect, dynamicCamera.orthographic, dynamicCamera.orthographicSize
                    );

                    Vector3Int p1 = WorldToTilePos(min);
                    Vector3Int p2 = WorldToTilePos(max);
                    Vector3Int start = new Vector3Int(Mathf.Min(p1.x, p2.x), Mathf.Min(p1.y, p2.y), Mathf.Min(p1.z, p2.z));
                    Vector3Int end = new Vector3Int(Mathf.Max(p1.x, p2.x), Mathf.Max(p1.y, p2.y), Mathf.Max(p1.z, p2.z));
                    if (start.x < config.minMapLimit.x) start.x = config.minMapLimit.x;
                    if (start.y < config.minMapLimit.y) start.y = config.minMapLimit.y;
                    if (start.z < config.minMapLimit.z) start.z = config.minMapLimit.z;
                    if (end.x > config.maxMapLimit.x) end.x = config.maxMapLimit.x;
                    if (end.y > config.maxMapLimit.y) end.y = config.maxMapLimit.y;
                    if (end.z > config.maxMapLimit.z) end.z = config.maxMapLimit.z;

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

                        if (sw.ElapsedMilliseconds > config.maxTimePerFrameMS) break;
                    }

                    if (sw.ElapsedMilliseconds > config.maxTimePerFrameMS) break;
                }

                sw.Stop();
                debuglastGenerationTimeMS = sw.ElapsedMilliseconds;

                Vector3 localCameraPos = transform.worldToLocalMatrix.MultiplyPoint(dynamicCamera.transform.position);
                Vector3 localCameraDir = transform.worldToLocalMatrix.MultiplyVector(dynamicCamera.transform.forward); localCameraDir.y = 0; localCameraDir.Normalize();
                var activeClusters = new List<WFCCluster>(tilemap.currentClusters);
                foreach (var cluster in tilemap.currentClusters)
                {
                    // Never destroy persistent clusters (linked to events, basically)
                    if (cluster.persistent) continue;

                    // Get cluster world position
                    Vector3 clusterCenter = cluster.basePos;
                    clusterCenter.x = (clusterCenter.x + 0.5f) * config.gridSize.x * config.clusterSize.x;
                    clusterCenter.y = (clusterCenter.y + 0.5f) * config.gridSize.y * config.clusterSize.y;
                    clusterCenter.z = (clusterCenter.z + 0.5f) * config.gridSize.z * config.clusterSize.z;

                    Vector3 toClusterCenter = clusterCenter - localCameraPos;
                    float distance = toClusterCenter.magnitude;
                    toClusterCenter /= distance;
                    if (distance > config.fadeOutDistance)
                    {
                        if (Vector3.Dot(toClusterCenter, localCameraDir) < -0.25f)
                        {
                            // Remove this cluster
                            tilemap.RemoveCluster(cluster);
                        }
                    }
                }
            }

            if ((updated) && (config.buildNavmesh))
            {
                UpdateNavMesh();
            }
        }
    }

    [Button("Run update (5s)"), ShowIf(nameof(isModeGenerate))]
    void RunUpdate5s()
    {
        var prevValue = config.maxTimePerFrameMS;
        config.maxTimePerFrameMS = 5000;
        UpdateStep();
        config.maxTimePerFrameMS = prevValue;
    }


    static Vector3Int? debugHoveredTile = null;
    static List<(Vector3Int p1, Vector3Int p2)> debugPropagations = new();

#if UNITY_EDITOR
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
#endif

    internal void ObserveTile(Vector3Int worldTilePos, byte tileId, byte rotation)
    {
        tilemap.Observe(worldTilePos, tileId, rotation);
    }

    private void OnDrawGizmos()
    {
        if (hasAnyData)
        {
            // Bounds are not loaded, so we need to compute them
            config.localBounds.SetMinMax(Vector3.zero,
                                  new Vector3(config.mapSize.x * config.gridSize.x, config.gridSize.y, config.mapSize.z * config.gridSize.z));
        }

        var prevMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (drawGrid)
        {
            Gizmos.color = Color.yellow;
            DrawXZGrid(config.localBounds.min.y);
        }
        if (drawDebugWFC)
        {
            if ((isDynamic) && (dynamicCamera))
            {
                Gizmos.color = Color.green;
                Gizmos.matrix = dynamicCamera.transform.localToWorldMatrix;
                Gizmos.DrawFrustum(Vector3.zero, dynamicCamera.fieldOfView, config.maxGenerationDistance, 1.0f, 16.0f / 9.0f);

                Vector3[] corners = new Vector3[4];
                dynamicCamera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), config.maxGenerationDistance, Camera.MonoOrStereoscopicEye.Mono, corners);

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
            Vector3 gSize = config.gridSize; gSize.y *= 0.05f;
            Vector3 worldPos = new Vector3((debugHoveredTile.Value.x + 0.5f) * gSize.x, (debugHoveredTile.Value.y + 0.5f) * gSize.y * 0.5f, (debugHoveredTile.Value.z + 0.5f) * gSize.z);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(worldPos, gSize);

            foreach (var p in debugPropagations)
            {
                if (p.p1 == debugHoveredTile)
                {
                    Vector3 p1 = new Vector3((p.p1.x + 0.5f) * config.gridSize.x, p.p1.y * config.gridSize.y, (p.p1.z + 0.5f) * config.gridSize.z);
                    Vector3 p2 = new Vector3((p.p2.x + 0.5f) * config.gridSize.x, p.p2.y * config.gridSize.y, (p.p2.z + 0.5f) * config.gridSize.z);

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

    private void DrawXZGrid(float y)
    {
        Vector3 min = config.localBounds.min;
        Vector3 max = config.localBounds.max;

        for (int z = 0; z <= config.mapSize.z; z++)
        {
            Gizmos.DrawLine(new Vector3(min.x, y, min.z + config.gridSize.z * z),
                            new Vector3(max.x, y, min.z + config.gridSize.z * z));
        }
        for (int x = 0; x <= config.mapSize.x; x++)
        {
            Gizmos.DrawLine(new Vector3(min.x + config.gridSize.x * x, y, min.z),
                            new Vector3(min.x + config.gridSize.x * x, y, max.z));
        }
    }

    private void DrawGridPos(Vector3Int pos)
    {
        Vector3 p = config.localBounds.min + config.gridSize * 0.5f + new Vector3(pos.x * config.gridSize.x, pos.y * config.gridSize.y, pos.z * config.gridSize.z);

        Gizmos.DrawWireCube(p, config.gridSize);
    }

#if UNITY_EDITOR
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
                    Vector3Int clusterSizeInTiles = config.clusterSize;
                    Vector3 gSize = config.gridSize; gSize.y *= 0.05f;

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
                                    debugInfo += $"Allowed states (n = {tileInfo.probMap.Count}):\n";
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
#endif

    public void CreateTile(Vector3 localPosition, Quaternion localRotation, WFCTile3d tilePrefab, WFCCluster cluster, Action<WFCTile3d> onComplete)
    {        
        if (clusterObjects.TryGetValue(cluster, out Transform container))
        {
            var tilePos = WorldToTilePos(localPosition);

            WFCTile3d newObj = null;

            newObj = GetFromPool(tilePrefab);
            if (newObj == null)
            {
                newObj = GameObject.Instantiate(tilePrefab);
                newObj.sourcePrefabObject = tilePrefab;
            }
            newObj.name = $"Tile {tilePos.x},{tilePos.y},{tilePos.z}";
            newObj.transform.SetParent(container);
            newObj.transform.localPosition = localPosition;
            newObj.transform.localRotation = localRotation;

            onComplete?.Invoke(newObj);
        }
        else
        {
            Debug.LogWarning("Failed to create tile, cluster doesn't exist!");
            onComplete?.Invoke(null);
        }
    }

    public void DestroyTile(WFCTile3d tile)
    {
        if ((usePooling) && (poolingContainer))
        {
            AddToPool(tile);
            
            return;
        }
#if UNITY_EDITOR
        if (EditorApplication.isPlaying)
            Destroy(tile.gameObject);
        else
            DestroyImmediate(tile.gameObject);
#else
        Destroy(tile.gameObject);
#endif

        Debug.Log("Destroy tile");
    }

    public void CreateCluster(WFCCluster cluster)
    {
        // Check if this cluster is already present for warning
        if (clusterObjects.TryGetValue(cluster, out Transform container))
        {
            Debug.LogWarning("Failed to create cluster, cluster already exists!");
            return;
        }

        var go = new GameObject();
        go.name = $"Cluster {cluster.basePos.x},{cluster.basePos.y},{cluster.basePos.z}";
        go.transform.parent = transform;
        go.transform.localPosition = Vector3.zero;
        go.AddComponent<WFCClusterComponent>();

        clusterObjects.Add(cluster, go.transform);

        onNewCluster?.Invoke(cluster);
    }

    public void DestroyCluster(WFCCluster cluster)
    {
        if (!clusterObjects.TryGetValue(cluster, out Transform container))
        {
            Debug.LogWarning("Cluster already destroyed!");
            return;
        }

#if UNITY_EDITOR
        if (EditorApplication.isPlaying)
            Destroy(container.gameObject);
        else
            DestroyImmediate(container.gameObject);
#else
        Destroy(container.gameObject);
#endif
        clusterObjects.Remove(cluster);
    }


    public Transform GetClusterTransform(WFCCluster cluster)
    {
        if (clusterObjects.TryGetValue(cluster, out Transform container))
        {
            Debug.LogWarning($"Cluster not found - {cluster.basePos}!");
            return null;
        }

        return container;
    }

    void AddToPool(WFCTile3d tile)
    {
        if (objectPool == null) ResetObjectPool();

        if (tile == null) return;

        tile.gameObject.SetActive(false);
        tile.name = "Pooled Tile";
        tile.transform.SetParent(poolingContainer);

        if (!objectPool.TryGetValue(tile.sourcePrefabObject, out var l))
        {
            l = new List<WFCTile3d>();
            objectPool.Add(tile.sourcePrefabObject, l);
        }
        l.Add(tile);
    }

    WFCTile3d GetFromPool(WFCTile3d tilePrefab)
    {
        if (objectPool == null) return null;
        if (objectPool.TryGetValue(tilePrefab, out var l))
        {
            if (l.Count > 0)
            {
                var newTile = l.PopFirst();
                newTile.gameObject.SetActive(true);
                return newTile;
            }
        }
        return null;
    }

    void ResetObjectPool()
    {
        if ((objectPool == null) && (usePooling) && (poolingContainer))
        {
            objectPool = new();
            var pObjects = poolingContainer.GetComponentsInChildren<WFCTile3d>(true);
            foreach (var p in pObjects)
            {
                AddToPool(p);
            }
        }
    }

    void SetupHandlers()
    {
        if ((config.multithreaded) && (mode == Mode.GenerateData) && (thread != null))
        {
            tilemap.createTileCallback = thread.CreateTile;
            tilemap.destroyTileCallback = thread.DestroyTile;
            tilemap.createClusterCallback = thread.CreateCluster;
            tilemap.destroyClusterCallback = thread.DestroyCluster;
        }
        else
        {
            tilemap.createTileCallback = CreateTile;
            tilemap.destroyTileCallback = DestroyTile;
            tilemap.createClusterCallback = CreateCluster;
            tilemap.destroyClusterCallback = DestroyCluster;
        }
    }

    private void OnApplicationQuit()
    {
        if (thread != null)
        {
            thread.SetExit(true, true);
            thread = null;
        }
    }
}
