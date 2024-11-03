using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.WSA;
using UnityMeshSimplifier.Internal;
using static UnityEditor.PlayerSettings;
using static WFCTileData;
using static WFCTileData.Cluster;

[System.Serializable]
public struct Tile
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

    public override string ToString()
    {
        return $"{{{tileId}, {rotation}}}";
    }
}

public enum GenResult { Ok, Conflict, Complete };
public enum Direction { PX = 0, PY = 1, PZ = 2, NX = 3, NY = 4, NZ = 5 };

public class WFCData
{
    public WFCData()
    {
        adjacency = new ProbList<Tile>[System.Enum.GetNames(typeof(Direction)).Length];
        for (int i = 0; i < System.Enum.GetNames(typeof(Direction)).Length; i++) adjacency[i] = new();
    }

    ProbList<Tile>[] adjacency;

    public void Add(Direction direction, Tile tile)
    {
        adjacency[(int)direction].Add(tile, 1);
    }

    internal void Save(BinaryWriter writer)
    {
        // For each direction (PX, PY, PZ, NX, NY, NZ)
        for (int dir = 0; dir < System.Enum.GetNames(typeof(Direction)).Length; dir++)
        {
            ProbList<Tile> tileProbs = adjacency[dir];

            // Write the number of TileProbability entries for this direction
            writer.Write(tileProbs.Count);

            // Write each TileProbability's tileId and count
            foreach (var tileProb in tileProbs)
            {
                writer.Write(tileProb.element.tileId);     // Write the tileId
                writer.Write(tileProb.element.rotation);   // Write the rotation
                writer.Write(tileProb.weight);              // Write the count
            }
        }
    }

    internal void Load(BinaryReader reader)
    {
        // For each direction (PX, PY, PZ, NX, NY, NZ)
        for (int dir = 0; dir < System.Enum.GetNames(typeof(Direction)).Length; dir++)
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
                float weight = reader.ReadSingle();  // Read the count

                // Add the TileProbability to the list for this direction
                adjacency[dir].Add(tile, weight);
            }
        }
    }

    internal ProbList<Tile> Get(Direction direction)
    {
        return adjacency[(int)direction];
    }
}

public class WFCTileData
{
    public delegate void OnTileSelected(Vector3Int tileSelected);
    public event OnTileSelected onTileSelected;
    public delegate void OnCompleted();
    public event OnCompleted onCompleted;
    public delegate void OnTileCreate(Vector3Int worldPos, Cluster cluster, Vector3Int clusterPos, Tile tile, ProbList<Tile> possibilities);
    public event OnTileCreate onTileCreate;
    public delegate void OnPropagate(Vector3Int prevWorldPos, Vector3Int nextWorldPos, ProbList<Tile> allowedTiles, int depth);
    public event OnPropagate onPropagate;
    public delegate void OnConflict(Vector3Int worldPos);
    public event OnConflict onConflict;

    public class WFCTile
    {
        public Tile             tile;
        public WFCTile3d        tile3d;
        public ProbList<Tile>   probMap;
    }

    static Dictionary<Tile, List<WFCTile3d>> tilePool = new();

    public class Cluster
    {
        public  Vector3Int  basePos;
        public  WFCTile[]   map;
        public  Transform   container;
        public  Vector3Int  clusterSize;

        public Cluster(Vector3Int basePos, ProbList<Tile> uniqueTiles, Vector3Int clusterSize, Transform container)
        {
            this.basePos = basePos;
            this.clusterSize = clusterSize;

            map = new WFCTile[clusterSize.x * clusterSize.y * clusterSize.z];
            // Initialize the cluster with default WFCTile values
            for (int i = 0; i < map.Length; i++)
            {
                map[i] = new WFCTile
                {
                    tile = new Tile() { tileId = 0 },
                    probMap = (uniqueTiles == null) ? (null) : new ProbList<Tile>(uniqueTiles)
                };
            }

            if (container != null)
            {
                var go = new GameObject();
                go.name = $"Cluster {basePos.x},{basePos.y},{basePos.z}";
                go.transform.parent = container;
                go.transform.localPosition = Vector3.zero;
                go.AddComponent<WFCCluster>();

                this.container = go.transform;
            }
        }

