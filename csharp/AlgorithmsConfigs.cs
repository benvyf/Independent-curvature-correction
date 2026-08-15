using CommonServiceLocator;
using LJ.Lus.DotnetDevices.DAQ;
using LJ.Lus.TechLibrary.Helpers;
using LJ.Lus.TechLibrary.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace LJ.Lus.Backend.Compute.Actors.Algorithm
{
    /// <summary>
    /// 标准信号信息
    /// </summary>
    public class StandardSignalInfo
    {
        /// <summary>
        /// 起始位置
        /// </summary>
        public int StartPosition { get; set; }
        /// <summary>
        /// 结束位置
        /// </summary>
        public int EndPosition { get; set; }
        /// <summary>
        /// 物理长度
        /// </summary>
        public float PhysicalLength { get; set; }
    }

    /// <summary>
    /// 单个模拟眼的标定数据节点，包含内环和外环的独立基准、多项式参数与独立残差
    /// </summary>
    public class CalibrationNode
    {
        public float R { get; set; }
        public float f0 { get; set; } // 绝对 0 位的光点距离

        // ================= 内环参数 =================
        public float Inner_C { get; set; }
        public float Inner_scale_L { get; set; }
        public float Inner_scale_S { get; set; }

        // 内环长轴多项式 (X=位移D)
        public float Inner_kL2 { get; set; }
        public float Inner_kL1 { get; set; }
        // 内环短轴多项式 (X=位移D)
        public float Inner_kS2 { get; set; }
        public float Inner_kS1 { get; set; }
        // 内环光点距离多项式 (X=位移D)
        public float Inner_kk2 { get; set; }
        public float Inner_kk1 { get; set; }

        // 内环双轴独立残差 (D=-1.0 处提取)
        public float Inner_res_coef_L { get; set; }
        public float Inner_res_coef_S { get; set; }

        // ================= 外环参数 =================
        public float Outer_C { get; set; }
        public float Outer_scale_L { get; set; }
        public float Outer_scale_S { get; set; }

        // 外环长轴多项式 (X=位移D)
        public float Outer_kL2 { get; set; }
        public float Outer_kL1 { get; set; }
        // 外环短轴多项式 (X=位移D)
        public float Outer_kS2 { get; set; }
        public float Outer_kS1 { get; set; }
        // 外环光点距离多项式 (X=位移D)
        public float Outer_kk2 { get; set; }
        public float Outer_kk1 { get; set; }

        // 外环双轴独立残差 (D=-1.0 处提取)
        public float Outer_res_coef_L { get; set; }
        public float Outer_res_coef_S { get; set; }
    }

    /// <summary>
    /// 第一个面校正文件参数
    /// </summary>
    public class CorrectFileConfigs
    {
        /// <summary>
        /// 标准信号位置
        /// </summary>
        public StandardSignalInfo StandardSignalInfo { get; set; }
        /// <summary>
        /// 焦点位置
        /// </summary>
        public int PolarizationCorrectionIndex { get; set; }
        /// <summary>
        /// 角膜校正初始位置（用于记录验证时候的范围）
        /// </summary>
        public float CorrectionCenterIndex { get; set; }
        /// <summary>
        /// 补偿系数
        /// </summary>
        public float SamplingTime { get; set; } = 1;
        /// <summary>
        /// 信号阈值
        /// </summary>
        public float SignalThreshold { get; set; } = 25;
        /// <summary>
        /// 校正起始位置
        /// </summary>
        public float CCT_FrontSignalThreshold { get; set; } = 80;
        public float CCT_backSignalThreshold { get; set; } = 40;
        public int FrontBaseIndex { get; set; }
        /// <summary>
        /// 校正结束位置
        /// </summary>
        public int BackBaseIndex { get; set; }
        /// <summary>
        /// 角膜曲率7.9最清晰位置（降采样的）
        /// </summary>
        public int FlagNum { get; set; }//凸
        public int ConcaveFlagNum { get; set; }//凹
        /// <summary>
        /// 靶环内环高度大小（实际物理大小）
        /// </summary>
        public float Inner_Cir { get; set; } = 11;
        /// <summary>
        /// 靶环外环高度大小（实际物理大小）
        /// </summary>
        public float Outer_Cir { get; set; } = 15;

        /// <summary>
        /// 实际像素点大小转换率
        /// </summary>
        public double Pixel { get; set; } = 0.0075;
        /// <summary>
        /// CCD放大倍率
        /// </summary>
        public float Magnification { get; set; } = 0.2958F;
        /// <summary>
        /// 光程校正文件目录
        /// </summary>
        public string OpticalFilePath { get; set; } = "";
        /// <summary>
        /// 标尺光数组文件名字
        /// </summary>
        public string TxTCycleNumName { get; set; } = "TotalNumssCycleNum.txt";

        public string CycleNumberTXT { get; set; } = "CycleNumber.txt"; //第一个面

        /// <summary>
        /// 白到白系数
        /// </summary>
        public float WTWSamplingTime { get; set; }

        // ================= 新版曲率校正核心参数 =================

        /// <summary>
        /// 靶向标准SDR，用于标定其他模拟眼和人眼的对焦基准
        /// </summary>
        public float StandardTargetSDR { get; set; }

        /// <summary>
        /// 出厂插值字典（以曲率 R 字符串为 Key，例如 "6.668"）
        /// 内部包含内/外环、长/短轴的零位基准参数与多项式系数
        /// </summary>
        public Dictionary<string, CalibrationNode> FactoryInterpolationDictionary { get; set; } = new Dictionary<string, CalibrationNode>();
    }

    /// <summary>
    /// 第二个面校正文件参数
    /// </summary>
    public class CorrectFileConfigs2
    {
        /// <summary>
        /// 标准信号位置
        /// </summary>
        public StandardSignalInfo StandardSignalInfo { get; set; }
        /// <summary>
        /// 焦点位置
        /// </summary>
        public int PolarizationCorrectionIndex { get; set; }
        /// <summary>
        /// 角膜校正初始位置（用于记录验证时候的范围）
        /// </summary>
        public int CorrectionCenterIndex { get; set; }
        /// <summary>
        /// 补偿系数
        /// </summary>
        public float SamplingTime { get; set; } = 1;
        /// <summary>
        /// 信号阈值
        /// </summary>
        public float SignalThreshold { get; set; } = 25;
        /// <summary>
        /// 校正起始位置
        /// </summary>
        public float CCT_FrontSignalThreshold { get; set; } = 80;
        public float CCT_backSignalThreshold { get; set; } = 40;
        public int FrontBaseIndex { get; set; }
        /// <summary>
        /// 校正结束位置
        /// </summary>
        public int BackBaseIndex { get; set; }
        /// <summary>
        /// 角膜曲率7.9最清晰位置（降采样的）
        /// </summary>
        public int FlagNum { get; set; }//凸
        public int ConcaveFlagNum { get; set; }//凹
        /// <summary>
        /// 靶环内环高度大小（实际物理大小）
        /// </summary>
        public float Inner_Cir { get; set; } = 11;
        /// <summary>
        /// 靶环外环高度大小（实际物理大小）
        /// </summary>
        public float Outer_Cir { get; set; } = 15;

        /// <summary>
        /// 实际像素点大小转换率
        /// </summary>
        public double Pixel { get; set; } = 0.0075;
        /// <summary>
        /// CCD放大倍率
        /// </summary>
        public float Magnification { get; set; } = 0.2958F;
        /// <summary>
        /// 光程校正文件目录
        /// </summary>
        public string OpticalFilePath { get; set; } = "";
        /// <summary>
        /// 标尺光数组文件名字
        /// </summary>
        public string TxTCycleNumName { get; set; } = "TotalNumssCycleNum2.txt";

        public string CycleNumberTXT { get; set; } = "CycleNumber2.txt";

        // ================= 新版曲率校正核心参数 =================
        public float StandardTargetSDR { get; set; }
        public Dictionary<string, CalibrationNode> FactoryInterpolationDictionary { get; set; } = new Dictionary<string, CalibrationNode>();
    }

    /// <summary>
    /// 信号滤波参数
    /// </summary>
    public class SignalFilterConfigs
    {
        //Ascan
        public int SamplingFrequency { get; set; } = 16000000;
        public int UpperCutoffFrequency { get; set; } = 2400000;
        public int LowerCutoffFrequency { get; set; } = 1050000;
        public int FinalCutoffFrequency { get; set; } = 25000;
        //分段处理
        public int UpperCutoffFrequency_CCT { get; set; } = 1700000;
        public int LowerCutoffFrequency_CCT { get; set; } = 1500000;
        public int FinalCutoffFrequency_CCT { get; set; } = 30000;
        public int UpperCutoffFrequency_LT { get; set; } = 1600000;
        public int LowerCutoffFrequency_LT { get; set; } = 1350000;
        public int UpperCutoffFrequency_AD { get; set; } = 1650000;
        public int LowerCutoffFrequency_AD { get; set; } = 1400000;
        //ruler
        public int UpperCutoffFrequency_Ruler { get; set; } = 105000;
        public int LowerCutoffFrequency_Ruler { get; set; } = 72000;
        //signal length
        public int Length { get; set; } = (int)(1024 * 1024 * 1.375);
    }

    /// <summary>
    /// 算法配置
    /// </summary>
    public class AlgorithmsConfigs : IConfigs
    {
        #region Fields
        private string _correctFileConfigsPath = "CorrectFileConfigs.json";
        private string _signalFilterConfigsPath = "SignalFilterConfigs.json";
        private string _daqConfigsPath = "DAQConfigs.json";
        #endregion

        #region Properties
        public int Height { get; set; } = 480;
        public int Width { get; set; } = 640;
        public int totalNumsLength { get; set; }
        public int fitArray1DLength { get; set; } = 4;
        public int fitArray2DLength { get; set; } = 86;
        public int fitArray3DLength { get; set; } = 229;
        public double[] totalNumsArray { get; set; }
        public double[] fitArray { get; set; }
        public double[] ConcaveFitArray { get; set; }
        public float[] TemplateSignalData { get; set; }
        public int TemplateSignalLength { get; set; }
        /// <summary>
        /// 信号滤波参数
        /// </summary>
        public SignalFilterConfigs SignalFilterConfigs { get; set; } = new SignalFilterConfigs();
        /// <summary>
        /// 校正文件参数
        /// </summary>
        public CorrectFileConfigs CorrectFileConfigs { get; set; } = new CorrectFileConfigs();
        /// <summary>
        /// ascan计算配置
        /// </summary>
        public AScanComputeConfig AscanComputeConfig { get; set; } = new AScanComputeConfig();
        /// <summary>
        /// IIR计算配置
        /// </summary>
        public IIRComputeConfig IIRComputeConfig { get; set; } = new IIRComputeConfig();
        /// <summary>
        /// 标尺光计算配置
        /// </summary>
        public ReferenceRulerComputeConfig ReferenceRulerComputeConfig { get; set; } = new ReferenceRulerComputeConfig();
        /// <summary>
        /// 找眼睛算法配置
        /// </summary>
        public EyeLocateComputeConfig EyeLocateComputeConfig { get; set; } = new EyeLocateComputeConfig();
        /// <summary>
        /// 凸角膜曲率公式计算配置
        /// </summary>
        public CornealCurvatureFormulaComputeConfig CornealCurvatureFormulaComputeConfig { get; set; } = new CornealCurvatureFormulaComputeConfig();
        /// <summary>
        /// 凹角膜曲率公式计算配置
        /// </summary>
        public CornealCurvatureFormulaComputeConfig ConcaveCornealCurvatureFormulaComputeConfig { get; set; } = new CornealCurvatureFormulaComputeConfig();
        #endregion

        #region Methods
        public void ReadConfigs()
        {
            try
            {
                CorrectFileConfigs = ServiceLocator.Current.GetInstance<CorrectFileConfigs>();
                SignalFilterConfigs = ServiceLocator.Current.GetInstance<SignalFilterConfigs>();
            }
            catch (Exception e)
            {
                WriteConfigs();
                LogHelper.Error(e.Message + e.StackTrace);
            }
        }

        public void WriteConfigs()
        {
            try
            {
                JsonHelper.Save<CorrectFileConfigs>(CorrectFileConfigs, _correctFileConfigsPath);
                JsonHelper.Save<SignalFilterConfigs>(SignalFilterConfigs, _signalFilterConfigsPath);
            }
            catch (Exception e)
            {
                LogHelper.Error(e.Message + e.StackTrace);
            }
        }

        public void ReadTxtFileToArray()
        {
            if (CorrectFileConfigs.TxTCycleNumName != null)
            {
                string[] strs = File.ReadAllLines(CorrectFileConfigs.TxTCycleNumName, Encoding.Default);
                totalNumsLength = strs.Length;
                totalNumsArray = new double[totalNumsLength];
                int t = 0;
                foreach (string str in strs)
                {
                    if (str.Contains("E"))
                    {
                        totalNumsArray[t] = Convert.ToDouble(decimal.Parse(str.ToString(), System.Globalization.NumberStyles.Float));
                    }
                    else
                    {
                        totalNumsArray[t] = double.Parse(str);
                    }
                    t++;
                }
            }
        }

        public void ReadBinFileToArray()
        {
            {
                FileStream fs = new FileStream("template1.bin", FileMode.Open, FileAccess.Read);
                TemplateSignalLength = (int)((fs.Length) / 4);
                TemplateSignalData = new float[TemplateSignalLength];
                using (BinaryReader br = new BinaryReader(fs))
                {
                    for (int ii = 0; ii < (fs.Length) / 4; ii++)
                    {
                        TemplateSignalData[ii] = br.ReadSingle();
                    }
                }
                fs.Close();
            }
        }

        public void ReadAlgorithmConfigFromConfigs()
        {
            #region 信号处理
            //信号
            AscanComputeConfig.SamplingFrequency = SignalFilterConfigs.SamplingFrequency;
            AscanComputeConfig.LowerCutoffFrequency = SignalFilterConfigs.LowerCutoffFrequency;
            AscanComputeConfig.UpperCutoffFrequency = SignalFilterConfigs.UpperCutoffFrequency;
            AscanComputeConfig.FinalCutoffFrequency = SignalFilterConfigs.FinalCutoffFrequency;
            //标尺光
            ReferenceRulerComputeConfig.LowerCutoffFrequency = SignalFilterConfigs.LowerCutoffFrequency_Ruler;
            ReferenceRulerComputeConfig.UpperCutoffFrequency = SignalFilterConfigs.UpperCutoffFrequency_Ruler;
            //IIR(适用)
            IIRComputeConfig.SamplingFrequency = SignalFilterConfigs.SamplingFrequency;
            IIRComputeConfig.LowerCutoffFrequency = 1450000;
            IIRComputeConfig.UpperCutoffFrequency = 1900000;
            IIRComputeConfig.FinalCutoffFrequency = 50000;
            IIRComputeConfig.FliterOrder = 5;
            #endregion

            #region 角膜公式计算
            CornealCurvatureFormulaComputeConfig.pixel = CorrectFileConfigs.Pixel;
            CornealCurvatureFormulaComputeConfig.magnificiation = CorrectFileConfigs.Magnification;
            //凸角膜
            CornealCurvatureFormulaComputeConfig.innerCir = CorrectFileConfigs.Inner_Cir;
            CornealCurvatureFormulaComputeConfig.outerCir = CorrectFileConfigs.Outer_Cir;
            //凹角膜
            ConcaveCornealCurvatureFormulaComputeConfig.innerCir = CorrectFileConfigs.Inner_Cir;
            ConcaveCornealCurvatureFormulaComputeConfig.outerCir = CorrectFileConfigs.Outer_Cir;
            #endregion
        }
        #endregion
    }
}
