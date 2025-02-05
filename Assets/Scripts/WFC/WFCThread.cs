
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using System;

public class WFCThread 
{
    struct CameraState
    {
        public Matrix4x4   matrix;
        public Vector3     position;
        public Vector3     forward;
        public float       fieldOfView;
        public float       aspect;
        public bool        orthographic;
        public float       orthographicSize;
    }

    bool    exit;
    Mutex   mutex;
    Thread  thread;

    WFCTilemap          tilemap;
    WFCTilemapComponent tilemapComponent;
    WFCTilemapConfig    config;
    CameraState         currentCamera;
    CameraState         nextCamera;
    Matrix4x4           containerMatrix;
    Matrix4x4           invContainerMatrix;
    float               lastGenerateTime;
    bool                updated;
    bool                resetGen;

    Mutex           logMutex;
    Mutex           updateMutex;
    Mutex           tileRequestMutex;
    Mutex           cameraMutex;
    Mutex           clusterRequestMutex;
    List<string>    messages = new();

    class CreateTileRequest
    {
        public Vector3              localPosition;
        public Quaternion           localRotation;
        public WFCTile3d            tilePrefab;
        public WFCCluster           cluster;
        public Action<WFCTile3d>    completeCallback;
    }
    List<CreateTileRequest> internalCreateTilesRequest = new();
    List<CreateTileRequest> createTilesRequest = new();
    List<WFCTile3d>         destroyTileRequest = new();
    List<WFCTile3d>         internalDestroyTileRequest = new();
    List<WFCCluster>        createClusterRequest = new();
    List<WFCCluster>        destroyClusterRequest = new();

    public WFCThread(WFCTilemap tilemap, WFCTilemapComponent tilemapComponent)
    {
        this.tilemap = tilemap;
        this.config = tilemap.config;
        this.containerMatrix = tilemapComponent.transform.localToWorldMatrix;
        this.invContainerMatrix = tilemapComponent.transform.worldToLocalMatrix;
        this.tilemapComponent = tilemapComponent;

        mutex = new Mutex();
        logMutex = new Mutex();
        updateMutex = new Mutex();
        tileRequestMutex = new Mutex();
        clusterRequestMutex = new Mutex();
        cameraMutex = new Mutex();

        thread = new Thread(ExecutionThread);
    }

    public void Start()
    {
        thread.Start();
    }

    void ExecutionThread()
    {
        while (true)
        {
            mutex.WaitOne();
            bool b = exit;
            mutex.ReleaseMutex();
            if (b) break;

            cameraMutex.WaitOne();
            currentCamera = nextCamera;
            resetGen = false;
            cameraMutex.ReleaseMutex();

            Generate();
            ClearOldClusters();
        }
    }

    void Generate()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        int currentDistance = Mathf.CeilToInt(Mathf.Min(2, config.maxGenerationDistance * 0.1f));
        int incDistance = currentDistance;

