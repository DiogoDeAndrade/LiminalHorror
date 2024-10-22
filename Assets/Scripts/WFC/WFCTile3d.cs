using UnityEngine;
using NaughtyAttributes;
using System;

public class WFCTile3d : MonoBehaviour
{
    private Bounds tileBound;
    private bool   tileBoundInit = false;

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
