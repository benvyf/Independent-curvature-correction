# Independent curvature correction

角膜凸曲率校正算法的独立归档，包括工装标定、MATLAB/C# 对照，以及 MD810E 设备上位机的配置加载和实际测量链路。

本仓库只讨论正半径的凸曲率，不包含凹曲率模型。

## 仓库内容

| 路径 | 内容 |
| --- | --- |
| `docs/角膜曲率校正方法说明.md` | 标定公式、运行时算法、工装验证及设备测量完整说明 |
| `matlab/CurvatureCor260331.m` | 260331 MATLAB 原始参考算法 |
| `matlab/CurvatureCor260331_CSharp.m` | 复现当前 C# 行为并与原 MATLAB 方法逐点对比 |
| `csharp/*.cs` | `feature/#1207` 工装的标定、验证和计算源码 |
| `csharp/device/LJ.Lus/**` | MD810E 上位机的配置加载源码 |
| `csharp/device/LJ.Lus.Backend/**` | MD810E 上位机的曲率采集、计算和后处理源码 |

## 端到端链路

```text
1207 工装扫描三只标准眼
  -> 生成 FactoryInterpolationDictionary
  -> 写入 CorrectFileConfigs.json
  -> 部署到 LJ.Lus 运行目录
  -> 上位机启动加载配置
  -> 对焦期间采集曲率候选图像
  -> 提取内外环长短轴、轴位角和光点距离
  -> 内外环分别执行 260331 运行时算法
  -> 单帧内外环半径平均
  -> 稳定帧汇总、设备补偿和 K 值换算
```

详细流程、参数映射和边界条件见[角膜曲率校正方法说明](docs/角膜曲率校正方法说明.md)。

## 工装标定摘要

1. 使用半径为 `6.668 mm`、`7.943 mm`、`9.320 mm` 的三只标准模拟眼。
2. 每只标准眼沿轴向扫描21个位置，标定坐标为 `D=-1.0:0.1:1.0 mm`。
3. 每个位置采集9帧，对 `f`、内外环长短轴取极差最小的连续窗口均值。
4. 分别拟合 `f(D)`、`dL(D)`、`dS(D)`，得到 `f0/d0L/d0S/kL/kS`。
5. 生成 `CfL/CfS/CL/CS`，按标准眼写入 `CalibrationNode`。
6. 内外环节点均写入模型标记 `260331`。

## 运行时算法摘要

1. 按 `f0` 对三个标定节点排序，以当前光点距离 `f` 插值或外推参数。
2. 通过 `d0L=CfL*f`、`d0S=CfS*f` 估算当前零位直径。
3. 根据长短轴差和轴位角构造等效光斑，反算离焦位移 `D`。
4. 用 `RL=(dL-kL*D)/CL`、`RS=(dS-kS*D)/CS` 求分轴直接曲率。
5. 非球面数据执行两轮 Euler/轴比融合。
6. 按 `K=337.5/R` 换算屈光度。

## MD810E 上位机测量

设备程序从当前工作目录加载 `CorrectFileConfigs.json`。运行时要求：

- `FactoryInterpolationDictionary` 至少包含3个标准眼节点；
- 当前环的每个节点都带有 `260331` 标记；
- 工装和设备共同使用 `0.0075 mm/pixel`；
- 旧配置不会自动回退到旧算法。

独立曲率图像流程先按离焦距离排序：候选数不超过5时全部保留；超过5时先取一半，并把粗选数量限制为5至25帧。粗选帧排除图像质量码 `<0` 和 `4`，再选择最多5帧，使长、短轴半径方差之和最小。每帧分别计算内环和外环，并对两个环的半径取平均。最终后处理对入选帧求平均、应用设备半径补偿，在半径差不超过 `0.03 mm` 时归并为球面；`D1=337.5/RShort`、`D2=337.5/RLong`，界面 `AVG_K=(D1+D2)/2`。

候选集不可用时，程序会退回原信号任务图像，最终帧数不保证是5。

## 设备源码入口

| 路径 | 职责 |
| --- | --- |
| `csharp/device/LJ.Lus/ViewModel/ViewModelLocator.cs` | 从运行目录加载配置并写入 IOC 单例 |
| `csharp/device/LJ.Lus.Backend/Compute/Actors/Algorithm/AlgorithmsConfigs.cs` | `CalibrationNode`、`CorrectFileConfigs` 和算法配置引用 |
| `csharp/device/LJ.Lus.Backend/Compute/Actors/Algorithm/Calculator/CornealCurvatureCalculator.cs` | 260331 单环单帧运行时计算 |
| `csharp/device/LJ.Lus.Backend/Compute/Actors/Algorithm/AlgorithmActor.cs` | 候选帧选择、特征提取、内外环调用和单帧融合 |
| `csharp/device/LJ.Lus.Backend/Compute/Actors/Algorithm/MessagesOfAlgotithmActor.cs` | 曲率图像任务和算法消息模型 |
| `csharp/device/LJ.Lus.Backend/Compute/Actors/DataPostProcess/DataPostProcessActor.cs` | 多帧平均、补偿、轴位和最终 K 值 |
| `csharp/device/LJ.Lus.Backend/Module/MeasureModule/FocusCompensator.cs` | 通过 `D_Shift` 对焦并采集曲率候选图像 |
| `csharp/device/LJ.Lus.Backend/Module/MeasureModule/CaptureDataModule.cs` | 缓存候选图像并构建计算任务 |

设备源码最初摘取自 `MD810E主分支` 提交 `4f0796c1`；`AlgorithmActor.cs` 在本次复核中补充了自动对焦严重质量码门控，并同步到当前设备工作区。这些文件保留完整项目上下文，但依赖 LUS 解决方案中的设备、图像、Actor、MVVM 和配置组件，不能作为独立项目直接编译。

## MATLAB 对比

`CurvatureCor260331_CSharp.m` 使用同一组图像提取数据，同时计算原始 MATLAB 260331 路径和当前 C# 路径，并输出 `D/RL/RS` 的逐点差值。

脚本依赖项目已有的 `ProcessCorneaImages` 和三只标准眼图像数据；这些数据未包含在仓库中。`CurvatureCor260331.m` 的上传版本仅从 GBK 转换为 UTF-8，算法内容未修改。

## 已有验证

- MATLAB `checkcode` 通过。
- `CurvatureCor260331_CSharp.m` 已完成现有数据的内外环共126点对比运行。
- 方法文档已结合工装、MD810E 上位机、MATLAB 和 C++ 曲率相关实现复核。
- 新增文档的 Markdown 代码围栏和源码路径已检查。
- 自动对焦入口已与候选帧路径统一拒绝图像质量码 `<0` 和 `4`。

## 部署注意事项

- `D_array` 的毫米尺度依赖工装电机每步 `0.1 mm` 且共21个位置。
- 安装或升级不能使用不含新标定字典的模板覆盖设备专属 `CorrectFileConfigs.json`。
- 自动对焦和候选帧路径已检查严重图像质量码；部分旧逐帧入口仍未统一，该问题属于测量调用层，不是 260331 公式差异。