        while (currentDistance <= config.maxGenerationDistance)
        {
            (Vector3[] corners, Vector3 min, Vector3 max) = WFCHelpers.CalculateFrustumCorners(
                currentCamera.matrix,
                0.0f, 0.0f, 1.0f, 1.0f,
                currentDistance,
                currentCamera.fieldOfView, currentCamera.aspect, currentCamera.orthographic, currentCamera.orthographicSize
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
                    updateMutex.WaitOne();
                    updated = true;
                    updateMutex.ReleaseMutex();
                }
                else
                {
                    currentDistance += incDistance;
                }

                if (sw.ElapsedMilliseconds > 5)
                {
                    cameraMutex.WaitOne();
                    bool b = resetGen;
                    cameraMutex.ReleaseMutex();
                    if (b)
                    {
                        lastGenerateTime = sw.ElapsedMilliseconds;
                        return;
                    }
                }
            }
        }

        lastGenerateTime = sw.ElapsedMilliseconds;
    }

    void ClearOldClusters()
    {
        Vector3 localCameraPos = invContainerMatrix.MultiplyPoint(currentCamera.position);
        Vector3 localCameraDir = invContainerMatrix.MultiplyVector(currentCamera.forward); localCameraDir.y = 0; localCameraDir.Normalize();
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

    public void AddLog(string message)
    {
        logMutex.WaitOne();
        messages.Add(message);
        logMutex.ReleaseMutex();
    }

    public void FlushLog()
    {
        logMutex.WaitOne();
        if (messages.Count == 0)
        {
            logMutex.ReleaseMutex();
            return;
        }
        var msgs = messages;
        messages = new();
        logMutex.ReleaseMutex();

        foreach (var msg in msgs)
        {
            Debug.Log(msg);
        }
    }

    public void SetExit(bool exit, bool waitExit)
    {
        this.exit = exit;

        if (waitExit)
        {
            thread.Join();
            Debug.Log("WFC thread terminated!");
        }
    }

    internal void SetCamera(Camera camera)
    {
        cameraMutex.WaitOne();
        nextCamera = new()
        {
            matrix = camera.transform.localToWorldMatrix,
            fieldOfView = camera.fieldOfView,
            aspect = camera.aspect,
            orthographic = camera.orthographic,
            orthographicSize = camera.orthographicSize,
            position = camera.transform.position,
            forward = camera.transform.forward
        };
        if (Vector3.Distance(nextCamera.position, currentCamera.position) > 10.0f)
        {
            resetGen = true;
        }
        else if (Vector3.Angle(nextCamera.forward, currentCamera.forward) > 15.0f)
        {
            resetGen = true;
        }
        cameraMutex.ReleaseMutex();
    }

    internal void UpdateClusters()
    {
        clusterRequestMutex.WaitOne();
        var createRequests = createClusterRequest;
        if (createRequests.Count > 0)
            createClusterRequest = new();
        else
            createRequests = null;
        var destroyRequests = destroyClusterRequest;
        if (destroyRequests.Count > 0)
            destroyClusterRequest = new();
        else
            destroyRequests = null;
        clusterRequestMutex.ReleaseMutex();

        if (createRequests != null)
        {
            foreach (var request in createRequests)
            {
                tilemapComponent.CreateCluster(request);
            }
        }
        if (destroyRequests != null)
        {
            foreach (var request in destroyRequests)
            {
                tilemapComponent.DestroyCluster(request);
            }
        }
    }

    internal void UpdateTiles()
    {
        tileRequestMutex.WaitOne();
        var createRequests = createTilesRequest;
        if (createRequests.Count > 0)
            createTilesRequest = new();
        else
            createRequests = null;
        var destroyRequests = destroyTileRequest;
        if (destroyRequests.Count > 0)
            destroyTileRequest = new();
        else
            destroyRequests = null;
        tileRequestMutex.ReleaseMutex();

        if (createRequests != null)
        {
            foreach (var request in createRequests)
            {
                tilemapComponent.CreateTile(request.localPosition, request.localRotation, request.tilePrefab, request.cluster, request.completeCallback);
            }
        }
        if (destroyRequests != null)
        {
            foreach (var request in destroyRequests)
            {
                tilemapComponent.DestroyTile(request);
            }
        }
    }

    public bool IsUpdated()
    {
        updateMutex.WaitOne();
        bool b = updated;
        updated = false;
        updateMutex.ReleaseMutex();

        return b;
    }

    public Vector3Int WorldToTilePos(Vector3 worldPos)
    {
        Vector3 localPos = containerMatrix.MultiplyPoint3x4(worldPos);

        Vector3Int tilePos = Vector3Int.zero;
        tilePos.x = Mathf.FloorToInt((localPos.x - config.localBounds.min.x) / config.gridSize.x);
        tilePos.y = Mathf.FloorToInt((localPos.y - config.localBounds.min.y) / config.gridSize.y);
        tilePos.z = Mathf.FloorToInt((localPos.z - config.localBounds.min.z) / config.gridSize.z);

        return tilePos;
    }

    internal void CreateTile(Vector3 localPosition, Quaternion localRotation, WFCTile3d tilePrefab, WFCCluster cluster, Action<WFCTile3d> completeCallback)
    {
        internalCreateTilesRequest.Add(new CreateTileRequest
        {
            localPosition = localPosition, 
            localRotation = localRotation,
            tilePrefab = tilePrefab,
            cluster = cluster,
            completeCallback = completeCallback
        });
        Copy(internalCreateTilesRequest, createTilesRequest, 5, tileRequestMutex);
    }

    internal void DestroyTile(WFCTile3d tile)
    {
        internalDestroyTileRequest.Add(tile);
        Copy(internalDestroyTileRequest, destroyTileRequest, 5, tileRequestMutex);
    }

    internal void CreateCluster(WFCCluster cluster)
    {
        clusterRequestMutex.WaitOne();
        createClusterRequest.Add(cluster);
        clusterRequestMutex.ReleaseMutex();
    }

    internal void DestroyCluster(WFCCluster cluster)
    {
        clusterRequestMutex.WaitOne();
        destroyClusterRequest.Add(cluster);
        clusterRequestMutex.ReleaseMutex();
    }

    bool Copy<T>(List<T> src, List<T> dest, int itemCount, Mutex mutex) where T : class
    {
        if (src.Count >= itemCount)
        {
            mutex.WaitOne();
            dest.AddRange(src);
            src.Clear();
            mutex.ReleaseMutex();

            return true;
        }

        return false;
    }
}
