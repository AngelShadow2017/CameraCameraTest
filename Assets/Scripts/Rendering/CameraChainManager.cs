using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static class CameraChainManager
{
    // 当前帧内参与链的标签集合
    private static readonly HashSet<ProgressiveCameraTag> s_Tags = new HashSet<ProgressiveCameraTag>();

    // 跨 Camera 持久的颜色结果（上一相机的最终输出）
    private static UnityEngine.Rendering.RTHandle s_LastOutputRT;

    // 记录分配参数，避免重复分配
    private static int s_Width, s_Height;
    private static GraphicsFormat s_ColorFormat;
    private static int s_MSAA;
    private static bool s_InitedFrame = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        ResetState();
        RenderPipelineManager.beginFrameRendering += OnBeginFrameRendering;
        RenderPipelineManager.endFrameRendering += OnEndFrameRendering;
    }

    public static void Register(ProgressiveCameraTag tag)
    {
        s_Tags.Add(tag);
        SortAndAssignDepth();
    }

    public static void Unregister(ProgressiveCameraTag tag)
    {
        s_Tags.Remove(tag);
        SortAndAssignDepth();
    }

    public static bool IsCameraInChain(Camera cam)
    {
        if (!cam) return false;
        foreach (var t in s_Tags)
        {
            if (!t || !t.enabledInChain) continue;
            var c = t.GetComponent<Camera>();
            if (c == cam) return true;
        }
        return false;
    }

    private static void SortAndAssignDepth()
    {
        var ordered = s_Tags.Where(t => t && t.enabledInChain)
                            .OrderBy(t => t.order).ToList();
        for (int i = 0; i < ordered.Count; ++i)
        {
            var cam = ordered[i].GetComponent<Camera>();
            if (!cam) continue;
            cam.depth = i;
            if (ordered[i].disableSkybox)
            {
                // 建议仅清深度或清颜色为透明/黑色，天空盒关闭
                cam.clearFlags = CameraClearFlags.Color;
            }
        }
    }

    private static void OnBeginFrameRendering(ScriptableRenderContext _, Camera[] __)
    {
        // 每帧开始，重置上一相机输出引用（但不释放 RTHandle）
        s_InitedFrame = true;
        // 清空上一输出指针，意味着链中第一台相机不会使用背景板
        // 第二台及之后才会使用上一台结果
        // 注意：不释放 RTHandle 以复用内存（避免频繁分配）
        // 仅重置“是否有上一输出”的逻辑
        s_HasLastOutputThisFrame = false;
    }

    private static void OnEndFrameRendering(ScriptableRenderContext _, Camera[] __)
    {
        s_InitedFrame = false;
        // 本方案可选择保留 RTHandle（避免GC与重分配），如果对内存敏感，也可以在此释放：
        // ReleaseRT();
    }

    private static bool s_HasLastOutputThisFrame = false;

    public static bool HasLastOutputThisFrame()
    {
        return s_InitedFrame && s_HasLastOutputThisFrame && s_LastOutputRT != null;
    }

    public static UnityEngine.Rendering.RTHandle GetLastOutputRT()
    {
        return s_LastOutputRT;
    }

    public static void MarkHasLastOutput()
    {
        s_HasLastOutputThisFrame = true;
    }

    public static UnityEngine.Rendering.RTHandle EnsureSharedOutputRT(int width, int height, GraphicsFormat format, int msaa)
    {
        // 若匹配当前参数，则直接复用
        if (s_LastOutputRT != null && width == s_Width && height == s_Height && format == s_ColorFormat && msaa == s_MSAA)
            return s_LastOutputRT;

        ReleaseRT();

        s_Width = width;
        s_Height = height;
        s_ColorFormat = format;
        s_MSAA = Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, msaa),2));

        // 使用 RTHandles.Alloc 创建持久 RTHandle
        s_LastOutputRT = 
        RTHandles.Alloc(
            width, 
            height,
            colorFormat:format,
            msaaSamples:(MSAASamples)(1<<s_MSAA),
            name:"ProgressiveChain_LastCameraOutput",
            useDynamicScale:true
            );

        return s_LastOutputRT;
    }

    private static void ReleaseRT()
    {
        if (s_LastOutputRT != null)
        {
            UnityEngine.Rendering.RTHandles.Release(s_LastOutputRT);
            s_LastOutputRT = null;
        }
    }

    private static void ResetState()
    {
        s_Tags.Clear();
        s_InitedFrame = false;
        s_HasLastOutputThisFrame = false;
        ReleaseRT();
        s_Width = s_Height = 0;
        s_MSAA = 1;
        s_ColorFormat = GraphicsFormat.None;
    }
}
