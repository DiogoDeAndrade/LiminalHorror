using System;
using UnityEngine;

// Represents a single instanced tile in the game world
// This is used mainly for building the adjacency rules and identify the tiles for the tileset

public class WFCTile3d : MonoBehaviour
{
    public WFCTile3d    sourcePrefabObject;

    private Bounds      tileBound;
    private bool        tileBoundInit = false;

    public Bounds GetExtents()
    {
        if (!tileBoundInit)
        {
            var meshRenderers = GetComponentsInChildren<MeshRenderer>();
            if ((meshRenderers != null) && (meshRenderers.Length >= 0))
            {
                tileBound = meshRenderers[0].bounds;

                foreach (var meshRenderer in meshRenderers)
                {
                    Bounds bounds = meshRenderer.bounds;
                    tileBound.Encapsulate(bounds);
                }
            }
            else
            {
                tileBound = new Bounds();
            }

        }
        return tileBound;
    }
}
