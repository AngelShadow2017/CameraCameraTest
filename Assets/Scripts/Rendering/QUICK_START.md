# 快速开始指南

## 概述

这是一个全新的URP 17渐进式多相机渲染方案，完全使用RenderGraph API实现。

## 核心思想

将前一个相机的渲染结果（包括后处理）作为下一个相机的"背景板"，从而保证混合模式（如Additive）正确工作，同时允许每个相机使用独立的RenderData。

## 5分钟设置指南

### 步骤1: 添加Renderer Features

1. 打开你的URP Renderer Asset（通常在 `Settings/` 文件夹）
2. 点击 "Add Renderer Feature"
3. 添加 **Progressive Multi-Camera Feature**
   - Render Pass Event: `BeforeRenderingOpaques`（默认）
   - Enable Debug Log: 可选勾选
4. 再次点击 "Add Renderer Feature"  
5. 添加 **Camera Capture Feature**
   - Enable Debug Log: 可选勾选

**重要**: Feature添加顺序无关紧要，因为它们通过不同的RenderPassEvent自动排序。

### 步骤2: 设置相机链

#### 方法A: 使用示例脚本（推荐用于测试）

1. 在Hierarchy中创建空GameObject，命名为 "CameraChainSystem"
2. 添加组件 `ProgressiveMultiCameraExample`
3. 在Inspector中点击右键 → "Setup Example"
4. 系统会自动创建3个示例相机

#### 方法B: 手动设置（推荐用于生产）

1. 创建空GameObject，命名为 "CameraChainManager"
2. 添加组件 `CameraChainManager`
3. 创建多个相机（Camera A, B, C）
4. 在CameraChainManager的Inspector中添加相机：
   ```
   Camera Chain:
   - Element 0:
     - Camera: CameraA
     - Enabled: ✓
     - Priority: 0
   - Element 1:
     - Camera: CameraB
     - Enabled: ✓
     - Priority: 100
   - Element 2:
     - Camera: CameraC
     - Enabled: ✓
     - Priority: 200
   ```

### 步骤3: 配置相机

**第一个相机 (Camera A)**:
- Clear Flags: `Skybox` 或 `Solid Color`
- Culling Mask: 选择要渲染的层（如 "Background"）
- Depth: 0

**后续相机 (Camera B, C, ...)**:
- Clear Flags: `Don't Clear` ⚠️ **关键设置！**
- Culling Mask: 选择各自要渲染的层（如 "MainScene", "UI"）
- Depth: 按顺序递增（1, 2, ...）

### 步骤4: 测试

1. 进入Play模式
2. 你应该看到所有相机的内容正确组合
3. 如果启用了Debug Log，查看Console确认Pass执行顺序

## 示例场景配置

### 场景1: 天空背景
```
Camera: SkyCamera
- Clear Flags: Skybox
- Culling Mask: Nothing (只渲染天空盒)
- Priority: 0
```

### 场景2: 主场景（含Additive效果）
```
Camera: MainCamera
- Clear Flags: Don't Clear ⚠️
- Culling Mask: MainScene
- Priority: 100

场景中的Additive材质会正确地在天空背景上混合！
```

### 场景3: UI覆盖层
```
Camera: UICamera
- Clear Flags: Don't Clear ⚠️
- Culling Mask: UI
- Priority: 200
```

## 验证设置

使用提供的验证工具：
1. 选中有 `ProgressiveMultiCameraExample` 的GameObject
2. 右键点击组件 → "Validate Setup"
3. 查看Console中的验证报告

## 常见问题

### ❌ 看不到前一个相机的内容
**原因**: 后续相机的Clear Flags设置错误
**解决**: 将后续相机的Clear Flags改为 `Don't Clear`

### ❌ 混合模式不正确
**原因**: 材质设置或相机设置问题
**解决**: 
- 检查材质的Render Queue和Blend Mode
- 确保相机的Clear Flags正确（Don't Clear）

### ❌ 性能问题
**原因**: 相机太多或分辨率太高
**解决**: 
- 限制相机数量在2-4个
- 考虑降低某些相机的渲染分辨率

### ❌ Console报错 "No previous camera output"
**原因**: CameraCaptureFeature未正确工作
**解决**: 
- 确保CameraCaptureFeature已添加到Renderer
- 检查Feature的执行顺序

## 调试技巧

### 启用详细日志
```
Progressive Multi-Camera Feature:
  ☑ Enable Debug Log

Camera Capture Feature:
  ☑ Enable Debug Log
```

### 使用Frame Debugger
1. Window → Analysis → Frame Debugger
2. Enable
3. 查找以下Pass:
   - "Camera Output Capture"
   - "Progressive Multi-Camera Composite"

### 查看相机顺序
```csharp
// 在CameraChainManager上右键
→ Print Render Order
```

## 文件说明

| 文件 | 用途 |
|------|------|
| `CameraChainManager.cs` | 管理相机渲染顺序 |
| `ProgressiveMultiCameraFeature.cs` | 应用前一个相机输出作为背景 |
| `CameraCaptureFeature.cs` | 捕获相机最终输出 |
| `ProgressiveMultiCameraExample.cs` | 示例和测试脚本 |
| `ProgressiveMultiCamera_README.md` | 完整文档 |
| `ARCHITECTURE.md` | 技术架构说明 |

## 下一步

- 阅读 `ProgressiveMultiCamera_README.md` 了解详细配置
- 阅读 `ARCHITECTURE.md` 理解技术原理
- 根据你的具体需求调整相机设置和渲染层

## 技术支持

如果遇到问题：
1. 检查Console中的错误和警告
2. 使用Frame Debugger查看渲染流程
3. 运行 Validate Setup 检查配置
4. 查阅 `ProgressiveMultiCamera_README.md` 的常见问题部分

---

**记住核心原则**: 
- 第一个相机清除背景 (Skybox/SolidColor)
- 后续相机不清除 (Don't Clear)
- 每个相机按Priority顺序渲染
- 混合模式自然正确工作！
