using UnityEngine;
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;


[RequireComponent(typeof(MeshFilter))]
public class MeshGenerator : MonoBehaviour{
    
    //public Texture2D inputRGB; //input texture. Only used to generate the depthmap
    //public RenderTexture renderTexture;
    public GameObject cubePrefab; // Reference to your cube prefab in the Inspector
    public float NonLinearityCompensationFactor = 1.1f;
    public int stabilizationThreshold = 1; //range in which the smaller changes in depth are stabilized, but large changes occur instantly
    
    public Shader resizeShader;
    private Material resizeMaterial;

    Texture2D inputRGB;
    public Texture2D splashImage;
    RenderTexture resizedTexture;
    Texture2D tex;

    private Material material;

    Mesh mesh;    
    Vector3[] depthVertices;
    Vector3[] flatVertices; 
    Vector3[] depthDirection;  
    int[] triangles;
    Vector2[] uvs;
    Vector3 defaultScreenPosition = new Vector3(0, 0, 0.5f); //default position of the screen
    Vector3 movedScreenPosition = new Vector3 (0, 0, 1.5f); //position when menu is open (mesh needs to move away or it interferes)

    //debug stopwatches
    private Stopwatch stopWatchtotalTime;
    private Stopwatch stopWatchVertex;
    private Stopwatch stopWatchUpdateTriangles;
    private Stopwatch stopWatchResizeImage;
    private Stopwatch stopWatchinferDepth;
    private Stopwatch stopWatchCreateTriangles;

    //output
    //public RenderTexture OutputRenderTextureDepth; //output is Depth render texture
    //public RenderTexture OutputRenderTextureRGB; //and RBG render texture

    [Range(0.1f, 10000f)]
    public float depth_multiplier = 0.7f;

    [Range(0.001f, 10.0f)]
    public float projectionfactor = 1.0f; //ToDo: make this FoV, and make this automatic. For now, assumption is that camera distance ~= picture width. higher = further away camera & less angled depth
    //public float[] depthValues; // array of depth values
    public int expectedImageSize = 518;
    public int minimumImageSize = 350; //smaller than this and sentis or the model gives an TensorShape.ValueError. somethingsomething broadcast
    public int maximumImageSize = 924; // ~16:9 image with 518 as minimum size. Larger than this gets really heavy
    public int ensureMultipleOf = 14; //depth anything expects image dimensions to be multiple of 14
    int nominalImageSize; //this is a variable and can go down to increase FPS for video play

    //these are for storing the current values
    Array currentDepthmapValues;
    private float[] previousDepthValues;
    float depth_multiplier_saved;
    private bool isResizing = false;
    private ComputeBuffer depthBuffer;

    public RenderTexture resizedImage;
 

    int currentDepthmapWidth;
    int currentDepthmapHeight;
    int previousDepthmapWidth;
    int previousDepthmapHeight;

    // declarations
    float depth; //per-pixel depth value  
    

    void Start()
    {   
        stopWatchtotalTime = new Stopwatch();
        stopWatchVertex = new Stopwatch();
        stopWatchUpdateTriangles = new Stopwatch();
        stopWatchResizeImage = new Stopwatch();
        stopWatchinferDepth = new Stopwatch();
        stopWatchCreateTriangles = new Stopwatch();  

        //initialize the resizing shader
        resizeMaterial = new Material(resizeShader);

        depth_multiplier_saved = depth_multiplier;

        //make sure that minimum and maximum expected values are compatible with the model
        minimumImageSize = ConstrainToMultipleOf(minimumImageSize,ensureMultipleOf);
        maximumImageSize = ConstrainToMultipleOf(maximumImageSize,ensureMultipleOf);

        material = GetComponent<MeshRenderer>().GetComponent<Renderer>().material;
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;  //16 bit has a maximum of ~65k triangles. 32 bit has more            
        mesh.MarkDynamic(); //optimize mesh for frequent updates
        GetComponent<MeshFilter>().mesh = mesh;

        

        // Display the splash image
        var depthInformation = inferDepthFromInput(ConvertToRenderTexture(splashImage), true);
        CreateOrUpdateScreen(depthInformation.depthArray, depthInformation.width, depthInformation.height);
        ApplyTexturesToScreen(ConvertToRenderTexture(splashImage), depthInformation.depthArray, depthInformation.width, depthInformation.height, 1);
    }