        public Tile GetTile(int index) => map[index].tile;
        public void SetTile(int index, Tile t) { map[index].tile = t; }
        public void SetProb(int index, ProbList<Tile> probMap) { map[index].probMap = probMap; }
        public WFCTile GetWFCTile(int index) => map[index];
        public WFCTile GetWFCTile(int x, int y, int z) => map[TilePosToClusterIndex(x, y, z)];
        public int TilePosToClusterIndex(int x, int y, int z)
        {
            return x + y * clusterSize.x + z * (clusterSize.x * clusterSize.y);
        }

        internal void SetTile3d(int localClusterIndex, WFCTile3d obj, bool usePool, Transform poolContainer)
        {
            var tileObj = map[localClusterIndex];
            var prevObj = tileObj.tile3d;
            if (prevObj)
            {
                if (usePool)
                {
                    SendTileToPool(tileObj, poolContainer);
                }
                else
                { 
                    GameObject.Destroy(prevObj.gameObject);
                }
            }
            
            tileObj.tile3d = obj;
        }

        internal void SendTileToPool(WFCTile tile, Transform poolContainer)
        {
            if (tile.tile3d != null)
            {
                if (!tilePool.TryGetValue(tile.tile, out var tileList))
                {
                    tileList = new();
                    tilePool.Add(tile.tile, tileList);
                }
                tileList.Add(tile.tile3d);
                tile.tile3d.transform.SetParent(poolContainer);
                tile.tile3d.gameObject.SetActive(false);
                tile.tile3d = null;
            }
        }

        internal void Clear(bool usePool, Transform poolContainer)
        {
            if (usePool)
            {
                foreach (var tile in map)
                {
                    SendTileToPool(tile, poolContainer);
                }
            }

            if (container)
            {
                container.gameObject.Delete();
            }
        }

        public delegate void SolveConflictFunction(Vector3Int pos);
        public delegate void PropagateCallbackFunction(Vector3Int prevWorldPos, Vector3Int nextWorldPos, ProbList<Tile> allowedTiles, int depth);

        // This propagates only within the cluster, so x, y and z are in local cluster coordinates
        internal void Propagate(int x, int y, int z, ProbList<Tile> tiles, int depth, WFCTileData mainData, SolveConflictFunction solveConflictFunction, PropagateCallbackFunction propagateCallbackFunction)
        {
            void PropagateDir(Direction direction, int x, int y, int z, int dx, int dy, int dz)
            {
                int newX = x + dx;
                int newY = y + dy;
                int newZ = z + dz;

                ProbList<Tile> allowed = new();
                foreach (var tile in tiles)
                {
                    int uniqueId = mainData.uniqueTiles.IndexOf(tile.element);
                    if (uniqueId != -1) allowed.Add(mainData.adjacencyInfo[uniqueId].Get(direction));
                }
                // Should check if the the cluster for this coordinate exists
                // Don't propagate to clusters that don't exist, if they are generated,
                // then make them work
                propagateCallbackFunction(new Vector3Int(x, y, z), new Vector3Int(newX, newY, newZ), allowed, depth - 1);

                Propagate(newX, newY, newZ, allowed, depth - 1, mainData, solveConflictFunction, propagateCallbackFunction);
            }

            if (depth == 0) return;

            bool propagateFurther = false;

            var t = GetWFCTile(x, y, z);

            var pm = t.probMap;
            if (pm != null)
            {                
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
                        pm.Set(tc.element, Mathf.Min(tc.weight, pm.GetWeight(tc.element)));
                    }
                }

                if (propagateFurther)
                {
                    pm.Cleanup();
                }

                if (pm.Count == 0)
                {
                    // this isn't collapsed, but there's no options, log conflict (for now)
                    t.probMap = null;

                    Vector3Int worldTilePos = new Vector3Int(basePos.x * clusterSize.x + x, basePos.y * clusterSize.y + y, basePos.z * clusterSize.z + z);

                    solveConflictFunction(worldTilePos);
                }
            }

