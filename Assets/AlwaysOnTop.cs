using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public class AlwaysOnTop : MonoBehaviour
{
    [SerializeField] private bool alwaysOnTop = true;
    private const int TopMostRenderQueue = 4000;
    private Canvas canvas;
    private Graphic[] graphics;

    private void Awake()
    {
        InitializeComponents();
    }

    private void OnValidate()
    {
        // Ensure components are initialized before setting render order
        if (canvas == null || graphics == null)
        {
            InitializeComponents();
        }
        SetRenderOrder(alwaysOnTop);
    }

    private void Start()
    {
        SetRenderOrder(alwaysOnTop);
    }

    private void InitializeComponents()
    {
        canvas = GetComponent<Canvas>();
        graphics = GetComponentsInChildren<Graphic>(true);
    }

    public void SetRenderOrder(bool onTop)
    {
        alwaysOnTop = onTop;
        
        if (canvas != null)
        {
            canvas.overrideSorting = onTop;
            canvas.sortingOrder = onTop ? 32767 : 0;
        }

        if (graphics != null)
        {
            foreach (var graphic in graphics)
            {
                if (graphic != null && graphic.material != null)
                {
                    if (onTop)
                    {
                        graphic.material.renderQueue = TopMostRenderQueue;
                    }
                    else
                    {
                        graphic.material.renderQueue = -1; // Reset to default
                    }
                }
            }
        }
    }
}