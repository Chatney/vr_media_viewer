using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;
using Unity.VisualScripting;
using UnityEngine.XR.Interaction.Toolkit;


//ToDo:
/*
-less dumb interface, or at least make the scroll rect position reset when switching folders
-make pause work without empty screen (it works when pressing the time scrubbar first.. somehow..)
-why is 4k video so slow, because it shouldn't matter
-remove the interface movement. Just overlay the canvas - even if the depth doesn't make sense
-favorite folders
-does the folder structure work for linux, android, mac?
*/

public class SlideshowManager : MonoBehaviour
{
    public GameObject drivesPanel; // Panel to display available drives
    public GameObject folderPanel; // Panel to display folders
    public GameObject folderButtonPrefab; // Prefab for folder buttons
    public GameObject fileButtonPrefab; // Prefab for file buttons
    public Renderer targetRenderer; // Renderer of the game object to apply the texture to
    public Button upButton; // Button to navigate up one folder
    public Button quitButton; // Button to navigate up one folder
    public Slider timelineSlider;
    public GameObject leftController; // Reference to the left VR controller
    public GameObject rightController; // Reference to the right VR controller
    public float depthmapUpdateRate = 15f; //depthmap FPS. is independant from video FPS

    public float defaultWaitTime = 1.0f / 60.0f; // Default to 1/60 for 60 FPS
    private float currentWaitTime;

    private XRRayInteractor leftRay;
    private XRRayInteractor rightRay;

    public Transform cameraTransform; // Reference to the VR camera transform
    private Vector3 menuOffsetPosition; // relative to world space
    private Quaternion menuRotation;
    private Vector3 originalPosition; // To store the original position of the menu
    private Coroutine videoFrameCoroutine;
    private Coroutine updateCoroutine;
    private bool isUserInteracting = false;

    private int errorCount = 0;
    private const int MAX_ERRORS = 10; // Adjust this value as needed
    private bool isPaused = false;
    private bool isSubscribedToFrameReady = false;


    //public Button nextButton; // Button to navigate to the next image
    //public Button previousButton; // Button to navigate to the previous image
    //public Material videoMaterial;
    //public Material imageMaterial;
    private VideoPlayer videoPlayer;    
    public MeshGenerator meshGenerator;
    public DepthGenerator depthGenerator;
    private bool isVideoFrameCoroutineRunning = false;
    private RenderTexture lastFrameTexture;

    //UI colors
    Color driveColor = new Color32(135, 135, 118, 231);
    Color folderColor = new Color32(189, 190, 58, 231);
    Color videoColor = new Color32(47, 166, 173, 231);
    Color imageColor = new Color32(47, 137, 173, 231);

    //Set button size
    int driveHeight = 40;
    int driveWidth = 80;
    int folderHeight = 40;
    int folderWidth = 1000;
    int fileHeight = 40;
    int fileWidth = 1000;

    
    Texture2D texture;
    Texture2D texturePrevious;

    private string[] compatibleFiles;
    private readonly string[] supportedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp"};
    private readonly string[] supportedVideoExtensions = new[] {".mp4", ".mov", ".avi", ".wmv", ".asf", ".dv", ".m4v", ".mpg", ".mpeg", ".ogv", ".vp8", ".webm"};
    private readonly string[] supportedSpatialVideoExtensions = new[] {".r3d"}; //ToDo!
    private string[] supportedExtensions;

    private int currentIndex = 0;
    private string currentFolder;

    bool MenuVisible = true;

    private ScrollRect folderScrollRect;

    void Start()
    {   
        depthGenerator.useDetailedWorker(); //detailed worker for splash image

        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.isLooping = true;
        videoPlayer.sendFrameReadyEvents = true;
        //videoPlayer.skipOnDrop = false; //slows down video instead of skipping horribly  

        leftRay = leftController.GetComponent<XRRayInteractor>();
        rightRay = rightController.GetComponent<XRRayInteractor>();   

        HideTimeline();   
        
        // Store the initial offset of the menu relative to the camera
        menuOffsetPosition = transform.position - cameraTransform.position;  

        supportedExtensions = supportedImageExtensions.Concat(supportedVideoExtensions).ToArray();

        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.Apply(false, false);
        texturePrevious = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texturePrevious.Apply(false, false);

        upButton.onClick.AddListener(NavigateUp);
        quitButton.onClick.AddListener(QuitApplication);

        ListDrives();
    }