    //create a new screen. if dimensions are different, create new. If only depth changed, update it.
    public void CreateOrUpdateScreen(float[] depthValues, int width, int height){
        currentDepthmapValues = depthValues;
        if(currentDepthmapWidth != width || currentDepthmapHeight != height){
            currentDepthmapWidth = width;
            currentDepthmapHeight = height;
            CreateMesh();
        }else{
            UpdateMesh();
        }
        
    }

    public void ApplyTexturesToScreen(RenderTexture RGB, float[] depthValuesNormalized, int textureWidth, int textureHeight, int frameCountToAvarage){
        //apply this image to the 3d screen
        if (RGB != null){
            material.SetTexture("_MainTex", RGB);
        }			
        
        if (depthValuesNormalized != null)
        {
            depthBuffer = new ComputeBuffer(depthValuesNormalized.Length, sizeof(float));
            depthBuffer.SetData(depthValuesNormalized);
            material.SetBuffer("_DepthBuffer", depthBuffer);
            material.SetInt("_TextureWidth", textureWidth);
            material.SetInt("_TextureHeight", textureHeight);
        }

        UnityEngine.Debug.Log($"Minimum depth value is {Mathf.Min(depthValuesNormalized)} maximum depth value is {Mathf.Max(depthValuesNormalized)}") ;   //should be 0 - 1?            

        SetCameraEnvironmentColor setCameraEnvironmentColor = UnityEngine.Object.FindAnyObjectByType<SetCameraEnvironmentColor>();
        //set the camera skybox to the avarage background color
        setCameraEnvironmentColor.SetEnvironmentColor(resizedImage, depthValuesNormalized, textureWidth, textureHeight, 0.98f, frameCountToAvarage); //for image, instantly change background. For video, avarage over x previousframes

    }

    void UpdateMesh(){
        UpdateVertexDepth();
        mesh.vertices = depthVertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        UpdateUVs();
    }

    public void CreateVertexCloud(){
        int newSize = (currentDepthmapWidth + 1) * (currentDepthmapHeight + 1);
        
        // Detect changes in dimensions
        bool dimensionsChanged = currentDepthmapWidth != previousDepthmapWidth || currentDepthmapHeight != previousDepthmapHeight;

        //only create a new vertex cloud if the  dimensions have changed. So don't do this for new video frames, or an image with equal dimensions
        //ToDo: remake the direction vectors if the FoV changes
        if (dimensionsChanged){ 
            previousDepthmapWidth = currentDepthmapWidth;
            previousDepthmapHeight = currentDepthmapHeight; 
            
            // Create vertices for flat image. This is a reference for the depthVertices and stays the same if the resolution doesn't change
            if (flatVertices == null || flatVertices.Length != newSize) 
            {
                flatVertices = new Vector3[newSize];  //amount of vertexes in the mesh            
            }

            // Create vertices array for depth image. This includes depthvalues animates back and forth along original line of sight, with flatVertices as starting point
            if (depthVertices == null || depthVertices.Length != newSize) 
            {
                depthVertices = new Vector3[newSize];  //amount of vertexes in the mesh  
                previousDepthValues = new float[newSize];       //for temporal coherence          
            }

            // Create array for depth directions. This is the direction depthVertices uses for depth movement
            if (depthDirection == null || depthDirection.Length != newSize) 
            {
                depthDirection = new Vector3[newSize];  //amount of vertexes in the mesh            
            }        
            

            float referenceSize = Mathf.Max(currentDepthmapWidth,currentDepthmapHeight);
            float cameraDistance = referenceSize; //TODO: replace with FoV estimation model if possible
            Vector3 estimatedCameraPosition =  new Vector3(0,0, -cameraDistance  * projectionfactor);

            //create static flat vertex plane and Line-of-sight direction per pixel
            for (int i = 0, y = 0; y <= currentDepthmapHeight; y++){
                for (int x = 0; x <= currentDepthmapWidth; x++){

                    // center mesh around csys
                    float xSizeHalf = currentDepthmapWidth/2;
                    float ySizeHalf = currentDepthmapHeight/2;

                    //make a vertex for each pixel in the depthmap, in a flat plane.
                    Vector3 pointOnPlane = new Vector3(x-xSizeHalf,y-ySizeHalf,0); //was new Vector3(x-xSizeHalf,0,y-ySizeHalf); but then the mesh had to be rotated 90* around X

                    // Save flat vertex
                    flatVertices[i] = pointOnPlane;

                    // Save direction vector from vertex position to estimated camera position
                    depthDirection[i] = (estimatedCameraPosition - flatVertices[i]).normalized;

                    i++;
                }
            }
        }
    }


