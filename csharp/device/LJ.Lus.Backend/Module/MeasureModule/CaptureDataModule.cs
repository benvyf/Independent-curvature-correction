using CommonServiceLocator;
using LJ.Lus.Backend.Acquire.Actors.Camera;
using LJ.Lus.Backend.Acquire.Actors.MCU;
using LJ.Lus.Backend.Basic;
using LJ.Lus.Backend.Compute.Actors.Algorithm;
using LJ.Lus.Backend.Compute.Actors.DataPostProcess;
using LJ.Lus.Backend.Model;
using LJ.Lus.Backend.Module.Model;
using LJ.Lus.Backend.Module.ModuleManager;
using LJ.Lus.Backend.Systems;
using LJ.Lus.Backend.Tooling.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LJ.Lus.Backend.Module.MeasureModule
{
    public class CaptureDataModule : ModuleAdvance<ResultShowModel>, IModule
    {
        #region Fields
        //外部基础+高级设置字段
        protected bool _isCompensateAL;
        protected bool _isBlueLEDMeasuring;
        protected bool _isBlueLEDMeasure;
        protected bool _isPolarizationRatate;
        protected int _offsetX = 0;
        protected int _offsetZ = 0;
        protected float _cctFrontThreshold = 0;
        protected float _cctBackThreshold = 0;
        //模块自身字段
        protected float[] _irisGrayImage = null;
        protected int _eyeMultiParameterNum = 8;//采集数量
        protected EyeType _eyeType = EyeType.无;
        protected Backend.Model.MeasureMode _measureMode;//测量模式
        protected List<EyeMultiParameterComputeTask> _rawEyePackets;//原始眼睛数据包
        protected List<CornealCurvatureImageTask> _cornealCurvatureImageTasks;//角膜曲率图像候选集
        protected List<CornealCurvatureImageTask> _abnormalCaptureStageImageTasks;//采集阶段distance异常图像
        protected List<float[]> _irisImagesForPSAndWTW;//用于存放闪烁蓝灯时的所计算的PS和WTW
        protected readonly object _cornealCurvatureImageTaskLocker = new object();
        protected readonly object _abnormalCaptureStageImageTaskLocker = new object();
        protected System.Threading.Timer _timer;//模块运行时间计时器
        protected int _runTimeLimit;//模块运行时间限制
        protected const float AcquireDistanceLimit = 0.4f;
        protected const int MaxCornealCurvatureCandidateCount = 50;
        protected const int MaxAbnormalCaptureImageCount = 20;
        protected volatile bool _isNeedNewSignal = false;
        protected Guid _caseID;
        protected string _saveDataPath;

        // 对焦补偿器
        protected readonly FocusCompensator _focusCompensator = new FocusCompensator();
        #endregion

        #region Singleton
        //private static readonly Lazy<CaptureDataModule> lazy = new Lazy<CaptureDataModule>(() => new CaptureDataModule());

        //public static CaptureDataModule Instance { get { return lazy.Value; } }
        #endregion

        #region Properties
        /// <summary>
        /// 采集有效数据进度通知
        /// </summary>
        public virtual EventHandler<int> ShowAcquireSignalProgress { get; set; }
        /// <summary>
        /// 保存完数据通知
        /// </summary>
        public virtual EventHandler FinishSaveDataEventHandler { get; set; }

        public bool IsFinishWork => _isFinish;
        #endregion

        #region Methods

        public void Quit()
        {
            //MemoryClear(); 中断模块时，马上清除资源会有异步冲突
            _timer?.Change(-1, -1);
            if (_isPolarizationRatate) ModuleManagerBase.PolarizationLocationSearchStrategy.MotorRatateStop(); ;
            MeasurementFlowLogger.Log(_caseID, _saveDataPath, $"[CaptureData] Quit. packetCount={_rawEyePackets.Count}, interruptStatus={InterruptStatus}");
            SignalFinish(false);
        }

        public override void SetMeasureTask(LusMeasureTask task)
        {
            _caseID = task.CaseID;
            _isCompensateAL = task.ExternalDeviceSettings.IsCompensateAL;
            _runTimeLimit = task.ExternalDeviceSettings.ModuleRunTimeLimit;
            _measureMode = task.ExternalDeviceSettings.MeasureMode;
            if (_measureMode == MeasureMode.AK)
            {
                _eyeMultiParameterNum = 5;
            }
            else
            {
                _eyeMultiParameterNum = 8;
            }
            MessageDispatcher.Send(new SetPatientInfoDataPostProcessActor(task.CaseID, task.PatientAge, _measureMode));
        }

        public bool Work()
        {
            if (ModuleManagerBase.IsTestEnvironment)
            {
                ParasReset();
                Task.Run(() =>
                {
                    Thread.Sleep(3000);//模拟
                    SignalFinish(true);
                });
            }
            else
            {
                ParasReset();
                ClearMeasurementBuffers();
                ClearAbnormalCaptureStageImageTasks();
                EventBind();
                MeasurementFlowLogger.Log(_caseID, _saveDataPath, $"[CaptureData] Start. measureMode={_measureMode}, packetTarget={_eyeMultiParameterNum}, runTimeLimit={_runTimeLimit}, blueLedMeasure={_isBlueLEDMeasure}, polarizationRotate={_isPolarizationRatate}");
                if (_runTimeLimit != -1)
                {
                    _timer.Change(_runTimeLimit * 1000, -1);
                }
            }
            WaitForFinish();
            EventUnBind();
            //向外反馈当前的采集进度
            ShowAcquireSignalProgress?.BeginInvoke(this, 0, null, null);
            return _isSucceed;
        }


        protected virtual void HandleSignalProcessComputeResultEvent(object sender, List<SignalProcessComputeResult> e)
        {
            if (!_isNeedNewSignal) return;
            if (_irisGrayImage == null) return;
            if (_rawEyePackets.Count >= _eyeMultiParameterNum) return;
            if (MCUActor.Instance.IsMotor3dWorking || _isFinish)
            {
                _isNeedNewSignal = true;
                return;
            }

            if (_focusCompensator.IsFocusStable && _focusCompensator.LastStableDistance != 0f)
            {
                for (int i = 0; i < e.Count; i++)
                {
                    //if (!e[i].IsValid) continue;
                    //AK模式下的限制
                    if (_measureMode == MeasureMode.AK)
                    {
                        //AK信号有2个干涉峰
                        if (e[i].SignalPositionInfos.Count != 2) continue;
                    }
                    //ASCAN模式下的限制
                    if (_measureMode == MeasureMode.AScan)
                    {
                        //ASCAN信号有5个干涉峰
                        if (e[i].SignalPositionInfos.Count != 5)
                        {
                            //ASCAN信号角膜前表面的干涉幅值应满足阈值
                            if (e[i].SignalPositionInfos[0].Value < _cctFrontThreshold || e[i].SignalPositionInfos[1].Value < _cctBackThreshold)
                                continue;
                        }
                    }
                    //构建数据包
                    _rawEyePackets.Add(new EyeMultiParameterComputeTask
                    {
                        CorrModel = CorrModelType.凸模拟眼,
                        IrisImage = (float[])_irisGrayImage.Clone(),
                        IsSimulateEye = false,
                        PolarRotateIndex = ModuleManagerBase.PolarizationLocationSearchStrategy.GetCurrentIndex(),
                        SignalProcessComputeResult = e[i],
                        IsCompensateAL = _isCompensateAL,
                        CornealCurvatureDistance = _focusCompensator.LastStableDistance
                    });
                    //向外传递当前测量次数
                    var counts = (int)(_rawEyePackets.Count / (_eyeMultiParameterNum / 5f));
                    ShowAcquireSignalProgress?.BeginInvoke(this, counts, null, null);
                    MeasurementFlowLogger.Log(_caseID, _saveDataPath, $"[CaptureData] Accepted packet {_rawEyePackets.Count}/{_eyeMultiParameterNum}. progress={counts}/5, peaks={e[i].SignalPositionInfos.Count}, distance={_focusCompensator.LastStableDistance:F4}");
                    Console.WriteLine(counts);
                    //采集达到数量要求
                    if (_rawEyePackets.Count == _eyeMultiParameterNum)
                    {
                        FinishAcquireAndCompute();
                        return;
                    }
                }
            }
            _isNeedNewSignal = true;

                //int xDiff = center[0] + _offsetX - 320;
                //int yDiff = 240 + _offsetZ - center[1];
                //if (!(Math.Abs(distance) <= 1 && xDiff * xDiff + yDiff * yDiff <= 2 * 2))
                //{
                //    //_alignmentYCount++;
                //    //if(_alignmentYCount == _alignmentYMinCount)
                //    //{
                //    //    _alignmentYCount = 0;
                //    //    int xSteps = xDiff * _xMovePixelStep;//这些参数是以前标定的，三维移动步数需要转换
                //    //    int zSteps = yDiff * _zMovePixelStep;
                //    //    int ySteps = -(int)((distance * 6400) / (20f / 16));
                //    //    MCUActor.Instance.IsMotor3dWorking = true;
                //    //    MessageDispatcher.Send(new Motor3dMoveFiniteMCUActor(xSteps, ySteps, zSteps));
                //    //}
                //    _isNeedNewSignal = true;
                //}
                //else
                //{
                //    for (int i = 0; i < e.Count; i++)
                //    {
                //        ////if (!e[i].IsValid) continue;
                //        ////AK模式下的限制
                //        //if (_measureMode == MeasureMode.AK)
                //        //{
                //        //    //AK信号有2个干涉峰
                //        //    if (e[i].SignalPositionInfos.Count != 2) continue;
                //        //}
                //        ////ASCAN模式下的限制
                //        //if (_measureMode == MeasureMode.AScan)
                //        //{
                //        //    //ASCAN信号有5个干涉峰
                //        //    if (e[i].SignalPositionInfos.Count != 5)
                //        //    {
                //        //        //ASCAN信号角膜前表面的干涉幅值应满足阈值
                //        //        if (e[i].SignalPositionInfos[0].Value < _cctFrontThreshold || e[i].SignalPositionInfos[1].Value < _cctBackThreshold)
                //        //            continue;
                //        //    }
                //        //}
                //        //构建数据包
                //        _rawEyePackets.Add(new EyeMultiParameterComputeTask
                //        {
                //            CorrModel = CorrModelType.凸模拟眼,
                //            IrisImage = (float[])_irisGrayImage.Clone(),
                //            IsSimulateEye = false,
                //            PolarRotateIndex = ModuleManagerBase.PolarizationLocationSearchStrategy.GetCurrentIndex(),
                //            SignalProcessComputeResult = e[i],
                //            IsCompensateAL = _isCompensateAL
                //        });
                //        //向外传递当前测量次数
                //        var count = (int)(_rawEyePackets.Count / (_eyeMultiParameterNum / 5f));
                //        ShowAcquireSignalProgress?.BeginInvoke(this, count, null, null);
                //        Console.WriteLine(count);
                //        //采集达到数量要求
                //        if (_rawEyePackets.Count == _eyeMultiParameterNum)
                //        {
                //            //关闭计时
                //            _timer?.Change(-1, -1);
                //            //发送数据包计算
                //            MessageDispatcher.Send(new EyeMultiParameterPacketsComputeTaskAlgorithmActor(_rawEyePackets));
                //            if (_isPolarizationRatate)
                //            {
                //                ModuleManagerBase.PolarizationLocationSearchStrategy.MotorRatateStop();
                //            }
                //            //是否闪烁蓝灯【烤机、信号定量情况下不闪烁】
                //            if (_isBlueLEDMeasure && !ServiceLocator.Current.GetInstance<DeviceConfigPara>().IsCricleRun)
                //            {
                //                MFCameraActor.Instance.UpdateMFByteArrayEventHandler += HandleImageNewFrameEvent;
                //                MessageDispatcher.Send(new LedBrightnessControlMCUActor(25, 25, 100, 0));
                //                Thread.Sleep(300);//0.3S闪烁间隔
                //                MessageDispatcher.Send(new LedBrightnessControlMCUActor(25, 25, 0, 0));
                //                //闪烁后的业务逻辑
                //                //TODO
                //                _isBlueLEDMeasuring = true;
                //                Thread.Sleep(1500);//等待采集1.5s
                //                _isBlueLEDMeasuring = false;
                //                MFCameraActor.Instance.UpdateMFByteArrayEventHandler -= HandleImageNewFrameEvent;
                //                //发送WTWAndPS数据包
                //                MessageDispatcher.Send(new CalculatePSAndWTWPacketsAlgorithmActor(_irisImagesForPSAndWTW, false));
                //            }
                //            //记录眼睛类型
                //            _eyeType = MCUActor.Instance.CurrentMotor3DStatus.MotorXPosition == DotnetDevices.MCU.DataModel.MotorPosition.Negative ? EyeType.右眼 : EyeType.左眼;
                //            //改变模块状态
                //            _isSucceed = true;
                //            _isFinish = true;
                //            _isNeedNewSignal = false;
                //            return;
                //        }
                //    }
                //}
                //int Ysteps = 0;
                ////Y方向判断
                //if(true) //(e[0].OffsetY == 0)
                //{
                //    _alignmentYCount = 0;
                //    for (int i = 0; i < e.Count; i++)
                //    {
                //        //if (!e[i].IsValid) continue;
                //        //AK模式下的限制
                //        if (_measureMode == MeasureMode.AK)
                //        {
                //            //AK信号有2个干涉峰
                //            if (e[i].SignalPositionInfos.Count != 2) continue;
                //        }
                //        //ASCAN模式下的限制
                //        if (_measureMode == MeasureMode.AScan)
                //        {
                //            //ASCAN信号有5个干涉峰
                //            if (e[i].SignalPositionInfos.Count != 5)
                //            {
                //                //ASCAN信号角膜前表面的干涉幅值应满足阈值
                //                if (e[i].SignalPositionInfos[0].Value < _cctFrontThreshold || e[i].SignalPositionInfos[1].Value < _cctBackThreshold)
                //                    continue;
                //            }
                //        }
                //        //构建数据包
                //        _rawEyePackets.Add(new EyeMultiParameterComputeTask
                //        {
                //            CorrModel = CorrModelType.凸模拟眼,
                //            IrisImage = (float[])_irisGrayImage.Clone(),
                //            IsSimulateEye = false,
                //            PolarRotateIndex = ModuleManagerBase.PolarizationLocationSearchStrategy.GetCurrentIndex(),
                //            SignalProcessComputeResult = e[i],
                //            IsCompensateAL = _isCompensateAL
                //        });
                //        //向外传递当前测量次数
                //        var count = (int)(_rawEyePackets.Count / (_eyeMultiParameterNum / 5f));
                //        ShowAcquireSignalProgress?.BeginInvoke(this, count, null, null);
                //        Console.WriteLine(count);
                //        //采集达到数量要求
                //        if (_rawEyePackets.Count == _eyeMultiParameterNum)
                //        {
                //            //关闭计时
                //            _timer?.Change(-1, -1);
                //            //发送数据包计算
                //            MessageDispatcher.Send(new EyeMultiParameterPacketsComputeTaskAlgorithmActor(_rawEyePackets));
                //            if (_isPolarizationRatate)
                //            {
                //                ModuleManagerBase.PolarizationLocationSearchStrategy.MotorRatateStop();
                //            }
                //            //是否闪烁蓝灯【烤机、信号定量情况下不闪烁】
                //            if (_isBlueLEDMeasure && !ServiceLocator.Current.GetInstance<DeviceConfigPara>().IsCricleRun)
                //            {
                //                MFCameraActor.Instance.UpdateMFByteArrayEventHandler += HandleImageNewFrameEvent;
                //                MessageDispatcher.Send(new LedBrightnessControlMCUActor(25, 25, 100, 0));
                //                Thread.Sleep(300);//0.3S闪烁间隔
                //                MessageDispatcher.Send(new LedBrightnessControlMCUActor(25, 25, 0, 0));
                //                //闪烁后的业务逻辑
                //                //TODO
                //                _isBlueLEDMeasuring = true;
                //                Thread.Sleep(1500);//等待采集1.5s
                //                _isBlueLEDMeasuring = false;
                //                MFCameraActor.Instance.UpdateMFByteArrayEventHandler -= HandleImageNewFrameEvent;
                //                //发送WTWAndPS数据包
                //                MessageDispatcher.Send(new CalculatePSAndWTWPacketsAlgorithmActor(_irisImagesForPSAndWTW, false));
                //            }
                //            //记录眼睛类型
                //            _eyeType = MCUActor.Instance.CurrentMotor3DStatus.MotorXPosition == DotnetDevices.MCU.DataModel.MotorPosition.Negative ? EyeType.右眼 : EyeType.左眼;
                //            //改变模块状态
                //            _isSucceed = true;
                //            _isFinish = true;
                //            return;
                //        }
                //    }
                //    Console.WriteLine("--------------------------------------------------");
                //}
                //else
                //{
                //    _alignmentYCount++;
                //    Ysteps = (int)(e[0].OffsetY / _pointsPerYStep);
                //}
                //if (_isPolarizationRatate)//偏振转动，Y轴不动
                //{
                //    AlignmentXYZEvent?.BeginInvoke(this, 0, null, null);
                //}
                //else//偏振不转动，满足次数动Y
                //{
                //    if (_alignmentYCount == _alignmentYMinCount)
                //    {
                //        //XYZ方向实时对准
                //        AlignmentXYZEvent?.BeginInvoke(this, Ysteps, null, null);
                //        _alignmentYCount = 0;
                //    }
                //    else
                //    {
                //        AlignmentXYZEvent?.BeginInvoke(this, 0, null, null);
                //    }
                //}
        }

        protected virtual void FinishAcquireAndCompute()
        {
            _timer?.Change(-1, -1);
            _isNeedNewSignal = false;
            _focusCompensator.DisableCornealImageCapture();

            int cornealCandidateCount;
            int abnormalCaptureCount;
            lock (_cornealCurvatureImageTaskLocker)
            {
                cornealCandidateCount = _cornealCurvatureImageTasks.Count;
            }
            lock (_abnormalCaptureStageImageTaskLocker)
            {
                abnormalCaptureCount = _abnormalCaptureStageImageTasks.Count;
            }
            MeasurementFlowLogger.Log(_caseID, _saveDataPath,
                $"[CaptureData] Finish acquire. packetCount={_rawEyePackets.Count}, cornealCandidates={cornealCandidateCount}, abnormalCaptureImages={abnormalCaptureCount}, blueLedMeasure={_isBlueLEDMeasure}");

            MessageDispatcher.Send(new EyeMultiParameterPacketsComputeTaskAlgorithmActor(BuildEyeMultiParameterPacketsComputeTask()));
            if (_isPolarizationRatate)
            {
                ModuleManagerBase.PolarizationLocationSearchStrategy.MotorRatateStop();
            }
            if (_isBlueLEDMeasure && !ServiceLocator.Current.GetInstance<DeviceConfigPara>().IsCricleRun)
            {
                // HandleImageNewFrameEvent is already bound for the whole module lifetime.
                LedBrightnessController.ApplyPreset(LedBrightnessPreset.WorkflowTargetRingWithBlueLight);
                Thread.Sleep(300);//0.3S闪烁间隔
                LedBrightnessController.ApplyPreset(LedBrightnessPreset.WorkflowTargetRing);
                _isBlueLEDMeasuring = true;
                Thread.Sleep(1500);//等待采集1.5s
                _isBlueLEDMeasuring = false;
                MessageDispatcher.Send(new CalculatePSAndWTWPacketsAlgorithmActor(_irisImagesForPSAndWTW, false));
            }
            _eyeType = MCUActor.Instance.CurrentMotor3DStatus.MotorXPosition == DotnetDevices.MCU.DataModel.MotorPosition.Negative ? EyeType.右眼 : EyeType.左眼;
            SignalFinish(true);
        }

        protected EyeMultiParameterPacketsComputeTask BuildEyeMultiParameterPacketsComputeTask()
        {
            var task = new EyeMultiParameterPacketsComputeTask
            {
                SignalTasks = _rawEyePackets.ToList()
            };
            lock (_cornealCurvatureImageTaskLocker)
            {
                task.CornealImageTasks = _cornealCurvatureImageTasks.ToList();
            }
            return task;
        }

        public virtual List<CornealCurvatureImageTask> GetAbnormalCaptureStageImageTasks()
        {
            lock (_abnormalCaptureStageImageTaskLocker)
            {
                return _abnormalCaptureStageImageTasks
                    .Select(item => new CornealCurvatureImageTask
                    {
                        IrisImage = item.IrisImage,
                        Distance = item.Distance,
                        PolarRotateIndex = item.PolarRotateIndex
                    })
                    .ToList();
            }
        }

        public virtual void TryCacheCornealCurvatureImageCandidate(float[] irisGrayImage, float distance)
        {
            if (irisGrayImage == null || irisGrayImage.Length == 0 || distance == 0f)
            {
                return;
            }

            var imageCopy = new float[irisGrayImage.Length];
            Buffer.BlockCopy(
                irisGrayImage, 0,
                imageCopy, 0,
                irisGrayImage.Length * sizeof(float)
            );

            StoreCornealCurvatureImageTask(imageCopy, distance);
        }

        public void TryCacheAbnormalCaptureStageImage(float[] irisGrayImage, float distance)
        {
            if (irisGrayImage == null || irisGrayImage.Length == 0)
            {
                return;
            }

            var imageCopy = new float[irisGrayImage.Length];
            Buffer.BlockCopy(
                irisGrayImage, 0,
                imageCopy, 0,
                irisGrayImage.Length * sizeof(float)
            );

            lock (_abnormalCaptureStageImageTaskLocker)
            {
                var abnormalImageTask = new CornealCurvatureImageTask
                {
                    IrisImage = imageCopy,
                    Distance = distance,
                    PolarRotateIndex = ModuleManagerBase.PolarizationLocationSearchStrategy.GetCurrentIndex()
                };

                if (_abnormalCaptureStageImageTasks.Count < MaxAbnormalCaptureImageCount)
                {
                    _abnormalCaptureStageImageTasks.Add(abnormalImageTask);
                    return;
                }

                int replaceIndex = -1;
                float minDistance = float.MaxValue;
                for (int i = 0; i < _abnormalCaptureStageImageTasks.Count; i++)
                {
                    float currentDistance = Math.Abs(_abnormalCaptureStageImageTasks[i].Distance);
                    if (currentDistance < minDistance)
                    {
                        minDistance = currentDistance;
                        replaceIndex = i;
                    }
                }

                if (replaceIndex >= 0 && Math.Abs(abnormalImageTask.Distance) > minDistance)
                {
                    _abnormalCaptureStageImageTasks[replaceIndex] = abnormalImageTask;
                }
            }
        }

        public void StoreCornealCurvatureImageTask(float[] imageCopy, float distance)
        {
            if (imageCopy == null || imageCopy.Length == 0 || distance == 0f)
            {
                return;
            }

            lock (_cornealCurvatureImageTaskLocker)
            {
                var cornealImageTask = new CornealCurvatureImageTask
                {
                    IrisImage = imageCopy,
                    Distance = distance,
                    PolarRotateIndex = ModuleManagerBase.PolarizationLocationSearchStrategy.GetCurrentIndex()
                };

                if (_cornealCurvatureImageTasks.Count < MaxCornealCurvatureCandidateCount)
                {
                    _cornealCurvatureImageTasks.Add(cornealImageTask);
                    return;
                }

                int replaceIndex = -1;
                float maxDistance = float.MinValue;
                for (int i = 0; i < _cornealCurvatureImageTasks.Count; i++)
                {
                    float currentDistance = Math.Abs(_cornealCurvatureImageTasks[i].Distance);
                    if (currentDistance > maxDistance)
                    {
                        maxDistance = currentDistance;
                        replaceIndex = i;
                    }
                }

                if (replaceIndex >= 0 && Math.Abs(cornealImageTask.Distance) < maxDistance)
                {
                    _cornealCurvatureImageTasks[replaceIndex] = cornealImageTask;
                }
            }
        }

        protected void HandleImageNewFrameEvent(object sender, float[] irisGrayImage)
        {
            //一直需要图片 注意：C#数组为引用类型
            if (_irisGrayImage == null || _irisGrayImage.Length != irisGrayImage.Length)
            {
                _irisGrayImage = new float[irisGrayImage.Length];
            }

            Buffer.BlockCopy(
                irisGrayImage, 0,
                _irisGrayImage, 0,
                irisGrayImage.Length * sizeof(float)
            );
            if (_isBlueLEDMeasuring)
            {
                var copy = new float[irisGrayImage.Length];

                Buffer.BlockCopy(
                    irisGrayImage, 0,
                    copy, 0,
                    irisGrayImage.Length * sizeof(float)
                );

                _irisImagesForPSAndWTW.Add(copy);
            }
            // 对焦补偿（角膜图像缓存判断 + 3帧中位数对焦决策 + 异常距离缓存）
            _focusCompensator.ProcessFrame(irisGrayImage, this, _isFinish, _isBlueLEDMeasuring);
        }
        protected override void EventBind()
        {
            DelEventBoundMethod(MFCameraActor.Instance.UpdateMFByteArrayEventHandler, "HandleImageNewFrameEvent");
            DelEventBoundMethod(AlgorithmActor.Instance.ShowSignalProcessComputeResultEventHandler, "HandleSignalProcessComputeResultEvent");
            //虹膜图像
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler += HandleImageNewFrameEvent;
            //信号
            AlgorithmActor.Instance.ShowSignalProcessComputeResultEventHandler += HandleSignalProcessComputeResultEvent;
        }

        protected override void EventUnBind()
        {
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler -= HandleImageNewFrameEvent;
            AlgorithmActor.Instance.ShowSignalProcessComputeResultEventHandler -= HandleSignalProcessComputeResultEvent;
        }

        protected override void ParasReset()
        {
            base.ParasReset();
            var eyeType = MCUActor.Instance.CurrentMotor3DStatus?.MotorXPosition == DotnetDevices.MCU.DataModel.MotorPosition.Negative ? EyeType.右眼 : EyeType.左眼;
            MessageDispatcher.Send(new SetCurrentDataInfoDataPostProcessActor(eyeType));
            var config = ServiceLocator.Current.GetInstance<DeviceConfigPara>();
            _saveDataPath = config.SaveDataPath;
            switch (eyeType)
            {
                case EyeType.左眼:
                    _offsetX = config.L_Offset.x;
                    _offsetZ = config.L_Offset.z;
                    break;
                case EyeType.右眼:
                    _offsetX = config.R_Offset.x;
                    _offsetZ = config.R_Offset.z;
                    break;
                case EyeType.无:
                    _offsetX = 0;
                    _offsetZ = 0;
                    break;
                default:
                    break;
            }
            _isBlueLEDMeasure = config.IsBlueLEDMeasure;
            _isBlueLEDMeasuring = false;
            _isPolarizationRatate = config.IsPolarizationAdjust;
            var correctConfig = ServiceLocator.Current.GetInstance<CorrectFileConfigs>();
            _cctFrontThreshold = correctConfig.CCT_FrontSignalThreshold;
            _cctBackThreshold = correctConfig.CCT_backSignalThreshold;
            _isNeedNewSignal = true;
            _focusCompensator.Reset(_offsetX, _offsetZ, _measureMode);
        }

        protected virtual void ClearMeasurementBuffers()
        {
            _rawEyePackets.Clear();
            _irisImagesForPSAndWTW.Clear();
        }

        public virtual void ClearCornealCurvatureImageTasks()
        {
            lock (_cornealCurvatureImageTaskLocker)
            {
                _cornealCurvatureImageTasks.Clear();
            }
        }

        public virtual void ClearAbnormalCaptureStageImageTasks()
        {
            lock (_abnormalCaptureStageImageTaskLocker)
            {
                _abnormalCaptureStageImageTasks.Clear();
            }
        }

        public virtual void MemoryClear()
        {
            ClearMeasurementBuffers();
            ClearCornealCurvatureImageTasks();
            ClearAbnormalCaptureStageImageTasks();
        }

        #endregion

        public CaptureDataModule()
        {
            _rawEyePackets = new List<EyeMultiParameterComputeTask>();
            _cornealCurvatureImageTasks = new List<CornealCurvatureImageTask>();
            _abnormalCaptureStageImageTasks = new List<CornealCurvatureImageTask>();
            _irisImagesForPSAndWTW = new List<float[]>();
            _timer = new System.Threading.Timer(ModuleRunTimeout);

        }

        protected void ModuleRunTimeout(object state)
        {
            if (_rawEyePackets.Count == 0)
            {
                InterruptStatus = ModuleInterruptStatus.CRITICAL;
                MeasurementFlowLogger.Log(_caseID, _saveDataPath, "[CaptureData] Timeout with zero packets. Escalate to critical.");
                Quit();
                return;
            }

            //关闭计时
            _timer?.Change(-1, -1);
            _isNeedNewSignal = false;
            _focusCompensator.DisableCornealImageCapture();

            //停止偏振转动
            //TODO
            if (_isPolarizationRatate)
            {
                ModuleManagerBase.PolarizationLocationSearchStrategy.MotorRatateStop();
            }

            //向外反馈当前的采集进度
            ShowAcquireSignalProgress?.BeginInvoke(this, 5, null, null);
            MeasurementFlowLogger.Log(_caseID, _saveDataPath, $"[CaptureData] Timeout fallback. packetCount={_rawEyePackets.Count}, progress forced to 5/5");


            MessageDispatcher.Send(new EyeMultiParameterPacketsComputeTaskAlgorithmActor(BuildEyeMultiParameterPacketsComputeTask()));
            //是否闪烁蓝灯
            if (_isBlueLEDMeasure && !ServiceLocator.Current.GetInstance<DeviceConfigPara>().IsCricleRun)
            {
                // 虹膜图像事件在模块开始时已绑定，这里只切换蓝灯采集窗口。
                LedBrightnessController.ApplyPreset(LedBrightnessPreset.WorkflowTargetRingWithBlueLight);
                Thread.Sleep(300);//0.3S闪烁间隔
                LedBrightnessController.ApplyPreset(LedBrightnessPreset.WorkflowTargetRing);
                //闪烁后的业务逻辑
                //TODO
                _isBlueLEDMeasuring = true;
                Thread.Sleep(1500);//等待采集1.5s
                _isBlueLEDMeasuring = false;
                //发送WTWAndPS数据包
                MessageDispatcher.Send(new CalculatePSAndWTWPacketsAlgorithmActor(_irisImagesForPSAndWTW, false));
            }
            //记录眼睛类型
            _eyeType = MCUActor.Instance.CurrentMotor3DStatus.MotorXPosition == DotnetDevices.MCU.DataModel.MotorPosition.Negative ? EyeType.右眼 : EyeType.左眼;
            SignalFinish(true);
        }

        public override string ToString()
        {
            return "采集数据|Collect data";
        }
    }
}

