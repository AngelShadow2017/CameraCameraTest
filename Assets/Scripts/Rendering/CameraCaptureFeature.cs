using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CameraCaptureFeature : ScriptableRendererFeature
{
    class CapturePass : ScriptableRenderPass
    {
        private readonly ProfilingSampler m_Profiler = new ProfilingSampler("CaptureCameraFinalColor");
        private Material m_BlitMaterial;

        private class PassData
        {
            public TextureHandle source; // 当前相机的最终颜色（activeColorTexture）
            public TextureHandle destination; // 导入的共享RT（上一相机输出持久纹理）
            public Material blitMat;
        }

        public CapturePass()
        {
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/BlitCopy"));
            // 从 AfterRendering 改为 AfterRenderingPostProcessing，避免 activeColorTexture 指向 back buffer
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var cam = cameraData.camera;

            if (cam == null) return;
            if (!ProgressiveCameraTag.IsProgressive(cam)) return;

            // 如果此时 activeColorTexture 是 back buffer，不能作为可采样纹理，直接跳过
            if (resourceData.isActiveTargetBackBuffer) return;

            var srcHandle = resourceData.activeColorTexture;
            if (!srcHandle.IsValid()) return;
            // 分配/复用共享RT（跨 Camera 持久），强制 msaaSamples = 1，避免后续采样问题
            var desc = cameraData.cameraTargetDescriptor;

            GraphicsFormat colorFormat = desc.graphicsFormat;
            if (colorFormat == GraphicsFormat.None)
            {
                colorFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            }

            var sharedRT = CameraChainManager.EnsureSharedOutputRT(
                desc.width,
                desc.height,
                colorFormat,
                1 // 强制 MSAA 关闭，便于采样
            );

            var dstHandle = renderGraph.ImportTexture(sharedRT);

            using (var builder =
                   renderGraph.AddRasterRenderPass<PassData>("CaptureCameraFinalColor", out var passData, m_Profiler))
            {
                passData.source = srcHandle;
                passData.destination = dstHandle;
                passData.blitMat = m_BlitMaterial;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);

                // 执行复制，把当前相机最终颜色保存到共享RT
                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.blitMat, 0);
                    // 标记该帧已有上一输出，供下一个相机使用
                    CameraChainManager.MarkHasLastOutput();
                });
            }
        }

        protected void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_BlitMaterial);
        }
    }

    CapturePass m_Pass;

    public override void Create()
    {
        m_Pass = new CapturePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cam = renderingData.cameraData.camera;
        if (cam != null && ProgressiveCameraTag.IsProgressive(cam))
        {
            renderer.EnqueuePass(m_Pass);
        }
    }
}