    void UpdateVertexDepth()
    {    
        //each frame, set depthVertices location        
        for (int i = 0, y = 0; y <= currentDepthmapHeight; y++){
            for (int x = 0; x <= currentDepthmapWidth; x++){

                // Calculate Z-depth based on depthmap value and depth_multiplier
                if (i < currentDepthmapWidth * currentDepthmapHeight){
                    depth = (float)currentDepthmapValues.GetValue((y * currentDepthmapWidth) + x);
                    depth = depth * depth_multiplier; //multiply with user preference.
                }

//               // Check if the difference with the previous frame's depth value is within the threshold
//               if (Mathf.Abs(depth - previousDepthValues[i]) < stabilizationThreshold)
//               {
//                   // Average the values to prevent jittering
//                   // depth = (depth + previousDepthValues[i]) / 2.0f;
//               }

                // Update previous depth value
                previousDepthValues[i] = depth;

                //compensate for non-linearity in the relative depth models
                //works... not super well for depth_anything-v2
                depth = Mathf.Pow(depth, NonLinearityCompensationFactor);

                // Set vertex position
                depthVertices[i] = flatVertices[i] + depthDirection[i] * depth;
                i++;
            }
        }
    }

    void UpdateUVs(){
        uvs = new Vector2[depthVertices.Length];
        for (int i = 0, y = 0; y <= currentDepthmapHeight; y++){
            for (int x = 0; x <= currentDepthmapWidth; x++){
                //uvs[i] = new Vector2((float)x / depthmapWidth, (float)y / depthmapHeight);
                uvs[i] = new Vector2((float)x / currentDepthmapWidth, 1 - (float)y / currentDepthmapHeight); //inverted vertically
                i++;
            }
        }
        mesh.uv = uvs; //apply
    }

    private int ConstrainToMultipleOf(double size, int multipleOf){
        //depth_anything needs to be multiple of 14
        int constrainedSize = multipleOf*(int)Math.Floor(size/multipleOf);
        return constrainedSize; 
    }

    void CreateMesh(){
        CreateVertexCloud(); //creates new vertex cloud or updates current one, depending on input size
        UpdateVertexDepth();
        CreateTriangles();
        UpdateMesh(); //attaches the triangles to the vertexes        
    }

    void CreateTriangles(){
        //stopWatchCreateTriangles.Restart();
        mesh.Clear();
        triangles = new int[currentDepthmapWidth * currentDepthmapHeight * 6];
        int vertIndex = 0;
        int triIndex = 0;       

        for (int y = 0; y < currentDepthmapHeight; y++){
            for (int x = 0; x < currentDepthmapWidth; x++){
        
                //create triangle 1
                triangles[triIndex + 0] = vertIndex + 0;
                triangles[triIndex + 1] = vertIndex + currentDepthmapWidth + 1;
                triangles[triIndex + 2] = vertIndex + 1;
                //create triangle 2
                triangles[triIndex + 3] = vertIndex + 1;
                triangles[triIndex + 4] = vertIndex + currentDepthmapWidth + 1;
                triangles[triIndex + 5] = vertIndex + currentDepthmapWidth + 2;
            
                vertIndex++;
                triIndex += 6;
            }
            vertIndex++;
        }
        //stopWatchCreateTriangles.Stop();
    }