            if (propagateFurther)
            {
                // Get all tiles allowed to the right (X+), and propagate that
                if (x < clusterSize.x - 1) PropagateDir(Direction.PX, x, y, z, 1, 0, 0);
                // Left (X-)
                if (x > 0) PropagateDir(Direction.NX, x, y, z, -1, 0, 0);
                // Forward (Z+)
                if (z < clusterSize.z - 1) PropagateDir(Direction.PZ, x, y, z, 0, 0, 1);
                // Back (Z-)
                if (z > 0) PropagateDir(Direction.NZ, x, y, z, 0, 0, -1);

                // Again, no propagation on Y (current support is lacking)
            }
        }
    }

    Dictionary<Vector3Int, Cluster> clusters = new Dictionary<Vector3Int, Cluster>();

    WFCTileset          tileset;
    Vector3             gridSize;
    Vector3Int          minMapLimits = new Vector3Int(-int.MaxValue, -int.MaxValue, -int.MaxValue);
    Vector3Int          maxMapLimits = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
    public Vector3Int   clusterSize { get; private set; }
    int                 maxDepth = 25;
    bool                clusterObjectEnable = true;
    ProbList<Tile>      uniqueTiles;
    WFCData[]           adjacencyInfo;
    List<WFCTile3d>     conflictTiles;
    Transform           container;  
    bool                poolEnable;
    Transform           poolContainer;

    public WFCTileData(Vector3Int initialSize, Vector3 gridSize, WFCTileset tileset, List<WFCTile3d> conflictTiles, Transform container)
    {
        this.tileset = tileset;
        this.clusterSize = new Vector3Int(Mathf.Min(8, initialSize.x), Mathf.Min(8, initialSize.y), Mathf.Min(8, initialSize.z)); ;
        this.gridSize = gridSize;
        this.conflictTiles = conflictTiles;
        this.container = container;
    }

    public void SetUniqueTiles(ProbList<Tile> uniqueTiles)
    {
        this.uniqueTiles = uniqueTiles;
    }
    public void SetAdjacencyInfo(WFCData[] adjacencyInfo)
    {
        this.adjacencyInfo = adjacencyInfo;
    }

    public void SetLimits(Vector3Int minMapLimits, Vector3Int maxMapLimits)
    {
        this.minMapLimits = minMapLimits;
        this.maxMapLimits = maxMapLimits;
    }

    public void SetMaxDepth(int maxDepth)
    {
        this.maxDepth = maxDepth;
    }

    public void DisableClusterObject()
    {
        clusterObjectEnable = false;
    }

    public void SetPooling(bool enable, Transform container = null)
    {
        this.poolEnable = enable;
        this.poolContainer = container;
    }

    // Create or retrieve the cluster
    private Cluster GetOrCreateCluster(Vector3Int clusterPos)
    {
        if (!clusters.ContainsKey(clusterPos))
        {
            var cluster = new Cluster(clusterPos, uniqueTiles, clusterSize, (clusterObjectEnable) ? (container) : (null));
            clusters[clusterPos] = cluster;

            // Initialize this cluster based on the data around it - THIS DOESN'T WORK FOR 3D, only for 2D (XZ) - Future work maybe
            // Get corner pos
            Vector3Int cornerPos = new Vector3Int(clusterPos.x * clusterSize.x, clusterPos.y * clusterSize.y, clusterPos.z * clusterSize.z);

            // Find clusters to the north, south, east, west, up and down, if any
            var clusterNorth = GetCluster(new Vector3Int(clusterPos.x, clusterPos.y, clusterPos.z + 1));
            var clusterSouth = GetCluster(new Vector3Int(clusterPos.x, clusterPos.y, clusterPos.z - 1));
            var clusterEast = GetCluster(new Vector3Int(clusterPos.x + 1, clusterPos.y, clusterPos.z));
            var clusterWest = GetCluster(new Vector3Int(clusterPos.x - 1, clusterPos.y, clusterPos.z));

            PropagateCallbackFunction propagateCallbackFunction = (Vector3Int prevWorldPos, Vector3Int nextWorldPos, ProbList<Tile> allowedTiles, int depth) =>
            {
                onPropagate?.Invoke(prevWorldPos, nextWorldPos, allowedTiles, depth - 1);
            };

            SolveConflictFunction solveConflictFunction = (Vector3Int worldTilePos) =>
            {
                onConflict?.Invoke(worldTilePos);

                if ((conflictTiles != null) && (conflictTiles.Count > 0))
                {
                    // Resolve this one - set map to one of the conflict tiles
                    WFCTile3d tile = conflictTiles.Random();
                    int localClusterIndex = TilePosToClusterIndex(WorldToClusterPosIndex(worldTilePos));
                    cluster.SetTile(localClusterIndex, new Tile() { tileId = tileset.GetTileIndex(tile), rotation = (byte)UnityEngine.Random.Range(0, 3) });
                    CreateTile(worldTilePos.x, worldTilePos.y, worldTilePos.z);
                }
            };

            if (clusterNorth != null)
            {
                for (int i = 0; i < clusterSize.x; i++)
                {
                    var tile = clusterNorth.GetWFCTile(i, 0, 0);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(i, 0, clusterSize.z - 1, pm, maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
            if (clusterSouth != null)
            {
                for (int i = 0; i < clusterSize.x; i++)
                {
                    var tile = clusterSouth.GetWFCTile(i, 0, clusterSize.z - 1);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(i, 0, 0, pm, maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
            if (clusterEast != null)
            {
                for (int i = 0; i < clusterSize.z; i++)
                {
                    var tile = clusterEast.GetWFCTile(0, 0, i);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(clusterSize.x - 1, 0, i, pm, maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
            if (clusterWest != null)
            {
                for (int i = 0; i < clusterSize.z; i++)
                {
                    var tile = clusterWest.GetWFCTile(clusterSize.x - 1, 0, i);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(0, 0, i, pm, maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
        }

        return clusters[clusterPos];
    }

    private Cluster GetCluster(Vector3Int clusterPos)
    {
        if (clusters.TryGetValue(clusterPos, out Cluster cluster))
        {
            return cluster;
        }

        return null;
    }

    public void RemoveCluster(Cluster cluster)
    {
        cluster.Clear(poolEnable, poolContainer);
        clusters.Remove(cluster.basePos);
    }


    // Get the tile from the correct cluster
    private WFCTile GetTileFromCluster(Vector3Int worldPos)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(worldPos);

        int index = TilePosToClusterIndex(localPos);
        return cluster.GetWFCTile(index);
    }

    // Set a tile in the correct cluster
    private void SetTileInCluster(Vector3Int worldPos, Tile tile)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(worldPos);

        int index = TilePosToClusterIndex(localPos);
        cluster.SetTile(index, tile);
    }

    // Convert world position to cluster position
    private (Cluster, Vector3Int) WorldToClusterPos(Vector3Int worldPos)
    {
        return WorldToClusterPos(worldPos.x, worldPos.y, worldPos.z);
    }

    private (Cluster, Vector3Int) WorldToClusterPos(int x, int y, int z)
    {
        var clusterPos = new Vector3Int(Mathf.FloorToInt(x / (float)clusterSize.x), Mathf.FloorToInt(y / (float)clusterSize.y), Mathf.FloorToInt(z / (float)clusterSize.z));
        var localPos = new Vector3Int(Mod(x, clusterSize.x), Mod(y, clusterSize.y), Mod(z, clusterSize.z));

        return (GetOrCreateCluster(clusterPos), localPos);
    }

    private Vector3Int WorldToClusterPosIndex(Vector3Int worldPos)
    {
        return WorldToClusterPosIndex(worldPos.x, worldPos.y, worldPos.z);
    }
    private Vector3Int WorldToClusterPosIndex(int x, int y, int z)
    {
        return new Vector3Int(Mod(x, clusterSize.x), Mod(y, clusterSize.y), Mod(z, clusterSize.z));
    }

    private int Mod(int a, int b)
    {
        return (a % b + b) % b;
    }

    public int TilePosToClusterIndex(Vector3Int tilePos)
    {
        return TilePosToClusterIndex(tilePos.x, tilePos.y, tilePos.z);
    }

    public int TilePosToClusterIndex(int x, int y, int z)
    {
        return x + y * clusterSize.x + z * (clusterSize.x * clusterSize.y);
    }

    Vector3Int IndexToTilePos(int index, int clusterSize)
    {
        int x = index % clusterSize;
        int y = (index / clusterSize) % clusterSize;
        int z = index / (clusterSize * clusterSize);

        return new Vector3Int(x, y, z);
    }

    internal void DestroyTileObject(Vector3Int worldPos)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(worldPos);

        int index = TilePosToClusterIndex(localPos);

        cluster.SetTile3d(index, null, poolEnable, poolContainer);
    }

    internal void CreateTile(int x, int y, int z)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var tile = cluster.GetTile(localClusterIndex);
        if (tile.tileId == 0)
        {
            cluster.SetTile3d(localClusterIndex, null, poolEnable, poolContainer);
            return;
        }

        var parent = cluster.container;
        if (parent == null) parent = container;
        var o = GameObject.Instantiate(tileset.GetTile(tile.tileId), parent);
        o.transform.position = GetWorldPos(x, y, z, parent.localToWorldMatrix);
        o.transform.localRotation = Quaternion.Euler(0, 90 * tile.rotation, 0);

        cluster.SetTile3d(localClusterIndex, o, poolEnable, poolContainer);
    }

    public Vector3 GetWorldPos(Vector3Int tilePos, Matrix4x4 localToWorldMatrix)
    {
        return GetWorldPos(tilePos.x, tilePos.y, tilePos.z, localToWorldMatrix);
    }
    public Vector3 GetWorldPos(int x, int y, int z, Matrix4x4 localToWorldMatrix)
    {
        return localToWorldMatrix.MultiplyPoint3x4(GetLocalPos(x, y, z));
    }
    public Vector3 GetLocalPos(int x, int y, int z)
    {
        Vector3 pos = Vector3.zero;
        pos.x = (x + 0.5f) * gridSize.x;
        pos.y = y * gridSize.y;
        pos.z = (z + 0.5f) * gridSize.z;

        return pos;
    }

    internal Tile GetRandomTileFor(Vector3Int worldPos)
    {
        return GetRandomTileFor(worldPos.x, worldPos.y, worldPos.z);
    }

    internal Tile GetRandomTileFor(int x, int y, int z)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var tile = cluster.GetWFCTile(localClusterIndex);

        return tile.probMap.Get();
    }

    internal GenResult GenerateTile(Vector3 cameraPos, Quaternion cameraDir, float fov, float aspect, float near, float far)
    {
        return GenerateTile(() => GetLowestEntropyIndex(cameraPos, cameraDir, fov, aspect, near, far));
    }

    internal GenResult GenerateTile(Vector3Int startPos, Vector3Int size)
    {
        return GenerateTile(() => GetLowestEntropyIndex(startPos, size));
    }

    private GenResult GenerateTile(Func<Vector3Int?> getLowestEntropyIndexFunction)
    {
        GenResult ret = GenResult.Ok;

        Vector3Int? mapCoord = getLowestEntropyIndexFunction();
        if (mapCoord.HasValue)
        {
            onTileSelected?.Invoke(mapCoord.Value);
            ret = GenerateTile(mapCoord.Value);
        }
        else
        {
            onCompleted?.Invoke();
            ret = GenResult.Complete;
        }

        return ret;
    }

    public GenResult GenerateTile(Vector3Int worldPos)
    {
        return GenerateTile(worldPos.x, worldPos.y, worldPos.z);
    }

    public GenResult GenerateTile(int x, int y, int z)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var t = cluster.GetWFCTile(localClusterIndex);

        Tile newTile = t.probMap.Get();
        if (newTile == null) return GenResult.Ok;

        onTileCreate?.Invoke(new Vector3Int(x, y, z), cluster, localPos, newTile, t.probMap);

        // Observe this map position
        cluster.SetTile(localClusterIndex, newTile);

        // Remove this, no need to keep the probability list
        cluster.SetProb(localClusterIndex, null);

        // Now propagate
        var ret = Propagate(x, y, z, new ProbList<Tile>(newTile), true, maxDepth);

        CreateTile(x, y, z);

        return ret;
    }

    private GenResult Propagate(int x, int y, int z, ProbList<Tile> tiles, bool force, int depth)
    {
        GenResult PropagateDir(Direction direction, int x, int y, int z, int dx, int dy, int dz)
        {
            int newX = x + dx;
            int newY = y + dy;
            int newZ = z + dz;

            ProbList<Tile> allowed = new();
            foreach (var tile in tiles)
            {
                int uniqueId = uniqueTiles.IndexOf(tile.element);
                allowed.Add(adjacencyInfo[uniqueId].Get(direction));
            }
            // Should check if the the cluster for this coordinate exists
            // Don't propagate to clusters that don't exist, if they are generated,
            // then make them work
            onPropagate?.Invoke(new Vector3Int(x, y, z), new Vector3Int(newX, newY, newZ), allowed, depth - 1);

            return Propagate(newX, newY, newZ, allowed, false, depth - 1);
        }

        GenResult ret = GenResult.Ok;
        if (depth == 0) return ret;

        bool propagateFurther = false;

        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var t = cluster.GetWFCTile(localClusterIndex);

        var pm = t.probMap;
        if (pm != null)
        {
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
                    pm.Set(tc.element, Mathf.Min(tc.weight, pm.GetWeight(tc.element)));
                }
            }

            if (propagateFurther)
            {
                pm.Cleanup();
            }

            if (pm.Count == 0)
            {
                // this isn't collapsed, but there's no options, log conflict (for now)
                t.probMap = null;

                onConflict?.Invoke(new Vector3Int(x, y, z));

                if ((conflictTiles != null) && (conflictTiles.Count > 0))
                {
                    // Resolve this one - set map to one of the conflict tiles
                    WFCTile3d tile = conflictTiles.Random();
                    cluster.SetTile(localClusterIndex, new Tile() { tileId = tileset.GetTileIndex(tile), rotation = (byte)UnityEngine.Random.Range(0, 3) });
                    CreateTile(x, y, z);
                }
                else
                {
                    Debug.LogWarning("Conflict found in propagation!");
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
            if (((x + 1) < maxMapLimits.x) && (PropagateDir(Direction.PX, x, y, z, 1, 0, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Left (X-)
            if (((x - 1) > minMapLimits.x) && (PropagateDir(Direction.NX, x, y, z, -1, 0, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Forward (Z+)
            if (((z + 1) < maxMapLimits.z) && (PropagateDir(Direction.PZ, x, y, z, 0, 0, 1) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Back (Z-)
            if (((z - 1) > minMapLimits.z) && (PropagateDir(Direction.NZ, x, y, z, 0, 0, -1) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Up (Y+)
            if (((y + 1) < maxMapLimits.y) && (PropagateDir(Direction.PY, x, y, z, 0, 1, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Down (Y-)
            if (((y - 1) > minMapLimits.y) && (PropagateDir(Direction.NY, x, y, z, 0, -1, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
        }

        return ret;
    }

    Vector3Int? GetLowestEntropyIndex(Vector3 cameraPos, Quaternion cameraDir, float fov, float aspect, float near, float far)
    {
        Vector3 TileToWorldPos(Vector3Int tilePos)
        {
            Vector3 localPos = new Vector3(
                tilePos.x * gridSize.x,
                tilePos.y * gridSize.y,
                tilePos.z * gridSize.z
            );

            return container.TransformPoint(localPos);
        }

        static Vector3[] GetFrustumCorners(Vector3 cameraPos, Quaternion cameraDir, float fov, float aspect, float near, float far)
        {
            // Calculate the height and width of the near and far planes
            float halfFov = Mathf.Deg2Rad * fov * 0.5f;
            float nearHeight = 2.0f * Mathf.Tan(halfFov) * near;
            float nearWidth = nearHeight * aspect;
            float farHeight = 2.0f * Mathf.Tan(halfFov) * far;
            float farWidth = farHeight * aspect;

            // Frustum corners in camera space
            Vector3[] frustumCorners = new Vector3[8];

            // Near plane corners (relative to camera position)
            frustumCorners[0] = new Vector3(-nearWidth * 0.5f, -nearHeight * 0.5f, near);  // Bottom-left
            frustumCorners[1] = new Vector3(nearWidth * 0.5f, -nearHeight * 0.5f, near);   // Bottom-right
            frustumCorners[2] = new Vector3(nearWidth * 0.5f, nearHeight * 0.5f, near);    // Top-right
            frustumCorners[3] = new Vector3(-nearWidth * 0.5f, nearHeight * 0.5f, near);   // Top-left

            // Far plane corners (relative to camera position)
            frustumCorners[4] = new Vector3(-farWidth * 0.5f, -farHeight * 0.5f, far);     // Bottom-left
            frustumCorners[5] = new Vector3(farWidth * 0.5f, -farHeight * 0.5f, far);      // Bottom-right
            frustumCorners[6] = new Vector3(farWidth * 0.5f, farHeight * 0.5f, far);       // Top-right
            frustumCorners[7] = new Vector3(-farWidth * 0.5f, farHeight * 0.5f, far);      // Top-left

            // Convert from camera space to world space
            for (int i = 0; i < frustumCorners.Length; i++)
            {
                frustumCorners[i] = cameraDir * frustumCorners[i];  // Apply camera rotation
                frustumCorners[i] += cameraPos;  // Translate to camera position in world space
            }

            return frustumCorners;
        }

        Vector3Int WorldToTilePos(Vector3 worldPos)
        {
            Vector3 localPos = container.worldToLocalMatrix.MultiplyPoint3x4(worldPos);

            Vector3Int tilePos = Vector3Int.zero;
            tilePos.x = Mathf.FloorToInt(localPos.x / gridSize.x);
            tilePos.y = Mathf.FloorToInt(localPos.y / gridSize.y);
            tilePos.z = Mathf.FloorToInt(localPos.z / gridSize.z);

            return tilePos;
        }

        void GetTileBoundsFromFrustum(Vector3 cameraPos, Quaternion cameraDir, float fov, float aspect, float near, float far, out Vector3Int minTile, out Vector3Int maxTile)
        {
            // Get frustum corners
            Vector3[] frustumCorners = GetFrustumCorners(cameraPos, cameraDir, fov, aspect, near, far);

            // Initialize min and max tile positions
            minTile = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            maxTile = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

            // Iterate over each frustum corner
            foreach (var corner in frustumCorners)
            {
                // Convert world-space corner to tile coordinates
                Vector3Int tileCoord = WorldToTilePos(corner);

                // Update min and max bounds
                minTile = Vector3Int.Min(minTile, tileCoord);
                maxTile = Vector3Int.Max(maxTile, tileCoord);
            }
        }

        Plane[] CalculateFrustumPlanes(Vector3 cameraPos, Quaternion cameraDir, float fov, float aspect, float near, float far)
        {
            // Step 1: Create the perspective projection matrix
            Matrix4x4 projectionMatrix = Matrix4x4.Perspective(fov, aspect, near, far);

            // Step 2: Create the view matrix by inverting the camera's transformation matrix
            Matrix4x4 viewMatrix = Matrix4x4.TRS(cameraPos, cameraDir, Vector3.one).inverse;

            // Step 3: Combine the projection and view matrices into a single matrix
            Matrix4x4 viewProjectionMatrix = projectionMatrix * viewMatrix;

            // Step 4: Calculate frustum planes from the view-projection matrix
            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(viewProjectionMatrix);

            return frustumPlanes;
        }

        List<Vector3Int> possibilities = new List<Vector3Int>();
        int minOptions = int.MaxValue;

        // Get frustum planes
        Plane[] frustumPlanes = CalculateFrustumPlanes(cameraPos, cameraDir, fov, aspect, near, far);

        // Determine the bounding box of tiles the frustum could intersect
        Vector3Int minTile, maxTile;
        GetTileBoundsFromFrustum(cameraPos, cameraDir, fov, aspect, near, far, out minTile, out maxTile);

        // Iterate over the tile coordinates in the potential frustum bounds
        for (int z = minTile.z; z <= maxTile.z; z++)
        {
            for (int y = minTile.y; y <= maxTile.y; y++)
            {
                for (int x = minTile.x; x <= maxTile.x; x++)
                {
                    Vector3Int tilePos = new Vector3Int(x, y, z);
                    Vector3 worldPos = TileToWorldPos(tilePos);

                    // Check if the tile's center point is inside the frustum
                    if (GeometryUtility.TestPlanesAABB(frustumPlanes, new Bounds(worldPos, gridSize)))
                    {
                        // This tile is inside the frustum, so check its entropy
                        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

                        int localClusterIndex = TilePosToClusterIndex(localPos);

                        var tile = cluster.GetWFCTile(localClusterIndex);

                        if (tile.probMap == null) continue;

                        if (tile.probMap.Count < minOptions)
                        {
                            possibilities.Clear();
                            possibilities.Add(new Vector3Int(x, y, z));
                            minOptions = tile.probMap.Count;
                        }
                        else if (tile.probMap.Count == minOptions)
                        {
                            possibilities.Add(new Vector3Int(x, y, z));
                        }
                    }
                }
            }
        }

        return (possibilities.Count > 0) ? (possibilities.Random()) : null;
    }

    Vector3Int? GetLowestEntropyIndex(Vector3Int startPos, Vector3Int size)
    {
        List<Vector3Int>    possibilities = new List<Vector3Int>();
        int                 minOptions = int.MaxValue;

        for (int z = startPos.z; z < startPos.z + size.z; z++)
        {
            for (int y = startPos.y; y < startPos.y + size.y; y++)
            {
                for (int x = startPos.x; x < startPos.x + size.x; x++)
                {
                    (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

                    int localClusterIndex = TilePosToClusterIndex(localPos);

                    var tile = cluster.GetWFCTile(localClusterIndex);

                    if (tile.probMap == null) continue;

                    if (tile.probMap.Count < minOptions)
                    {
                        possibilities.Clear();
                        possibilities.Add(new Vector3Int(x, y, z));
                        minOptions = tile.probMap.Count;
                    }
                    else if (tile.probMap.Count == minOptions)
                    {
                        possibilities.Add(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        return (possibilities.Count > 0) ? (possibilities.Random()) : null;
    }

    internal void Set(Vector3Int worldPos, Tile t)
    {
        Set(worldPos.x, worldPos.y, worldPos.z, t);
    }

    internal void Set(int x, int y, int z, Tile t)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        cluster.SetTile(localClusterIndex, t);
    }

    internal Tile GetTile(Vector3Int tilePos)
    {
        return GetTile(tilePos.x, tilePos.y, tilePos.z);
    }

    internal WFCTile GetWFCTile(Vector3Int tilePos)
    {
        return GetWFCTile(tilePos.x, tilePos.y, tilePos.z);
    }

    internal Tile GetTile(int x, int y, int z)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        return cluster.GetTile(localClusterIndex);
    }
    internal WFCTile GetWFCTile(int x, int y, int z)
    {
        (Cluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        return cluster.GetWFCTile(localClusterIndex);
    }

    internal List<Cluster> currentClusters => new List<Cluster>(clusters.Values);

    public Vector3 GetClusterWorldSize()
    {
        return new Vector3(clusterSize.x * gridSize.x, clusterSize.y * gridSize.y, clusterSize.x * gridSize.z);
    }
}
