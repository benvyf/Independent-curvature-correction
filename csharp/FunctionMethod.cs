using LJ.Lus.Backend.Compute.Actors.Algorithm;
using LJ.Lus.ToolingDashboard.FullMachine.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LJ.Lus.ToolingDashboard.FullMachine.Tool
{
    public class FunctionMethod
    {

        private int _acquireNumber = 5;

        public double GetFrontIndexMean(double[] _Indexdata)
        {
            return GetClosestDataMean(_Indexdata);
        }

        private double GetClosestDataMean(double[] array)
        {
            // 1. 安全性检查
            if (array == null || array.Length == 0)
            {
                return 0; // 或者抛出异常 throw new ArgumentException("数组不能为空");
            }

            // 曲率校正链路只接受正向有效值，先过滤坏帧带来的 0/NaN/Infinity。
            double[] validArray = array.Where(value => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0).ToArray();
            if (validArray.Length == 0)
            {
                return 0;
            }

            // 建议直接使用 array.Length，而不是依赖外部变量 _acquireNumber，这样更严谨
            int totalCount = validArray.Length;

            // 计算需要截取的连续数字个数 (即滑动窗口的大小)
            int windowSize = (totalCount + 1) / 2;

            if (windowSize <= 0) return 0;

            // 2. 克隆数组并排序 (避免打乱原数组的顺序)
            double[] sortedArray = (double[])validArray.Clone();
            Array.Sort(sortedArray);

            // 3. 寻找数据最集中（最大值和最小值差值最小）的窗口起点
            int bestStartIndex = 0;
            double minDiff = double.MaxValue; // 使用 double 的最大值作为初始判断值

            // 窗口滑动的总次数为：总长度 - 窗口长度 + 1
            int loopCount = totalCount - windowSize + 1;

            for (int i = 0; i < loopCount; i++)
            {
                // 计算当前窗口内最大值与最小值的差值
                double diff = sortedArray[i + windowSize - 1] - sortedArray[i];

                // 记录差值最小的那个窗口的起始索引
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestStartIndex = i;
                }
            }

            // 4. 对选出的最集中的数据进行求和并计算均值
            double sum = 0;
            for (int i = 0; i < windowSize; i++)
            {
                sum += sortedArray[bestStartIndex + i];
            }

            // 5. 返回均值（除数必须是窗口大小，而不是写死的 3）
            return sum / windowSize;
        }


        /// <summary>
        /// 多项式拟合
        /// </summary>
        /// <param name="x">X数组</param>
        /// <param name="y">Y数组</param>
        /// <param name="degree">次数 (2表示二次拟合)</param>
        /// <returns>系数数组 [a, b, c] (最高次幂在前)</returns>
        public double[] PolynomialFit(double[] x, double[] y, int degree)
        {
            int n = x.Length;
            int m = degree + 1; // 系数个数 (二次拟合需要3个系数)

            // 构建正规方程组的增广矩阵: A * X = B
            double[,] matrix = new double[m, m + 1];

            // 填充矩阵
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < n; k++)
                    {
                        // 计算 x 的 (i+j) 次幂之和
                        sum += Math.Pow(x[k], i + j);
                    }
                    matrix[i, j] = sum;
                }

                double sumY = 0;
                for (int k = 0; k < n; k++)
                {
                    // 计算 x^i * y 之和
                    sumY += y[k] * Math.Pow(x[k], i);
                }
                matrix[i, m] = sumY;
            }

            // 高斯消元法求解线性方程组
            for (int i = 0; i < m - 1; i++)
            {
                for (int k = i + 1; k < m; k++)
                {
                    double t = matrix[k, i] / matrix[i, i];
                    for (int j = 0; j <= m; j++)
                    {
                        matrix[k, j] = matrix[k, j] - t * matrix[i, j];
                    }
                }
            }

            // 回代求解
            double[] result = new double[m];
            for (int i = m - 1; i >= 0; i--)
            {
                double sum = 0;
                for (int j = i + 1; j < m; j++)
                {
                    sum += matrix[i, j] * result[j];
                }
                result[i] = (matrix[i, m] - sum) / matrix[i, i];
            }

            Array.Reverse(result);

            return result;
        }

        /// <summary>
        /// 带有健壮性（抗噪点）的多项式拟合
        /// </summary>
        /// <param name="x">X数组</param>
        /// <param name="y">Y数组</param>
        /// <param name="degree">次数 (2表示二次拟合)</param>
        /// <param name="sigmaThreshold">判定偏离点的严格程度(默认1.5倍标准差)</param>
        /// <returns>系数数组 [a, b, c]</returns>
        public double[] RobustPolynomialFit(double[] x, double[] y, int degree, double sigmaThreshold = 1.5)
        {
            // 1. 第一次基础拟合 (包含所有点)
            double[] initialCoeffs = PolynomialFit(x, y, degree);

            int n = x.Length;
            if (n <= degree + 1) return initialCoeffs; // 数据量太少，直接返回

            // 2. 计算所有点的预测值和绝对误差
            double[] errors = new double[n];
            double sumError = 0;

            for (int i = 0; i < n; i++)
            {
                double predicted = 0;
                // 依据多项式次数累加，例如 a*x^2 + b*x + c
                for (int j = 0; j <= degree; j++)
                {
                    predicted += initialCoeffs[j] * Math.Pow(x[i], degree - j);
                }
                errors[i] = Math.Abs(y[i] - predicted);
                sumError += errors[i];
            }

            // 3. 计算误差的平均值与标准差 (Standard Deviation)
            double meanError = sumError / n;
            double variance = 0;
            for (int i = 0; i < n; i++)
            {
                variance += Math.Pow(errors[i] - meanError, 2);
            }
            double stdDev = Math.Sqrt(variance / n);

            // 4. 如果数据已经非常完美 (标准差极小)，无需剔除
            if (stdDev < 1e-6) return initialCoeffs;

            // 5. 寻找离群点并剔除
            List<double> cleanX = new List<double>();
            List<double> cleanY = new List<double>();

            for (int i = 0; i < n; i++)
            {
                // 如果误差在合理范围内，保留为“好点”
                if (errors[i] <= meanError + sigmaThreshold * stdDev)
                {
                    cleanX.Add(x[i]);
                    cleanY.Add(y[i]);
                }
            }

            // 6. 如果有坏点被剔除，且剩下的点仍满足拟合最低数量要求，则进行“二次纯净拟合”
            if (cleanX.Count >= degree + 1 && cleanX.Count < n)
            {
                return PolynomialFit(cleanX.ToArray(), cleanY.ToArray(), degree);
            }

            // 否则退回第一次的结果
            return initialCoeffs;
        }
    }

    // ==========================================
    // 2. 核心算法类
    // ==========================================

        /// <summary>
        /// 曲率计算算法（同步 MATLAB 260331：f0 插值 + 分轴 d0/C + Ang_deg 加权）
        /// </summary>
        public class EyeCalculator
        {
            public const float CalibrationModelMarker260331 = 260331f;

            // ==========================================
            // 辅助方法：一维线性插值及外推 (复刻 MATLAB interp1)
            // ==========================================
            private double Interp1(double[] x, double[] v, double xq)
            {
                if (x.Length != v.Length || x.Length < 2)
                    throw new ArgumentException("插值数组长度必须相等且至少为2。");

                // 假定输入的 x 数组是升序排列的 (如 6.668 -> 7.943 -> 9.320 对应的 f0)
                int n = x.Length;

                // 左侧外推
                if (xq <= x[0])
                {
                    return v[0] + (xq - x[0]) * (v[1] - v[0]) / (x[1] - x[0]);
                }
                // 右侧外推
                if (xq >= x[n - 1])
                {
                    return v[n - 1] + (xq - x[n - 1]) * (v[n - 1] - v[n - 2]) / (x[n - 1] - x[n - 2]);
                }

                // 内部线性插值
                for (int i = 0; i < n - 1; i++)
                {
                    if (xq >= x[i] && xq <= x[i + 1])
                    {
                        return v[i] + (xq - x[i]) * (v[i + 1] - v[i]) / (x[i + 1] - x[i]);
                    }
                }

                return v[0]; // 兜底
            }

            /// <summary>
            /// 提取按 f0 排序的指定特征数组，用于插值
            /// </summary>
            private double[] ExtractFeatureArray(List<CalibrationNode> nodes, Func<CalibrationNode, double> selector)
            {
                return nodes.Select(selector).ToArray();
            }

            private bool Is260331Node(CalibrationNode node, bool isInnerRing)
            {
                double marker = isInnerRing ? node.Inner_kk1 : node.Outer_kk1;
                return Math.Abs(marker - CalibrationModelMarker260331) < 0.5;
            }

            private void Ensure260331Calibration(List<CalibrationNode> sortedNodes, bool isInnerRing)
            {
                if (!sortedNodes.All(node => Is260331Node(node, isInnerRing)))
                {
                    throw new InvalidOperationException("当前 CorrectFileConfigs.json 不是 260331 新版曲率校正数据，请先重新完成曲率校正。");
                }
            }

            private EyeResult Calculate260331(
                double dL_mm,
                double dS_mm,
                double f_mm,
                double angleDeg,
                CalibrationParams calib,
                List<CalibrationNode> sortedNodes,
                double[] f0_vec)
            {
                // CalibrationNode 仍沿用旧字段名，260331 参数映射如下：
                // C=d0_L, scale_L=d0_S, scale_S=Cf_L, kL2=Cf_S, kL1=k_L, kS2=k_S, kS1=C_L, kk2=C_S
                double Cf_L, Cf_S, k_L, k_S, C_L, C_S;
                if (calib.IsInnerRing)
                {
                    Cf_L = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Inner_scale_S), f_mm);
                    Cf_S = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Inner_kL2), f_mm);
                    k_L = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Inner_kL1), f_mm);
                    k_S = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Inner_kS2), f_mm);
                    C_L = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Inner_kS1), f_mm);
                    C_S = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Inner_kk2), f_mm);
                }
                else
                {
                    Cf_L = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Outer_scale_S), f_mm);
                    Cf_S = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Outer_kL2), f_mm);
                    k_L = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Outer_kL1), f_mm);
                    k_S = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Outer_kS2), f_mm);
                    C_L = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Outer_kS1), f_mm);
                    C_S = Interp1(f0_vec, ExtractFeatureArray(sortedNodes, n => n.Outer_kk2), f_mm);
                }

                double d0_L_est = Cf_L * f_mm;
                double d0_S_est = Cf_S * f_mm;

                bool isSpherical = Math.Abs(dL_mm - dS_mm) < 0.005;
                double theta = isSpherical ? 0.0 : angleDeg * (Math.PI / 180.0);
                double c2 = Math.Pow(Math.Cos(theta), 2);
                double s2 = Math.Pow(Math.Sin(theta), 2);

                double d_spot, d0_spot, k_spot;
                if (isSpherical)
                {
                    d_spot = (dL_mm + dS_mm) / 2.0;
                    d0_spot = (d0_L_est + d0_S_est) / 2.0;
                    k_spot = (k_L + k_S) / 2.0;
                }
                else
                {
                    d_spot = 1.0 / (c2 / dL_mm + s2 / dS_mm);
                    d0_spot = 1.0 / (c2 / d0_L_est + s2 / d0_S_est);
                    k_spot = c2 * k_L + s2 * k_S;
                }

                double D = (d_spot - d0_spot) / k_spot;

                double R_L_direct = (dL_mm - k_L * D) / C_L;
                double R_S_direct = (dS_mm - k_S * D) / C_S;

                double R_L_final, R_S_final;
                if (!isSpherical && dS_mm > 1e-9 && dL_mm > 1e-9)
                {
                    double[] R_vec = sortedNodes.Select(n => (double)n.R).ToArray();
                    double R_spot = Interp1(f0_vec, R_vec, f_mm);
                    double r = dL_mm / dS_mm;

                    double R_L_euler = R_spot * (c2 + r * s2);
                    double R_S_euler = R_spot * (c2 / r + s2);

                    double R_L_pass1 = c2 * R_L_direct + s2 * R_L_euler;
                    double R_S_pass1 = s2 * R_S_direct + c2 * R_S_euler;

                    double R_L_euler2 = R_S_pass1 * r;
                    double R_S_euler2 = R_L_pass1 / r;

                    R_L_final = c2 * R_L_direct + s2 * R_L_euler2;
                    R_S_final = s2 * R_S_direct + c2 * R_S_euler2;
                }
                else
                {
                    R_L_final = R_L_direct;
                    R_S_final = R_S_direct;
                }

                double k1 = 337.5 / R_L_final;
                double k2 = 337.5 / R_S_final;

                return new EyeResult
                {
                    RMajor = R_L_final,
                    RMinor = R_S_final,
                    KFlat = Math.Min(k1, k2),
                    KSteep = Math.Max(k1, k2),
                    Cyl = Math.Abs(k1 - k2),
                    D_Shift = D
                };
            }

            /// <summary>
            /// 执行曲率实测函数。
            /// 仅支持 260331 新版曲率校正数据。
            /// </summary>
            public EyeResult Calculate(
                double semiA_px,//半长轴
                double semiB_px,//半短轴
                double fDist_px, //光点距离
                double angleDeg,//轴位角
                CalibrationParams calib)
            {
                if (calib.FactoryInterpolationDictionary == null || calib.FactoryInterpolationDictionary.Count < 3)
                    throw new ArgumentException("出厂插值字典必须包含至少3个标准眼数据！");

                double pixelSize = calib.PixelSize;

                // 当前实测物理尺寸
                double dL_mm = semiA_px * 2 * pixelSize;//长轴物理直径
                double dS_mm = semiB_px * 2 * pixelSize;//短轴物理直径
                double f_mm = fDist_px * pixelSize;//实测光点距

                // --- 1. 以 f0 为键做动态插值 ---
                var sortedNodes = calib.FactoryInterpolationDictionary.Values.OrderBy(n => n.f0).ToList();
                double[] f0_vec = sortedNodes.Select(n => (double)n.f0).ToArray();
                Ensure260331Calibration(sortedNodes, calib.IsInnerRing);
                return Calculate260331(dL_mm, dS_mm, f_mm, angleDeg, calib, sortedNodes, f0_vec);
            }
        }

        public class CornealCurvatureCalculator : EyeCalculator
        {
        }
}

