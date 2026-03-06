using System;
using UnityEngine;

public class ScreenshotCapture : MonoBehaviour
{
    [Header("Capture Settings")]
    public Camera targetCamera;
    public int captureWidth = 512;
    public int captureHeight = 512;
    [Range(10, 100)]
    public int jpegQuality = 75;

    RenderTexture renderTexture;
    Texture2D texture2D;

    // Debug: last captured texture copy (for UI display)
    Texture2D lastCapturedCopy;
    public Texture2D LastCapturedTexture => lastCapturedCopy;

    // 데이터 수집용: 마지막 캡처의 JPEG 바이트
    byte[] lastJpegBytes;
    public byte[] LastJpegBytes => lastJpegBytes;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    /// <summary>
    /// Captures the default camera view as a base64-encoded JPEG string.
    /// </summary>
    public string CaptureBase64()
    {
        return CaptureBase64(targetCamera);
    }

    /// <summary>
    /// Captures a specific camera's view as a base64-encoded JPEG string.
    /// Works on disabled cameras too (uses Camera.Render() explicitly).
    /// </summary>
    public string CaptureBase64(Camera cam)
    {
        if (cam == null)
        {
            Debug.LogError("[ScreenshotCapture] No camera assigned!");
            return null;
        }

        // Create or resize RenderTexture
        if (renderTexture == null || renderTexture.width != captureWidth || renderTexture.height != captureHeight)
        {
            if (renderTexture != null)
                renderTexture.Release();

            renderTexture = new RenderTexture(captureWidth, captureHeight, 24);
            texture2D = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        }

        // Render camera to texture
        RenderTexture previousRT = cam.targetTexture;
        cam.targetTexture = renderTexture;
        cam.Render();
        cam.targetTexture = previousRT;

        // Read pixels
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = renderTexture;
        texture2D.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        texture2D.Apply();
        RenderTexture.active = previousActive;

        // Store a copy for debug UI
        if (lastCapturedCopy == null || lastCapturedCopy.width != captureWidth || lastCapturedCopy.height != captureHeight)
            lastCapturedCopy = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        Graphics.CopyTexture(texture2D, lastCapturedCopy);

        // Encode to JPEG and convert to base64
        byte[] jpegBytes = texture2D.EncodeToJPG(jpegQuality);
        lastJpegBytes = jpegBytes; // 데이터 수집 로깅용 저장
        string base64 = Convert.ToBase64String(jpegBytes);

        Debug.Log($"[ScreenshotCapture] Captured {captureWidth}x{captureHeight} from {cam.name}, {jpegBytes.Length / 1024}KB");
        return base64;
    }

    void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
        if (texture2D != null)
        {
            Destroy(texture2D);
        }
        if (lastCapturedCopy != null)
        {
            Destroy(lastCapturedCopy);
        }
    }
}
