using Algorithms;
using CommonServiceLocator;
using LJ.Lus.Backend.Basic;
using LJ.Lus.Backend.Compute.Actors.Algorithm.Calculator;
using LJ.Lus.Backend.Compute.Actors.DataPostProcess;
using LJ.Lus.Backend.Compute.ComputeDataModel;
using LJ.Lus.Backend.Info.Actors.Database;
using LJ.Lus.Backend.Model;
using LJ.Lus.TechLibrary.Extend;
using LJ.Lus.TechLibrary.Helpers;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;

namespace LJ.Lus.Backend.Compute.Actors.Algorithm
{
    /// <summary>
    /// 算法计算Actor
    /// </summary>
    public class AlgorithmActor : BasicActor
    {
        #region Singleton
        public AlgorithmActor()
        {
            //read json
            _algorithmsConfigs.ReadConfigs();//读取校正文件
            _algorithmsConfigs.ReadTxtFileToArray();//读取文本文件
            _algorithmsConfigs.ReadBinFileToArray();//读取参考频率/模板bin文件
            _algorithmsConfigs.ReadAlgorithmConfigFromConfigs();//将校正文件参数分到具体算法的config
            LaunchActor();
        }

        private static readonly Lazy<AlgorithmActor> lazy = new Lazy<AlgorithmActor>(() => new AlgorithmActor());
        public static AlgorithmActor Instance { get { return lazy.Value; } }
        #endregion

        #region Fields
        protected CornealCurvatureCalculator _cornealCurvatureCalculator = null;
        /// <summary>
        /// 信号重建计算
        /// </summary>
        protected Algorithms.SignalReBuildCompute _signalReBuildCompute = null;
        /// <summary>
        /// 人眼图像质量计算
        /// </summary>
        protected Algorithms.IrisImageQualityCompute _irisImageQualityCompute = null;
        /// <summary>
        /// 眼睛定位计算
        /// </summary>
        protected Algorithms.EyeLocateCompute _eyeLocateCompute = null;
        /// <summary>
        /// 白到白计算
        /// </summary>
        protected Algorithms.InnerAndOuterDiameterCompute _innerAndOuterDiameterCompute = null;
        /// <summary>
        /// 角膜曲率计算+眼睛实时跟踪
        /// </summary>
        protected Algorithms.CornealCurvatureCompute _cornealCurvatureCompute = null;
        /// <summary>
        /// IOL计算
        /// </summary>
        protected Algorithms.IOLCompute _iolCompute = null;
        /// <summary>
        /// 信号处理
        /// </summary>
        protected Algorithms.AScanCompute1 _ascanCompute = null;
        protected Algorithms.LockInAmplifier _lockInAmplifier = null;
        //锁相放大器算法资源是否就绪：参考频率数组缺失（referenceFreArray.bin缺失/读取失败）或native Init失败时为false，测量走无效结果路径
        protected bool _isLockInAmplifierReady = false;
        //protected IIRCompute _iirCompute = null;
        /// <summary>
        /// 标尺光计算
        /// </summary>
        protected Algorithms.ReferenceRulerCompute _referenceRulerCompute = null;

        /// <summary>
        /// 找靶环半径(定位Y移动步长)
        /// </summary>
        protected Algorithms.TargetRingRadiusCompute _targetRingRadiusCompute = null;
        /// <summary>
        /// 角膜曲率计算公式
        /// </summary>
        protected List<Algorithms.CornealCurvatureFormulaCompute> _cornealCurvatureFormulaCompute = new List<CornealCurvatureFormulaCompute>();
        /// <summary>
        /// 蓝灯和白灯光斑提取(蓝灯和白灯检测是否正常工作)
        /// </summary>
        protected Algorithms.LEDFaculaNumCompute _LEDFaculaNumCompute = null;
        /// <summary>
        /// 虹膜图像宽度
        /// </summary>
        protected int _irisWidth = 640;

        /// <summary>
        /// 虹膜图像高度
        /// </summary>
        protected int _irisHeight = 480;

        /// <summary>
        /// ascan信号长度
        /// </summary>
        protected int _signalLength = (int)(1.5f * 1024 * 1024);

        /// <summary>
        /// 算法参数配置
        /// </summary>
        protected AlgorithmsConfigs _algorithmsConfigs = new AlgorithmsConfigs();
        protected const int MaxCornealImageCandidateCount = 50;
        protected const int MaxCornealImageRoughSelectionCount = 25;
        protected const int DesiredCornealFrameCount = 5;

        #endregion

        #region Properties
        public EventHandler<IrisImageQualityComputeResult> ShowIrisImageQualityComputeResultEventHandler { get; set; }
        public EventHandler<EyeLocateComputeResult> ShowEyeLocateComputeResultEventHandler { get; set; }
        public EventHandler<TargetRingFocusComputeResult> ShowTargetRingFocusComputeResultEventHandler { get; set; }
        public EventHandler<RealTimeTrackComputeResult> ShowRealTimeTrackComputeResultEventHandler { get; set; }
        public EventHandler<RealTimeSingalComputeResult> ShowRealTimeSingalComputeResultEventHandler { get; set; }
        public EventHandler<List<SignalProcessComputeResult>> ShowSignalProcessComputeResultEventHandler { get; set; }

        public EventHandler<SyncDataComputeResult> ShowSyncDataComputeResultEventHandler { get; set; }
        public EventHandler<EyeMultiParameterComputeResult> EyeMultiParameterComputeEventHandler { get; set; }
        public EventHandler<List<EyeMultiParameterComputeResult>> EyeMultiParameterPacketsComputeEventHandler { get; set; }

        public EventHandler<Case> ShowCalculateThicknessResultEventHandler { get; set; }

        public EventHandler<InnerAndOuterDiameterComputeResult> ShowInnerAndOuterDiameterComputeResultEventHandler { get; set; }
        public EventHandler<List<InnerAndOuterDiameterComputeResult>> ShowPSAndWTWPacketsComputeEventHandler { get; set; }
        public EventHandler<int> ShowLEDFaculaNumComputeResultEventHandler { get; set; }
        #endregion

        protected override void PreStart()
        {
            //加载资源
        }

        #region Handlers

        /// <summary>
        /// 设置配置
        /// </summary>
        public virtual void HandleReadAlgorithmComputeConfig()
        {
            _algorithmsConfigs.ReadConfigs();//重新读取Configs文件
        }