    private RenderTexture ConvertToRenderTexture(Texture2D texture)
    {
        RenderTexture rt = new RenderTexture(texture.width, texture.height, 24);
        Graphics.Blit(texture, rt);
        return rt;
    }

    public (float[] depthArray, float[] depthArrayNormalized, int width, int height)  inferDepthFromInput(RenderTexture image, bool normalize, bool smallImage = true){
        resizedImage = ResizeRenderTexture(image, smallImage);
        DepthGenerator depthGenerator = UnityEngine.Object.FindAnyObjectByType<DepthGenerator>();
        var depth = depthGenerator.GetDepth(resizedImage, normalize);
        return depth;        
    }

    private RenderTexture ResizeRenderTexture(RenderTexture sourceTexture, bool smallImage = true)
    {
        double originalWidth = sourceTexture.width;
        double originalHeight = sourceTexture.height;
        double largest = Math.Max(originalWidth, originalHeight);
        double smallest = Math.Min(originalWidth, originalHeight);

        double scale = smallImage ? expectedImageSize / largest : expectedImageSize / smallest;

        // Calculate new dimensions while preserving aspect ratio
        int newWidth = ConstrainToMultipleOf((int)(originalWidth * scale), ensureMultipleOf);
        int newHeight = ConstrainToMultipleOf((int)(originalHeight * scale), ensureMultipleOf);

        // Clamp dimensions if out of bounds
        newWidth = Mathf.Clamp(newWidth, minimumImageSize, maximumImageSize);
        newHeight = Mathf.Clamp(newHeight, minimumImageSize, maximumImageSize);

        // Calculate downsampling factor
        float downsampleFactor = Mathf.Max(1, Mathf.Min(
            Mathf.FloorToInt((float)originalWidth / newWidth),
            Mathf.FloorToInt((float)originalHeight / newHeight)
        ));

        // Check if the target RenderTexture needs to be recreated or reused
        if (resizedTexture == null || resizedTexture.width != newWidth || resizedTexture.height != newHeight)
        {
            if (resizedTexture != null)
            {
                resizedTexture.Release();
            }

            resizedTexture = new RenderTexture(newWidth, newHeight, 0, sourceTexture.format);
            resizedTexture.enableRandomWrite = true;
            resizedTexture.Create();
        }

        // Set shader parameters
        resizeMaterial.SetVector("_TexelSize", new Vector4(1.0f / (float)originalWidth, 1.0f / (float)originalHeight, (float)originalWidth, (float)originalHeight));
        resizeMaterial.SetFloat("_DownsampleFactor", downsampleFactor);

        // Perform the resize and optional downsample in one pass
        Graphics.Blit(sourceTexture, resizedTexture, resizeMaterial);

        return resizedTexture;
    }

    private RenderTexture ResizeRenderTextureBACKUP(RenderTexture sourceTexture, bool smallImage = true)
    {

        double originalWidth = sourceTexture.width;
        double originalHeight = sourceTexture.height;
        double largest = Math.Max(originalWidth,originalHeight);
        double smallest = Math.Min(originalWidth,originalHeight);

        double scale;
        
        // Calculate new dimensions while preserving aspect ratio
        if(smallImage){
            scale = expectedImageSize / largest; //scale largest to 518
        }else{
            scale = expectedImageSize / smallest; //scale smallest to 518
        }
   
        // Ensure dimensions are multiples of ensureMultipleOf
        int newWidth = ConstrainToMultipleOf((int)(originalWidth * scale), ensureMultipleOf);
        int newHeight = ConstrainToMultipleOf((int)(originalHeight* scale), ensureMultipleOf);

        // clamp them if either side is out of bounds
        if(newWidth < minimumImageSize)
            newWidth = minimumImageSize;
        if(newWidth > maximumImageSize)
            newWidth = maximumImageSize; 

        if(newHeight < minimumImageSize)
            newHeight = minimumImageSize;        
        if(newHeight > maximumImageSize)
            newHeight = maximumImageSize;

        // Check if the target RenderTexture needs to be recreated or reused
        if (resizedTexture == null || resizedTexture.width != newWidth || resizedTexture.height != newHeight)
        {
            if (resizedTexture != null)
            {
                resizedTexture.Release();
            }

            resizedTexture = new RenderTexture(newWidth, newHeight, 0, sourceTexture.format);
            resizedTexture.enableRandomWrite = true;
            resizedTexture.Create();
   //         UnityEngine.Debug.Log($"Created new RenderTexture with size: {newWidth} x {newHeight}");
        }
        
        Graphics.Blit(sourceTexture, resizedTexture);
       
        return resizedTexture;
    }

