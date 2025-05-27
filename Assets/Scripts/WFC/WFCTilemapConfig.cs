using UnityEngine;
using NaughtyAttributes;
using System.Collections.Generic;
using System.IO;
using UC;

[CreateAssetMenu(fileName = "WFCTilemapConfig", menuName = "WFC/Tilemap Config")]
public class WFCTilemapConfig : ScriptableObject
{
    public Vector3 gridSize = Vector3.one;
    public Vector3Int mapSize = Vector3Int.zero;
    public Bounds localBounds;
    [ShowIf(nameof(displayTilemapData))]
    public TextAsset tilemapData;
    [ShowIf(nameof(displayAdjacencyData))]
    public TextAsset adjacencyData;
    public WFCTileset tileset;

    [ShowIf(nameof(canGen)), Header("Generation Parameters")]
    public int maxDepth = 25;
    [ShowIf(nameof(canGen))]
    public Vector3Int minMapLimit = new Vector3Int(-10000, 0, -10000);
    [ShowIf(nameof(canGen))]
    public Vector3Int maxMapLimit = new Vector3Int(10000, 1, 10000);
    [ShowIf(nameof(canGen))]
    public Vector3Int clusterSize = new Vector3Int(8, 1, 8);
    [ShowIf(nameof(canGen))]
    public bool dynamicUpdate;
    [ShowIf(nameof(isDynamicGen)), HideInInspector]
    public Matrix4x4 cameraMatrix;
    [ShowIf(nameof(isDynamicGen)), Header("Dynamic Generation Parameters")]
    public bool multithreaded = false;
    [ShowIf(nameof(isDynamicGen))]
    public float maxGenerationDistance = 50;
    [ShowIf(nameof(isDynamicGen))]
    public float fadeOutDistance = 10;
    [ShowIf(nameof(isDynamicGenAndNotMultithreaded))]
    public float maxTimePerFrameMS = 15.0f;
    [ShowIf(nameof(isDynamicGen))]
    public bool buildNavmesh = false;


    bool displayTilemapData => adjacencyData == null;
    bool displayAdjacencyData => tilemapData == null;
    bool canGen => adjacencyData != null;
    bool isDynamicGen => canGen && dynamicUpdate;
    bool isDynamicGenAndNotMultithreaded => isDynamicGen && !multithreaded;


    private List<WFCAdjacencyData>  adjacencyInfo;
    private ProbList<Tile>          uniqueTiles;

    
    public bool Load()
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
                float count = reader.ReadSingle();
                uniqueTiles.Add(tile, count);
            }

            // Read adjacency array
            int adjacencyLength = reader.ReadInt32();
            adjacencyInfo = new List<WFCAdjacencyData>(); for (int i = 0; i < adjacencyLength; i++) adjacencyInfo.Add(null);
            for (int i = 0; i < adjacencyLength; i++)
            {
                adjacencyInfo[i] = new WFCAdjacencyData();
                adjacencyInfo[i].Load(reader);
            }
        }

        return true;
    }

    public ProbList<Tile> GetAdjacency(int uniqueId, Direction direction)
    {
        if (adjacencyInfo == null)
        {
            // No adjacency information
            Debug.LogWarning("No adjacency information available!");
            return null;
        }

        return adjacencyInfo[uniqueId].Get(direction);
    }

    public List<WFCAdjacencyData> GetAdjacencyInfo()
    {
        return adjacencyInfo;
    }

    public ProbList<Tile> GetUniqueTiles()
    {
        return uniqueTiles;
    }

    public int FindUniqueTile(Tile element)
    {
        if (uniqueTiles == null) 
        {
            // No adjacency information
            Debug.LogWarning("No unique tiles information available!");
            return -1;
        }

        return uniqueTiles.IndexOf(element);
    }

    public WFCTile3d GetTile(byte tileId)
    {
        if (tileset == null)
        {
            // No adjacency information
            Debug.LogWarning("No tileset available!");
            return null;
        }

        return tileset.GetTile(tileId);
    }

    public byte GetTileIndex(WFCTile3d tile)
    {
        if (tileset == null)
        {
            // No adjacency information
            Debug.LogWarning("No tileset available!");
            return 255;
        }

        return tileset.GetTileIndex(tile);
    }
}
