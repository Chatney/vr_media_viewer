using UnityEngine;

public class SetCameraEnvironmentColor : MonoBehaviour
{
    public Camera mainCamera;
    public Shader averageColorShader;

    private Material material;
    private RenderTexture temporaryRenderTexture;

    private Color accumulatedColor = Color.black; // To store the running average color

    public void SetEnvironmentColor(RenderTexture rgbImage, float[] depthArray, int depthWidth, int depthHeight, float backgroundDepthThreshold, int framesToAverage)
    {
        if (rgbImage == null || depthArray == null || depthArray.Length != depthWidth * depthHeight)
        {
            Debug.LogError("Invalid input data to SetEnvironmentColor.");
            return;
        }

        // Create a temporary RenderTexture to read the colors from the rgbImage
        if (temporaryRenderTexture == null || temporaryRenderTexture.width != rgbImage.width || temporaryRenderTexture.height != rgbImage.height)
        {
            if (temporaryRenderTexture != null)
                temporaryRenderTexture.Release();

            temporaryRenderTexture = new RenderTexture(rgbImage.width, rgbImage.height, 0, RenderTextureFormat.ARGB32);
        }

        // Copy the rgbImage to the temporaryRenderTexture
        Graphics.Blit(rgbImage, temporaryRenderTexture);

        // Read back the color data from the RenderTexture
        RenderTexture.active = temporaryRenderTexture;
        Texture2D rgbTexture = new Texture2D(depthWidth, depthHeight, TextureFormat.RGB24, false);
        rgbTexture.ReadPixels(new Rect(0, 0, depthWidth, depthHeight), 0, 0);
        rgbTexture.Apply();
        RenderTexture.active = null;

        // Array to accumulate valid colors for this frame
        Color frameAccumulatedColor = Color.black;
        int validPixelCount = 0;

        // Iterate over each depth value
        for (int y = 0; y < depthHeight; y++)
        {
            for (int x = 0; x < depthWidth; x++)
            {
                int index = y * depthWidth + x;
                float depthValue = depthArray[index];

                // Check if the depth is within the valid range
                if (depthValue >= backgroundDepthThreshold && depthValue <= 1f)
                {
                    // Get the corresponding color from the rgbTexture
                    Color pixelColor = rgbTexture.GetPixel(x, y);

                    // Accumulate the color for this frame
                    frameAccumulatedColor += pixelColor;
                    validPixelCount++;
                }
            }
        }

        // Calculate the average color for this frame if there are valid pixels
        if (validPixelCount > 0)
        {
            Color averageFrameColor = frameAccumulatedColor / validPixelCount;

            // Compute the new running average using the moving average formula
            accumulatedColor = (accumulatedColor * (framesToAverage - 1) + averageFrameColor) / framesToAverage;

            // Set the camera background color to the updated moving average color
            mainCamera.backgroundColor = accumulatedColor;
        }
        else
        {
            Debug.LogWarning("No valid pixels found within the depth range.");
        }

        // Cleanup
        Destroy(rgbTexture);
    }

    private void OnDestroy()
    {
        if (temporaryRenderTexture != null)
            temporaryRenderTexture.Release();
    }
}
