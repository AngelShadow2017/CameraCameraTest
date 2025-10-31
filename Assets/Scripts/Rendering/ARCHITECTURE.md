# Progressive Multi-Camera System - 架构设计

## 系统架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                     CameraChainManager                          │
│  - 管理相机列表和渲染顺序                                         │
│  - 提供静态查询接口                                               │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ 配置
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Renderer Asset                             │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Progressive Multi-Camera Feature                         │  │
│  │  - RenderPassEvent: BeforeRenderingOpaques               │  │
│  │  - 在当前相机渲染前应用前一个相机的输出                     │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Camera Capture Feature                                   │  │
│  │  - RenderPassEvent: AfterRendering                        │  │
│  │  - 捕获当前相机的最终输出（含后处理）                       │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

## 数据流

```
Frame N 开始
│
├─ Camera A 渲染 (First Camera)
│  ├─ BeforeRenderingOpaques
│  │  └─ [Skip] Progressive Pass (无前一个相机)
│  ├─ 正常渲染流程
│  │  ├─ Render Opaques
│  │  ├─ Render Transparents
│  │  └─ Post Processing
│  └─ AfterRendering
│     └─ [Execute] Capture Pass
│        └─ 捕获 Camera A 输出 → RTHandle_A
│
├─ Camera B 渲染 (Second Camera)
│  ├─ BeforeRenderingOpaques
│  │  └─ [Execute] Progressive Pass
│  │     └─ Blit RTHandle_A → Camera B Color Buffer
│  ├─ 正常渲染流程 (在 RTHandle_A 的基础上)
│  │  ├─ Render Opaques (混合到已有内容上)
│  │  ├─ Render Transparents (混合到已有内容上)
│  │  └─ Post Processing
│  └─ AfterRendering
│     └─ [Execute] Capture Pass
│        └─ 捕获 Camera B 输出 → RTHandle_B
│
└─ Camera C 渲染 (Last Camera)
   ├─ BeforeRenderingOpaques
   │  └─ [Execute] Progressive Pass
   │     └─ Blit RTHandle_B → Camera C Color Buffer
   ├─ 正常渲染流程 (在 RTHandle_B 的基础上)
   │  ├─ Render Opaques
   │  ├─ Render Transparents
   │  └─ Post Processing
   └─ AfterRendering
      └─ [Skip] Capture Pass (最后一个相机，不需要捕获)
      └─ 输出到屏幕
```

## 核心组件

### 1. CameraChainManager.cs
**职责**: 相机链管理

**功能**:
- 维护相机列表
- 按Priority排序
- 提供查询接口（前一个相机、是否第一个等）
- 静态字典映射Camera → Manager

**关键API**:
```csharp
static Camera GetPreviousCameraStatic(Camera current)
static bool IsFirstCameraStatic(Camera current)
```

### 2. ProgressiveMultiCameraFeature.cs
**职责**: 应用前一个相机的输出作为背景

**执行时机**: `BeforeRenderingOpaques`

**工作流程**:
1. 检查当前相机是否在链中
2. 如果不是第一个相机，获取前一个相机的输出
3. 使用RenderGraph创建Pass
4. 在Pass中Blit前一个相机的输出到当前颜色缓冲

**关键代码**:
```csharp
// AddRenderPasses
RTHandle previousOutput = CameraCaptureFeature.GetCameraOutput(previousCamera);
renderPass.Setup(currentCamera, previousCamera, previousOutput);

// RecordRenderGraph
TextureHandle previousTexture = renderGraph.ImportTexture(previousOutput);
builder.SetRenderAttachment(cameraColor, 0);
builder.UseTexture(previousTexture, AccessFlags.Read);

// ExecutePass
Blitter.BlitTexture(context.cmd, previousTextureHandle, ...);
```

### 3. CameraCaptureFeature.cs
**职责**: 捕获相机的最终渲染输出

**执行时机**: `AfterRendering`

**工作流程**:
1. 检查是否需要捕获（不是最后一个相机）
2. 使用RenderGraph创建Pass
3. 在Pass中创建RTHandle
4. Blit当前相机的输出到RTHandle
5. 保存到静态字典供下一个相机使用

**关键代码**:
```csharp
// AddRenderPasses
if (index < renderOrder.Count - 1) {
    capturePass.Setup(currentCamera);
    renderer.EnqueuePass(capturePass);
}

// ExecutePass
RTHandle captureRT = RTHandles.Alloc(...);
Blitter.BlitCameraTexture(context.cmd, sourceTexture, captureRT);
SetCameraOutput(targetCamera, captureRT);
```

