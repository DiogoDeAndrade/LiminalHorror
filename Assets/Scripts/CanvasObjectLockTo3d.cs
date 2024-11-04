using UnityEngine;
using UnityEngine.UI;

public class CanvasObjectLockTo3d : MonoBehaviour
{
    public Transform sourceTransform;

    Camera          mainCamera;
    Canvas          canvas;
    RectTransform   uiImageRectTransform;
    Image           image;

    void Start()
    {
        mainCamera = FindAnyObjectByType<Camera>();
        canvas = GetComponentInParent<Canvas>();
        uiImageRectTransform = GetComponent<RectTransform>();
        image = GetComponent<Image>();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        RunUpdate();
    }

    private void Update()
    {
        RunUpdate();
    }

    private void LateUpdate()
    {
        RunUpdate();
    }

    void RunUpdate()
    { 
        if (mainCamera != null && sourceTransform != null)
        {
            // Convert the 3D position of the source to screen space
            Vector3 screenPos = mainCamera.WorldToScreenPoint(sourceTransform.position);

            // Check if the object is in front of the camera
            if (screenPos.z > 0)
            {
                float scaleFactor = canvas.scaleFactor;
                Vector2 adjustedPosition = new Vector2(screenPos.x / scaleFactor, screenPos.y / scaleFactor);

                // Set the anchored position to the adjusted position
                uiImageRectTransform.anchoredPosition = adjustedPosition;
                image.enabled = true;
            }
            else
            {
                // Optional: hide the UI if the object is behind the camera
                image.enabled = false;
            }
        }
    }
}
