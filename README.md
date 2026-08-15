# Independent curvature correction

角膜凸曲率校正算法的独立归档与 MATLAB/C# 对照材料。内容来自 LUS 2.0 工装程序当前 `feature/#1207` 分支，并以 `CurvatureCor260331.m` 为 MATLAB 参考。

本仓库只讨论正半径的凸曲率，不包含凹曲率模型。

## 仓库内容

| 路径 | 内容 |
| --- | --- |
| `docs/角膜曲率校正方法说明.md` | 标定、运行时校正、验证流程及公式说明 |
| `matlab/CurvatureCor260331.m` | 260331 MATLAB 原始参考算法 |
| `matlab/CurvatureCor260331_CSharp.m` | 复现当前 C# 行为并与原 MATLAB 方法逐点对比 |
| `csharp/CurvatureCorrectionViewModel.cs` | 工装标定采集、稳帧和标定参数生成 |
| `csharp/FunctionMethod.cs` | `EyeCalculator` 运行时曲率计算及鲁棒拟合 |
| `csharp/CurvatureVerificationViewModel.cs` | 标准眼验证和结果统计 |
| `csharp/AlgorithmsConfigs.cs` | `CalibrationNode` 与 `CorrectFileConfigs` 数据模型 |

## 校正方法摘要

1. 使用半径为 `6.668 mm`、`7.943 mm`、`9.320 mm` 的三只标准模拟眼。
2. 每只标准眼沿轴向扫描 21 个位置，标定坐标为 `D=-1.0:0.1:1.0 mm`。
3. C# 工装在每个位置采集 9 帧，对 `f`、内外环长短轴分别取极差最小的连续窗口均值。
4. 对每只标准眼及每个环拟合：
   - `f(D)=kf*D+f0`
   - `dL(D)=kL*D+d0L`
   - `dS(D)=kS*D+d0S`
5. 生成归一化参数 `CfL=d0L/f0`、`CfS=d0S/f0`，以及几何参数 `CL=d0L/R`、`CS=d0S/R`。
6. 实测时按当前 `f` 在三个 `f0` 标定节点间线性插值/外推参数，并估算 `d0L`、`d0S`。
7. 根据椭圆轴位角对长短轴进行欧拉加权，反算轴向位移 `D`，再求两主轴曲率半径。
8. 非球面测量继续执行两轮 Euler/轴比融合；最后按 `K=337.5/R` 换算屈光度。

完整参数映射、公式、边界条件和验证口径见[方法说明](docs/角膜曲率校正方法说明.md)。

## MATLAB 对比

`CurvatureCor260331_CSharp.m` 使用同一组图像提取数据，同时计算：

- 原始 MATLAB 260331 路径；
- 当前 C# 路径，包括正值过滤、一次鲁棒剔除、`float` 参数存储、按 `f0` 排序插值、球面阈值和两轮融合；
- 两条路径在 `D`、`RL`、`RS` 上的逐点差值。

脚本依赖项目已有的 `ProcessCorneaImages`，以及三只标准眼的标定图像数据。图像数据未包含在本仓库中。`CurvatureCor260331.m` 保留原脚本的数据目录写法；上传版本仅将文件编码转换为 UTF-8，算法内容不变。

## C# 源码说明

`csharp/` 中的文件是从完整工装项目摘取的算法相关源码，用于审查和对照，不是可独立编译的项目。它们仍依赖 LUS 解决方案中的设备、图像算法、MVVM 和配置组件。

重点入口：

- 标定参数生成：`CurvatureCorrectionViewModel`
- 运行时计算：`FunctionMethod.EyeCalculator`
- 验证：`CurvatureVerificationViewModel`
- 持久化模型：`CalibrationNode`、`CorrectFileConfigs`

## 已有验证

- MATLAB 静态检查 `checkcode` 通过。
- `CurvatureCor260331_CSharp.m` 已完成现有数据的内环、外环共 126 点对比运行。
- 方法文档已结合 C#、MATLAB 和 C++ 曲率相关实现复核。

## 注意事项

`D_array` 的毫米尺度依赖工装电机每步 `0.1 mm` 且共 21 个位置。若修改步距或采样点数，必须同步修改标定坐标，否则斜率和运行时位移尺度会失真。
