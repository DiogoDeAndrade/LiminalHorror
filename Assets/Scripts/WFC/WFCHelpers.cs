using UnityEngine;

public static class WFCHelpers
{
    public static (Vector3[], Vector3, Vector3) CalculateFrustumCorners(Matrix4x4 cameraLocalToWorldMatrix,
                                                    float x, float y, float width, float height,
                                                    float distance, float fieldOfView,
                                                    float aspect, bool orthographic = false,
                                                    float orthographicSize = 0f)
    {
        var corners = CalculateFrustumCorners(x, y, width, height, distance, fieldOfView, aspect, orthographic, orthographicSize);

        Vector3 min = cameraLocalToWorldMatrix.MultiplyPoint(Vector3.zero);
        Vector3 max = min;
        for (int i = 0; i < 4; i++)
        {
            corners[i] = cameraLocalToWorldMatrix.MultiplyPoint3x4(corners[i]);
            if (corners[i].x < min.x) min.x = corners[i].x;
            if (corners[i].y < min.y) min.y = corners[i].y;
            if (corners[i].z < min.z) min.z = corners[i].z;
            if (corners[i].x > max.x) max.x = corners[i].x;
            if (corners[i].y > max.y) max.y = corners[i].y;
            if (corners[i].z > max.z) max.z = corners[i].z;
        }
        return (corners, min, max);
    }

    public static Vector3[] CalculateFrustumCorners(float x, float y, float width, float height,
                                                    float distance, float fieldOfView,
                                                    float aspect, bool orthographic = false,
                                                    float orthographicSize = 0f)
    {
        float halfHeight, halfWidth;

        if (!orthographic)
        {
            // Convert field of view from degrees to radians.
            float fovRad = fieldOfView * (float)Mathf.PI / 180f;
            // For a perspective camera the half?height at the given distance is:
            halfHeight = distance * (float)Mathf.Tan(fovRad / 2f);
            halfWidth = halfHeight * aspect;
        }
        else
        {
            // For an orthographic camera the half?height is given by orthographicSize.
            halfHeight = orthographicSize;
            halfWidth = orthographicSize * aspect;
        }

        // The idea is that for a full viewport (0,0,1,1), the x coordinate in camera space is:
        //    x_camera = (u - 0.5)*2 * halfWidth
        // and similarly for y.
        // When a viewport rectangle is used, u and v are taken from the rectangle.

        Vector3[] corners = new Vector3[4];

        // Bottom-Left Corner (viewport u = x, v = y)
        float u = x;
        float v = y;
        float cornerX = (u - 0.5f) * 2f * halfWidth;
        float cornerY = (v - 0.5f) * 2f * halfHeight;
        corners[0] = new Vector3(cornerX, cornerY, distance);

        // Top-Left Corner (viewport u = x, v = y + height)
        u = x;
        v = y + height;
        cornerX = (u - 0.5f) * 2f * halfWidth;
        cornerY = (v - 0.5f) * 2f * halfHeight;
        corners[1] = new Vector3(cornerX, cornerY, distance);

        // Top-Right Corner (viewport u = x + width, v = y + height)
        u = x + width;
        v = y + height;
        cornerX = (u - 0.5f) * 2f * halfWidth;
        cornerY = (v - 0.5f) * 2f * halfHeight;
        corners[2] = new Vector3(cornerX, cornerY, distance);

        // Bottom-Right Corner (viewport u = x + width, v = y)
        u = x + width;
        v = y;
        cornerX = (u - 0.5f) * 2f * halfWidth;
        cornerY = (v - 0.5f) * 2f * halfHeight;
        corners[3] = new Vector3(cornerX, cornerY, distance);

        return corners;
    }
}