    public void ChangeDepthMultiplier(float amount){
        float depth_prev = depth_multiplier;
        depth_multiplier = (int)(depth_multiplier * amount);
        UpdateMesh();
    }

    public void makeFlat(float temporaryDepthMultiplier){
        // Start the coroutine to change depth_multiplier over time
        //StartCoroutine(ChangeDepthOverTime(flat ? 0 : depth_multiplier_saved, 1.0f)); // 1.0f is the duration in seconds
        // depth_multiplier_saved = depth_multiplier;
        StartCoroutine(ChangeDepthOverTime(temporaryDepthMultiplier * depth_multiplier_saved, 1.0f));
    }

    IEnumerator ChangeDepthOverTime(float targetDepth, float duration)
    {
        float time = 0;
        float startDepth = depth_multiplier;
        
        while (time < duration)
        {
            // Use SmoothStep for easing in and out
            depth_multiplier = Mathf.SmoothStep(startDepth, targetDepth, time / duration);
            
            // Update the mesh with the new depth_multiplier
            UpdateMesh();
            
            // Increment the time by the time passed since the last frame
            time += Time.deltaTime;
            
            // Wait until next frame to continue the loop
            yield return null;
        }
        
        // Ensure the final value is set precisely to the target depth
        depth_multiplier = targetDepth;
        UpdateMesh();
    }

    Texture2D toTexture2D(RenderTexture rTex)
    {
        if (tex != null)
        {
            Destroy(tex);
        }
        
        // Create a new Texture2D
        tex = new Texture2D(rTex.width, rTex.height, TextureFormat.RGB24, false);
        
        // / Read the RenderTexture contents into the Texture2D
        RenderTexture.active = rTex;
        tex.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
        tex.Apply();
        return tex;
    }

    public float[] TextureToFloatArray(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        float[] floatArray = new float[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            floatArray[i] = pixels[i].r; // Assuming you want the red channel
        }

        return floatArray;
    }

    //to move the mesh away from camera, f.i. when the menu is open
    public void moveAway(float duration){
        StartCoroutine(MoveCoroutine(movedScreenPosition, duration));
        
    }

    public void moveBack(float duration){
         StartCoroutine(MoveCoroutine(defaultScreenPosition, duration));
    }

// Coroutine for smooth movement over time with ease-out
    private IEnumerator MoveCoroutine(Vector3 targetPosition, float duration)
    {
        float elapsedTime = 0;

        Vector3 startingPosition = transform.position;

        while (elapsedTime < duration)
        {
            // Calculate interpolation factor with ease-out
            float t = Mathf.SmoothStep(0, 1, elapsedTime / duration);

            // Lerp between starting position and target position with ease-out
            transform.position = Vector3.Lerp(startingPosition, targetPosition, t);

            // Increment elapsedTime by the time between frames
            elapsedTime += Time.deltaTime;

            // Yield until next frame
            yield return null;
        }

        // Ensure final position is exactly target position
        transform.position = targetPosition;
    }

    void OnDestroy()
    {
        // Unsubscribe from events to avoid memory leaks
        // videoPlayer.prepareCompleted -= OnVideoPrepareCompleted;

        // Release RenderTexture resources
        /*
        if (renderTexture != null)
        {
            renderTexture.Release();
        }
        */

        // Destroy the Texture2D if it exists
        /*
        if (inputRGB != null)
        {
            Destroy(inputRGB);
        }
        */
    }
    
}