    void Update()
    {
        if(MenuVisible){
            transform.position = cameraTransform.position + menuOffsetPosition;
        }

        //video events
        if (videoPlayer == null)
        {
            Debug.LogError("VideoPlayer component is missing!");
            return;
        }
        if (videoPlayer.isPlaying && !isSubscribedToFrameReady)
        {
            Debug.Log($"Not subscribed to new video frames yet, subscribing");

            SubscribeToFrameReady(); //this displays the frame
        }
//        if (!videoPlayer.isPlaying && isSubscribedToFrameReady)
//        {
//            Debug.Log($"Subscribed but not playing, Unsubscribing");
//            UnsubscribeFromFrameReady();
//        }

    }

    public void ListDrives()
    {
        bool isFirstDrive = true; // Flag to check if the current drive is the first one

        foreach (Transform child in drivesPanel.transform)
        {
            Destroy(child.gameObject);
        }

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                GameObject button = Instantiate(folderButtonPrefab, drivesPanel.transform);

                // Set size
                RectTransform rectTransform = button.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    rectTransform.sizeDelta = new Vector2(driveWidth, driveHeight);
                }

                // Set text to drive name
                TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();
                if (buttonText != null)
                {
                    buttonText.text = drive.Name;
                    //Debug.Log(drive.Name);
                }

                // Set color
                Image buttonImage = button.GetComponent<Image>();
                if (buttonImage != null)
                {                    
                    buttonImage.color = driveColor;
                }

                // Do something
                button.GetComponent<Button>().onClick.AddListener(() => OnDriveClick(drive.Name));

