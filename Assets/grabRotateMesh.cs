using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class grabRotateMesh : MonoBehaviour
{
    bool isGrabbing = false;
    public GameObject mesh;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    void Update() 
    {
        // Assuming 'controller' is your VR controller gameObject
        if (isGrabbing) // 'isGrabbing' should be set based on your input logic
        {
            transform.rotation = this.transform.rotation;
            mesh.transform.SetParent(this.transform);
        }else{
            mesh.transform.SetParent(null);
        }
    }

    public void Isgrabbing(bool yes){
        if(yes){
            isGrabbing = true;
        }else{
            isGrabbing = false;
        }
    }
}