        /// <summary>
        /// 初始化资源 
        /// </summary>
        public virtual void HandleInitAlgorithmCompute()
        {
            if (_algorithmsConfigs == null) _algorithmsConfigs = new AlgorithmsConfigs();
            _signalLength = _algorithmsConfigs.SignalFilterConfigs.Length;

            //角膜曲率公式计算
            _cornealCurvatureCalculator = new CornealCurvatureCalculator();

            _signalReBuildCompute = new SignalReBuildCompute();
            //信号重建的模拟信号默认1.375 *1024*1024
            _signalReBuildCompute.Init((int)(1.5 * 1024 * 1024));//_algorithmsConfigs.SignalFilterConfigs.Length
            double gauss_sigma = 6.985099476493443e+02;
            double gauss_mu = 13000 / 2;
            float const_value = 1.2f;
            int range = 2000;
            _signalReBuildCompute.LoadGaussParameter(gauss_sigma, gauss_mu, const_value, range);
            //_signalReBuildCompute.LoadNoiseSignal(_algorithmsConfigs.NoiseSignal);

            // 申请虹膜图像对焦计算资源
            _irisImageQualityCompute = new IrisImageQualityCompute();
            if (!_irisImageQualityCompute.Init(_irisHeight, _irisWidth)) return;

            // 申请找靶环半径资源
            _targetRingRadiusCompute = new Algorithms.TargetRingRadiusCompute();
            _targetRingRadiusCompute.Init(_irisWidth, _irisHeight);
            // 申请找眼计算资源
            _eyeLocateCompute = new Algorithms.EyeLocateCompute();
            string eye_casade_name = @".\haarcascade_eye_tree_eyeglasses.xml";
            //_eyeLocateCompute.Init(_algorithmsConfigs.Height, _algorithmsConfigs.Width, eye_casade_name);
            _eyeLocateCompute.Init(_irisWidth, _irisHeight, eye_casade_name);
            //// 申请白到白和瞳孔计算资源
            _innerAndOuterDiameterCompute = new Algorithms.InnerAndOuterDiameterCompute();
            _innerAndOuterDiameterCompute.Init(_irisWidth, _irisHeight);
            // 申请角膜曲率计算资源
            _cornealCurvatureCompute = new Algorithms.CornealCurvatureCompute();
            _cornealCurvatureCompute.Init(_irisHeight, _irisWidth);
            //申请标尺光计算资源
            _referenceRulerCompute = new Algorithms.ReferenceRulerCompute();
            _referenceRulerCompute.SetConfig(_algorithmsConfigs.ReferenceRulerComputeConfig);
            _referenceRulerCompute.Init(_signalLength);
            // 申请信号处理计算资源
            _ascanCompute = new Algorithms.AScanCompute1();
            _ascanCompute.SetConfig(_algorithmsConfigs.AscanComputeConfig);
            _ascanCompute.Init(_signalLength);
            // 信号锁相处理

            _lockInAmplifier = new LockInAmplifier();
            const float rulerLightWavelengthNm = 1310f;
            const float measurementLightWavelengthNm = 1050f;
            var scale = rulerLightWavelengthNm / measurementLightWavelengthNm;
            var seg_size = 1024 * 8;
            var seg_num = _algorithmsConfigs.SignalFilterConfigs.Length / seg_size;
            //referenceFreArray.bin缺失/读取失败时ReferenceFreArray为null，native的Init对null参数仅return -1，
            //锁相会静默保持未初始化、测量期输出错误数据；此处显式检查参数与返回值并记录资源缺失状态（不抛异常，避免构造期崩启动）
            if (_algorithmsConfigs.ReferenceFreArray == null)
            {
                _isLockInAmplifierReady = false;
                LogHelper.Error("锁相放大器初始化失败：参考频率数组为空（referenceFreArray.bin缺失或读取失败），后续测量将无有效信号");
            }
            else
            {
                var lockInInitCode = _lockInAmplifier.Init(
                    _algorithmsConfigs.SignalFilterConfigs.SamplingFrequency,
                    scale,
                    _algorithmsConfigs.ReferenceFreArray,
                    seg_num,
                    seg_size,
                    24000,
                    13000,
                    2
                );
                _isLockInAmplifierReady = lockInInitCode == 0;
                if (!_isLockInAmplifierReady)
                {
                    LogHelper.Error("锁相放大器初始化失败，错误码：" + lockInInitCode + "，后续测量将无有效信号");
                }
            }

            var cornealCurvatureFormulaCompute = new Algorithms.CornealCurvatureFormulaCompute();
            ////申请角膜曲率公式计算资源
            //for (int i = 0; i < _algorithmsConfigs.CorrectFileConfigs.Count; i++)
            //{
            //    var cornealCurvatureFormulaCompute = new Algorithms.CornealCurvatureFormulaCompute();
            //    cornealCurvatureFormulaCompute.Init(_signalLength, _algorithmsConfigs.totalNumsLength, _algorithmsConfigs.fitArray1DLength, _algorithmsConfigs.fitArray2DLength, _algorithmsConfigs.fitArray3DLength);
            //    cornealCurvatureFormulaCompute.LoadComputeResource(_algorithmsConfigs.TotalNumsArray[i], _algorithmsConfigs.FitArray[i], _algorithmsConfigs.ConcaveFitArray[i]);
            //    cornealCurvatureFormulaCompute.SetConfig(_algorithmsConfigs.CornealCurvatureFormulaComputeConfig[i], _algorithmsConfigs.ConcaveCornealCurvatureFormulaComputeConfig[i]);
            //    _cornealCurvatureFormulaCompute.Add(cornealCurvatureFormulaCompute);
            //}
            //申请蓝灯和白灯光斑数量计算资源
            _LEDFaculaNumCompute = new LEDFaculaNumCompute();
            _LEDFaculaNumCompute.Init(_irisWidth, _irisHeight);
            //申请IOL计算资源
            _iolCompute = new IOLCompute();
            _iolCompute.Init();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void HandleFreeAlgorithmCompute()
        {
            _signalReBuildCompute?.Free();
            _irisImageQualityCompute?.Free();
            _targetRingRadiusCompute?.Free();
            _eyeLocateCompute?.Free();
            _innerAndOuterDiameterCompute?.Free();
            _referenceRulerCompute?.Free();
            _ascanCompute?.Free();
            _cornealCurvatureCompute?.Free();
            _LEDFaculaNumCompute?.Free();
            _lockInAmplifier?.Free();
        }
        /// <summary>
        /// 信号重建（历史病例界面用于展示信号，目前在用）
        /// </summary>
        /// <param name="peaksPosition"></param>
        /// <param name="peaksValue"></param>
        /// <returns></returns>
        public virtual float[] HandleSignalReBuildCompute(int[] peaksPosition, double[] peaksValue)
        {
            if (_signalReBuildCompute == null) return null;
            var rebuildSignal = new float[(int)(1.5 * 1024 * 1024)];
            if (_signalReBuildCompute.Compute(rebuildSignal, peaksPosition, peaksValue, peaksPosition.Length))
            {
                return rebuildSignal;
            }
            return new float[(int)(1.5 * 1024 * 1024)];
        }

        /// <summary>
        /// 图像自动对焦（目前在用）
        /// </summary>
        /// <param name="irisImage"></param>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public virtual void HandleIrisImageQualityCompute(float[] irisImage)
        {
            if (_irisImageQualityCompute == null) return;
            try
            {
                var result = _irisImageQualityCompute.Compute(irisImage, out int centerX, out int centerY, out int radius, out float quality);
                ShowIrisImageQualityComputeResultEventHandler?.BeginMulticastInvoke(this, new IrisImageQualityComputeResult
                {
                    CenterX = centerX,
                    CenterY = centerY,
                    Radius = radius,
                    Quality = quality,
                    IsExist = result,
                    Image = irisImage,
                });
            }
            catch (Exception ex)
            {
                //算法Actor线程上不弹模态框（弹框会阻塞实时对焦管线且每帧坏图像重复弹），也不重抛；记日志+留存异常图像后发布"未检出"结果继续运行
                LogHelper.Error("虹膜图像质量计算异常", ex);
                SaveWrongIrisImage(irisImage);
                ShowIrisImageQualityComputeResultEventHandler?.BeginMulticastInvoke(this, new IrisImageQualityComputeResult
                {
                    IsExist = false,
                    Image = irisImage,
                });
            }
        }

        /// <summary>
        /// 保存计算异常时的虹膜图像供技术人员回溯（自身失败仅记日志）
        /// </summary>
        private void SaveWrongIrisImage(float[] irisImage)
        {
            if (irisImage == null) return;
            try
            {
                string savePath = ServiceLocator.Current.GetInstance<DeviceConfigPara>().SaveDataPath;
                string targetFolderPath = Path.Combine(savePath, "WrongData");
                Directory.CreateDirectory(targetFolderPath);
                SaveDateToFileHelper.Save(new List<float[]> { irisImage }, targetFolderPath, "IrisImage" + Guid.NewGuid().ToString());
            }
            catch (Exception ex)
            {
                LogHelper.Error("异常虹膜图像保存失败", ex);
            }
        }

        /// <summary>
        /// 靶环模糊对焦计算（目前弃用）
        /// </summary>
        /// <param name="task"></param>
        public virtual void HandleTargetRingFocusCompute(List<float[]> FocusImages)
        {
            if (_targetRingRadiusCompute == null) return;
            //var Image = task?.FocusImage;
            if (FocusImages.Count == 0) return;
            List<Tuple<int, int, int>> validArray = new List<Tuple<int, int, int>>();
            int centerX = 0;
            int centerY = 0;
            int diamter = 0;
            bool isExist = false;
            int count = FocusImages.Count;
            for (int i = 0; i < count; ++i)
            {
                if (FocusImages[i] != null)
                {
                    bool result = _targetRingRadiusCompute.Execute(FocusImages[i], out int radius);
                    int x = _targetRingRadiusCompute.GetCenterX();
                    int y = _targetRingRadiusCompute.GetCenterY();
                    if (result)
                    {
                        validArray.Add(new Tuple<int, int, int>(x, y, radius));
                    }
                }
            }
            if (validArray.Count > FocusImages.Count / 2)
            {
                validArray.Sort((t1, t2) =>
                {
                    if (t1.Item3 != t2.Item3)
                    {
                        return t1.Item3.CompareTo(t2.Item3);
                    }
                    return 0;
                });
                centerX = validArray[validArray.Count / 2].Item1;
                centerY = validArray[validArray.Count / 2].Item2;
                diamter = validArray[validArray.Count / 2].Item3 * 2;
                isExist = true;
            }
            //bool result = _targetRingRadiusCompute.Execute(FocusImage, out int radius);
            //int x = _targetRingRadiusCompute.GetCenterX();
            //int y = _targetRingRadiusCompute.GetCenterY();
            ShowTargetRingFocusComputeResultEventHandler?.BeginMulticastInvoke(this, new TargetRingFocusComputeResult
            {
                CenterX = centerX,
                CenterY = centerY,
                IsExist = isExist,
                Diameter = diamter,
            });
        }


        /// <summary>
        /// 图像实时对准（目前在用）
        /// </summary>
        /// <param name="task">任务</param>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public virtual void HandleRealTimeTrackCompute(float[] irisImage)
        {

            //var irisImage = task?.IrisImage;
            // 当前采集图像为空则直接返回
            if (irisImage == null) return;
            //角膜计算未初始化则返回
            if (_cornealCurvatureCompute == null) return;

            //float[] InnerR = new float[3];
            //int[] centerArea = new int[1];
            //_cornealCurvatureCompute.RingsCenterCompute(irisImage, InnerR, out int single, out int lightNum);
            var eyeDetectResultType = EyeDetectResultType.NO_EYE;
            //int signal = -1;
            //bool computeResult = _cornealCurvatureCompute.CenterFaculaCentroidCompute(irisImage, out int x, out int y,out signal);
            bool computeResult = false;
            int x = 0, y = 0;
            try
            {
                computeResult = _irisImageQualityCompute.Compute(irisImage, out x, out y, out int r, out float q);
            }
            catch (Exception ex)
            {
                //算法Actor线程上不弹模态框（弹框会阻塞实时对准管线且每帧坏图像重复弹），也不重抛；记日志+留存异常图像后按NO_EYE继续发布结果
                LogHelper.Error("实时对准计算异常", ex);
                SaveWrongIrisImage(irisImage);
            }
            //if (computeResult) eyeDetectResultType = EyeDetectResultType.FULL_EYE_TARGETRING;

            // 向任务发送者发生计算结果
            //int offsetX = x == 0 ? x : _irisWidth / 2 - x;
            //int offsetY = y == 0 ? y : _irisHeight / 2 - y;

            if (computeResult)
            {
                eyeDetectResultType = EyeDetectResultType.FULL_EYE_TARGETRING;
                //switch (signal)
                //{
                //    case 0:
                //        eyeDetectResultType = EyeDetectResultType.FULL_EYE_TARGETRING;
                //        break;
                //    case 1:
                //        eyeDetectResultType = EyeDetectResultType.FULL_EYE_CENTER_FACULA;
                //        break;
                //    case 2:
                //        eyeDetectResultType = EyeDetectResultType.HALF_EYE;
                //        break;
                //    case 3:
                //        eyeDetectResultType = EyeDetectResultType.INNER_CIRCLE;
                //        break;
                //    default:
                //        break;
                //}
            }


            var result = new RealTimeTrackComputeResult
            {
                CenterX = x,
                CenterY = y,
                EyeDetectResultType = eyeDetectResultType
            };

            ShowRealTimeTrackComputeResultEventHandler?.BeginMulticastInvoke(this, result);
        }

        /// <summary>
        /// 采集信号过程中实施对准（目前在用）
        /// </summary>
        /// <param name="irisImage"></param>
        /// <returns></returns>
        public virtual int[] Reply_HandleRealTimeTrackCompute(float[] irisImage)
        {

            //var irisImage = task?.IrisImage;
            // 当前采集图像为空则直接返回
            if (irisImage == null) return null;
            //角膜计算未初始化则返回
            if (_irisImageQualityCompute == null) return null;

            //float[] InnerR = new float[3];
            //int[] centerArea = new int[1];
            //_cornealCurvatureCompute.RingsCenterCompute(irisImage, InnerR, out int single, out int lightNum);
            var eyeDetectResultType = EyeDetectResultType.NO_EYE;
            //int signal = -1;
            bool computeResult = _irisImageQualityCompute.Compute(irisImage, out int x, out int y, out int r, out float f);
            //bool computeResult = _cornealCurvatureCompute.CenterFaculaCentroidCompute(irisImage, out int x, out int y, out signal);
            //int x = (int)InnerR[0], y = (int)InnerR[1];
            //float r = InnerR[2];
            // 向任务发送者发生计算结果


            if (computeResult)
            {
                eyeDetectResultType = EyeDetectResultType.FULL_EYE_TARGETRING;
                //switch (signal)
                //{
                //    case 0:
                //        eyeDetectResultType = EyeDetectResultType.FULL_EYE_TARGETRING;
                //        break;
                //    case 1:
                //        eyeDetectResultType = EyeDetectResultType.FULL_EYE_CENTER_FACULA;
                //        break;
                //    case 2:
                //        eyeDetectResultType = EyeDetectResultType.HALF_EYE;
                //        break;
                //    case 3:
                //        eyeDetectResultType = EyeDetectResultType.INNER_CIRCLE;
                //        break;
                //    default:
                //        break;
                //}
            }
            if (eyeDetectResultType != EyeDetectResultType.NO_EYE)
            {
                return new int[2] { x, y };
            }
            else
            {
                return null;
            }
        }


        /// <summary>
        /// 小相机找眼（目前在用）
        /// </summary>
        /// <param name="task"></param>
        public virtual void HandleEyeLocateCompute(List<float[]> eyeImage)
        {
            //var eyeImage = task?.EyeImage;
            if (eyeImage.Count == 0) return;
            if (_eyeLocateCompute == null) return;
            //计算
            int centerX = 0;
            int centerY = 0;
            var status = EyeLocateResultType.NoEye;
            List<Tuple<int, int>> validArray = new List<Tuple<int, int>>();
            int count = eyeImage.Count;//异步线程只会对集合进行添加，不会删除，所以先记录集合大小，再执行。
            for (int i = 0; i < count; ++i)
            {
                if (eyeImage[i] != null)
                {
                    bool isFindEye = _eyeLocateCompute.Compute(eyeImage[i], out int circleX, out int circleY);
                    if (isFindEye)
                    {
                        //storage
                        validArray.Add(new Tuple<int, int>(circleX, circleY));
                    }
                }
            }
            //判断计算结果是否有效
            if (validArray.Count > eyeImage.Count / 2)//need at least half of the list size
            {
                status = EyeLocateResultType.HaveEye;
                //取有效列表中的中位数作为结果
                validArray.Sort((t1, t2) =>
                {
                    if (t1.Item1 != t2.Item1)
                    {
                        return t1.Item1.CompareTo(t2.Item1);
                    }
                    return 0;
                });
                centerX = validArray[validArray.Count / 2].Item1;
                centerY = validArray[validArray.Count / 2].Item2;
            }
            //结果存储
            var result = new EyeLocateComputeResult()
            {
                CenterX = centerX,
                CenterY = centerY,
                EyeLocateResultType = status,
            };

            //将结果反馈
            ShowEyeLocateComputeResultEventHandler?.BeginMulticastInvoke(this, result);
        }


        public virtual float HandleLightSpotCompute(float[] irisImage, int[] center)
        {
            if (irisImage == null) return 0;
            if (_irisImageQualityCompute == null) return 0;
            float distance = 0;
            var innerCornealCurvatureParse = new double[5];
            var outerCornealCurvatureParse = new double[5];
            var calibrationParams = new CalibrationParams
            {
                //该角膜曲率计算方式不受信号影响，默认第一个面矫正文件即可
                FactoryInterpolationDictionary = _algorithmsConfigs.CorrectFileConfigs[0].FactoryInterpolationDictionary,
                PixelSize = 0.0075,
                ObliqueDotGain = 0.005,
                IsInnerRing = false
            };


            int qualityCode = _irisImageQualityCompute.CornealCurvatureNewCompute(irisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, out distance);
            if (qualityCode < 0 || qualityCode == 4)
            {
                return 0;
            }

            var outerResult = _cornealCurvatureCalculator.Calculate(
                outerCornealCurvatureParse[2],
                outerCornealCurvatureParse[3],
                distance,
                outerCornealCurvatureParse[4],
                calibrationParams);

            center[0] = (int)innerCornealCurvatureParse[0];
            center[1] = (int)innerCornealCurvatureParse[1];
            return (float)outerResult.D_Shift;
        }

        private class CornealImageMetrics
        {
            public CornealCurvatureImageTask ImageTask { get; set; }
            public float LongAxisCornealCurvature { get; set; }
            public float ShortAxisCornealCurvature { get; set; }
        }

        public virtual void HandleEyeMultiParameterCompute(EyeMultiParameterPacketsComputeTask task)
        {
            if (!ShouldUseSeparatedCornealWorkflow(task))
            {
                HandleEyeMultiParameterCompute(task?.SignalTasks);
                return;
            }

            var selectedCornealImageTasks = SelectBestCornealImageTasks(task.CornealImageTasks);
            if (selectedCornealImageTasks.Count == 0)
            {
                HandleEyeMultiParameterCompute(task.SignalTasks);
                return;
            }

            var combinedTasks = BuildSeparatedCornealWorkflowTasks(task.SignalTasks, selectedCornealImageTasks);
            if (combinedTasks.Count == 0)
            {
                HandleEyeMultiParameterCompute(task.SignalTasks);
                return;
            }

            HandleEyeMultiParameterCompute(combinedTasks);
        }

        private bool ShouldUseSeparatedCornealWorkflow(EyeMultiParameterPacketsComputeTask task)
        {
            if (task?.SignalTasks == null || task.SignalTasks.Count == 0)
            {
                return false;
            }
            if (task.CornealImageTasks == null || task.CornealImageTasks.Count == 0)
            {
                return false;
            }

            var signalPeakCounts = task.SignalTasks
                .Where(item => item != null && item.SignalProcessComputeResult?.SignalPositionInfos != null)
                .Select(item => item.SignalProcessComputeResult.SignalPositionInfos.Count)
                .Distinct()
                .ToList();

            if (signalPeakCounts.Count != 1)
            {
                return false;
            }

            return signalPeakCounts[0] == 2 || signalPeakCounts[0] == 5;
        }

        private List<CornealCurvatureImageTask> SelectBestCornealImageTasks(List<CornealCurvatureImageTask> imageTasks)
        {
            var validImageTasks = imageTasks
                .Where(item => item?.IrisImage != null && item.IrisImage.Length == _irisHeight * _irisWidth)
                .OrderBy(item => Math.Abs(item.Distance))
                .Take(MaxCornealImageCandidateCount)
                .ToList();

            if (validImageTasks.Count == 0)
            {
                return new List<CornealCurvatureImageTask>();
            }

            int roughSelectionCount = GetCornealImageRoughSelectionCount(validImageTasks.Count);
            var nearestImageTasks = validImageTasks
                .Take(roughSelectionCount)
                .ToList();

            var metricsList = new List<CornealImageMetrics>();
            foreach (var imageTask in nearestImageTasks)
            {
                if (TryComputeCornealImageMetrics(imageTask, out var metrics))
                {
                    metricsList.Add(metrics);
                }
            }

            if (metricsList.Count == 0)
            {
                return new List<CornealCurvatureImageTask>();
            }

            int finalSelectionCount = Math.Min(DesiredCornealFrameCount, metricsList.Count);
            if (metricsList.Count <= finalSelectionCount)
            {
                return metricsList
                    .OrderBy(item => Math.Abs(item.ImageTask.Distance))
                    .Select(item => item.ImageTask)
                    .ToList();
            }

            return SelectMostConsistentCornealImageMetrics(metricsList, finalSelectionCount)
                .OrderBy(item => Math.Abs(item.ImageTask.Distance))
                .Select(item => item.ImageTask)
                .ToList();
        }

        private int GetCornealImageRoughSelectionCount(int validCount)
        {
            if (validCount <= DesiredCornealFrameCount)
            {
                return validCount;
            }

            int roughCount = validCount / 2;
            if (roughCount < DesiredCornealFrameCount)
            {
                roughCount = DesiredCornealFrameCount;
            }

            return Math.Min(MaxCornealImageRoughSelectionCount, roughCount);
        }

        private bool TryComputeCornealImageMetrics(CornealCurvatureImageTask imageTask, out CornealImageMetrics metrics)
        {
            metrics = null;
            if (imageTask?.IrisImage == null || imageTask.IrisImage.Length != _irisHeight * _irisWidth)
            {
                return false;
            }

            var innerCornealCurvatureParse = new double[5];
            var outerCornealCurvatureParse = new double[5];
            var calibrationParams = new CalibrationParams
            {
                FactoryInterpolationDictionary = _algorithmsConfigs.CorrectFileConfigs[0].FactoryInterpolationDictionary,
                PixelSize = 0.0075,
                ObliqueDotGain = 0.005,
                IsInnerRing = true
            };

            int qualityCode = _irisImageQualityCompute.CornealCurvatureNewCompute(imageTask.IrisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, out float lightSpotDistance);
            if (qualityCode < 0 || qualityCode == 4)
            {
                return false;
            }

            var innerResult = _cornealCurvatureCalculator.Calculate(
                innerCornealCurvatureParse[2],
                innerCornealCurvatureParse[3],
                lightSpotDistance,
                innerCornealCurvatureParse[4],
                calibrationParams);

            calibrationParams.IsInnerRing = false;
            var outerResult = _cornealCurvatureCalculator.Calculate(
                outerCornealCurvatureParse[2],
                outerCornealCurvatureParse[3],
                lightSpotDistance,
                outerCornealCurvatureParse[4],
                calibrationParams);

            float longAxisCornealCurvature = ((float)innerResult.RMajor + (float)outerResult.RMajor) / 2f;
            float shortAxisCornealCurvature = ((float)innerResult.RMinor + (float)outerResult.RMinor) / 2f;
            if (Math.Abs(longAxisCornealCurvature - shortAxisCornealCurvature) <= 0.03f)
            {
                float cornealCurvatureMeanValue = (longAxisCornealCurvature + shortAxisCornealCurvature) / 2f;
                longAxisCornealCurvature = cornealCurvatureMeanValue;
                shortAxisCornealCurvature = cornealCurvatureMeanValue;
            }
            if (float.IsNaN(longAxisCornealCurvature) || float.IsInfinity(longAxisCornealCurvature) ||
                float.IsNaN(shortAxisCornealCurvature) || float.IsInfinity(shortAxisCornealCurvature))
            {
                return false;
            }

            metrics = new CornealImageMetrics
            {
                ImageTask = imageTask,
                LongAxisCornealCurvature = longAxisCornealCurvature,
                ShortAxisCornealCurvature = shortAxisCornealCurvature
            };
            return true;
        }

        private void SaveCornealCurvatureRawResult(
            EyeMultiParameterComputeResult computeResult,
            EyeResult innerResult,
            EyeResult outerResult,
            double[] innerCornealCurvatureParse,
            double[] outerCornealCurvatureParse,
            float lightSpotDistance)
        {
            if (computeResult == null)
            {
                return;
            }

            computeResult.LightSpotDistance = lightSpotDistance;
            if (computeResult.CornealCurvatureComputeResult == null)
            {
                computeResult.CornealCurvatureComputeResult = new CornealCurvatureComputeResult();
            }

            computeResult.CornealCurvatureComputeResult.InnerCornealCurvatureParses.LongAxis = innerResult.RMajor;
            computeResult.CornealCurvatureComputeResult.InnerCornealCurvatureParses.ShortAxis = innerResult.RMinor;
            computeResult.CornealCurvatureComputeResult.InnerCornealCurvatureParses.SemiLongAxisPixel = innerCornealCurvatureParse != null && innerCornealCurvatureParse.Length > 2 ? innerCornealCurvatureParse[2] : 0;
            computeResult.CornealCurvatureComputeResult.InnerCornealCurvatureParses.SemiShortAxisPixel = innerCornealCurvatureParse != null && innerCornealCurvatureParse.Length > 3 ? innerCornealCurvatureParse[3] : 0;
            computeResult.CornealCurvatureComputeResult.InnerCornealCurvatureParses.Xtheta = innerCornealCurvatureParse != null && innerCornealCurvatureParse.Length > 4 ? innerCornealCurvatureParse[4] : 0;

            computeResult.CornealCurvatureComputeResult.OuterCornealCurvatureParses.LongAxis = outerResult.RMajor;
            computeResult.CornealCurvatureComputeResult.OuterCornealCurvatureParses.ShortAxis = outerResult.RMinor;
            computeResult.CornealCurvatureComputeResult.OuterCornealCurvatureParses.SemiLongAxisPixel = outerCornealCurvatureParse != null && outerCornealCurvatureParse.Length > 2 ? outerCornealCurvatureParse[2] : 0;
            computeResult.CornealCurvatureComputeResult.OuterCornealCurvatureParses.SemiShortAxisPixel = outerCornealCurvatureParse != null && outerCornealCurvatureParse.Length > 3 ? outerCornealCurvatureParse[3] : 0;
            computeResult.CornealCurvatureComputeResult.OuterCornealCurvatureParses.Xtheta = outerCornealCurvatureParse != null && outerCornealCurvatureParse.Length > 4 ? outerCornealCurvatureParse[4] : 0;
        }

        private List<CornealImageMetrics> SelectMostConsistentCornealImageMetrics(List<CornealImageMetrics> metricsList, int targetCount)
        {
            if (metricsList == null || metricsList.Count == 0 || targetCount <= 0)
            {
                return new List<CornealImageMetrics>();
            }

            if (metricsList.Count <= targetCount)
            {
                return metricsList.ToList();
            }

            double bestScore = double.MaxValue;
            double bestDistanceScore = double.MaxValue;
            List<CornealImageMetrics> bestSubset = null;
            var currentSubset = new List<CornealImageMetrics>(targetCount);
            SelectMostConsistentCornealImageMetricsCore(metricsList, targetCount, 0, currentSubset, ref bestScore, ref bestDistanceScore, ref bestSubset);

            return bestSubset ?? metricsList.Take(targetCount).ToList();
        }

        private void SelectMostConsistentCornealImageMetricsCore(
            List<CornealImageMetrics> metricsList,
            int targetCount,
            int startIndex,
            List<CornealImageMetrics> currentSubset,
            ref double bestScore,
            ref double bestDistanceScore,
            ref List<CornealImageMetrics> bestSubset)
        {
            if (currentSubset.Count == targetCount)
            {
                double score = CalculateVariance(currentSubset.Select(item => item.LongAxisCornealCurvature)) +
                    CalculateVariance(currentSubset.Select(item => item.ShortAxisCornealCurvature));
                double distanceScore = currentSubset.Sum(item => Math.Abs(item.ImageTask.Distance));
                if (score < bestScore || (Math.Abs(score - bestScore) < 0.000001 && distanceScore < bestDistanceScore))
                {
                    bestScore = score;
                    bestDistanceScore = distanceScore;
                    bestSubset = currentSubset.ToList();
                }
                return;
            }

            int remainingRequiredCount = targetCount - currentSubset.Count;
            for (int i = startIndex; i <= metricsList.Count - remainingRequiredCount; i++)
            {
                currentSubset.Add(metricsList[i]);
                SelectMostConsistentCornealImageMetricsCore(metricsList, targetCount, i + 1, currentSubset, ref bestScore, ref bestDistanceScore, ref bestSubset);
                currentSubset.RemoveAt(currentSubset.Count - 1);
            }
        }

        private double CalculateVariance(IEnumerable<float> values)
        {
            var valueList = values.ToList();
            if (valueList.Count == 0)
            {
                return double.MaxValue;
            }

            double mean = valueList.Average();
            double sum = 0;
            foreach (var value in valueList)
            {
                double diff = value - mean;
                sum += diff * diff;
            }
            return sum / valueList.Count;
        }

        private List<EyeMultiParameterComputeTask> BuildSeparatedCornealWorkflowTasks(
            List<EyeMultiParameterComputeTask> signalTasks,
            List<CornealCurvatureImageTask> cornealImageTasks)
        {
            var combinedTasks = new List<EyeMultiParameterComputeTask>();
            if (signalTasks == null || cornealImageTasks == null)
            {
                return combinedTasks;
            }

            int cornealTaskCount = Math.Min(DesiredCornealFrameCount, cornealImageTasks.Count);
            for (int i = 0; i < signalTasks.Count; i++)
            {
                if (signalTasks[i] == null)
                {
                    continue;
                }

                bool useForCornealCurvature = i < cornealTaskCount && cornealImageTasks[i] != null;
                var irisImage = useForCornealCurvature ? cornealImageTasks[i].IrisImage : signalTasks[i].IrisImage;
                if (irisImage == null)
                {
                    continue;
                }

                combinedTasks.Add(new EyeMultiParameterComputeTask
                {
                    SignalProcessComputeResult = signalTasks[i].SignalProcessComputeResult,
                    IrisImage = irisImage,
                    CorrModel = signalTasks[i].CorrModel,
                    IsSimulateEye = signalTasks[i].IsSimulateEye,
                    PolarRotateIndex = useForCornealCurvature ? cornealImageTasks[i].PolarRotateIndex : signalTasks[i].PolarRotateIndex,
                    IsCompensateAL = signalTasks[i].IsCompensateAL,
                    UseForCornealCurvature = useForCornealCurvature,
                    CornealCurvatureDistance = useForCornealCurvature ? cornealImageTasks[i].Distance : signalTasks[i].CornealCurvatureDistance
                });
            }
            return combinedTasks;
        }

        /// <summary>
        /// 计算多参数（目前在用）
        /// </summary>
        /// <param name="tasks"></param>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public virtual void HandleEyeMultiParameterCompute(List<EyeMultiParameterComputeTask> tasks)
        {
            if (tasks == null || tasks.Count == 0)
            {
                return;
            }
            var computeResults = new List<EyeMultiParameterComputeResult>();
            int count = tasks.Count;//异步线程只会对集合进行添加，不会删除，所以先记录集合大小，再执行。
            int n = 0;
            var calibrationParams = new CalibrationParams
            {
                //该角膜曲率计算方式不受信号影响，默认第一个面矫正文件即可
                FactoryInterpolationDictionary = _algorithmsConfigs.CorrectFileConfigs[0].FactoryInterpolationDictionary,
                PixelSize = 0.0075,
                ObliqueDotGain = 0.005,
                IsInnerRing = true
            };
            try
            {
                for (n = 0; n < count; ++n)
                {
                    if (tasks[n] == null ||
                        tasks[n].IrisImage == null ||
                        tasks[n].IrisImage.Length != _irisHeight * _irisWidth ||
                        tasks[n].SignalProcessComputeResult.DAQData.Channel1 == null ||
                        tasks[n].SignalProcessComputeResult.SignalPositionInfos == null ||
                        tasks[n].SignalProcessComputeResult.SignalPositionInfos.Count == 0)
                    {
                        continue;
                    }

                    var computeResult = new EyeMultiParameterComputeResult();
                    computeResult.EyeMultiParameterComputeTask = tasks[n];
                    computeResult.UseForCornealCurvature = tasks[n].UseForCornealCurvature;

                    #region SignalProcess
                    var signalIndex = tasks[n].SignalProcessComputeResult.Index;//信号索引，对应第几个面的信号
                    double p1 = -0.0550531645935891;//眼轴长转换公式
                    double p2 = 1.4272541328905;
                    //标尺光计算，得到每个信号峰位于标尺光中第几个周期，即标尺光矫正数组中的第几个索引
                    _referenceRulerCompute.Compute(tasks[n].SignalProcessComputeResult.DAQData.Channel1);
                    int signalPeaksNum = tasks[n].SignalProcessComputeResult.SignalPositionInfos.Count;
                    int[] multiSignalPeaksRulerIndexArray = new int[signalPeaksNum];
                    for (int i = 0; i < signalPeaksNum; ++i)
                    {
                        //_referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex, tasks[n].SignalProcessComputeResult.SignalPositionInfos[i].Position);
                        //multiSignalPeaksRulerIndexArray[i] = _referenceRulerCompute.GetCycleNum();//对应标尺光索引位置

                        if (signalIndex == 0)
                        {
                            _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex, tasks[n].SignalProcessComputeResult.SignalPositionInfos[i].Position);
                            multiSignalPeaksRulerIndexArray[i] = _referenceRulerCompute.GetCycleNum();
                        }
                        else
                        {
                            _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex - 1572864, tasks[n].SignalProcessComputeResult.SignalPositionInfos[i].Position);
                            multiSignalPeaksRulerIndexArray[i] = _referenceRulerCompute.GetCycleNum();
                        }
                    }
                    if (signalPeaksNum == 2)//AK
                    {

                        var eyeAxisLength = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[0], multiSignalPeaksRulerIndexArray[1], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                        //眼轴转换公式
                        if (!tasks[n].IsSimulateEye) eyeAxisLength = (float)(eyeAxisLength - (p1 * eyeAxisLength + p2));
                        computeResult.Case.Model = MeasureMode.AK;
                        computeResult.Case.AxialLength = eyeAxisLength;
                        Console.WriteLine("AL: {0}  【{1}】", eyeAxisLength, signalIndex);
                    }
                    if (signalPeaksNum == 5)//AScan
                    {
                        float[] eyeMultiParameters = new float[tasks[n].SignalProcessComputeResult.SignalPositionInfos.Count];//存储顺序为AL CCT ACD LT VT（其中VT=AL-CCT-ACD-LT）
                                                                                                                              //AL
                        eyeMultiParameters[0] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[0], multiSignalPeaksRulerIndexArray[4], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                        //CCT
                        eyeMultiParameters[1] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[0], multiSignalPeaksRulerIndexArray[1], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                        //ACD
                        eyeMultiParameters[2] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[1], multiSignalPeaksRulerIndexArray[2], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                        //LT
                        eyeMultiParameters[3] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[2], multiSignalPeaksRulerIndexArray[3], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);

                        //AL和LT转换公式
                        float k_lt = 1.008f;
                        float b_lt = -0.1892f;
                        float k_acd = 1.064f;
                        float b_acd = -0.2613f;
                        if (!tasks[n].IsSimulateEye)
                        {
                            eyeMultiParameters[0] = (float)(eyeMultiParameters[0] - (p1 * eyeMultiParameters[0] + p2));//眼轴长转换
                            eyeMultiParameters[2] = eyeMultiParameters[2] * k_acd + b_acd;//ACD转换
                            eyeMultiParameters[3] = eyeMultiParameters[3] * k_lt + b_lt;//LT转换
                        }
                        //VT
                        eyeMultiParameters[4] = eyeMultiParameters[0] - eyeMultiParameters[1] - eyeMultiParameters[2] - eyeMultiParameters[3];

                        computeResult.Case.Model = MeasureMode.AScan;
                        computeResult.Case.AxialLength = eyeMultiParameters[0];
                        computeResult.Case.CentralCornealThickness = eyeMultiParameters[1] * 1000;//转化为um
                        computeResult.Case.AnteriorchamberDepth = eyeMultiParameters[2];
                        computeResult.Case.LensThickness = eyeMultiParameters[3];
                        computeResult.Case.VitreousThickness = eyeMultiParameters[4];
                        //Console.WriteLine("AL: " + eyeMultiParameters[0]);
                        //Console.WriteLine("CCT: " + eyeMultiParameters[1] * 1000);
                        //Console.WriteLine("ACD: " + eyeMultiParameters[2]);
                        //Console.WriteLine("LT: " + eyeMultiParameters[3]);
                        //Console.WriteLine("VT: " + eyeMultiParameters[4]);
                    }


                    #endregion

                    #region ImageProcess
                    var innerCornealCurvatureParse = new double[5];
                    var outerCornealCurvatureParse = new double[5];
                    var innerCircleParse = new float[3];
                    float lightSpotDistance = 0;

                    var inputAxisPixelArray = new double[4];
                    var outputKArray = new float[4];
                    int fitArrayIndex = 0;

                    if (signalIndex == 0)
                    {
                        fitArrayIndex = (int)((tasks[n].SignalProcessComputeResult.SignalPositionInfos[0].Position - (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex + (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FlagNum - _algorithmsConfigs.fitArray3DLength / 2) * 128)) / 128);
                    }
                    else
                    {
                        fitArrayIndex = (int)((tasks[n].SignalProcessComputeResult.SignalPositionInfos[0].Position - (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex - 1572864 + (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FlagNum - _algorithmsConfigs.fitArray3DLength / 2) * 128)) / 128);
                    }


                    //角膜曲率图像计算
                    //result有5种返回结果：
                    //-1-靶环区域提取失败
                    //0-计算准确；
                    //1-计算可能不准确（光斑点数不是严格的32个）；
                    //2-计算可能不准确（内环光斑面积标准差较大，形状不一致）；
                    //3-计算可能不准确（外环光斑面积标准差较大，形状不一致）；
                    //4-计算不准确（光斑点数少于10个）；
                    if (tasks[n].IsSimulateEye)
                    {
                        //_cornealCurvatureCompute.Compute(tasks[n].IrisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, innerCircleParse, out int single, out int num, out int neiArea);
                        _irisImageQualityCompute.CornealCurvatureNewCompute(tasks[n].IrisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, out lightSpotDistance);
                    }
                    else
                    {
                        int result = _irisImageQualityCompute.CornealCurvatureNewCompute(tasks[n].IrisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, out lightSpotDistance);
                    }
                    //_cornealCurvatureCompute.Compute(tasks[n].IrisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, innerCircleParse, out int single, out int num, out int neiArea);
                    inputAxisPixelArray[0] = innerCornealCurvatureParse[2];
                    inputAxisPixelArray[1] = innerCornealCurvatureParse[3];
                    inputAxisPixelArray[2] = outerCornealCurvatureParse[2];
                    inputAxisPixelArray[3] = outerCornealCurvatureParse[3];
                    
                    { //新810E角膜曲率
                        //角膜曲率公式计算
                        calibrationParams.IsInnerRing = true;
                        var innerResult = _cornealCurvatureCalculator.Calculate(
                            innerCornealCurvatureParse[2],
                            innerCornealCurvatureParse[3],
                            lightSpotDistance,
                            innerCornealCurvatureParse[4],
                            calibrationParams);

                        calibrationParams.IsInnerRing = false;
                        var outerResult = _cornealCurvatureCalculator.Calculate(
                            outerCornealCurvatureParse[2],
                            outerCornealCurvatureParse[3],
                            lightSpotDistance,
                            outerCornealCurvatureParse[4],
                            calibrationParams);

                        //重建outputKArray数组
                        outputKArray[0] = (float)innerResult.RMinor;
                        outputKArray[1] = (float)innerResult.RMajor;
                        outputKArray[2] = (float)outerResult.RMinor;
                        outputKArray[3] = (float)outerResult.RMajor;
                        SaveCornealCurvatureRawResult(computeResult, innerResult, outerResult, innerCornealCurvatureParse, outerCornealCurvatureParse, lightSpotDistance);
                        Console.WriteLine("compute result lightSpotDistance:" + lightSpotDistance + "  inner-D:" + innerResult.D_Shift + "  outer-D:" + outerResult.D_Shift);
                    }


                    //float minR = 4.0F;
                    //float maxR = 12.5F;
                    //if (_algorithmsConfigs.fitArray2DLength == 39)
                    //{
                    //    minR = 6.0F;
                    //    maxR = 9.8F;
                    //}
                    //switch (tasks[n].CorrModel)
                    //{
                    //    case CorrModelType.凹模拟眼:
                    //        fitArrayIndex = ((tasks[n].SignalProcessComputeResult.SignalPositionInfos[0].Position - (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex + (_algorithmsConfigs.CorrectFileConfigs[signalIndex].ConcaveFlagNum - _algorithmsConfigs.fitArray3DLength / 2) * 128)) / 128);
                    //        _cornealCurvatureFormulaCompute[signalIndex].Compute(inputAxisPixelArray, outputKArray, fitArrayIndex, multiSignalPeaksRulerIndexArray[0], maxR, minR, true, 1.45F);//CornealCurvatureFormula
                    //        break;
                    //    case CorrModelType.凸模拟眼:
                    //        _cornealCurvatureFormulaCompute[signalIndex].Compute(inputAxisPixelArray, outputKArray, fitArrayIndex, multiSignalPeaksRulerIndexArray[0], maxR, minR, false, 1.45F);//CornealCurvatureFormula
                    //        break;
                    //    default:
                    //        break;
                    //}

                    float InnerRadiusShortAxis = outputKArray[0];//长短轴找中值
                    float InnerRadiusLongAxis = outputKArray[1];
                    float OuterRadiusShortAxis = outputKArray[2];
                    float OuterRadiuslongAxis = outputKArray[3];
                    float longAxisCornealCurvature = (InnerRadiusLongAxis + OuterRadiuslongAxis) / 2;//K1
                    float shortAxisCornealCurvature = (InnerRadiusShortAxis + OuterRadiusShortAxis) / 2;//K2
                    float cornealCurvatureMeanValue = (longAxisCornealCurvature + shortAxisCornealCurvature) / 2;//AVG k

                    Console.WriteLine("K1: {0}  【{1}】", longAxisCornealCurvature, signalIndex);
                    //Console.WriteLine("K2: {0}  【{1}】", shortAxisCornealCurvature, signalIndex);
                    //轴位角
                    int LongAxis_Axial_angle_mean = (int)innerCornealCurvatureParse[4];
                    if (Math.Abs(longAxisCornealCurvature - shortAxisCornealCurvature) <= 0.03)
                    {
                        longAxisCornealCurvature = cornealCurvatureMeanValue;
                        shortAxisCornealCurvature = cornealCurvatureMeanValue;
                        LongAxis_Axial_angle_mean = 0;
                    }
                    computeResult.Case.LongAxisCornealCurvature = longAxisCornealCurvature;//K2
                    computeResult.Case.ShortAxisCornealCurvature = shortAxisCornealCurvature;//K1
                    computeResult.Case.CornealCurvatureMeanValue = cornealCurvatureMeanValue;//AVG k
                    computeResult.Case.LongAxis_Axial_angle_mean = LongAxis_Axial_angle_mean;//轴位角

                    //白到白
                    float[] psAndWTWParse = new float[6];
                    _innerAndOuterDiameterCompute.Compute(tasks[n].IrisImage, psAndWTWParse, -1, 1);
                    computeResult.PSAndWTWComputeResult = psAndWTWParse;
                    float ps = (float)((psAndWTWParse[2] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].magnificiation) * 1.095f;
                    float wtw = (float)((psAndWTWParse[5] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].magnificiation) * 1.095f;
                    computeResult.Case.PupillaryDiameter = ps;
                    computeResult.Case.WhiteToWhite = wtw;

                    //Alpha角、Kappa角
                    //坐标轴转换[图像坐标轴->角膜为中心点的坐标轴]
                    var centralCornealCenterPointX = (int)((innerCornealCurvatureParse[0] + outerCornealCurvatureParse[0]) / 2);
                    var centralCornealCenterPointY = (int)((innerCornealCurvatureParse[1] + outerCornealCurvatureParse[1]) / 2);
                    var axisConversionToolModel = new ComputeToolModel.ImageAxisConversionToolModel(centralCornealCenterPointX, centralCornealCenterPointY);//该类是一个工具类，用于转换图像坐标轴到角膜中心点坐标轴
                    //计算Alpha角
                    var alphaAngle = axisConversionToolModel.AxisConverse(new Tuple<int, int>((int)psAndWTWParse[3], (int)psAndWTWParse[4]));//输入图像坐标，即可转化为角膜中心点的坐标轴对应的坐标
                    computeResult.Case.Ix = (float)(alphaAngle.Item1 * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].pixel / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].magnificiation);
                    computeResult.Case.Iy = (float)(alphaAngle.Item2 * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].pixel / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].magnificiation);
                    //计算kappa角
                    var kappaAngle = axisConversionToolModel.AxisConverse(new Tuple<int, int>((int)psAndWTWParse[0], (int)psAndWTWParse[1]));
                    var cwc_len = Math.Sqrt(kappaAngle.Item1 * kappaAngle.Item1 + kappaAngle.Item2 * kappaAngle.Item2);
                    var cwc_angle = Math.Atan2(kappaAngle.Item2, kappaAngle.Item1) * 180 / Math.PI;
                    computeResult.Case.CWC_Length = (float)(cwc_len * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].pixel / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].magnificiation);
                    computeResult.Case.CWC_Angle = (int)cwc_angle;
                    if (computeResult.Case.CWC_Angle < 0) computeResult.Case.CWC_Angle = 360 + computeResult.Case.CWC_Angle;
                    #endregion

                    computeResults.Add(computeResult);
                }
            }
            catch (Exception ex)
            {
                //save signals
                // 获取保存目录
                string savePath = ServiceLocator.Current.GetInstance<DeviceConfigPara>().SaveDataPath;

                // 指定目标文件夹的名称
                string folderName = "WrongData";
                // 构建目标文件夹的完整路径
                string targetFolderPath = Path.Combine(savePath, folderName);
                // 检查目标文件夹是否存在，如果不存在则创建它
                if (!Directory.Exists(targetFolderPath))
                {
                    Directory.CreateDirectory(targetFolderPath);
                }
                //save
                SaveDateToFileHelper.Save(new List<short[]> { tasks[n].SignalProcessComputeResult.DAQData.Channel1 }, targetFolderPath, "Channel1" + Guid.NewGuid().ToString());
                SaveDateToFileHelper.Save(new List<short[]> { tasks[n].SignalProcessComputeResult.DAQData.Channel2 }, targetFolderPath, "Channel2" + Guid.NewGuid().ToString());
                SaveDateToFileHelper.Save(new List<float[]> { tasks[n].IrisImage }, targetFolderPath, "IrisImage" + Guid.NewGuid().ToString());
                MessageBox.Show("数据处理出现异常！请及时联系技术人员上报异常数据，便于进一步解决问题。\n " +
                "给您带来了不便，深感抱歉！\n" +
                ex.ToString());

                throw;
            }
            if (!ServiceLocator.Current.GetInstance<DeviceConfigPara>().IsBlueLEDMeasure) Flush();//这个方法计算最耗时，会将信号处理消息堵住多帧，为了信号的实时性，需要清除队列缓存。
            MessageDispatcher.Send(new EyeMultiParameterPacketsComputeDataPostProcessActor(computeResults));
        }
        /// <summary>
        /// 模拟眼计算（目前在用）
        /// </summary>
        /// <param name="task"></param>
        public virtual void HandleEyeMultiParameterCompute(EyeMultiParameterComputeTask task)
        {
            if (task == null ||
                task.IrisImage == null ||
                task.IrisImage.Length != _irisHeight * _irisWidth ||
                task.SignalProcessComputeResult.DAQData.Channel1 == null ||
                task.SignalProcessComputeResult.SignalPositionInfos == null ||
                task.SignalProcessComputeResult.SignalPositionInfos.Count == 0)
            {
                EyeMultiParameterComputeEventHandler?.BeginInvoke(this, null, null, null);
                return;
            }

            var computeResult = new EyeMultiParameterComputeResult();
            computeResult.EyeMultiParameterComputeTask = task;
            computeResult.UseForCornealCurvature = task.UseForCornealCurvature;
            var calibrationParams = new CalibrationParams
            {
                //该角膜曲率计算方式不受信号影响，默认第一个面矫正文件即可
                FactoryInterpolationDictionary = _algorithmsConfigs.CorrectFileConfigs[0].FactoryInterpolationDictionary,
                PixelSize = 0.0075,
                ObliqueDotGain = 0.005,
                IsInnerRing = true
            };

            #region SignalProcess
            var signalIndex = task.SignalProcessComputeResult.Index;//信号索引，对应第几个面的信号
            double p1 = -0.0550531645935891;//眼轴长转换公式
            double p2 = 1.4272541328905;
            //标尺光计算，得到每个信号峰位于标尺光中第几个周期，即标尺光矫正数组中的第几个索引
            _referenceRulerCompute.Compute(task.SignalProcessComputeResult.DAQData.Channel1);
            int signalPeaksNum = task.SignalProcessComputeResult.SignalPositionInfos.Count;
            int[] multiSignalPeaksRulerIndexArray = new int[signalPeaksNum];
            for (int i = 0; i < task.SignalProcessComputeResult.SignalPositionInfos.Count; ++i)
            {
                _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex, task.SignalProcessComputeResult.SignalPositionInfos[i].Position);
                multiSignalPeaksRulerIndexArray[i] = _referenceRulerCompute.GetCycleNum();//对应标尺光索引位置
            }
            if (signalPeaksNum == 2)//AK
            {
                var eyeAxisLength = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[0], multiSignalPeaksRulerIndexArray[1], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                //眼轴转换公式
                if (!task.IsSimulateEye) eyeAxisLength = (float)(eyeAxisLength - (p1 * eyeAxisLength + p2));
                computeResult.Case.Model = MeasureMode.AK;
                computeResult.Case.AxialLength = eyeAxisLength;
            }
            if (signalPeaksNum == 5)//AScan
            {
                //var positions = new int[5];
                //var values = new double[5];
                //double[] ThresholdSet = new double[5] { 50000, 6000, 10000, 6000, _algorithmsConfigs.CorrectFileConfigs.SignalThreshold };
                //_ascanCompute.Find_FiveCrest1(task.SignalProcessComputeResult.ProcessedSignal, positions, values, task.SignalProcessComputeResult.ProcessedSignal.Length, ThresholdSet);
                //for (int i = 0; i < task.SignalProcessComputeResult.SignalPositionInfos.Count; ++i)
                //{
                //    _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs.FrontBaseIndex, positions[i]);
                //    multiSignalPeaksRulerIndexArray[i] = _referenceRulerCompute.GetCycleNum();//对应标尺光索引位置
                //}
                float[] eyeMultiParameters = new float[task.SignalProcessComputeResult.SignalPositionInfos.Count];//存储顺序为AL CCT ACD LT VT（其中VT=AL-CCT-ACD-LT）
                //AL
                eyeMultiParameters[0] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[0], multiSignalPeaksRulerIndexArray[4], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                //CCT
                eyeMultiParameters[1] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[0], multiSignalPeaksRulerIndexArray[1], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                //ACD
                eyeMultiParameters[2] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[1], multiSignalPeaksRulerIndexArray[2], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                //LT
                eyeMultiParameters[3] = CalculateActualAxialThickness(multiSignalPeaksRulerIndexArray[2], multiSignalPeaksRulerIndexArray[3], GetTotalNumsOrNull(signalIndex), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                //AL和LT转换公式
                float k = 0.01875f;
                float b = 0.127f;
                if (!task.IsSimulateEye)
                {
                    eyeMultiParameters[0] = (float)(eyeMultiParameters[0] - (p1 * eyeMultiParameters[0] + p2));//眼轴长转换
                    eyeMultiParameters[3] = eyeMultiParameters[3] - (eyeMultiParameters[3] * k + b);
                }
                //VT
                eyeMultiParameters[4] = eyeMultiParameters[0] - eyeMultiParameters[1] - eyeMultiParameters[2] - eyeMultiParameters[3];

                computeResult.Case.Model = MeasureMode.AScan;
                computeResult.Case.AxialLength = eyeMultiParameters[0];
                computeResult.Case.CentralCornealThickness = eyeMultiParameters[1] * 1000;//转化为um
                computeResult.Case.AnteriorchamberDepth = eyeMultiParameters[2];
                computeResult.Case.LensThickness = eyeMultiParameters[3];
                computeResult.Case.VitreousThickness = eyeMultiParameters[4];
            }

            #endregion

            #region ImageProcess
            var innerCornealCurvatureParse = new double[5];
            var outerCornealCurvatureParse = new double[5];
            var innerCircleParse = new float[3];

            var inputAxisPixelArray = new double[4];
            var outputKArray = new float[4];
            int fitArrayIndex = (int)((task.SignalProcessComputeResult.SignalPositionInfos[0].Position - (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex + (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FlagNum - _algorithmsConfigs.fitArray3DLength / 2) * 128)) / 128);

            //角膜曲率图像计算
            //_cornealCurvatureCompute.Compute(task.IrisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, innerCircleParse, out int single, out int num, out int neiArea);
            _irisImageQualityCompute.CornealCurvatureNewCompute(task.IrisImage, innerCornealCurvatureParse, outerCornealCurvatureParse, out float lightSpotDistance);
            inputAxisPixelArray[0] = innerCornealCurvatureParse[3];
            inputAxisPixelArray[1] = innerCornealCurvatureParse[2];
            inputAxisPixelArray[2] = outerCornealCurvatureParse[3];
            inputAxisPixelArray[3] = outerCornealCurvatureParse[2];

            { //新810E角膜曲率
              //角膜曲率公式计算
                calibrationParams.IsInnerRing = true;
                var innerResult = _cornealCurvatureCalculator.Calculate(
                    innerCornealCurvatureParse[2],
                    innerCornealCurvatureParse[3],
                    lightSpotDistance,
                    innerCornealCurvatureParse[4],
                    calibrationParams);

                calibrationParams.IsInnerRing = false;
                var outerResult = _cornealCurvatureCalculator.Calculate(
                    outerCornealCurvatureParse[2],
                    outerCornealCurvatureParse[3],
                    lightSpotDistance,
                    outerCornealCurvatureParse[4],
                    calibrationParams);

                //重建outputKArray数组
                outputKArray[0] = (float)innerResult.RMinor;
                outputKArray[1] = (float)innerResult.RMajor;
                outputKArray[2] = (float)outerResult.RMinor;
                outputKArray[3] = (float)outerResult.RMajor;
                SaveCornealCurvatureRawResult(computeResult, innerResult, outerResult, innerCornealCurvatureParse, outerCornealCurvatureParse, lightSpotDistance);
            }


            ////角膜曲率公式计算
            //float minR = 4.0F;
            //float maxR = 12.5F;
            //if (_algorithmsConfigs.fitArray2DLength == 39)
            //{
            //    minR = 6.0F;
            //    maxR = 9.8F;
            //}
            //switch (task.CorrModel)
            //{
            //    case CorrModelType.凹模拟眼:
            //        fitArrayIndex = ((task.SignalProcessComputeResult.SignalPositionInfos[0].Position - (_algorithmsConfigs.CorrectFileConfigs[signalIndex].FrontBaseIndex + (_algorithmsConfigs.CorrectFileConfigs[signalIndex].ConcaveFlagNum - _algorithmsConfigs.fitArray3DLength / 2) * 128)) / 128);
            //        _cornealCurvatureFormulaCompute[signalIndex].Compute(inputAxisPixelArray, outputKArray, fitArrayIndex, multiSignalPeaksRulerIndexArray[0], maxR, minR, true, 1.45F);//CornealCurvatureFormula
            //        break;
            //    case CorrModelType.凸模拟眼:
            //        _cornealCurvatureFormulaCompute[signalIndex].Compute(inputAxisPixelArray, outputKArray, fitArrayIndex, multiSignalPeaksRulerIndexArray[0], maxR, minR, false, 1.45F);//CornealCurvatureFormula
            //        break;
            //    default:
            //        break;
            //}
            float InnerRadiusShortAxis = outputKArray[0];//长短轴找中值
            float InnerRadiusLongAxis = outputKArray[1];
            float OuterRadiusShortAxis = outputKArray[2];
            float OuterRadiuslongAxis = outputKArray[3];
            float longAxisCornealCurvature = (InnerRadiusLongAxis + OuterRadiuslongAxis) / 2;//K1
            float shortAxisCornealCurvature = (InnerRadiusShortAxis + OuterRadiusShortAxis) / 2;//K1
            float cornealCurvatureMeanValue = (longAxisCornealCurvature + shortAxisCornealCurvature) / 2;//AVG k

            //轴位角
            int LongAxis_Axial_angle_mean = (int)innerCornealCurvatureParse[4];
            if (Math.Abs(longAxisCornealCurvature - shortAxisCornealCurvature) <= 0.03)
            {
                longAxisCornealCurvature = cornealCurvatureMeanValue;
                shortAxisCornealCurvature = cornealCurvatureMeanValue;
                LongAxis_Axial_angle_mean = 0;
            }
            computeResult.Case.LongAxisCornealCurvature = longAxisCornealCurvature;//K2
            computeResult.Case.ShortAxisCornealCurvature = shortAxisCornealCurvature;//K1
            computeResult.Case.CornealCurvatureMeanValue = cornealCurvatureMeanValue;//AVG k
            computeResult.Case.LongAxis_Axial_angle_mean = LongAxis_Axial_angle_mean;//轴位角

            //白到白
            float[] psAndWTWParse = new float[6];
            _innerAndOuterDiameterCompute.Compute(task.IrisImage, psAndWTWParse, -1, 1);
            computeResult.PSAndWTWComputeResult = psAndWTWParse;
            float ps = (float)((psAndWTWParse[2] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].magnificiation);
            float wtw = (float)((psAndWTWParse[5] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[signalIndex].magnificiation);
            computeResult.Case.PupillaryDiameter = ps;
            computeResult.Case.WhiteToWhite = wtw;
            #endregion

            EyeMultiParameterComputeEventHandler?.BeginInvoke(this, computeResult, null, null);
        }

        /// <summary>
        /// 数据库迁移软件在使用（目前在用）
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public virtual Case Reply_HandleSyncDataCompute(SyncDataComputeTask task)
        {
            // 创建同步数据计算结果，设置执行时间
            var result = new Case();
            // 为空则返回
            if (task == null || task.SyncData == null)
            {
                //反馈结果
                return null;
            }

            // 虹膜图像集合
            var images = task.SyncData.IrisImages;
            // 为空则返回
            if (images == null)
            {
                //反馈结果
                return null;
            }

            #region 信号处理
            int frontIndex = 0;
            int backIndex = 0;
            //Ascan处理
            var signalPositionInfo = new List<SignalPositionInfo>();
            int cornealCurvatureCycleNum = 0;
            // 计算信号处理
            //float[] processedSignal = new float[task.SyncData.DaqData.Channel2.Length];
            double p1 = -0.0550531645935891;//眼轴长转换公式
            double p2 = 1.4272541328905;
            float ALLength = 0;

            switch (task.MeasureMode)
            {
                case MeasureMode.AK:
                    int[] positions = new int[2];
                    double[] values = new double[2];
                    // 计算最大值和位置
                    //int[] IsCornTwoSignal = new int[1];
                    //double[] threshold = new double[2] { 300000, 30 };
                    if (task.IsSimulanteye)
                    {
                        _ascanCompute.Find_TwoCrest1(task.SyncData.ProcessedSignal, positions, values, task.SyncData.ProcessedSignal.Length, 10000);
                        if (values.Min() < _algorithmsConfigs.CorrectFileConfigs[0].SignalThreshold && values[1] > values[0])//测角膜曲率防止只有一个信号的情况
                        {
                            positions[0] = positions[1];
                        }
                    }
                    else
                    {
                        _ascanCompute.Find_TwoCrest1(task.SyncData.ProcessedSignal, positions, values, task.SyncData.ProcessedSignal.Length, task.SyncData.ProcessedSignal.Length / 3);

                    }

                    frontIndex = positions[0];
                    backIndex = positions[1];

                    //计算实际眼轴长实际长度
                    _referenceRulerCompute.Compute(task.SyncData.DaqData.Channel1);
                    _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex, frontIndex);
                    cornealCurvatureCycleNum = _referenceRulerCompute.GetCycleNum();//角膜标尺光位置索引
                    _referenceRulerCompute.GetDistance(frontIndex, backIndex);
                    var axisCycleNum = _referenceRulerCompute.GetCycleNum() + cornealCurvatureCycleNum;//轴长
                    #region 眼轴长转换公式
                    ALLength = CalculateActualAxialThickness(cornealCurvatureCycleNum, axisCycleNum, GetTotalNumsOrNull(0), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                    if (!task.IsSimulanteye) ALLength = (float)(ALLength - (p1 * ALLength + p2));
                    #endregion
                    result.AxialLength = ALLength;
                    break;
                case MeasureMode.AScan:
                    // 计算最大值和位置
                    positions = new int[5];
                    values = new double[5];
                    double[] ThresholdSet = new double[5] { 50000, 6000, 10000, 6000, _algorithmsConfigs.CorrectFileConfigs[0].SignalThreshold };
                    if (task.IsSimulanteye)
                    {
                        ThresholdSet = new double[5] { 5000, 6000, 10000, 6000, _algorithmsConfigs.CorrectFileConfigs[0].SignalThreshold };
                        _ascanCompute.Find_SimulationFiveCrest(task.SyncData.ProcessedSignal, positions, values, task.SyncData.ProcessedSignal.Length, ThresholdSet);
                    }
                    else
                    {
                        _ascanCompute.Find_FivePeaks250803(task.SyncData.ProcessedSignal, positions, values, task.SyncData.ProcessedSignal.Length, _algorithmsConfigs.CorrectFileConfigs[0].SignalThreshold);
                    }
                    frontIndex = positions[0];


                    _referenceRulerCompute.Compute(task.SyncData.DaqData.Channel1);



                    float[] TempResult = new float[5];
                    for (int i = 0; i < 5; i++)
                    {
                        if (i < 4)
                        {
                            _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex, positions[i]);
                            int Temp1CycleNum = _referenceRulerCompute.GetCycleNum();
                            _referenceRulerCompute.GetDistance(positions[i], positions[i + 1]);
                            var Temp2CycleNum = _referenceRulerCompute.GetCycleNum() + Temp1CycleNum;//计算CCT.AD.LT VT
                            TempResult[i] = CalculateActualAxialThickness(Temp1CycleNum, Temp2CycleNum, GetTotalNumsOrNull(0), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                        }
                        else
                        {
                            _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex, positions[0]);
                            cornealCurvatureCycleNum = _referenceRulerCompute.GetCycleNum();//角膜标尺光位置索引
                            _referenceRulerCompute.GetDistance(positions[0], positions[4]);
                            var Temp2CycleNum = _referenceRulerCompute.GetCycleNum() + cornealCurvatureCycleNum;//计算眼轴长
                            TempResult[i] = CalculateActualAxialThickness(cornealCurvatureCycleNum, Temp2CycleNum, GetTotalNumsOrNull(0), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                        }

                    }
                    ALLength = TempResult[4];
                    if (!task.IsSimulanteye) ALLength = (float)(ALLength - (p1 * ALLength + p2));//眼轴长转换
                    result.CentralCornealThickness = TempResult[0] * 1000;//转化为um
                    result.AnteriorchamberDepth = TempResult[1];
                    float k = 0.01875f;
                    float b = 0.127f;

                    if (!task.IsSimulanteye)
                    {
                        result.LensThickness = TempResult[2] - (TempResult[2] * k + b);
                    }
                    else
                    {
                        result.LensThickness = TempResult[2];
                    }
                    result.VitreousThickness = TempResult[3];
                    result.AxialLength = ALLength;
                    break;
                case MeasureMode.KER:
                    break;
                case MeasureMode.ZTA:
                    break;
                case MeasureMode.REF:
                    break;
                case MeasureMode.AL:
                    break;
                default:
                    break;
            }

            #endregion

            #region 图像处理 
            float[] outdata = new float[6];
            var innerCornealCurvatureParse = new double[5];
            var outerCornealCurvatureParse = new double[5];
            var innerCircleParse = new float[3];

            var inputAxisPixelArray = new double[4];
            var outputKArray = new float[4];
            int fitArrayIndex = (int)((frontIndex - (_algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex + (_algorithmsConfigs.CorrectFileConfigs[0].FlagNum - _algorithmsConfigs.fitArray3DLength / 2) * 128)) / 128);

            //图像处理
            float[] image = null;
            //int num = 0;
            foreach (var item in images)//得到一帧可以处理的图像
            {
                if (item == null) continue;
                if (_cornealCurvatureCompute.Compute(item, innerCornealCurvatureParse, outerCornealCurvatureParse, innerCircleParse, out int single, out int lightNum, out int neiArea))//CornealCurvature
                {
                    image = item;
                    break;
                }

            }


            inputAxisPixelArray[0] = innerCornealCurvatureParse[3];
            inputAxisPixelArray[1] = innerCornealCurvatureParse[2];
            inputAxisPixelArray[2] = outerCornealCurvatureParse[3];
            inputAxisPixelArray[3] = outerCornealCurvatureParse[2];


            //判断是否凹凸
            //...
            float minR = 4.0F;
            float maxR = 12.5F;
            if (_algorithmsConfigs.fitArray2DLength == 39)
            {
                minR = 6.0F;
                maxR = 9.8F;
            }
            switch (task.CorrModel)
            {
                case CorrModelType.凹模拟眼:
                    fitArrayIndex = ((frontIndex - (_algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex + (_algorithmsConfigs.CorrectFileConfigs[0].ConcaveFlagNum - _algorithmsConfigs.fitArray3DLength / 2) * 128)) / 128);
                    _cornealCurvatureFormulaCompute[0].Compute(inputAxisPixelArray, outputKArray, fitArrayIndex, cornealCurvatureCycleNum, maxR, minR, true, 1.45F);//CornealCurvatureFormula
                    break;
                case CorrModelType.凸模拟眼:
                    _cornealCurvatureFormulaCompute[0].Compute(inputAxisPixelArray, outputKArray, fitArrayIndex, cornealCurvatureCycleNum, maxR, minR, false, 1.45F);//CornealCurvatureFormula
                    break;
                default:
                    break;
            }

            //补算PD/WTW：outdata此前从未被填充，迁移病例的瞳孔直径与白到白恒为0；无有效图像或计算失败时置ErrorValue而非0
            //本Reply调用链全程无catch，历史图像异常若穿透会永久杀死算法Actor且调用方Send永久阻塞（影响数据库迁移工具），
            //比照HandleRealTimeTrackCompute的先例包try/catch：异常时记日志、PD/WTW置ErrorValue，保证Reply正常返回
            float TempPD = EyeParameters.ErrorValue;
            float TempWTW = EyeParameters.ErrorValue;
            try
            {
                if (image != null && _innerAndOuterDiameterCompute != null)
                {
                    _innerAndOuterDiameterCompute.Compute(image, outdata, -1, 1);//PD and WTW
                }
                TempPD = ClinicalValueHelper.ConvertPixelDiameterOrError(outdata[2], _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].pixel, _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].magnificiation);//转换为实际测量值 1.077f
                TempWTW = ClinicalValueHelper.ConvertPixelDiameterOrError(outdata[5], _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].pixel, _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].magnificiation);
            }
            catch (Exception ex)
            {
                LogHelper.Error("历史数据PD/WTW补算异常，PD/WTW置为无效值", ex);
            }

            float InnerRadiusShortAxis = outputKArray[0];//长短轴找中值
            float InnerRadiusLongAxis = outputKArray[1];
            float OuterRadiusShortAxis = outputKArray[2];
            float OuterRadiuslongAxis = outputKArray[3];
            float longAxisCornealCurvature = (InnerRadiusLongAxis + OuterRadiuslongAxis) / 2;//K1
            float shortAxisCornealCurvature = (InnerRadiusShortAxis + OuterRadiusShortAxis) / 2;//K2
            float cornealCurvatureMeanValue = (longAxisCornealCurvature + shortAxisCornealCurvature) / 2;//AVG k

            //轴位角
            int LongAxis_Axial_angle_mean = (int)innerCornealCurvatureParse[4];
            result.PupillaryDiameter = TempPD;
            result.WhiteToWhite = TempWTW;
            if (Math.Abs(longAxisCornealCurvature - shortAxisCornealCurvature) <= 0.03)
            {
                longAxisCornealCurvature = cornealCurvatureMeanValue;
                shortAxisCornealCurvature = cornealCurvatureMeanValue;
                LongAxis_Axial_angle_mean = 0;
            }
            result.LongAxisCornealCurvature = longAxisCornealCurvature;//K2
            result.ShortAxisCornealCurvature = shortAxisCornealCurvature;//K1
            result.CornealCurvatureMeanValue = cornealCurvatureMeanValue;//AVG k
            result.LongAxis_Axial_angle_mean = LongAxis_Axial_angle_mean;//轴位角
            #endregion

            //反馈结果
            return result;

        }


        /// <summary>
        /// 实时信号对焦（目前在用）
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public virtual void HandleSignalProcessCompute(DAQData data, Model.MeasureMode measureMode, Model.DeviceConfigPara deviceConfigPara)
        {
            DiopterSimulationModel diopterSimulationModel = deviceConfigPara.DiopterSimulationModel;
            bool isSimulateEye = deviceConfigPara.IsSimulanteye;
            // 处理信号计算未初始化则返回
            if (_ascanCompute == null) return;
            // 无数据返回
            if (data.Channel2 == null || data.Channel1 == null) return;

            int signal_num = (int)Math.Floor((float)data.Channel2.Length / _signalLength);//不足一帧的直接去掉，所以这里用FLOOR
            int signal_head_ptr = 0;
            bool reuseWholeFrameRuler = signal_num == 1 && data.Channel1.Length == _signalLength;
            List<SignalProcessComputeResult> signalProcessComputeResults = new List<SignalProcessComputeResult>(signal_num);
            for (int num = 0; num < signal_num; ++num)
            {
                //如果算法提供指针偏移量参数的话，那么就不用开启额外的空间，直接采用指针偏移量拿到对应的内存空间
                var signal = new short[_signalLength];
                Array.Copy(data.Channel2, signal_head_ptr, signal, 0, _signalLength);
                signal[0] = signal[1];
                short[] ruler;
                if (reuseWholeFrameRuler)
                {
                    ruler = data.Channel1;
                }
                else
                {
                    ruler = new short[_signalLength];
                    Array.Copy(data.Channel1, signal_head_ptr, ruler, 0, _signalLength);
                }
                float[] processedSignal = new float[_signalLength];
                //var startTime = DateTime.Now;
                //_ascanCompute.Execute(signal, processedSignal);
                //锁相资源未就绪（bin缺失/Init失败）时不调native：processedSignal保持全零，后续找峰失败走无效结果路径，不产出假数据
                if (_isLockInAmplifierReady)
                {
                    _lockInAmplifier?.Calculate(inputSignal: data.Channel2, outputSignal: processedSignal, signalLength: processedSignal.Length);
                }

                //var endTime = DateTime.Now;
                //Console.WriteLine($"use {(endTime - startTime).TotalMilliseconds} ms");

                //去掉光程矫正后的信号
                if (_algorithmsConfigs.CorrectFileConfigs[num].BackBaseIndex != 0)
                {
                    if (num == 0)
                    {
                        for (int i = _algorithmsConfigs.CorrectFileConfigs[num].BackBaseIndex; i < processedSignal.Length; ++i) processedSignal[i] = 0;
                        for (int i = 0; i <= _algorithmsConfigs.CorrectFileConfigs[num].FrontBaseIndex; ++i) processedSignal[i] = 0;
                    }
                    else
                    {
                        for (int i = _algorithmsConfigs.CorrectFileConfigs[num].BackBaseIndex - 1572864; i < data.Channel2.Length - 1572864; ++i) processedSignal[i] = 0;
                        for (int i = 0; i <= _algorithmsConfigs.CorrectFileConfigs[num].FrontBaseIndex - 1572864; ++i) processedSignal[i] = 0;
                    }


                }
                // 
                int[] positions = null;
                double[] values = null;
                int frontIndex = 0;
                int backIndex = 0;
                bool isValid = false;
                //
                int offsetY = -1;
                int thresholdValue = (int)_algorithmsConfigs.CorrectFileConfigs[num].SignalThreshold;
                int centerIndex = (_algorithmsConfigs.CorrectFileConfigs[num].FrontBaseIndex + _algorithmsConfigs.CorrectFileConfigs[num].BackBaseIndex) >> 1;
                int offset = _algorithmsConfigs.fitArray3DLength / 2;
                int TargetCenter = _algorithmsConfigs.CorrectFileConfigs[num].FrontBaseIndex + _algorithmsConfigs.CorrectFileConfigs[num].FlagNum * 128;//前峰信号需要移动到的中心位置
                double[] ThresholdSet = new double[7] { 50000, 8000, 15000, 23000, thresholdValue, _algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex, _algorithmsConfigs.CorrectFileConfigs[0].BackBaseIndex };
                if (num == 1)
                {
                    ThresholdSet = new double[7] { 50000, 8000, 15000, 23000, thresholdValue, _algorithmsConfigs.CorrectFileConfigs[num].FrontBaseIndex - 1572864, _algorithmsConfigs.CorrectFileConfigs[num].BackBaseIndex - 1572864 };
                }
                switch (measureMode)
                {
                    case Model.MeasureMode.AK:
                        positions = new int[2] { 0, 0 };
                        values = new double[2] { 0, 0 };
                        // 计算最大值和位置
                        if (isSimulateEye)//是否为模拟眼数据
                        {
                            _ascanCompute.Find_TwoCrest1(processedSignal, positions, values, processedSignal.Length, 10000);//要改成Find_TwoCrest1方法

                            if (diopterSimulationModel == DiopterSimulationModel.凹角膜 || diopterSimulationModel == DiopterSimulationModel.凸角膜)
                            {
                                if (values[1] > values[0])
                                {
                                    values[0] = values[1];
                                    positions[0] = positions[1];
                                }
                            }
                        }
                        else if (deviceConfigPara.IsCricleRun || deviceConfigPara.IsPolarizationCorrect || deviceConfigPara.WhiteSimulateEyeMeasure)
                        {
                            _ascanCompute.Find_TwoCrest1(processedSignal, positions, values, processedSignal.Length, 10000);//要改成Find_TwoCrest1方法

                        }
                        else
                        {
                            var positions1 = new int[5] { 0, 0, 0, 0, 0 };
                            var values1 = new double[5] { 0, 0, 0, 0, 0 };

                            _ascanCompute.Find_FivePeaks250803(processedSignal, positions1, values1, processedSignal.Length, thresholdValue);

                            positions[0] = positions1[0];
                            positions[1] = positions1[4];
                            values[0] = values1[0];
                            values[1] = values1[4];
                        }
                        //if (values[0] == 0)
                        //{
                        //    return;
                        //}
                        break;
                    case Model.MeasureMode.AScan:
                        // 计算最大值和位置
                        positions = new int[5] { 0, 0, 0, 0, 0 };
                        values = new double[5] { 0, 0, 0, 0, 0 };


                        if (isSimulateEye)   //是否为模拟眼数据
                        {
                            ThresholdSet = new double[5] { 5000, 6000, 10000, 5000, thresholdValue };
                            _ascanCompute.Find_SimulationFiveCrest(processedSignal, positions, values, processedSignal.Length, ThresholdSet);
                        }
                        else
                        {
                            try
                            {
                                _ascanCompute.Find_FivePeaks250803(processedSignal, positions, values, processedSignal.Length, _algorithmsConfigs.CorrectFileConfigs[0].SignalThreshold);
                                //_ascanCompute.Find_FiveCrest1(processedSignal, positions, values, processedSignal.Length, ThresholdSet);
                                if (values[0] == 0)
                                {
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                //save signals
                                // 获取保存目录
                                string savePath = ServiceLocator.Current.GetInstance<DeviceConfigPara>().SaveDataPath;

                                // 指定目标文件夹的名称
                                string folderName = "WrongData";
                                // 构建目标文件夹的完整路径
                                string targetFolderPath = Path.Combine(savePath, folderName);
                                // 检查目标文件夹是否存在，如果不存在则创建它
                                if (!Directory.Exists(targetFolderPath))
                                {
                                    Directory.CreateDirectory(targetFolderPath);
                                }
                                //save
                                SaveDateToFileHelper.Save(new List<float[]> { processedSignal }, targetFolderPath, "processedSignal" + Guid.NewGuid().ToString());
                                MessageBox.Show("数据处理出现异常！请及时联系技术人员上报异常数据，便于进一步解决问题。\n " +
                                "给您带来了不便，深感抱歉！\n" +
                                ex.ToString());

                                throw;
                            }
                        }

                        frontIndex = positions[0];
                        backIndex = positions[4];
                        int CCTPostionIndex = 0;//角膜位置
                        int CCTPostionI = 0;
                        bool IsFindCCTPostion = false;//是否找到有效的角膜位置
                        for (int i = 0; i < values.Length - 1; i++)
                        {
                            if (positions[i] > 0 && values[i] > 0)
                            {
                                CCTPostionIndex = positions[i];
                                CCTPostionI = i;
                                IsFindCCTPostion = true;
                                break;
                            }
                        }
                        bool hasCompleteFivePeaks = values.All(value => value > 0);
                        bool hasFrontPeak = positions[0] > 0 && values[0] > 0;
                        bool hasCCTPair = IsFindCCTPostion &&
                                          CCTPostionI + 1 < positions.Length &&
                                          positions[CCTPostionI + 1] > 0 &&
                                          values[CCTPostionI + 1] > 0;
                        int CT = hasCCTPair ? positions[CCTPostionI + 1] - positions[CCTPostionI] : 0;//角膜厚度
                                                                                                      //找峰算法内部已经完成阈值判断，这里只按是否输出有效峰位做流程判断
                        if (IsFindCCTPostion && hasCompleteFivePeaks && hasFrontPeak && frontIndex < centerIndex)
                        {
                            isValid = true;
                            if (frontIndex > TargetCenter - offset * 128 && frontIndex < TargetCenter + offset * 128)
                            {
                                offsetY = 0;
                            }
                            else
                            {
                                offsetY = frontIndex - TargetCenter;//若offsetY>0,则往前移动 反则 往后
                            }
                        }
                        else if (IsFindCCTPostion)//没有五个有效信息但信号中存在角膜信号
                        {
                            isValid = true;
                            if (hasCCTPair && CT < offset * 2 * 128)//准确有角膜厚度信号
                            {
                                offsetY = positions[CCTPostionI] - TargetCenter;
                            }
                            else//没有角膜厚度信号，角膜厚度只有单峰情况
                            {
                                offsetY = (positions[CCTPostionI] - TargetCenter) / 2;
                            }

                        }

                        break;
                    default:
                        break;
                }

                var signalPositionInfos = new List<SignalPositionInfo>(positions.Length);
                for (int i = 0; i < positions.Length; i++)
                {
                    signalPositionInfos.Add(new SignalPositionInfo(positions[i], values[i]));
                }
                signalProcessComputeResults.Add(new SignalProcessComputeResult()
                {
                    DAQData = new DAQData
                    {
                        Channel2 = signal,
                        Channel1 = ruler,
                        DataFlag = data.DataFlag,
                    },
                    IsValid = isValid,
                    OffsetY = offsetY,
                    ProcessedSignal = processedSignal,
                    SignalPositionInfos = signalPositionInfos,
                    Index = (byte)num,
                });
                signal_head_ptr += _signalLength;
            }
            ShowSignalProcessComputeResultEventHandler?.BeginMulticastInvoke(this, signalProcessComputeResults);
        }

        /// <summary>
        /// 计算信号阈值（目前在用）
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public virtual float Reply_HandleSignalThresholdValueCompute(float[] data)
        {
            // 处理信号计算未初始化则返回
            if (_ascanCompute == null) return 0;
            // 无数据返回
            if (data == null) return 0;

            //去掉光程矫正后的信号
            if (_algorithmsConfigs.CorrectFileConfigs[0].BackBaseIndex != 0)
            {
                for (int i = _algorithmsConfigs.CorrectFileConfigs[0].BackBaseIndex; i < data.Length; ++i) data[i] = 0;
                for (int i = 0; i <= _algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex; ++i) data[i] = 0;
            }
            int percentage = (int)ServiceLocator.Current.GetInstance<DeviceConfigPara>().SignalThresholdPercentage;
            float thresholdValue = 0;
            try
            {
                thresholdValue = _ascanCompute.Custom_prctile2threshold(data, data.Length, percentage, _algorithmsConfigs.CorrectFileConfigs[0].FrontBaseIndex, _algorithmsConfigs.CorrectFileConfigs[0].BackBaseIndex);
            }
            catch (Exception ex)
            {
                //save signals
                // 获取保存目录
                string savePath = ServiceLocator.Current.GetInstance<DeviceConfigPara>().SaveDataPath;

                // 指定目标文件夹的名称
                string folderName = "WrongData";
                // 构建目标文件夹的完整路径
                string targetFolderPath = Path.Combine(savePath, folderName);
                // 检查目标文件夹是否存在，如果不存在则创建它
                if (!Directory.Exists(targetFolderPath))
                {
                    Directory.CreateDirectory(targetFolderPath);
                }
                //save
                SaveDateToFileHelper.Save(new List<float[]> { data }, targetFolderPath, "processedSignal" + Guid.NewGuid().ToString());
                MessageBox.Show("数据处理出现异常！请及时联系技术人员上报异常数据，便于进一步解决问题。\n " +
                "给您带来了不便，深感抱歉！\n" +
                ex.ToString());
                throw;
            }
            return thresholdValue;
        }

        /// <summary>
        /// 计算蓝灯和白灯LED光斑数量（弃用）
        /// </summary>
        /// <param name="irisImage"></param>
        public virtual void HandleLEDFaculaNumCompute(float[] irisImage)
        {
            if (irisImage == null) return;
            bool result = _LEDFaculaNumCompute.Execute(irisImage, out int num);
            if (!result) num = -1;
            ShowLEDFaculaNumComputeResultEventHandler?.BeginMulticastInvoke(this, num);
        }

        /// <summary>
        /// 光程累加计算厚度
        /// </summary>
        /// <param name="Channel1"></param>
        /// <param name="Signals"></param>
        public virtual void HandleCalculateThickness(short[] Channel1, List<SignalPositionInfo> Signals, byte index)
        {
            if (Channel1 == null || Signals.Count == 0)
            {
                return;
            }
            double p1 = -0.0550531645935891;//眼轴长转换公式
            double p2 = 1.4272541328905;
            float ALLength = 0;
            Case Case = new Case();
            if (Signals.Count == 8)
            {
                _referenceRulerCompute.Compute(Channel1);
                var TempResult = new List<float>();

                for (int i = 0; i < Signals.Count; i = i + 2)
                {
                    _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[index].FrontBaseIndex, Signals[i].Position);
                    int Temp1CycleNum = _referenceRulerCompute.GetCycleNum();
                    _referenceRulerCompute.GetDistance(Signals[i].Position, Signals[i + 1].Position);
                    var Temp2CycleNum = _referenceRulerCompute.GetCycleNum() + Temp1CycleNum;
                    TempResult.Add(CalculateActualAxialThickness(Temp1CycleNum, Temp2CycleNum, GetTotalNumsOrNull(index), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime));
                }
                float k = 0.01875f;
                float b = 0.127f;
                ALLength = TempResult[0];
                ALLength = (float)(ALLength - (p1 * ALLength + p2)); ;//眼轴长转换
                Case.CentralCornealThickness = TempResult[1] * 1000;
                Case.AnteriorchamberDepth = TempResult[2];
                Case.LensThickness = TempResult[3] - (TempResult[3] * k + b);
                Case.VitreousThickness = ALLength - TempResult[1] - TempResult[2] - TempResult[3];
                Case.AxialLength = ALLength;
            }
            else
            {
                //计算实际眼轴长实际长度
                _referenceRulerCompute.Compute(Channel1);
                _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[index].FrontBaseIndex, Signals[0].Position);
                int cornealCurvatureCycleNum = _referenceRulerCompute.GetCycleNum();//角膜标尺光位置索引
                _referenceRulerCompute.GetDistance(Signals[0].Position, Signals[1].Position);
                var axisCycleNum = _referenceRulerCompute.GetCycleNum() + cornealCurvatureCycleNum;//轴长
                #region 眼轴长转换公式
                ALLength = CalculateActualAxialThickness(cornealCurvatureCycleNum, axisCycleNum, GetTotalNumsOrNull(index), _algorithmsConfigs.CorrectFileConfigs[0].SamplingTime);
                ALLength = (float)(ALLength - (p1 * ALLength + p2));
                #endregion
                Case.AxialLength = ALLength;
                Case.CentralCornealThickness = EyeParameters.ErrorValue;
                Case.AnteriorchamberDepth = EyeParameters.ErrorValue;
                Case.LensThickness = EyeParameters.ErrorValue;
                Case.VitreousThickness = EyeParameters.ErrorValue;
            }
            ShowCalculateThicknessResultEventHandler.BeginMulticastInvoke(this, Case);
        }

        /// <summary>
        /// 计算焦点距离角膜的距离（分析界面在用）
        /// </summary>
        /// <param name="Channel1"></param>
        /// <param name="corneaPosition"></param>
        /// <returns></returns>
        public float HandleGetCorneaToFocusDistance(short[] Channel1, int corneaPosition, byte index)
        {
            _referenceRulerCompute.Compute(Channel1);
            _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[index].FrontBaseIndex, corneaPosition);
            int cornealCurvatureCycleNum1 = _referenceRulerCompute.GetCycleNum();
            _referenceRulerCompute.GetDistance(_algorithmsConfigs.CorrectFileConfigs[index].FrontBaseIndex, _algorithmsConfigs.CorrectFileConfigs[index].PolarizationCorrectionIndex * 128);
            int cornealCurvatureCycleNum2 = _referenceRulerCompute.GetCycleNum();
            return CalculateActualAxialThickness(cornealCurvatureCycleNum1, cornealCurvatureCycleNum2, GetTotalNumsOrNull(index), 1.452F);
        }

        /// <summary>
        /// 计算白到白和瞳孔大小计算（目前只用于模拟眼）
        /// </summary>
        /// <param name="irisImage"></param>
        /// <param name="isMockEye"></param>
        public virtual void HandleCalculatePDAndWTW(float[] irisImage, bool isMockEye)
        {
            if (isMockEye)
            {
                float radius = 0;
                int[] diffRadiusRange = new int[4] { 50, 200, 300, 1000 };
                int Result = _innerAndOuterDiameterCompute.CalculateMockEye(irisImage, 180, ref radius, diffRadiusRange);
                float MeanWTW = 0;
                float MeanPD = 0;
                switch (Result)
                {
                    case -1:
                        MeanWTW = 0f;
                        MeanPD = 0f;
                        break;

                    case 0:
                        MeanWTW = 1.7f;
                        MeanPD = 1.7f;

                        break;
                    case 1:
                        MeanWTW = 6.3f;
                        MeanPD = 6.3f;
                        break;
                    case 2:
                        MeanWTW = 13.7f;
                        MeanPD = 13.7f;

                        break;
                    case 3:
                        MeanWTW = 16.8f;
                        MeanPD = 16.8f;

                        break;
                    default:
                        break;
                }
                ShowInnerAndOuterDiameterComputeResultEventHandler.BeginMulticastInvoke(this, new InnerAndOuterDiameterComputeResult()
                {
                    WTWDiameter = MeanWTW,
                    PDDiameter = MeanPD
                });
            }
            else
            {
                float[] outdata = new float[6];
                _innerAndOuterDiameterCompute.Compute(irisImage, outdata, -1, 1);//PD and WTW

                ShowInnerAndOuterDiameterComputeResultEventHandler.BeginMulticastInvoke(this, new InnerAndOuterDiameterComputeResult()
                {
                    WTWDiameter = (float)((outdata[5] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].magnificiation) * 1.095f,
                    PDDiameter = (float)((outdata[2] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].magnificiation) * 1.095f,
                    PDAndWTWComputeResult = outdata,
                    IrisImageForSHow = irisImage
                });
            }
        }
        /// <summary>
        /// 单独计算蓝灯闪烁的WTW和PS（目前在用）
        /// </summary>
        /// <param name="irisImages"></param>
        /// <param name="isMockEye"></param>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public virtual void HandleCalculatePDAndWTW(List<float[]> irisImages, bool isMockEye)
        {
            var results = new List<InnerAndOuterDiameterComputeResult>();
            int i = 0;
            try
            {
                int count = irisImages.Count;//异步线程只会对集合进行添加，所以先记录集合大小，再执行。
                for (i = 0; i < count; ++i)
                {
                    float[] outdata = new float[6];
                    _innerAndOuterDiameterCompute.Compute(irisImages[i], outdata, -1, 1);//PD and WTW
                    if (outdata[2] != 0)//不为0则添加
                    {
                        var result = new InnerAndOuterDiameterComputeResult()
                        {
                            WTWDiameter = (float)((outdata[5] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].magnificiation) * 1.095f,
                            PDDiameter = (float)((outdata[2] * _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].pixel) / _algorithmsConfigs.CornealCurvatureFormulaComputeConfig[0].magnificiation) * 1.095f,
                            PDAndWTWComputeResult = outdata,
                            IrisImageForSHow = irisImages[i]
                        };
                        results.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                // 获取保存目录
                string savePath = ServiceLocator.Current.GetInstance<DeviceConfigPara>().SaveDataPath;
                // 指定目标文件夹的名称
                string folderName = "WrongData";
                // 构建目标文件夹的完整路径
                string targetFolderPath = Path.Combine(savePath, folderName);
                // 检查目标文件夹是否存在，如果不存在则创建它
                if (!Directory.Exists(targetFolderPath))
                {
                    Directory.CreateDirectory(targetFolderPath);
                }
                //save
                if (irisImages.Count > 0 && i < irisImages.Count) SaveDateToFileHelper.Save(new List<float[]> { irisImages[i] }, targetFolderPath, "IrisImage" + Guid.NewGuid().ToString());
                MessageBox.Show("数据处理出现异常！请及时联系技术人员上报异常数据，便于进一步解决问题。\n " +
                "给您带来了不便，深感抱歉！\n" +
                ex.ToString());
                throw;
            }
            Flush();
            MessageDispatcher.Send(new PSAndWTWCalculateResultDataPostProcessActor(results));
        }

        //已记录过“标尺光矫正数组缺失”日志的面索引，避免每次测量重复刷日志
        private readonly HashSet<int> _missingTotalNumsLoggedIndexes = new HashSet<int>();

        /// <summary>
        /// 安全获取标尺光矫正数组：对应txt文件缺失/读取失败时该下标为null占位（或下标越界），返回null，
        /// 由CalculateActualAxialThickness按无效结果(0)处理，避免测量期索引越界异常杀死算法Actor
        /// </summary>
        /// <param name="index">面索引，对应CorrectFileConfigs下标</param>
        private double[] GetTotalNumsOrNull(int index)
        {
            var totalNumsArray = _algorithmsConfigs.TotalNumsArray;
            double[] result = (index >= 0 && index < totalNumsArray.Count) ? totalNumsArray[index] : null;
            if (result == null || result.Length == 0)
            {
                if (_missingTotalNumsLoggedIndexes.Add(index))
                {
                    LogHelper.Error("标尺光矫正数组缺失（面索引" + index + "），请检查对应标尺光周期txt文件，该面测量结果按无效处理");
                }
                return null;
            }
            return result;
        }

        /// <summary>
        /// 计算实际长度
        /// </summary>
        /// <param name="position1"></param>
        /// <param name="position2"></param>
        /// <param name="totalNumsArray"></param>
        /// <param name="samplingTime"></param>
        /// <param name="result"></param>
        private float CalculateActualAxialThickness(int position1, int position2, double[] totalNumsArray, float samplingTime)
        {
            //标尺光矫正数组缺失（文件缺失/读取失败的null占位）时按无效结果返回0，与下方越界返回0的既有约定一致
            if (totalNumsArray == null || totalNumsArray.Length == 0)
            {
                return 0;
            }
            if (position1 < 0 || position2 < 0)
            {
                return 0;
            }


            int front = position1;
            int back = position2;
            if (position1 > position2)
            {
                front = position2;
                back = position1;
            }
            double thickness = 0;
            for (int k = front; k < back; k++)
            {
                if (k < totalNumsArray.Length)
                {
                    thickness += totalNumsArray[k];
                }
                else
                {
                    return 0;
                }
            }

            thickness *= samplingTime;  //补偿系数
            return (float)thickness;
        }


        internal ValueTuple<float, float, float[], float[]> IOLPreoperativeCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.PreoperativeCompute_A(input.AL, input.Kpo, input.Const, input.PACD, input.A0, input.A1, input.A2, input.ACD, input.LT, input.DR, input.Chamber, input.Mode, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.PreoperativeCompute_A(input.AL, input.Kpo, input.Const, input.PACD, input.A0, input.A1, input.A2, input.ACD, input.LT, input.DR, input.Chamber, input.Mode, iol, out dem, out dam, out refr);
                resIOL[i] = iol;
                resRefr[i] = refr;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }

        internal ValueTuple<float, float, float[], float[]> HISTORYderivedCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            var _input = input as HistoryDerivedInputEntity;
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.HISTORYderivedComputer_A(_input.AL, _input.ACD, _input.Const, input.PACD, input.A0, input.A1, input.A2, _input.LT, _input.DR, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.Kpre, _input.Chamber, input.Mode, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.HISTORYderivedComputer_A(_input.AL, _input.ACD, _input.Const, input.PACD, input.A0, input.A1, input.A2, _input.LT, _input.DR, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.Kpre, _input.Chamber, input.Mode, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }

        internal ValueTuple<float, float, float[], float[]> RefractionDerivedCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            var _input = input as RefractionDerivedInputEntity;
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.RefractionDerivedComputer_A(_input.AL, _input.ACD, _input.Const, input.PACD, input.A0, input.A1, input.A2, _input.LT, _input.DR, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.Kpo, _input.Chamber, input.Mode, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.RefractionDerivedComputer_A(_input.AL, _input.ACD, _input.Const, input.PACD, input.A0, input.A1, input.A2, _input.LT, _input.DR, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.Kpo, _input.Chamber, input.Mode, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }

        internal ValueTuple<float, float, float[], float[]> Double_K_SRK_TCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            var _input = input as DoubleKSRKTInputEntity;
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.Double_K_SRK_TComputer(_input.AL, _input.Kpre, _input.Kpo, _input.Const, _input.DR, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.Double_K_SRK_TComputer(_input.AL, _input.Kpre, _input.Kpo, _input.Const, _input.DR, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }

        internal ValueTuple<float, float, float[], float[]> ShammasCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.ShammasComputer(input.AL, input.Kpo, input.Const, input.DR, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.ShammasComputer(input.AL, input.Kpo, input.Const, input.DR, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }

        internal ValueTuple<float, float, float[], float[]> ROSACompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.ROSAComputer(input.AL, input.Kpo, input.Const, input.DR, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.ROSAComputer(input.AL, input.Kpo, input.Const, input.DR, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }

        internal ValueTuple<float, float, float[], float[]> Haigis_LCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.Haigis_LComputer(input.AL, input.Kpo, input.ACD, input.A0, input.A1, input.A2, input.DR, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.Haigis_LComputer(input.AL, input.Kpo, input.ACD, input.A0, input.A1, input.A2, input.DR, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }


        internal ValueTuple<float, float, float[], float[]> MasketCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            var _input = input as MasketInputEntity;
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.MasketComputer(_input.AL, _input.Kpo, _input.Const, _input.PACD, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.DR, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.MasketComputer(_input.AL, _input.Kpo, _input.Const, _input.PACD, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.DR, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }

        internal ValueTuple<float, float, float[], float[]> ModifyMasketCompute(IOLBasiceInputEntity input)
        {
            EnsureIOLComputeCreated();
            var _input = input as MasketInputEntity;
            float iol = 0f;
            float resDem = 0f;
            float resDam = 0f;
            float[] resIOL = new float[5];
            float[] resRefr = new float[5];
            float refr;
            float dam;
            float dem;
            _iolCompute.Modify_MasketComputer(_input.AL, _input.Kpo, _input.Const, _input.PACD, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.DR, iol, out dem, out dam, out refr);
            resDam = dam;
            resDem = dem;
            iol = RoundToNearestHalf(dam) - 1;
            for (int i = 0; i < 5; i++)
            {
                _iolCompute.Modify_MasketComputer(_input.AL, _input.Kpo, _input.Const, _input.PACD, _input.DSPr, _input.DCPr, _input.DSPo, _input.DCPo, _input.DR, iol, out dem, out dam, out refr);
                resRefr[i] = refr;
                resIOL[i] = iol;
                iol += 0.5f;
            }
            return (resDem, resDam, resIOL, resRefr);
        }



        private float RoundToNearestHalf(float value)
        {
            return (float)(Math.Round(value * 2) / 2.0);
        }

        private void EnsureIOLComputeCreated()
        {
            if (_iolCompute == null)
            {
                _iolCompute = new IOLCompute();
                _iolCompute.Init();
            }
        }
        #endregion
    }
}

