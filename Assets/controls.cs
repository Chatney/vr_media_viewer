using UnityEngine;
using UnityEngine.InputSystem;

public class VRSlideshowController : MonoBehaviour
{
    private VRControls controls; // Reference to the generated C# class

    public SlideshowManager slideshowManager; // Reference to the slideshow manager
    public MeshGenerator deformableScreen; // Reference to the slideshow manager
    public DepthGenerator depthGenerator; // Reference to the slideshow manager

    private float navigateCooldown = 0.5f; // Cooldown period in seconds.
    private float lastNavigateTime;
    private bool isDetailedModel = false;

    private void Awake()
    {
        controls = new VRControls();

        //bind Action Mappings to Methods
        controls.SlideshowControls.ThumbstickMove_right.performed += OnThumbstickMove;
        controls.SlideshowControls.ThumbstickMove_left.performed += OnThumbstickMove; //left and right sticks do the same
        controls.SlideshowControls.SwitchModel.performed += OnSwitchModel;
        controls.SlideshowControls.Browser_back.performed += Browser_back;
        controls.SlideshowControls.Menu.performed += Menu;
        controls.SlideshowControls.Video_PlayPause.performed += Video_PlayPause;
    }

    private void OnEnable()
    {
        controls.SlideshowControls.Enable();
    }

    private void OnDisable()
    {
        controls.SlideshowControls.Disable();
    }

// Methods
    private void OnThumbstickMove(InputAction.CallbackContext context)
    {
        Vector2 thumbstickValue = context.ReadValue<Vector2>();

        // next / previous
        if (thumbstickValue.x > 0.5f)
        {
            if (Time.time - lastNavigateTime < navigateCooldown){return;}
            lastNavigateTime = Time.time;
            Debug.Log("Thumbstick Right");
            slideshowManager.NextMedia();
        }
        else if (thumbstickValue.x < -0.5f)
        {
            if (Time.time - lastNavigateTime < navigateCooldown){return;}
            lastNavigateTime = Time.time;
            Debug.Log("Thumbstick Left");
            slideshowManager.PreviousMedia();
        }

        // more / less depth
        float depthChangeSpeed = 1.1f; // Adjust the speed as needed

        if (thumbstickValue.y > 0.5f)
        {
            //float smallValue = 1 + thumbstickValue.y * depthChangeSpeed;
            deformableScreen.ChangeDepthMultiplier(depthChangeSpeed);
        }
        if (thumbstickValue.y < -0.5f)
        {
            //float smallValue = 1 + thumbstickValue.y * depthChangeSpeed;
            deformableScreen.ChangeDepthMultiplier(1 / depthChangeSpeed);
        }
    }


    private void OnSwitchModel(InputAction.CallbackContext context){
        Debug.Log("Switching model");
        if (Time.time - lastNavigateTime < navigateCooldown){return;}

        slideshowManager.PauseVideo();
        if(isDetailedModel){
            isDetailedModel = false;
            depthGenerator.useFastWorker();
        }else{
            depthGenerator.useDetailedWorker();
            isDetailedModel = true;
        }
        //slideshowManager.ReloadImage();
        lastNavigateTime = Time.time;
        slideshowManager.PlayVideo(0.25f);
    }

    private void Browser_back(InputAction.CallbackContext context){
        Debug.Log("Browser back");
        if (Time.time - lastNavigateTime < navigateCooldown){return;}
        slideshowManager.NavigateUp();
        lastNavigateTime = Time.time;
    }

    private void Video_PlayPause(InputAction.CallbackContext context){
        slideshowManager.PlayPauseVideo();
    }

    private void Menu(InputAction.CallbackContext context){
        slideshowManager.ShowHideMenu();
    }











}
