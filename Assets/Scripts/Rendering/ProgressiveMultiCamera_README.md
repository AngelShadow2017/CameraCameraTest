# Progressive Multi-Camera Rendering System

## 概述

这是一个基于URP 17和RenderGraph的多场景渐进式相机渲染系统。它允许多个相机按顺序渲染，每个相机的渲染结果都会作为下一个相机的"背景板"，从而实现正确的混合模式渲染。

## 核心特性

- ✅ **渐进式渲染**：每个相机在前一个相机的渲染结果上继续渲染
- ✅ **保留混合模式**：不会因为单独渲染到RT而破坏additive等混合模式
- ✅ **独立RenderData**：每个相机可以使用不同的Renderer和后处理设置
- ✅ **RenderGraph支持**：完全使用RenderGraph API，符合URP 17+标准
- ✅ **后处理友好**：捕获包含后处理的最终输出

## 工作原理

```
Camera A (Scene 1) → 渲染完成（含后处理）→ 捕获输出
                                              ↓
Camera B (Scene 2) → 将A的输出作为背景 → 继续渲染 → 捕获输出
                                                      ↓
Camera C (Scene 3) → 将B的输出作为背景 → 继续渲染 → 最终输出
```

### 关键机制

1. **CameraChainManager**：管理相机的渲染顺序
2. **CameraCaptureFeature**：在每个相机渲染完成后捕获其输出（包括后处理）
3. **ProgressiveMultiCameraFeature**：在相机渲染前，将前一个相机的输出Blit作为背景

## 安装和配置

### 1. 添加Renderer Features

在你的URP Renderer Asset上添加以下两个Feature（顺序很重要）：

1. **Progressive Multi-Camera Feature**
   - Render Pass Event: `BeforeRenderingOpaques`
   - 用于将前一个相机的输出应用为背景

2. **Camera Capture Feature**
   - Render Pass Event: `AfterRendering`（自动设置）
   - 用于捕获相机的最终输出

### 2. 设置相机链

#### 方法A：使用CameraChainManager组件

1. 创建一个空GameObject，命名为"CameraChainManager"
2. 添加`CameraChainManager`组件
3. 在Inspector中添加相机：
   ```
   Camera Chain:
   - Element 0:
     - Camera: Camera_Scene1
     - Enabled: ✓
     - Priority: 0
   - Element 1:
     - Camera: Camera_Scene2
     - Enabled: ✓
     - Priority: 100
   - Element 2:
     - Camera: Camera_Scene3
     - Enabled: ✓
     - Priority: 200
   ```

4. 相机将按Priority从小到大的顺序渲染

#### 方法B：使用Context Menu快速设置

1. 选中CameraChainManager对象
2. 右键点击组件 → `Auto Find Cameras in Children`
3. 系统会自动查找子对象中的所有相机并按顺序添加

### 3. 配置相机设置

对于**第一个相机**（如Camera_Scene1）：
- Clear Flags: `Skybox` 或 `Solid Color`
- 正常渲染

对于**后续相机**（如Camera_Scene2, Camera_Scene3）：
- Clear Flags: `Don't Clear` ⚠️ **重要**
- 这样它们会在前一个相机的输出上继续渲染

### 4. 场景设置

每个相机可以：
- 渲染不同的场景（通过Culling Mask）
- 使用不同的Renderer Asset
- 使用不同的后处理配置
- 使用不同的渲染路径

例如：
```
Camera_Scene1:
- Culling Mask: Layer "Scene1"
- Renderer: Renderer_Scene1
- Post Processing: ✓ (Volume Profile 1)

Camera_Scene2:
- Culling Mask: Layer "Scene2"
- Renderer: Renderer_Scene2
- Post Processing: ✓ (Volume Profile 2)

Camera_Scene3:
- Culling Mask: Layer "Scene3"  
- Renderer: Renderer_Scene3
- Post Processing: ✓ (Volume Profile 3)
```

## 工作流程示例

### 场景1：天空背景
```csharp
// Camera_Scene1 设置
clearFlags = CameraClearFlags.Skybox
cullingMask = LayerMask.GetMask("Background")
depth = 0
```

### 场景2：主要场景（使用Additive材质）
```csharp
// Camera_Scene2 设置
clearFlags = CameraClearFlags.Nothing  // Don't Clear
cullingMask = LayerMask.GetMask("MainScene")
depth = 1

// 场景中可以使用Additive混合的材质
// 它们会正确地在Scene1的结果上进行additive混合
```

