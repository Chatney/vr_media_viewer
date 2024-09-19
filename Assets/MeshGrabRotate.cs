using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class MeshGrabRotate : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;
    private bool isGrabbed = false;
    private Quaternion initialRotation;
    private Quaternion initialInteractorRotation;
    private IXRSelectInteractor currentInteractor;
    private Vector3 initialPosition;

    private void Awake()
    {
        grabInteractable = gameObject.AddComponent<XRGrabInteractable>();
        grabInteractable.movementType = XRBaseInteractable.MovementType.Kinematic;
        grabInteractable.trackPosition = false;
        grabInteractable.trackRotation = false;

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        // Store the initial position
        initialPosition = transform.position;

        // Remove Rigidbody if it exists
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            Destroy(rb);
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        initialRotation = transform.rotation;
        currentInteractor = args.interactorObject;
        initialInteractorRotation = currentInteractor.transform.rotation;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isGrabbed = false;
        currentInteractor = null;
        // Reset position when released
        transform.position = initialPosition;
    }

    private void Update()
    {
        if (isGrabbed && currentInteractor != null)
        {
            RotateMesh();
        }
        else
        {
            // Ensure the mesh stays at its initial position when not grabbed
            transform.position = initialPosition;
        }
    }

    private void RotateMesh()
    {
        Quaternion rotationDelta = Quaternion.Inverse(initialInteractorRotation) * currentInteractor.transform.rotation;
        transform.rotation = rotationDelta * initialRotation;
    }
}