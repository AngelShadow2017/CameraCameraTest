using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Rendering
{
    /// <summary>
    /// 渐进式多相机渲染Feature
    /// 将前一个相机的渲染结果作为当前相机的"背景板"
    /// 使用RenderGraph API实现
    /// </summary>
    public class ProgressiveMultiCameraFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("在哪个阶段注入Pass")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            
            [Tooltip("是否启用调试日志")]
            public bool enableDebugLog = false;
        }

        public Settings settings = new Settings();
        private ProgressiveMultiCameraPass renderPass;

        public override void Create()
        {
            renderPass = new ProgressiveMultiCameraPass(settings);
            renderPass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 只在Game相机或Scene相机上生效
            if (renderingData.cameraData.cameraType != CameraType.Game && 
                renderingData.cameraData.cameraType != CameraType.SceneView)
                return;

            Camera currentCamera = renderingData.cameraData.camera;
            
            // 检查当前相机是否在某个CameraChain中
            if (!CameraChainManager.IsFirstCameraStatic(currentCamera))
            {
                // 不是第一个相机，需要应用前一个相机的输出作为背景
                Camera previousCamera = CameraChainManager.GetPreviousCameraStatic(currentCamera);
                
                if (previousCamera != null)
                {
                    // 从CameraCaptureFeature获取前一个相机的输出
                    RTHandle previousOutput = CameraCaptureFeature.GetCameraOutput(previousCamera);
                    
                    if (previousOutput != null)
                    {
                        renderPass.Setup(currentCamera, previousCamera, previousOutput);
                        renderer.EnqueuePass(renderPass);
                        
                        if (settings.enableDebugLog)
                        {
                            Debug.Log($"[ProgressiveMultiCamera] Enqueued pass for camera '{currentCamera.name}' " +
                                      $"(previous: '{previousCamera.name}')");
                        }
                    }
                    else if (settings.enableDebugLog)
                    {
                        Debug.LogWarning($"[ProgressiveMultiCamera] Previous camera '{previousCamera.name}' " +
                                         $"has no captured output yet");
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            renderPass?.Dispose();
        }

        private class ProgressiveMultiCameraPass : ScriptableRenderPass
        {
            private Settings settings;
            private Camera currentCamera;
            private Camera previousCamera;
            private RTHandle previousOutput;

            private const string profilerTag = "Progressive Multi-Camera Composite";
            private static readonly int s_PreviousFrameID = Shader.PropertyToID("_PreviousCameraOutput");

            public ProgressiveMultiCameraPass(Settings settings)
            {
                this.settings = settings;
            }

            public void Setup(Camera currentCamera, Camera previousCamera, RTHandle previousOutput)
            {
                this.currentCamera = currentCamera;
                this.previousCamera = previousCamera;
                this.previousOutput = previousOutput;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                // 如果没有前一个相机的输出，跳过
                if (previousOutput == null)
                {
                    if (settings.enableDebugLog)
                    {
                        Debug.LogWarning($"[ProgressiveMultiCamera] No previous camera output for '{currentCamera.name}'");
                    }
                    return;
                }

                // 创建Pass Data
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(profilerTag, out var passData))
                {
                    // 设置Pass Data
                    passData.previousCameraOutput = previousOutput;
                    passData.cameraData = cameraData;

                    // 导入前一个相机的纹理
                    TextureHandle previousTexture = renderGraph.ImportTexture(previousOutput);
                    passData.previousTextureHandle = previousTexture;

                    // 获取当前相机的颜色目标
                    TextureHandle cameraColor = resourceData.activeColorTexture;
                    passData.cameraColorHandle = cameraColor;

                    // 设置渲染目标
                    builder.SetRenderAttachment(cameraColor, 0);
                    
                    // 读取前一个相机的输出
                    builder.UseTexture(previousTexture, AccessFlags.Read);

                    // 设置渲染函数 - 使用static lambda避免内存分配
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ExecutePass(data, context);
                    });

                    if (settings.enableDebugLog)
                    {
                        Debug.Log($"[ProgressiveMultiCamera] RenderGraph pass recorded for '{currentCamera.name}'");
                    }
                }
            }

            private static void ExecutePass(PassData data, RasterGraphContext context)
            {
                // 从TextureHandle获取实际的RTHandle
                RTHandle previousRT = (data.previousTextureHandle);
                
                // 将前一个相机的输出Blit到当前相机的颜色缓冲（已设置为渲染目标）
                // 这样就相当于把前一个相机的结果作为"背景板"
                Blitter.BlitTexture(
                    context.cmd,
                    previousRT,
                    new Vector4(1, 1, 0, 0), // scale and bias
                    0, // mipLevel
                    false // bilinear
                );
            }

            public void Dispose()
            {
                // 清理资源
            }

            private class PassData
            {
                public RTHandle previousCameraOutput;
                public UniversalCameraData cameraData;
                public TextureHandle previousTextureHandle;
                public TextureHandle cameraColorHandle;
            }
        }
    }
}