                // Automatically click the first drive
                if (isFirstDrive)
                {
                    OnDriveClick(drive.Name);
                    isFirstDrive = false; // Set flag to false after the first drive is clicked
                }
            }
        }
    }

    void QuitApplication()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    public void OnDriveClick(string drivePath)
    {
        currentFolder = drivePath;
        ListDirectoriesAndFiles(currentFolder);
        compatibleFiles = GetCompatibleFiles(currentFolder);
        resetScrollPosition();
    }
    
    public void ListDirectoriesAndFiles(string path)
    {
        // Clear previous contents
        foreach (Transform child in folderPanel.transform)
        {
            Destroy(child.gameObject);
        }

        string extendedPath = GetExtendedPath(path);

        try
        {
            // List directories
            string[] directories = Directory.GetDirectories(extendedPath);
            foreach (string directory in directories)
            {
                GameObject button = Instantiate(folderButtonPrefab, folderPanel.transform);

                // Set size
                RectTransform rectTransform = button.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    rectTransform.sizeDelta = new Vector2(folderWidth, folderHeight);
                }

                // Set color
                Image buttonImage = button.GetComponent<Image>();
                if (buttonImage != null)
                {                    
                    buttonImage.color = folderColor;
                }

                TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();

                if (buttonText != null)
                {
                    buttonText.text = Path.GetFileName(directory);
                }

                button.GetComponent<Button>().onClick.AddListener(() => OnFolderClick(directory));
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"Error listing directories: {ex.Message}");
        }

        try
        {
            // List files
            string[] files = Directory.GetFiles(extendedPath)
                                      .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                                      .ToArray();

            foreach (string file in files)
            {
                GameObject button = Instantiate(fileButtonPrefab, folderPanel.transform);

                // Set size
                RectTransform rectTransform = button.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    rectTransform.sizeDelta = new Vector2(fileWidth, fileHeight);
                }

                // Set color
                Image buttonImage = button.GetComponent<Image>();
                if (buttonImage != null)
                {
                    if(IsVideoFile(file)){
                        buttonImage.color = videoColor;
                    }else{
                        buttonImage.color = imageColor;
                    }
                }

                TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();

                if (buttonText != null)
                {
                    buttonText.text = Path.GetFileName(file);
                }

                button.GetComponent<Button>().onClick.AddListener(() => OnFileClick(file));
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"Error listing files: {ex.Message}");
        }
    }

    void resetScrollPosition(){
        //doesn't work?
        Canvas.ForceUpdateCanvases();
        folderScrollRect = folderPanel.GetComponent<ScrollRect>();
        
        if (folderScrollRect != null)
        {
            folderScrollRect.verticalNormalizedPosition = 1.0f; // Scroll to the top
        }
        else
        {
            Debug.Log("folderScrollRect component is not assigned.");
        }
    }

    private string GetExtendedPath(string path)
    {
        // Ensure the path is absolute and add the extended-length path prefix if needed
        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        if (!path.StartsWith(@"\\?\"))
        {
            path = @"\\?\" + path;
        }

        return path;
    }

public void OnFolderClick(string path)
    {
        currentFolder = path;
        ListDirectoriesAndFiles(currentFolder);
        compatibleFiles = GetCompatibleFiles(currentFolder);
        resetScrollPosition();

        if (compatibleFiles.Length > 0)
        {
            currentIndex = 0;
            //Display3DImageOrVideo(compatibleFiles[currentIndex]); // Load the first compatible file (be it image or video)
        }
        else
        {
            Debug.LogWarning("No compatible files found in the folder.");
        }
    }

    private string[] GetCompatibleFiles(string path)
    {
        //Debug.Log("GetCompatibleFiles");
        return Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                        .ToArray();
    }

    private string previousPath;
    public void OnFileClick(string path)
    {
        // Is it a new file?
        if(path == previousPath){
            return; //same file, do nothing
        }

        // Find the index of the clicked file in imageFiles array
        currentIndex = Array.IndexOf(compatibleFiles, path);

        //and display the image
        Display3DImageOrVideo(path);
    }

    public void Display3DImageOrVideo(string path)
    {
        string extendedPath = GetExtendedPath(path);
        StopVideo(); //stop video player if still playing

        DepthGenerator depthGenerator = UnityEngine.Object.FindAnyObjectByType<DepthGenerator>();
        depthGenerator.ResetNormalisation();

        try
        {
            // Check if the path is a video file
            if (IsVideoFile(extendedPath))
            {
                Display3dVideo(extendedPath);
            }
            else
            {
                UnsubscribeFromFrameReady();
                videoPlayer.Stop();
                HideTimeline();
                Display3dImage(extendedPath);
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"Error displaying 3D image or video: {ex.Message}");
            //NextImage(); // Skip to the next image or video
        }
    }

    private bool IsVideoFile(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        return supportedVideoExtensions.Contains(ext);
    }

    private void Display3dImage(string path)
    {        
        depthGenerator.useDetailedWorker();        
        string extendedPath = GetExtendedPath(path);
        RenderTexture currentImage = LoadImageToRenderTexture(extendedPath);
        
        if (meshGenerator != null)
        {   
            RenderTexture.active = currentImage;
            Display(currentImage, true, false, 1);
        }
    }

    private void Display3dVideo(string path)
    {
        //ResetVideoPlayer();        
        //ShowTimeline();
        //InitializeTimeline();
        depthGenerator.useFastWorker();        
        string extendedPath = GetExtendedPath(path);
        
        PrepareNewVideo(extendedPath);
    }

    private void OnVideoPlayerPrepared(VideoPlayer source)
    {
        Debug.Log("Video prepared successfully");
        source.prepareCompleted -= OnVideoPlayerPrepared;
        ShowTimeline();
        InitializeTimeline();
        PlayVideo(1f);
        videoPlayer.time = 0f; //does this fix the pause issue? MJEH! 
    }
    
    private void InitializeTimeline(){
        timelineSlider.onValueChanged.AddListener(HandleSliderChange); //to handle user timeline input
        updateCoroutine = StartCoroutine(UpdateTimelineSlider()); //to update the timeline slider position every x seconds

        // Initialize slider
        timelineSlider.minValue = 0;
        timelineSlider.maxValue = (float)videoPlayer.length;

        // Set the current value to match the video's current time
        timelineSlider.value = (float)videoPlayer.time;
    }

    private IEnumerator UpdateTimelineSlider()
    {
        while (true)
        {
            if (videoPlayer.isPlaying && !isUserInteracting)
            {
                timelineSlider.SetValueWithoutNotify((float)videoPlayer.time);
            }
            yield return new WaitForSeconds(5f); // Update every x seconds
        }
    }

    public void OnTimelinePointerDown()
    {
        isUserInteracting = true;
    }

    public void OnTimelinePointerUp()
    {
        isUserInteracting = false;
    }

    private void ShowTimeline(){
        timelineSlider.gameObject.SetActive(true);
        InitializeTimeline();
    }

    private void HideTimeline(){
        //hide
        timelineSlider.gameObject.SetActive(false);

        //and stop the coroutines
        if (updateCoroutine != null)
        {
            StopCoroutine(updateCoroutine);
        }
        timelineSlider.onValueChanged.RemoveListener(HandleSliderChange);
    }

    private void HandleSliderChange(float value)
    {
        // Seek the video player to the slider's value
        videoPlayer.time = value;
    }

    private IEnumerator UpdateVideoFrame()
    {
        while (videoPlayer.isPlaying)
        {
            if(videoPlayer.isPlaying){
                RenderTexture currentFrame = videoPlayer.texture as RenderTexture;

                //limit video fps for performance reasons?
                if(MathF.Max(currentFrame.width, currentFrame.height) > 1920){
                    videoPlayer.playbackSpeed = 1f;
                    yield return new WaitForSeconds(0.05f);
                }else{
                    videoPlayer.playbackSpeed = 1f;
                    yield return new WaitForSeconds(0.05f);
                }

                if (videoPlayer.texture != null)
                {             
                    Display(currentFrame, false, true, 10);
                }
            }       
         }
    //    videoFrameCoroutine = null;
    }

    void OnNewFrameReady(VideoPlayer source, long frameIdx)
    {
        UnityEngine.Debug.Log("OnNewFrameReady event triggered");
        if (source.texture != null)
        {             
            RenderTexture currentFrame = source.texture as RenderTexture;
            Display(currentFrame, true, true, 10);
        }
    }

    private void OnSeekCompleted(VideoPlayer source)
    {
        AdjustWaitTime();
    }

    private void OnErrorReceived(VideoPlayer source, string message)
    {
        AdjustWaitTime();
    }

    private void AdjustWaitTime() //testing to see if this can lower the depthmapFPS, incase of bad performance
    {
        if (currentWaitTime > 1.0f / 30.0f)
        {
            currentWaitTime /= 2.0f;
        }
        else
        {
            currentWaitTime = defaultWaitTime;
        }
    }

    void SubscribeToFrameReady()
    {
        videoPlayer.frameReady += OnNewFrameReady;
        isSubscribedToFrameReady = true;
    }

    void UnsubscribeFromFrameReady()
    {
        videoPlayer.frameReady -= OnNewFrameReady;
        isSubscribedToFrameReady = false;
    }

    private void ResetVideoPlayer()
    {
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }
        
        videoPlayer.url = "";
        videoPlayer.prepareCompleted -= OnVideoPlayerPrepared;
        videoPlayer.errorReceived -= OnVideoPlayerError;
        
        // Clear the render texture if it's being used
        if (videoPlayer.targetTexture != null)
        {
            videoPlayer.targetTexture.Release();
            videoPlayer.targetTexture = null;
        }
        
        // Reset any other relevant properties
        isPaused = false;
        StopAllCoroutines();
        
        // Clear the last frame texture if it exists
        if (lastFrameTexture != null)
        {
            lastFrameTexture.Release();
            Destroy(lastFrameTexture);
            lastFrameTexture = null;
        }
        
        // Reset the timeline slider if it exists
        if (timelineSlider != null)
        {
            timelineSlider.value = 0;
        }

        // Reset the videoplayer speed to default
       //  videoPlayer.playbackSpeed = 1f;
    }

    private void PrepareNewVideo(string path)
    {
        ResetVideoPlayer();
        
        videoPlayer.url = path;
        videoPlayer.prepareCompleted += OnVideoPlayerPrepared;
        videoPlayer.errorReceived += OnVideoPlayerError;
        // videoPlayer.targetTexture = new RenderTexture(width, height, 24);
        
        videoPlayer.Prepare();
    }

    private void OnVideoPlayerError(VideoPlayer vp, string message)
    {
        Debug.LogError($"VideoPlayer Error: {message}");
        ResetVideoPlayer();
        NextMedia(); // Move to the next media item
    }

    public void Display(RenderTexture rendertextureRGB, bool normalize, bool smallImage, int framesToAvarageForBackgroundColorFade){
        var depthInformation = meshGenerator.inferDepthFromInput(rendertextureRGB, normalize, smallImage);             
        meshGenerator.CreateOrUpdateScreen(depthInformation.depthArray, depthInformation.width, depthInformation.height);
        meshGenerator.ApplyTexturesToScreen(rendertextureRGB, depthInformation.depthArrayNormalized, depthInformation.width, depthInformation.height, framesToAvarageForBackgroundColorFade);
    }

    public void NavigateUp()
    {
        if(!MenuVisible){ //if the menu is hidden, don't navigate up
            ShowMenu(); //show the menu instead
            return;
        }
 
        if (currentFolder == Directory.GetDirectoryRoot(currentFolder))
        {
            Debug.Log("Already at the root directory. Cannot navigate up.");
            return; //do nothing
        }
        try
        {
            DirectoryInfo parentDirectory = Directory.GetParent(currentFolder);
            if (parentDirectory != null)
            {
                currentFolder = parentDirectory.FullName;
                ListDirectoriesAndFiles(currentFolder);
            }
            else
            {
                Debug.Log("No parent directory found.");
            }
        }
        catch (Exception ex)  // Catching a more general exception
        {
            Debug.Log($"Error while navigating up: {ex.Message}");
        }
    }

    public void ReloadImage(){
        if (compatibleFiles == null || compatibleFiles.Length == 0) return;
        Display3DImageOrVideo(compatibleFiles[currentIndex]);
    }

    public void NextMedia()
    {
        if (compatibleFiles == null || compatibleFiles.Length == 0)
        {
            Debug.LogWarning("No compatible files loaded.");
            return;
        }
        errorCount = 0; // Reset error count at the start of navigation
        TryDisplayNextMedia();
    }

    private void TryDisplayNextMedia()
    {
        if (errorCount >= MAX_ERRORS || compatibleFiles.Length == 0)
        {
            Debug.LogWarning("Too many errors or no files left. Stopping navigation.");
            return;
        }

        try
        {
            currentIndex = (currentIndex + 1) % compatibleFiles.Length;
            Display3DImageOrVideo(compatibleFiles[currentIndex]);
            errorCount = 0; // Reset error count on successful display
        }
        catch (Exception ex)
        {
            Debug.Log($"Error processing file: {ex.Message}");
            RemoveCurrentFile();
            errorCount++;
            TryDisplayNextMedia(); // Recursively try the next file
        }
    }

    private void RemoveCurrentFile()
    {
        if (compatibleFiles.Length > 0)
        {
            string removedFile = compatibleFiles[currentIndex];
            compatibleFiles = compatibleFiles.Where((source, index) => index != currentIndex).ToArray();

            // Adjust currentIndex if necessary
            if (currentIndex >= compatibleFiles.Length)
            {
                currentIndex = compatibleFiles.Length > 0 ? compatibleFiles.Length - 1 : 0;
            }

            Debug.Log($"Removed file from list: {removedFile}");
        }
    }

    public void PreviousMedia()
    {
        if (compatibleFiles == null || compatibleFiles.Length == 0)
        {
            Debug.LogWarning("No compatible files loaded.");
            return;
        }

        errorCount = 0; // Reset error count at the start of navigation
        TryDisplayPreviousMedia();
    }

    private void TryDisplayPreviousMedia()
    {
        if (errorCount >= MAX_ERRORS || compatibleFiles.Length == 0)
        {
            Debug.LogWarning("Too many errors or no files left. Stopping navigation.");
            return;
        }

        try
        {
            currentIndex = (currentIndex - 1 + compatibleFiles.Length) % compatibleFiles.Length;
            Display3DImageOrVideo(compatibleFiles[currentIndex]);
            errorCount = 0; // Reset error count on successful display
        }
        catch (Exception ex)
        {
            Debug.Log($"Error processing file: {ex.Message}");
            RemoveCurrentFile();
            errorCount++;
            TryDisplayPreviousMedia(); // Recursively try the previous file
        }
    }

    public RenderTexture LoadImageToRenderTexture(string extendedPath)
    {
        // Load image file data into a Texture2D
        byte[] fileData = File.ReadAllBytes(extendedPath);

        //Load new one
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(fileData);

        // Create a RenderTexture with the same dimensions as the Texture2D
        RenderTexture renderTexture = new RenderTexture(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        renderTexture.Create();

        // Copy the Texture2D to the RenderTexture
        Graphics.Blit(texture, renderTexture);

        // Clean up the Texture2D if no longer needed
        Destroy(texture);

        return renderTexture;
    }
    
    public void PlayPauseVideo()
    {
        if (videoPlayer.isPrepared)
        {
            if (videoPlayer.isPlaying)
            {
                //depthGenerator.useDetailedWorker();  
                PauseVideo();
                
            }
            else
            {
                //depthGenerator.useFastWorker();  
                PlayVideo(1f);
            }
        }
    }

    public void PlayVideo(float speed)
    {
        if (!videoPlayer.isPrepared)
        {
            Debug.LogWarning("Video is not prepared yet.");
            return;
        }
        videoPlayer.playbackSpeed = speed;
        videoPlayer.Play();
        Debug.Log("Video playback started");
    }

    public void PauseVideo()
    {
        //float currentTime = (float)videoPlayer.time;
        videoPlayer.Pause();

        StopCoroutine(UpdateVideoFrame());
//        isPaused = true;
        if(!videoPlayer.isPlaying || videoPlayer.texture == null){
            return;
        }
        //RenderTexture currentFrame = videoPlayer.texture as RenderTexture;
        //videoPlayer.time = currentTime;
        //Display(currentFrame, false, true, 10);

        

    //    DisplayLastFrameTexture();
        //videoPlayer.playbackSpeed = 0f;
    }

    public void StopVideo()    
    {
//        if (videoFrameCoroutine != null)
//        {
//            StopCoroutine(videoFrameCoroutine);
//            videoFrameCoroutine = null;
//            //StopCoroutine(UpdateVideoFrame());
//        }
        //unsubscribe from new frames
        UnsubscribeFromFrameReady();
        videoPlayer.Stop();
        isPaused = false;

        // Release the lastFrameTexture
 //      if (lastFrameTexture != null)
 //      {
 //          lastFrameTexture.Release();
 //          Destroy(lastFrameTexture);
 //          lastFrameTexture = null;
 //      }

        // Clear the display
        // Display(null, false, true, 10);
        // DisplayLastFrameTexture();
    }
    
    public void SkipVideo(int seconds)
    {
        if (videoPlayer.isPrepared && videoPlayer.isPlaying || videoPlayer.isPrepared && videoPlayer.isPaused)
        {
            videoPlayer.time += seconds;
        }
    }

    private void DisplayLastFrameTexture()
    {
        if (videoPlayer.texture == null)
        {
            Debug.LogWarning("No video texture available.");
            return;
        }

        // Ensure lastFrameTexture is created and up to date
        if (lastFrameTexture == null || lastFrameTexture.width != videoPlayer.texture.width || lastFrameTexture.height != videoPlayer.texture.height)
        {
            if (lastFrameTexture != null)
            {
                lastFrameTexture.Release();
                Destroy(lastFrameTexture);
            }
            lastFrameTexture = new RenderTexture(videoPlayer.texture.width, videoPlayer.texture.height, 24, RenderTextureFormat.ARGB32);
            lastFrameTexture.Create();
        }

        // Copy the current frame to our lastFrameTexture
        Graphics.Blit(videoPlayer.texture, lastFrameTexture);

        // Display the lastFrameTexture
        Display(lastFrameTexture, false, true, 10);
    }

    public void ScrubVideo(float scrubAmount)
    {
        if (videoPlayer == null || !videoPlayer.isPlaying)
            return;

        // Calculate new time
        double newTime = videoPlayer.time + scrubAmount;

        // Clamp within bounds
        newTime = Mathf.Clamp((float)newTime, 0f, (float)videoPlayer.length);

        // Set new time
        videoPlayer.time = newTime;

        // Optionally update video player state after scrubbing
        if (!videoPlayer.isPlaying)
        {
            videoPlayer.Play();
        }
    }

    public void ShowHideMenu(){
        if(MenuVisible){
            HideMenu();            
        }else{            
            ShowMenu();
        }
    }

    public void HideMenu()
    {
        MenuVisible = false;

        transform.position = cameraTransform.position - cameraTransform.forward * 5; // Move it behind the camera
        MenuVisible = false;

        meshGenerator.makeFlat(1f);
        meshGenerator.moveBack(1f);

        leftRay.enabled = false;
        rightRay.enabled = false;
    }

    public void ShowMenu()
    {
        MenuVisible = true;
        //videoPlayer.Stop();        
                
        transform.position = cameraTransform.position + menuOffsetPosition; // Move the menu in front of the camera
        MenuVisible = true;  

        meshGenerator.makeFlat(0.2f);          
        meshGenerator.moveAway(1f);

        leftRay.enabled = true;
        rightRay.enabled = true;
    }

    private void CleanupCurrentMedia()
    {
        StopVideo();
        if (lastFrameTexture != null)
        {
            lastFrameTexture.Release();
            Destroy(lastFrameTexture);
            lastFrameTexture = null;
        }
        // Add any other cleanup needed for images or other resources
    }

    private void StopAllCoroutines()
    {
        if (videoFrameCoroutine != null)
        {
            StopCoroutine(videoFrameCoroutine);
            videoFrameCoroutine = null;
        }
        if (updateCoroutine != null)
        {
            StopCoroutine(updateCoroutine);
            updateCoroutine = null;
        }
        // Stop any other coroutines here
    }

}
