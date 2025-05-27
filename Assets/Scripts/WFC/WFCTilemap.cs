using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UC;

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

public class WFCAdjacencyData
{
    public WFCAdjacencyData()
    {
        adjacency = new ProbList<Tile>[System.Enum.GetNames(typeof(Direction)).Length];
        for (int i = 0; i < System.Enum.GetNames(typeof(Direction)).Length; i++) adjacency[i] = new();
    }

    ProbList<Tile>[] adjacency;

    public void Add(Direction direction, Tile tile)
    {
        adjacency[(int)direction].Add(tile, 1);
    }

    public void Set(Direction direction, ProbList<Tile> tiles)
    {
        adjacency[(int)direction] = new(tiles);
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

public class WFCTilemap
{
    // Debug callbacks
    public delegate void OnTileSelected(Vector3Int tileSelected);
    public delegate void OnCompleted();
    public delegate void OnTileCreate(Vector3Int worldPos, WFCCluster cluster, Vector3Int clusterPos, Tile tile, ProbList<Tile> possibilities);
    public delegate void OnPropagate(Vector3Int prevWorldPos, Vector3Int nextWorldPos, ProbList<Tile> allowedTiles, int depth);
    public delegate void OnConflict(Vector3Int worldPos);

    public event OnTileSelected onTileSelected;
    public event OnCompleted onCompleted;
    public event OnTileCreate onTileCreate;
    public event OnPropagate onPropagate;
    public event OnConflict onConflict;

    // Actual link between C# and Unity - Called when we need to create objects on the Unity side
    public delegate void CreateTileCallback(Vector3 localPosition, Quaternion localRotation, WFCTile3d tilePrefab, WFCCluster cluster, Action<WFCTile3d> completeCallback);
    public delegate void DestroyTileCallback(WFCTile3d tile);
    public delegate void CreateClusterCallback(WFCCluster cluster);
    public delegate void DestroyClusterCallback(WFCCluster cluster);

    public CreateTileCallback       createTileCallback;
    public DestroyTileCallback      destroyTileCallback;
    public CreateClusterCallback    createClusterCallback;
    public CreateClusterCallback    destroyClusterCallback;

    WFCTilemapConfig                    _config;
    List<WFCTile3d>                     conflictTiles;
    Dictionary<Vector3Int, WFCCluster>  clusters = new Dictionary<Vector3Int, WFCCluster>();
    System.Random                       randomGenerator = new System.Random();

    public WFCTilemapConfig             config => _config;

    public WFCTilemap(WFCTilemapConfig config, List<WFCTile3d> conflictTiles)
    {
        if ((config.clusterSize.x > config.mapSize.x) ||
            (config.clusterSize.y > config.mapSize.y) ||
            (config.clusterSize.z > config.mapSize.z))
        {
            Debug.LogWarning($"Possible problem: {config.clusterSize} > {config.mapSize}");
        }
        this._config = config;
        this.conflictTiles = conflictTiles;
    }

    // Create or retrieve the cluster
    private WFCCluster GetOrCreateCluster(Vector3Int clusterPos)
    {
        if (!clusters.ContainsKey(clusterPos))
        {
            var cluster = new WFCCluster(clusterPos, this);
            clusters[clusterPos] = cluster;

            // Initialize this cluster based on the data around it - THIS DOESN'T WORK FOR 3D, only for 2D (XZ) - Future work maybe
            var clusterSize = _config.clusterSize;
            // Get corner pos
            Vector3Int cornerPos = new Vector3Int(clusterPos.x * clusterSize.x, clusterPos.y * clusterSize.y, clusterPos.z * clusterSize.z);

            // Find clusters to the north, south, east, west, up and down, if any
            var clusterNorth = GetCluster(new Vector3Int(clusterPos.x, clusterPos.y, clusterPos.z + 1));
            var clusterSouth = GetCluster(new Vector3Int(clusterPos.x, clusterPos.y, clusterPos.z - 1));
            var clusterEast = GetCluster(new Vector3Int(clusterPos.x + 1, clusterPos.y, clusterPos.z));
            var clusterWest = GetCluster(new Vector3Int(clusterPos.x - 1, clusterPos.y, clusterPos.z));

            WFCCluster.PropagateCallbackFunction propagateCallbackFunction = (Vector3Int prevWorldPos, Vector3Int nextWorldPos, ProbList<Tile> allowedTiles, int depth) =>
            {
                onPropagate?.Invoke(prevWorldPos, nextWorldPos, allowedTiles, depth - 1);
            };

            WFCCluster.SolveConflictFunction solveConflictFunction = (Vector3Int worldTilePos) =>
            {
                onConflict?.Invoke(worldTilePos);

                if ((conflictTiles != null) && (conflictTiles.Count > 0))
                {
                    // Resolve this one - set map to one of the conflict tiles
                    WFCTile3d tile = conflictTiles.Random(randomGenerator);
                    int localClusterIndex = TilePosToClusterIndex(WorldToClusterPosIndex(worldTilePos));
                    cluster.SetTile(localClusterIndex, new Tile() { tileId = _config.tileset.GetTileIndex(tile), rotation = (byte)(randomGenerator.Next() % 4) });
                    CreateTile(worldTilePos.x, worldTilePos.y, worldTilePos.z);
                }
            };

            if (clusterNorth != null)
            {
                for (int i = 0; i < clusterSize.x; i++)
                {
                    var tile = clusterNorth.GetWFCTile(i, 0, 0);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(i, 0, clusterSize.z - 1, pm, config.maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
            if (clusterSouth != null)
            {
                for (int i = 0; i < clusterSize.x; i++)
                {
                    var tile = clusterSouth.GetWFCTile(i, 0, clusterSize.z - 1);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(i, 0, 0, pm, config.maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
            if (clusterEast != null)
            {
                for (int i = 0; i < clusterSize.z; i++)
                {
                    var tile = clusterEast.GetWFCTile(0, 0, i);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(clusterSize.x - 1, 0, i, pm, config.maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
            if (clusterWest != null)
            {
                for (int i = 0; i < clusterSize.z; i++)
                {
                    var tile = clusterWest.GetWFCTile(clusterSize.x - 1, 0, i);
                    var pm = (tile.probMap != null) ? (tile.probMap) : (new(tile.tile));
                    cluster.Propagate(0, 0, i, pm, config.maxDepth, this, solveConflictFunction, propagateCallbackFunction);
                }
            }
        }

        return clusters[clusterPos];
    }

    private WFCCluster GetCluster(Vector3Int clusterPos)
    {
        if (clusters.TryGetValue(clusterPos, out WFCCluster cluster))
        {
            return cluster;
        }

        return null;
    }

    public void RemoveCluster(WFCCluster cluster)
    {
        cluster.Clear();
        clusters.Remove(cluster.basePos);
    }


    // Get the tile from the correct cluster
    private WFCTile GetTileFromCluster(Vector3Int worldPos)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(worldPos);

        int index = TilePosToClusterIndex(localPos);
        return cluster.GetWFCTile(index);
    }

    // Set a tile in the correct cluster
    private void SetTileInCluster(Vector3Int worldPos, Tile tile)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(worldPos);

        int index = TilePosToClusterIndex(localPos);
        cluster.SetTile(index, tile);
    }

    // Convert world position to cluster position
    private (WFCCluster, Vector3Int) WorldToClusterPos(Vector3Int worldPos)
    {
        return WorldToClusterPos(worldPos.x, worldPos.y, worldPos.z);
    }

    private (WFCCluster, Vector3Int) WorldToClusterPos(int x, int y, int z)
    {
        var clusterSize = _config.clusterSize;
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
        var clusterSize = _config.clusterSize;
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
        var clusterSize = _config.clusterSize;
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
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(worldPos);

        int index = TilePosToClusterIndex(localPos);

        cluster.SetTile3d(index, null);
    }

    public void CreateTile(int x, int y, int z)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var tile = cluster.GetTile(localClusterIndex);
        if (tile.tileId == 0)
        {
            cluster.SetTile3d(localClusterIndex, null);
            return;
        }

        createTileCallback(GetLocalPos(x, y, z), Quaternion.Euler(0, 90 * tile.rotation, 0), _config.GetTile(tile.tileId), cluster, (newTileObj) => cluster.SetTile3d(localClusterIndex, newTileObj));
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
        pos.x = (x + 0.5f) * config.gridSize.x;
        pos.y = y * config.gridSize.y;
        pos.z = (z + 0.5f) * config.gridSize.z;

        return pos;
    }

    internal Tile GetRandomTileFor(Vector3Int worldPos)
    {
        return GetRandomTileFor(worldPos.x, worldPos.y, worldPos.z);
    }

    internal Tile GetRandomTileFor(int x, int y, int z)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var tile = cluster.GetWFCTile(localClusterIndex);

        return tile.probMap.Get(randomGenerator);
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
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var t = cluster.GetWFCTile(localClusterIndex);

        Tile newTile = t.probMap.Get(randomGenerator);
        if (newTile == null) return GenResult.Ok;

        return GenerateTile(x, y, z, newTile);
    }

    public GenResult GenerateTile(int x, int y, int z, Tile tile)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var t = cluster.GetWFCTile(localClusterIndex);

        onTileCreate?.Invoke(new Vector3Int(x, y, z), cluster, localPos, tile, t.probMap);

        // Observe this map position
        cluster.SetTile(localClusterIndex, tile);

        // Remove this, no need to keep the probability list
        cluster.SetProb(localClusterIndex, null);

        // Now propagate
        var ret = Propagate(x, y, z, new ProbList<Tile>(tile), true, config.maxDepth);

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
                int uniqueId = _config.FindUniqueTile(tile.element);
                var ai = _config.GetAdjacency(uniqueId, direction);                
                allowed.Add(ai);
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

        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

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
                    WFCTile3d tile = conflictTiles.Random(randomGenerator);
                    cluster.SetTile(localClusterIndex, new Tile() { tileId = _config.GetTileIndex(tile), rotation = (byte)(randomGenerator.Next() % 4) });
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
            if (((x + 1) < config.maxMapLimit.x) && (PropagateDir(Direction.PX, x, y, z, 1, 0, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Left (X-)
            if (((x - 1) > config.minMapLimit.x) && (PropagateDir(Direction.NX, x, y, z, -1, 0, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Forward (Z+)
            if (((z + 1) < config.maxMapLimit.z) && (PropagateDir(Direction.PZ, x, y, z, 0, 0, 1) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Back (Z-)
            if (((z - 1) > config.minMapLimit.z) && (PropagateDir(Direction.NZ, x, y, z, 0, 0, -1) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Up (Y+)
            if (((y + 1) < config.maxMapLimit.y) && (PropagateDir(Direction.PY, x, y, z, 0, 1, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
            // Down (Y-)
            if (((y - 1) > config.minMapLimit.y) && (PropagateDir(Direction.NY, x, y, z, 0, -1, 0) == GenResult.Conflict))
            {
                ret = GenResult.Conflict;
            }
        }

        return ret;
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
                    (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

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

        return (possibilities.Count > 0) ? (possibilities.Random(randomGenerator)) : null;
    }

    internal void Set(Vector3Int worldPos, Tile t)
    {
        Set(worldPos.x, worldPos.y, worldPos.z, t);
    }

    internal void Set(int x, int y, int z, Tile t)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

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
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        return cluster.GetTile(localClusterIndex);
    }
    internal WFCTile GetWFCTile(int x, int y, int z)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(x, y, z);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        return cluster.GetWFCTile(localClusterIndex);
    }

    internal List<WFCCluster> currentClusters => new List<WFCCluster>(clusters.Values);

    public Vector3 GetClusterWorldSize()
    {
        var clusterSize = _config.clusterSize;
        return new Vector3(clusterSize.x * config.gridSize.x, clusterSize.y * config.gridSize.y, clusterSize.x * config.gridSize.z);
    }

    internal GenResult Observe(Vector3Int worldTilePos, byte tileId, byte rotation)
    {
        (WFCCluster cluster, Vector3Int localPos) = WorldToClusterPos(worldTilePos);

        int localClusterIndex = TilePosToClusterIndex(localPos);

        var t = cluster.GetWFCTile(localClusterIndex);

        var newTile = new Tile { tileId = tileId, rotation = rotation };
        onTileCreate?.Invoke(worldTilePos, cluster, localPos, newTile, t.probMap);

        // Observe this map position
        cluster.SetTile(localClusterIndex, newTile);

        // Remove this, no need to keep the probability list
        cluster.SetProb(localClusterIndex, null);

        // Now propagate
        var ret = Propagate(worldTilePos.x, worldTilePos.y, worldTilePos.z, new ProbList<Tile>(newTile), true, config.maxDepth);

        CreateTile(worldTilePos.x, worldTilePos.y, worldTilePos.z);

        return ret;
    }
}
