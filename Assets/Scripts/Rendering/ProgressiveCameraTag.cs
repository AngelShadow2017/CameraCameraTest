using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[ExecuteAlways]
public class ProgressiveCameraTag : MonoBehaviour
{
    [Tooltip("链中的渲染顺序（越小越先渲染）")]
    public int order = 0;

    [Tooltip("参与渐进式渲染链")]
    public bool enabledInChain = true;

    [Tooltip("强制禁用天空盒（建议开启）")]
    public bool disableSkybox = true;

    void OnEnable()
    {
        CameraChainManager.Register(this);
    }

    void OnDisable()
    {
        CameraChainManager.Unregister(this);
    }

    public static bool IsProgressive(Camera cam)
    {
        return CameraChainManager.IsCameraInChain(cam);
    }
}
