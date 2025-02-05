using System;
using UnityEngine;

public class WFCTile
{
    public Tile tile;
    public WFCTile3d tile3d;
    public ProbList<Tile> probMap;
}

public class WFCCluster
{
    public delegate void SolveConflictFunction(Vector3Int pos);
    public delegate void PropagateCallbackFunction(Vector3Int prevWorldPos, Vector3Int nextWorldPos, ProbList<Tile> allowedTiles, int depth);

    public Vector3Int       basePos;
    public WFCTile[]        map;
    public bool             persistent;
    public WFCTilemap       tilemap;

    public WFCTilemapConfig config => (tilemap != null) ? (tilemap.config) : (null);

    public WFCCluster(Vector3Int basePos, WFCTilemap tilemap)
    {
        this.basePos = basePos;
        this.tilemap = tilemap;

        map = new WFCTile[config.clusterSize.x * config.clusterSize.y * config.clusterSize.z];
        // Initialize the cluster with default WFCTile values
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = new WFCTile
            {
                tile = new Tile() { tileId = 0 },
                probMap = new ProbList<Tile>(config.GetUniqueTiles())
            };
        }

        tilemap?.createClusterCallback(this);
    }

    public Tile GetTile(int index) => map[index].tile;
    public void SetTile(int index, Tile t) { map[index].tile = t; }
    public void SetProb(int index, ProbList<Tile> probMap) { map[index].probMap = probMap; }
    public WFCTile GetWFCTile(int index) => map[index];
    public WFCTile GetWFCTile(int x, int y, int z) => map[TilePosToClusterIndex(x, y, z)];
    public int TilePosToClusterIndex(int x, int y, int z)
    {
        return x + y * config.clusterSize.x + z * (config.clusterSize.x * config.clusterSize.y);
    }

    internal void SetTile3d(int localClusterIndex, WFCTile3d obj)
    {
        var tileObj = map[localClusterIndex];
        var prevObj = tileObj.tile3d;
        if (prevObj)
        {
            DeleteTile(prevObj);
        }

        tileObj.tile3d = obj;
    }

    void DeleteTile(WFCTile3d tile)
    {
        if (tile != null)
        {
            tilemap.destroyTileCallback(tile);
        }
    }

    void DeleteCluster()
    {
        tilemap.destroyClusterCallback(this);
    }

    internal void Clear()
    {
        foreach (var tile in map)
        {
            DeleteTile(tile.tile3d);
        }

        DeleteCluster();
    }

    // This propagates only within the cluster, so x, y and z are in local cluster coordinates
    internal void Propagate(int x, int y, int z, ProbList<Tile> tiles, int depth, WFCTilemap mainData, SolveConflictFunction solveConflictFunction, PropagateCallbackFunction propagateCallbackFunction)
    {
        void PropagateDir(Direction direction, int x, int y, int z, int dx, int dy, int dz)
        {
            int newX = x + dx;
            int newY = y + dy;
            int newZ = z + dz;

            ProbList<Tile> allowed = new();
            foreach (var tile in tiles)
            {
                int uniqueId = config.FindUniqueTile(tile.element);

                if (uniqueId != -1) allowed.Add(config.GetAdjacency(uniqueId, direction));
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

                Vector3Int worldTilePos = new Vector3Int(basePos.x * config.clusterSize.x + x, basePos.y * config.clusterSize.y + y, basePos.z * config.clusterSize.z + z);

                solveConflictFunction(worldTilePos);
            }
        }

        if (propagateFurther)
        {
            // Get all tiles allowed to the right (X+), and propagate that
            if (x < config.clusterSize.x - 1) PropagateDir(Direction.PX, x, y, z, 1, 0, 0);
            // Left (X-)
            if (x > 0) PropagateDir(Direction.NX, x, y, z, -1, 0, 0);
            // Forward (Z+)
            if (z < config.clusterSize.z - 1) PropagateDir(Direction.PZ, x, y, z, 0, 0, 1);
            // Back (Z-)
            if (z > 0) PropagateDir(Direction.NZ, x, y, z, 0, 0, -1);

            // Again, no propagation on Y (current support is lacking)
        }
    }
}
