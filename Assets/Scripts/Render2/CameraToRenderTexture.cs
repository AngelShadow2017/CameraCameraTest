using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CameraToRenderTexture : MonoBehaviour
{
    [Tooltip("作为背景源的相机。如果留空则使用本组件所在相机。")]
    public Camera sourceCamera;

    [Tooltip("采样分辨率缩放，1表示与相机分辨率一致。")]
    [Range(0.25f, 2f)]
    public float scale = 1f;

    [Tooltip("若相机允许HDR则使用半精度HDR格式。")]
    public bool preferHDR = true;

    [Tooltip("RT名称，便于调试。")]
    public string rtName = "CameraA_Output_RT";

    [Tooltip("目标相机（使用该纹理作为背景的相机B）。")]
    public Camera targetCamera;

    // 跨图形帧共享给其他相机使用的源贴图（B的背景贴图）
    public static RenderTexture SourceRT { get; private set; }
    
    // 共享的目标相机引用
    public static Camera TargetCamera { get; private set; }

    Camera _cam;

    void OnEnable()
    {
        _cam = sourceCamera ? sourceCamera : GetComponent<Camera>();
        CreateOrResizeRT();
        _cam.targetTexture = SourceRT;
        
        // 设置共享的目标相机引用
        TargetCamera = targetCamera;

        // 监听以便在分辨率变化时重建RT（比如屏幕或相机Viewport变化）
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        if (_cam != null && _cam.targetTexture == SourceRT)
            _cam.targetTexture = null;

        if (SourceRT != null)
        {
            SourceRT.Release();
            Destroy(SourceRT);
            SourceRT = null;
        }
        
        TargetCamera = null;
    }

    void OnValidate()
    {
        if (isActiveAndEnabled && _cam != null)
        {
            CreateOrResizeRT();
            _cam.targetTexture = SourceRT;
        }
    }

    void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        // 如果是源相机，并且分辨率或HDR状态有变化，则重建RT
        if (cam == _cam)
        {
            if (NeedsResize())
            {
                CreateOrResizeRT();
                _cam.targetTexture = SourceRT;
            }
        }
    }

    bool NeedsResize()
    {
        if (SourceRT == null) return true;
        if (targetCamera == null) return false;

        int targetW = Mathf.Max(1, Mathf.RoundToInt(targetCamera.pixelWidth * scale));
        int targetH = Mathf.Max(1, Mathf.RoundToInt(targetCamera.pixelHeight * scale));
        bool hdrDesired = preferHDR && _cam.allowHDR;

        bool sameSize = (SourceRT.width == targetW && SourceRT.height == targetH);
        bool sameHdr =
#if UNITY_6000_0_OR_NEWER
            (GraphicsFormatIsHDR(SourceRT.graphicsFormat) == hdrDesired);
#else
            (SourceRT.format == RenderTextureFormat.ARGBHalf) == hdrDesired;
#endif

        return !(sameSize && sameHdr);
    }

    void CreateOrResizeRT()
    {
        if (targetCamera == null) return;
        
        int w = Mathf.Max(1, Mathf.RoundToInt(targetCamera.pixelWidth * scale));
        int h = Mathf.Max(1, Mathf.RoundToInt(targetCamera.pixelHeight * scale));

#if UNITY_6000_0_OR_NEWER
        var colorSpaceLinear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        var useHDR = preferHDR && _cam.allowHDR;
        var gf = useHDR
            ? UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            : (colorSpaceLinear
                ? UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm
                : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);

        var desc = new RenderTextureDescriptor(w, h)
        {
            graphicsFormat = gf,
            depthBufferBits = 24,
            msaaSamples = 1,
            sRGB = !colorSpaceLinear, // 当在Gamma空间时开启sRGB
            useMipMap = false,
            autoGenerateMips = false
        };
#else
        var useHDR = preferHDR && _cam.allowHDR;
        var desc = new RenderTextureDescriptor(w, h,
            useHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, 0)
        {
            msaaSamples = 1,
            sRGB = QualitySettings.activeColorSpace == ColorSpace.Gamma,
            useMipMap = false,
            autoGenerateMips = false
        };
#endif

        var newRT = new RenderTexture(desc)
        {
            name = rtName
        };
        newRT.Create();

        if (SourceRT != null)
        {
            SourceRT.Release();
            Destroy(SourceRT);
        }
        SourceRT = newRT;
    }

#if UNITY_6000_0_OR_NEWER
    static bool GraphicsFormatIsHDR(UnityEngine.Experimental.Rendering.GraphicsFormat gf)
    {
        // 简单判断：是否是半浮点格式
        return gf == UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
            || gf == UnityEngine.Experimental.Rendering.GraphicsFormat.B10G11R11_UFloatPack32
            || gf == UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16_SFloat;
    }
#endif
}