### 场景3：UI覆盖层
```csharp
// Camera_Scene3 设置
clearFlags = CameraClearFlags.Nothing  // Don't Clear
cullingMask = LayerMask.GetMask("UI")
depth = 2
```

## API参考

### CameraChainManager

```csharp
// 获取相机的渲染顺序
List<Camera> renderOrder = manager.GetRenderOrder();

// 获取相机在链中的索引
int index = manager.GetCameraIndex(camera);

// 获取前一个相机
Camera previous = manager.GetPreviousCamera(camera);

// 判断是否是第一个相机
bool isFirst = manager.IsFirstCamera(camera);

// 静态方法
CameraChainManager manager = CameraChainManager.GetManagerForCamera(camera);
Camera previous = CameraChainManager.GetPreviousCameraStatic(camera);
bool isFirst = CameraChainManager.IsFirstCameraStatic(camera);
```

### 调试

启用调试日志：
1. 选中Renderer Asset
2. 展开Progressive Multi-Camera Feature
3. 勾选 `Enable Debug Log`
4. 展开Camera Capture Feature  
5. 勾选 `Enable Debug Log`

这将在Console中输出详细的渲染过程信息。

## 常见问题

### Q: 为什么我的第二个相机看不到第一个相机的内容？

A: 确保：
1. 第二个相机的Clear Flags设置为 `Don't Clear`
2. CameraChainManager中相机的Priority顺序正确
3. 两个Renderer Feature都已正确添加
4. 查看Console是否有错误或警告

### Q: 混合模式还是不正确？

A: 这个系统的优势就是保留混合模式。如果还有问题：
1. 确认材质的混合模式设置正确
2. 检查是否使用了自定义Shader，确保它们支持正确的混合
3. 确认相机的Clear Flags设置（后续相机必须是Don't Clear）

### Q: 性能如何？

A: 
- 每个相机会额外进行一次纹理拷贝操作（Capture）
- 每个后续相机会进行一次Blit操作（Background）
- 相比传统方法，多了纹理拷贝开销，但保证了正确性
- 建议在移动设备上控制相机数量（2-3个）

### Q: 可以动态添加/移除相机吗？

A: 可以，只需：
```csharp
cameraChainManager.UpdateRenderOrder();
```

### Q: 支持VR/AR吗？

A: 理论上支持，但需要测试。RenderGraph本身支持XR，但多相机渲染需要额外验证。

## 技术细节

### RenderGraph执行顺序

对于3个相机的情况：

```
Frame N:
1. Camera A - BeforeRenderingOpaques
   (没有前一个相机，跳过ProgressiveMultiCameraPass)
2. Camera A - 正常渲染（Opaques, Transparents, Post-Processing）
3. Camera A - AfterRendering
   → CameraCapturePass: 捕获Camera A的输出到RT_A

4. Camera B - BeforeRenderingOpaques
   → ProgressiveMultiCameraPass: Blit RT_A到Camera B的颜色缓冲
5. Camera B - 正常渲染（在RT_A的基础上渲染）
6. Camera B - AfterRendering
   → CameraCapturePass: 捕获Camera B的输出到RT_B

7. Camera C - BeforeRenderingOpaques
   → ProgressiveMultiCameraPass: Blit RT_B到Camera C的颜色缓冲
8. Camera C - 正常渲染（在RT_B的基础上渲染）
9. Camera C - AfterRendering
   (最后一个相机，不需要捕获)
```

### 内存管理

- RTHandle使用RTHandles.Alloc分配
- 每个相机的捕获纹理会被复用（释放旧的，创建新的）
- 在Feature Dispose时自动清理所有RTHandle

## 限制和注意事项

1. **深度缓冲不共享**：每个相机有独立的深度缓冲，不会传递深度信息
2. **只传递颜色**：只有颜色信息会从一个相机传递到下一个
3. **不支持相机Stack**：这个系统替代了Camera Stack的功能
4. **顺序依赖**：相机必须按顺序渲染，不能并行

## 版本要求

- Unity 2022.3+（URP 14+）
- 推荐 Unity 6（URP 17+）
- RenderGraph必须启用

## 许可

此代码为示例实现，可自由使用和修改。

## 更新日志

### v1.0.0 (2025-11-01)
- 初始版本
- 基于RenderGraph的渐进式多相机渲染
- 支持独立Renderer和后处理
