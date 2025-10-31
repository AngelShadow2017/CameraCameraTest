using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class ProgressiveMultiCameraFeature : ScriptableRendererFeature
{
    class ProgressiveBackgroundPass : ScriptableRenderPass
    {
        private readonly ProfilingSampler m_Profiler = new ProfilingSampler("ProgressiveBackground");
        private Material m_BlitMaterial;

        // RenderGraph 传递的数据
        private class PassData
        {
            public TextureHandle source;      // 上一相机的最终颜色（通过 ImportTexture）
            public TextureHandle destination; // 当前相机的 activeColorTexture
            public Material blitMat;
        }

        public ProgressiveBackgroundPass()
        {
            // 使用引擎自带Blit材质（你也可以用自定义纯拷贝材质）
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/BlitCopy"));
            // 在不清除目标的情况下直接覆盖像素，这样相机的背景就来自上一相机
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var cam = cameraData.camera;
            if (cam == null) return;
            if (!ProgressiveCameraTag.IsProgressive(cam)) return;

            // 第一台参与链的相机没有"上一输出"，直接跳过
            if (!CameraChainManager.HasLastOutputThisFrame()) return;

            // 导入上一相机结果作为 RG 的输入纹理
            var lastRT = CameraChainManager.GetLastOutputRT();
            if (lastRT == null) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("ProgressiveBackground", out var passData, m_Profiler))
            {
                passData.source = renderGraph.ImportTexture(lastRT);
                passData.destination = resourceData.activeColorTexture;
                passData.blitMat = m_BlitMaterial;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(passData.destination, 0);

                // 全屏拷贝：把上一相机结果写到当前的activeColorTexture
                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    // 分析：不需要手动设置RenderTarget，因为SetRenderAttachment已经自动设置了
                    // RenderGraph会自动将目标纹理绑定到渲染管线的当前渲染目标
                    
                    // 直接进行全屏复制
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.blitMat, 0);
                });
            }
        }

        protected void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_BlitMaterial);
        }
    }

    ProgressiveBackgroundPass m_Pass;

    public override void Create()
    {
        m_Pass = new ProgressiveBackgroundPass();
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