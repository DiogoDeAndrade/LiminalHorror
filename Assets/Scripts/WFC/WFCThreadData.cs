using UnityEngine;
using System.Collections.Generic;
using System.Threading;

public class WFCThreadData
{
    private List<string>    logEntries = new();
    private object          lockObject = new object();
    private bool            threadExit;
    private Matrix4x4       _cameraMatrix;
    private float           _cameraFOV;
    private float           _cameraAspect;
    private bool            _cameraOrthographic;
    private float           _cameraOrthographicSize;

    private Dictionary<Vector3Int, Transform>   clusterObjects = new();
    private List<WFCTileData.Cluster>           clusterRequest = new();

    public Matrix4x4 cameraMatrix
    {
        get { lock (lockObject) return _cameraMatrix; }
        set { lock (lockObject) _cameraMatrix = value; }
    }
    public float cameraFOV
    {
        get { lock (lockObject) return _cameraFOV; }
        set { lock (lockObject) _cameraFOV = value; }
    }
    public float cameraAspect
    {
        get { lock (lockObject) return _cameraAspect; }
        set { lock (lockObject) _cameraAspect = value; }
    }
    public bool cameraOrthographic
    {
        get { lock (lockObject) return _cameraOrthographic; }
        set { lock (lockObject) _cameraOrthographic = value; }
    }
    public float cameraOrthographicSize
    {
        get { lock (lockObject) return _cameraOrthographicSize; }
        set { lock (lockObject) _cameraOrthographicSize = value; }
    }

    public void Log(string message)
    {
        // Lock to ensure that only one thread can modify logEntries at a time.
        lock (lockObject)
        {
            logEntries.Add(message);
        }
    }
    public void FlushLog()
    {
        List<string> entriesToLog;

        lock (lockObject)
        {
            entriesToLog = logEntries;
            logEntries = new();
        }

        foreach (var entry in entriesToLog)
        {
            Debug.Log(entry);
        }
    }
    public void Quit()
    {
        lock (lockObject)
        {
            threadExit = true;
        }
    }
    public bool ShouldQuit()
    {
        lock (lockObject)
        {
            return threadExit;
        }
    }

    public void SetCamera(Camera camera)
    {
        lock (lockObject)
        {
            _cameraFOV = camera.fieldOfView;
            _cameraAspect = camera.aspect;
            _cameraOrthographic = camera.orthographic;
            _cameraOrthographicSize = camera.orthographicSize;
            _cameraMatrix = camera.transform.localToWorldMatrix;
        }
    }

    public void GetCamera(out Matrix4x4 camMatrix, out float fov, out float aspect, out bool isOrtho, out float orthoSize)
    {
        lock (lockObject)
        {
            camMatrix = _cameraMatrix;
            fov = _cameraFOV;
            aspect = _cameraAspect;
            isOrtho = _cameraOrthographic;
            orthoSize = _cameraOrthographicSize;
        }
    }

    public Transform GetClusterTransform(WFCTileData.Cluster cluster, bool waitCreation)
    {
        lock (lockObject)
        {
            if (clusterObjects.TryGetValue(cluster.basePos, out var ret))
            {
                return ret;
            }
            // Request creation
            clusterRequest.Add(cluster);
        }

        if (waitCreation)
        {
            while (true)
            {
                lock (lockObject)
                {
                    if (clusterObjects.TryGetValue(cluster.basePos, out var ret))
                    {
                        return ret;
                    }
                }
                Thread.Sleep(5);

            }
        }

        return null;
    }

    public void UpdateClusters(Transform parentTransform, WFCTilemap.OnNewCluster onNewCluster)
    {
        List<WFCTileData.Cluster> clustersToCreate;
        lock (lockObject)
        {
            clustersToCreate = clusterRequest;
            clusterRequest = new();
        }
        
        List<Transform> newTransforms = new();

        foreach (var cluster in clustersToCreate)
        {
            var reqPos = cluster.basePos;

            var go = new GameObject();
            go.name = $"Cluster {reqPos.x},{reqPos.y},{reqPos.z}";
            go.transform.parent = parentTransform;
            go.transform.localPosition = Vector3.zero;
            var wfcCluster = go.AddComponent<WFCCluster>();
            wfcCluster.cluster = cluster;

            newTransforms.Add(go.transform);
        }

        lock (lockObject)
        {
            for (int i = 0; i < clustersToCreate.Count; i++)
            {
                clusterObjects.Add(clustersToCreate[i].basePos, newTransforms[i]);
            }

            clusterObjects.RemoveAll(kvp => kvp.Value == null);

            for (int i = 0; i < clustersToCreate.Count; i++)
            {
                onNewCluster?.Invoke(newTransforms[i].GetComponent<WFCCluster>());
            }
        }
    }
}