## RenderGraph集成

### Pass注册
```csharp
using (var builder = renderGraph.AddRasterRenderPass<PassData>(profilerTag, out var passData))
{
    // 配置Pass Data
    passData.previousTextureHandle = previousTexture;
    
    // 声明资源依赖
    builder.UseTexture(previousTexture, AccessFlags.Read);
    builder.SetRenderAttachment(cameraColor, 0);
    
    // 设置执行函数
    builder.SetRenderFunc<PassData>((data, context) => {
        ExecutePass(data, context);
    });
}
```

### 纹理导入/导出
```csharp
// 导入外部RTHandle
TextureHandle imported = renderGraph.ImportTexture(rtHandle);

// 使用资源
builder.UseTexture(imported, AccessFlags.Read);

// 获取URP的活动颜色纹理
TextureHandle activeColor = resourceData.activeColorTexture;
```

## 混合模式保留原理

### 传统方案的问题
```
Camera A → RT_A (背景 + Additive物体)
           问题：Additive物体与自己混合，而不是与背景混合
           
Camera B → RT_B
           
合成 RT_A + RT_B → 结果错误
```

### 本方案的优势
```
Camera A → 正常渲染 → 捕获完整输出 (Output_A)

Camera B → 先Blit Output_A到颜色缓冲
        → 然后在此基础上渲染
        → Additive物体正确地与Output_A混合
        → 捕获完整输出 (Output_B)

Camera C → 先Blit Output_B到颜色缓冲
        → 然后在此基础上渲染
        → 最终输出

结果：所有混合都是正确的！
```

## 性能考虑

### 开销分析
每个相机（除第一个）:
- **1x Blit操作** (Progressive Pass): 将前一个相机输出作为背景
- **1x Blit操作** (Capture Pass): 捕获当前输出

3个相机总开销:
- Camera A: 1 Capture
- Camera B: 1 Progressive + 1 Capture  
- Camera C: 1 Progressive
- **总计**: 4次全屏Blit

### 优化建议
1. **分辨率控制**: 可以让不同相机使用不同分辨率
2. **相机数量**: 建议2-4个相机，不要太多
3. **移动平台**: 考虑降低RTHandle分辨率或使用更低精度格式
4. **剔除优化**: 确保每个相机只渲染必要的对象

## 扩展性

### 支持的扩展
1. ✅ **不同Renderer**: 每个相机可以使用不同的Renderer Asset
2. ✅ **独立后处理**: 每个相机可以有自己的Volume Profile
3. ✅ **动态相机**: 可以运行时添加/移除相机
4. ✅ **多场景**: 完美支持多场景渲染

### 潜在扩展方向
1. **深度传递**: 可以扩展支持深度缓冲传递
2. **自定义混合**: 可以添加自定义混合模式
3. **相机分组**: 支持多个独立的相机链
4. **条件渲染**: 根据条件动态启用/禁用某些相机

## 调试技巧

### 启用调试日志
```csharp
Progressive Multi-Camera Feature → Enable Debug Log ✓
Camera Capture Feature → Enable Debug Log ✓
```

### 检查RenderGraph执行
使用Frame Debugger:
1. Window → Analysis → Frame Debugger
2. 查找 "Progressive Multi-Camera Composite" 和 "Camera Output Capture"
3. 验证Pass执行顺序和纹理状态

### 常见问题排查
| 问题 | 可能原因 | 解决方案 |
|------|---------|---------|
| 看不到前一个相机内容 | Clear Flags设置错误 | 后续相机使用Don't Clear |
| 混合模式错误 | 材质设置问题 | 检查材质Blend Mode |
| 性能下降 | 相机太多或分辨率太高 | 减少相机数量或降低分辨率 |
| RTHandle为null | Feature顺序错误 | 确保Capture在Progressive之后 |

## 总结

这个系统通过**渐进式渲染**的方式解决了多相机多场景渲染的核心问题：

1. ✅ **保留混合模式**: 物体在正确的背景上混合
2. ✅ **独立Renderer**: 每个相机可以有不同的渲染设置
3. ✅ **RenderGraph兼容**: 使用现代RenderGraph API
4. ✅ **后处理友好**: 捕获包含后处理的最终输出

**核心思想**: 把每个相机的完整输出作为下一个相机的起点，而不是分别渲染再合并。
