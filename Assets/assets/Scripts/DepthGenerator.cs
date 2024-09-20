using UnityEngine;
using Unity.Sentis;
using System.Linq;
using System;
using System.Diagnostics;
using System.Windows.Forms;

public class DepthGenerator : MonoBehaviour
{
    
    public ModelAsset depthModelDetailed;
    public ModelAsset depthModelFast;

    Model runtimemodel;
    Model runtimemodelDetailed;
    Model runtimemodelFast;

    bool fastWorker;
    public IWorker worker;
    Texture2D depthTexture;

    float[] DepthValuesPrev;
    float[] DepthValuesPrevNormalized;
    int widthPrev, heightPrev;
    RenderTexture depthRenderTexturePrev;
    RenderTexture depthRenderTexture;

    // public float historicalMinimum;
    // public float historicalMaximum;
    // public float minDepthValueDefault = 0f;
    //  float maxDepthValueDefault = 1f;

    void Start()
    {
        //load default model into memory and set flag
        runtimemodelFast = ModelLoader.Load(depthModelFast); //fast model is default for faster startup
        runtimemodelDetailed = ModelLoader.Load(depthModelDetailed);
        runtimemodel = runtimemodelFast;
        //fastWorker = false;

        //create default worker
        worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, runtimemodel);
    }

    public void ResetNormalisation(){ //gets called whenever a new image or video is to be displayed. an image gets normalized to 0-1. video frame as well, but the scope will be updated every frame (and never gets smaller to avoid flickering)
        //historicalMinimum = minDepthValueDefault;
        //historicalMaximum = maxDepthValueDefault;
    }

    public (float[] depthvalues, float[] depthValuesNormalized, int width, int height) GetDepth(RenderTexture input, bool normalize){
        //run depth_anything
        var inTensor = TextureConverter.ToTensor(input, channels:3);
        UnityEngine.Debug.Log($"inTensor created: {inTensor}");
        try
        {
            //use the model
            worker.Execute(inTensor);    

            //get output   
            var outTensor = worker.PeekOutput() as TensorFloat;
            outTensor.CompleteOperationsAndDownload();
            float[] depthArray = outTensor.ToReadOnlyArray();

            //cleanup Memory
            inTensor.Dispose();
            outTensor.Dispose();
            UnityEngine.Debug.Log("Tensors cleaned up");

            //values for normalisation, if used
            float currentMinimum = depthArray.Min();
            float currentMaximum = depthArray.Max();

            //store in 2 arrays.
            float[] depthValues = new float[depthArray.Length]; // raw float16 values for an accurate and stable video mesh 
            float[] depthValuesNormalized = new float[depthArray.Length]; // normalized float16 values, for images and for the shaders (mesh transparency and camera background colour fade)

            //apply useful ranges to the arrays
            for (int i = 0; i < depthArray.Length; i++) {

                //update the normalisation scope. Will be reset each new video / image
                //doesn't work
                /*
                if(currentMinimum < historicalMinimum){
                    historicalMinimum = currentMinimum;
                }
                if(currentMaximum < historicalMaximum){
                    historicalMaximum = currentMaximum;
                }
                */

                //normalize
                //depthValues[i] = (depthArray[i] - historicalMinimum) / (historicalMaximum - historicalMinimum);
                depthValuesNormalized[i] = (depthArray[i] - currentMinimum) / (currentMaximum - currentMinimum);

                //depthValues[i] = depthArray[i] / max; //normalize but don't set minvalue to 0
                // depthValues[i] = 1 - depthValues[i]; //invert depth

                
                if(normalize){
                    depthValues[i] = depthValuesNormalized[i];
                }else{
                    depthValues[i] = depthArray[i] / 25; //this number seems to work for depth_anything-v2 base
                }
                

                depthValues[i] = LinearizeDepth(depthValues[i], 1.2f);  

            }  

            //store current values, so they can be returned, incase next inference goes wrong. Tenshorshape somethingsomething
            DepthValuesPrev = depthValues;
            DepthValuesPrevNormalized = depthValuesNormalized;

            UnityEngine.Debug.Log($"depth min:{currentMinimum} max{currentMaximum}");
            return (depthValues, depthValuesNormalized, input.width, input.height);
        }
        catch (Exception ex)
        {
            return(DepthValuesPrev, DepthValuesPrevNormalized, input.width, input.height);
        }
    }

    public void useFastWorker(){
       if(!fastWorker){
           fastWorker = true;
           runtimemodel = runtimemodelFast;

        //  unload the other model. Seems like depth_anything-v2 models arent very big so commented out and leave both in memory
        //  DisposeTensorData();
        //  runtimemodel = ModelLoader.Load(depthModelFast);
        //  worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, runtimemodel);
           UnityEngine.Debug.Log($"loaded depthModelFast");
        }
    }

    public void useDetailedWorker(){
        if(fastWorker){
            fastWorker = false;
            runtimemodel = runtimemodelDetailed;

        //  unload the other model. Seems like depth_anything-v2 models arent very big so commented out and leave both in memory
        //  DisposeTensorData();
        //  runtimemodel = ModelLoader.Load(depthModelDetailed);
        //  worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, runtimemodel);
            UnityEngine.Debug.Log($"loaded depthModelDetailed");
       }  
    }

    float LinearizeDepth(float nonLinearDepth, float alpha = 5f)
    {
        return 1.0f - Mathf.Exp(-alpha * nonLinearDepth);
    }

    void DisposeTensorData(){
        worker.Dispose();
    }

    void OnDestroy(){
        DisposeTensorData();
        Destroy(depthTexture);
    }
}
