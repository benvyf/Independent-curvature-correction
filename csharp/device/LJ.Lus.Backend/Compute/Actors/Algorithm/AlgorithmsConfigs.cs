using CommonServiceLocator;
using LJ.Lus.TechLibrary.Helpers;
using LJ.Lus.TechLibrary.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LJ.Lus.Backend.Compute.Actors.Algorithm
{
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
    /// 校正文件参数
    /// </summary>
    public class CorrectFileConfigs
    {
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
        /// 第N个模拟眼内环到校正开始的实际距离（N∈[1,3]）(D1=shortAxis,D2=longAxis)
        /// </summary>
        public double Inner_FirstD1 { get; set; }
        public double Inner_FirstD2 { get; set; }
        public double Inner_SecondD1 { get; set; }
        public double Inner_SecondD2 { get; set; }
        public double Inner_ThirdD1 { get; set; }
        public double Inner_ThirdD2 { get; set; }
        public double ConcaveInner_FirstD1 { get; set; }
        public double ConcaveInner_FirstD2 { get; set; }
        public double ConcaveInner_SecondD1 { get; set; }
        public double ConcaveInner_SecondD2 { get; set; }
        public double ConcaveInner_ThirdD1 { get; set; }
        public double ConcaveInner_ThirdD2 { get; set; }
        /// <summary>
        /// 第N个模拟眼外环到校正开始的实际距离（N∈[1,3]）(D1=shortAxis,D2=longAxis)
        /// </summary>
        public double Outer_FirstD1 { get; set; }
        public double Outer_FirstD2 { get; set; }
        public double Outer_SecondD1 { get; set; }
        public double Outer_SecondD2 { get; set; }
        public double Outer_ThirdD1 { get; set; }
        public double Outer_ThirdD2 { get; set; }
        public double ConcaveOuter_FirstD1 { get; set; }
        public double ConcaveOuter_FirstD2 { get; set; }
        public double ConcaveOuter_SecondD1 { get; set; }
        public double ConcaveOuter_SecondD2 { get; set; }
        public double ConcaveOuter_ThirdD1 { get; set; }
        public double ConcaveOuter_ThirdD2 { get; set; }
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

        public string CycleNumberTXT { get; set; } = "CycleNumber.txt";
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
        //public int Length { get; set; } = (int)(1024 * 1024 * 1.5);
        public int Length { get; set; } = (int)(1024 * 1024 * 1.375);
    }

    /// <summary>
    /// 采集卡设置参数
    /// </summary>
    //public class DAQConfigs
    //{
    //    public int AcquirePoints { get; set; }
    //    public int SamplingFrequency { get; set; } = 16;
    //    public int TriggerDelay { get; set; } = 9;
    //}

    /// <summary>
    /// 算法配置
    /// </summary>
    public class AlgorithmsConfigs : IConfigs
    {
        #region Fields
        private List<string> _correctFileConfigsPath = new List<string>() { "CorrectFileConfigs.json", "CorrectFileConfigs2.json" };
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
        public List<double[]> TotalNumsArray { get; set; } = new List<double[]>();
        public List<double[]> FitArray { get; set; } = new List<double[]>();
        public List<double[]> ConcaveFitArray { get; set; } = new List<double[]>();
        public float[] NoiseSignal { get; set; }
        public float[] TemplateSignalData { get; set; }
        public int TemplateSignalLength { get; set; }
        public int[] ReferenceFreArray { get; set; } = null;
        /// <summary>
        /// 信号滤波参数
        /// </summary>
        public SignalFilterConfigs SignalFilterConfigs { get; set; } = new SignalFilterConfigs();
        /// <summary>
        /// 校正文件参数
        /// </summary>
        public List<CorrectFileConfigs> CorrectFileConfigs { get; set; } = new List<CorrectFileConfigs>();
        /// <summary>
        /// 采集卡设置参数
        /// </summary>
        //public DAQConfigs DAQConfigs { get; set; } = new DAQConfigs();
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
        public List<CornealCurvatureFormulaComputeConfig> CornealCurvatureFormulaComputeConfig { get; set; } = new List<CornealCurvatureFormulaComputeConfig>();
        /// <summary>
        /// 凹角膜曲率公式计算配置
        /// </summary>
        public List<CornealCurvatureFormulaComputeConfig> ConcaveCornealCurvatureFormulaComputeConfig { get; set; } = new List<CornealCurvatureFormulaComputeConfig>();
        
        
        
        #endregion

        #region Methods
        public void ReadConfigs()
        {
            try
            {
                if (CorrectFileConfigs.Count != 0) return;//目前最多只有两个面
                CorrectFileConfigs.Add(ServiceLocator.Current.GetInstance<CorrectFileConfigs>());
                ////暂时直接读取config
                //CorrectFileConfigs.Add(JsonHelper.Load<CorrectFileConfigs>(_correctFileConfigsPath[1]));

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
                for (int i = 0; i < CorrectFileConfigs.Count; i++)
                {
                    JsonHelper.Save<CorrectFileConfigs>(CorrectFileConfigs[i], _correctFileConfigsPath[i]);
                }
                JsonHelper.Save<SignalFilterConfigs>(SignalFilterConfigs, _signalFilterConfigsPath);
                //JsonHelper.Save<DAQConfigs>(DAQConfigs, _daqConfigsPath);
            }
            catch (Exception e)
            {

                LogHelper.Error(e.Message + e.StackTrace);
            }
        }

        public void ReadTxtFileToArray()
        {
            for (int i = 0; i < CorrectFileConfigs.Count; i++)
            {
                //文件缺失/空行/坏行时不能抛未捕获异常：本方法在AlgorithmActor等Lazy单例构造函数里调用，异常会被Lazy缓存导致Instance永久不可用甚至整机闪退
                //注意：TotalNumsArray按下标与CorrectFileConfigs对齐（消费点用TotalNumsArray[signalIndex]索引），
                //因此读取失败时也必须Add一个null占位，否则下标错位/数组变短会在测量期抛越界异常杀死算法Actor
                double[] totalNums = null;
                try
                {
                    if (CorrectFileConfigs[i].TxTCycleNumName == null)
                    {
                        LogHelper.Error("标尺光周期文件名未配置（面索引" + i + "），该面按标尺缺失处理");
                    }
                    else if (!File.Exists(CorrectFileConfigs[i].TxTCycleNumName))
                    {
                        LogHelper.Error("标尺光周期文件缺失：" + CorrectFileConfigs[i].TxTCycleNumName + "（面索引" + i + "），该面按标尺缺失处理");
                    }
                    else
                    {
                        string[] strs = File.ReadAllLines(CorrectFileConfigs[i].TxTCycleNumName, Encoding.Default);
                        var totalNumsList = new List<double>(strs.Length);
                        foreach (string str in strs)
                        {
                            if (string.IsNullOrWhiteSpace(str)) continue;//跳过空行，避免double.Parse("")抛FormatException

                            if (str.Contains("E"))
                            {
                                totalNumsList.Add(Convert.ToDouble(decimal.Parse(str.ToString(), System.Globalization.NumberStyles.Float)));
                            }
                            else
                            {
                                totalNumsList.Add(double.Parse(str));
                            }
                        }
                        totalNumsLength = totalNumsList.Count;
                        totalNums = totalNumsList.ToArray();
                    }
                }
                catch (Exception e)
                {
                    LogHelper.Error("标尺光周期文件读取失败：" + CorrectFileConfigs[i].TxTCycleNumName + " " + e.Message + e.StackTrace);
                }
                TotalNumsArray.Add(totalNums);//失败时Add(null)占位，保持与CorrectFileConfigs下标对齐
            }
        }

        public void ReadBinFileToArray()
        {
            // 当前设备仅使用参考频率和模板信号 bin 文件。
            //文件缺失/被占用时不能抛未捕获异常：本方法在AlgorithmActor等Lazy单例构造函数里调用，异常会被Lazy缓存导致Instance永久不可用甚至整机闪退
            try
            {
                if (File.Exists("referenceFreArray.bin"))
                {
                    using (FileStream fs = new FileStream("referenceFreArray.bin", FileMode.Open, FileAccess.Read))
                    {
                        int length = (int)(fs.Length / 4);
                        ReferenceFreArray = new int[length];
                        using (BinaryReader br = new BinaryReader(fs))
                        {
                            for (int ii = 0; ii < length; ii++)
                            {
                                ReferenceFreArray[ii] = br.ReadInt32();
                            }
                        }
                    }
                }

                if (ReferenceFreArray == null)
                {
                    if (!File.Exists("template1.bin"))
                    {
                        LogHelper.Error("模板信号文件缺失：template1.bin（且无referenceFreArray.bin）");
                        return;
                    }
                    using (FileStream fs = new FileStream("template1.bin", FileMode.Open, FileAccess.Read))
                    {
                        TemplateSignalLength = (int)(fs.Length / 4);
                        TemplateSignalData = new float[TemplateSignalLength];
                        using (BinaryReader br = new BinaryReader(fs))
                        {
                            for (int ii = 0; ii < (fs.Length) / 4; ii++)
                            {
                                TemplateSignalData[ii] = br.ReadSingle();
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogHelper.Error("参考频率/模板信号bin文件读取失败：" + e.Message + e.StackTrace);
            }

            //噪声信号文件
            //{
            //    FileStream fs = new FileStream("NoiseSignal.bin", FileMode.Open, FileAccess.Read);
            //    NoiseSignal = new float[(fs.Length) / 4];
            //    using (BinaryReader br = new BinaryReader(fs))
            //    {
            //        for (int ii = 0; ii < NoiseSignal.Length; ii++)
            //        {
            //            NoiseSignal[ii] = br.ReadSingle();
            //        }
            //    }
            //    fs.Close();
            //}
            //模板信号文件
            //{
            //    FileStream fs = new FileStream("template1.bin", FileMode.Open, FileAccess.Read);
            //    TemplateSignalLength = (int)((fs.Length) / 4);
            //    TemplateSignalData = new float[TemplateSignalLength];
            //    using (BinaryReader br = new BinaryReader(fs))
            //    {
            //        for (int ii = 0; ii < (fs.Length) / 4; ii++)
            //        {
            //            TemplateSignalData[ii] = br.ReadSingle();
            //        }
            //    }
            //    fs.Close();
            //}
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
            for (int i = 0; i < CorrectFileConfigs.Count; i++)
            {
                CornealCurvatureFormulaComputeConfig.Add(new CornealCurvatureFormulaComputeConfig());
                CornealCurvatureFormulaComputeConfig[i].pixel = CorrectFileConfigs[i].Pixel;
                CornealCurvatureFormulaComputeConfig[i].magnificiation = CorrectFileConfigs[i].Magnification;
                //凸角膜
                CornealCurvatureFormulaComputeConfig[i].innerCir = CorrectFileConfigs[i].Inner_Cir;
                CornealCurvatureFormulaComputeConfig[i].innerFirstD1 = CorrectFileConfigs[i].Inner_FirstD1;
                CornealCurvatureFormulaComputeConfig[i].innerFirstD2 = CorrectFileConfigs[i].Inner_FirstD2;
                CornealCurvatureFormulaComputeConfig[i].innerSecondD1 = CorrectFileConfigs[i].Inner_SecondD1;
                CornealCurvatureFormulaComputeConfig[i].innerSecondD2 = CorrectFileConfigs[i].Inner_SecondD2;
                CornealCurvatureFormulaComputeConfig[i].innerThirdD1 = CorrectFileConfigs[i].Inner_ThirdD1;
                CornealCurvatureFormulaComputeConfig[i].innerThirdD2 = CorrectFileConfigs[i].Inner_ThirdD2;

                CornealCurvatureFormulaComputeConfig[i].outerCir = CorrectFileConfigs[i].Outer_Cir;
                CornealCurvatureFormulaComputeConfig[i].outerFirstD1 = CorrectFileConfigs[i].Outer_FirstD1;
                CornealCurvatureFormulaComputeConfig[i].outerFirstD2 = CorrectFileConfigs[i].Outer_FirstD2;
                CornealCurvatureFormulaComputeConfig[i].outerSecondD1 = CorrectFileConfigs[i].Outer_SecondD1;
                CornealCurvatureFormulaComputeConfig[i].outerSecondD2 = CorrectFileConfigs[i].Outer_SecondD2;
                CornealCurvatureFormulaComputeConfig[i].outerThirdD1 = CorrectFileConfigs[i].Outer_ThirdD1;
                CornealCurvatureFormulaComputeConfig[i].outerThirdD2 = CorrectFileConfigs[i].Outer_ThirdD2;
                //凹角膜
                ConcaveCornealCurvatureFormulaComputeConfig.Add(new CornealCurvatureFormulaComputeConfig());
                ConcaveCornealCurvatureFormulaComputeConfig[i].innerCir = CorrectFileConfigs[i].Inner_Cir;
                ConcaveCornealCurvatureFormulaComputeConfig[i].innerFirstD1 = CorrectFileConfigs[i].ConcaveInner_FirstD1;
                ConcaveCornealCurvatureFormulaComputeConfig[i].innerFirstD2 = CorrectFileConfigs[i].ConcaveInner_FirstD2;
                ConcaveCornealCurvatureFormulaComputeConfig[i].innerSecondD1 = CorrectFileConfigs[i].ConcaveInner_SecondD1;
                ConcaveCornealCurvatureFormulaComputeConfig[i].innerSecondD2 = CorrectFileConfigs[i].ConcaveInner_SecondD2;
                ConcaveCornealCurvatureFormulaComputeConfig[i].innerThirdD1 = CorrectFileConfigs[i].ConcaveInner_ThirdD1;
                ConcaveCornealCurvatureFormulaComputeConfig[i].innerThirdD2 = CorrectFileConfigs[i].ConcaveInner_ThirdD2;

                ConcaveCornealCurvatureFormulaComputeConfig[i].outerCir = CorrectFileConfigs[i].Outer_Cir;
                ConcaveCornealCurvatureFormulaComputeConfig[i].outerFirstD1 = CorrectFileConfigs[i].ConcaveOuter_FirstD1;
                ConcaveCornealCurvatureFormulaComputeConfig[i].outerFirstD2 = CorrectFileConfigs[i].ConcaveOuter_FirstD2;
                ConcaveCornealCurvatureFormulaComputeConfig[i].outerSecondD1 = CorrectFileConfigs[i].ConcaveOuter_SecondD1;
                ConcaveCornealCurvatureFormulaComputeConfig[i].outerSecondD2 = CorrectFileConfigs[i].ConcaveOuter_SecondD2;
                ConcaveCornealCurvatureFormulaComputeConfig[i].outerThirdD1 = CorrectFileConfigs[i].ConcaveOuter_ThirdD1;
                ConcaveCornealCurvatureFormulaComputeConfig[i].outerThirdD2 = CorrectFileConfigs[i].ConcaveOuter_ThirdD2;
            }
            #endregion


        }
        #endregion
    }
}

