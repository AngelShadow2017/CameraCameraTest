using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public class CameraCaptureFeature : ScriptableRendererFeature
{
    class CapturePass : ScriptableRenderPass
    {
        private readonly ProfilingSampler m_Profiler = new ProfilingSampler("CaptureCameraFinalColor");
        private Material m_BlitMaterial;

        private class PassData
        {
            public TextureHandle source;      // 当前相机的最终颜色（activeColorTexture）
            public TextureHandle destination; // 导入的共享RT（上一相机输出持久纹理）
            public Material blitMat;
        }

        public CapturePass()
        {
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/BlitCopy"));
            // 在相机完成全部渲染（含后处理）后捕获
            renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var cam = cameraData.camera;
            
            if (cam == null) return;
            if (!ProgressiveCameraTag.IsProgressive(cam)) return;

            // 获取当前相机最终颜色句柄（URP RG中，activeColorTexture在AfterRendering时包含后处理结果）
            var srcHandle = resourceData.activeColorTexture;

            // 按当前相机的输出描述，分配/复用共享RT（跨Camera持久）
            var desc = cameraData.cameraTargetDescriptor;
            
            // 注意：URP的cameraTargetDescriptor可能用到默认格式（无图形格式），这里使用RenderTextureDescriptor转换为GraphicsFormat
            GraphicsFormat colorFormat = desc.graphicsFormat;
            if (colorFormat == GraphicsFormat.None)
            {
                // 合理回退：如果为None，用常见的8位RGBA sRGB格式
                colorFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            }

            var sharedRT = CameraChainManager.EnsureSharedOutputRT(desc.width, desc.height, colorFormat, desc.msaaSamples);
            var dstHandle = renderGraph.ImportTexture(sharedRT);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CaptureCameraFinalColor", out var passData, m_Profiler))
            {
                passData.source = srcHandle;
                passData.destination = dstHandle;
                passData.blitMat = m_BlitMaterial;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(passData.destination, 0);

                // 执行复制，把当前相机最终颜色保存到共享RT
                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    var src = data.source;
                    Blitter.BlitTexture(ctx.cmd, src, new Vector4(1, 1, 0, 0), data.blitMat, 0);
                    
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