using LJ.Lus.Backend.Compute.ComputeDataModel;
using LJ.Lus.Backend.Info.Actors.Database;
using LJ.Lus.Backend.Model;
using LJ.Lus.TechLibrary.ActorFrameWork;
using LJ.Lus.TechLibrary.ActorFrameWork.Message;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace LJ.Lus.Backend.Compute.Actors.Algorithm
{
    /// <summary>
    /// 同步数据处理任务 task-5
    /// </summary>
    public class SyncDataComputeTask
    {
        /// <summary>
        /// 同步数据
        /// </summary>
        public SyncData SyncData { get; set; }
        /// <summary>
        /// 测量模式
        /// </summary>
        public MeasureMode MeasureMode { get; set; }
        /// <summary>
        /// 角膜曲率类型（凸或者凹）
        /// </summary>
        public CorrModelType CorrModel { get; set; }
        /// <summary>
        /// 任务添加的时间
        /// </summary>
        public DateTime TaskAddTime { get; set; }

        public bool IsSimulanteye { get; set; } = false;

        public int PolarRotateIndex { get; set; }

        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
    }

    public class EyeMultiParameterComputeTask
    {
        public SignalProcessComputeResult SignalProcessComputeResult { get; set; }
        public float[] IrisImage { get; set; }
        public CorrModelType CorrModel { get; set; }
        public bool IsSimulateEye { get; set; } = false;
        public int PolarRotateIndex { get; set; }
        public bool IsCompensateAL { get; set; }
        public bool UseForCornealCurvature { get; set; } = true;
        public float? CornealCurvatureDistance { get; set; }
    }

    public class CornealCurvatureImageTask
    {
        public float[] IrisImage { get; set; }
        public float Distance { get; set; }
        public int PolarRotateIndex { get; set; }
    }

    public class EyeMultiParameterPacketsComputeTask
    {
        public List<EyeMultiParameterComputeTask> SignalTasks { get; set; } = new List<EyeMultiParameterComputeTask>();
        public List<CornealCurvatureImageTask> CornealImageTasks { get; set; } = new List<CornealCurvatureImageTask>();
    }

    /// <summary>
    /// 广角相机眼睛定位消息
    /// </summary>
    public class EyeLocateComputeAlgorithmActor : Message
    {
        /// <summary>
        /// 眼睛定位计算任务，用于广角相机找眼 task-1
        /// </summary>
        //public EyeLocateComputeTask EyeImageComputeTask { get; set; }
        public List<float[]> EyeImages { get; set; }
        public EyeLocateComputeAlgorithmActor(List<float[]> images)
        {
            EyeImages = images;
        }
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleEyeLocateCompute(EyeImages);
            }
        }
    }

    public class LightSpotComputeAlgorithmActor : ReplyMessage<float>
    {
        private float[] _irisImage;
        private int[] _center;
        public LightSpotComputeAlgorithmActor(float[] irisImage, int[] center)
        {
            _irisImage = irisImage;
            _center = center;
        }

        public override float DoCore(Actor actor)
        {
            if(actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.HandleLightSpotCompute(_irisImage, _center);
            }
            return 0;
        }
    }

    public class SignalReBuildComputeAlgorithmActor : ReplyMessage<float[]>
    {
        private int[] _peaksPosition;
        private double[] _peaksValue;

        public SignalReBuildComputeAlgorithmActor(int[] peaksPosition, double[] peaksValue)
        {
            _peaksPosition = peaksPosition;
            _peaksValue = peaksValue;
        }
        public override float[] DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.HandleSignalReBuildCompute(_peaksPosition, _peaksValue);
            }
            return null;
        }
    }


    /// <summary>
    /// 虹膜图像质量计算消息
    /// </summary>
    public class IrisImageQualityComputeAlgorithmActor : Message
    {
        private float[] _irisImage;

        public IrisImageQualityComputeAlgorithmActor(float[] irisImage)
        {
            _irisImage = irisImage;
        }

        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleIrisImageQualityCompute(_irisImage);
            }
        }
    }

    /// <summary>
    /// 对焦靶环计算消息
    /// </summary>
    public class TargetRingRadiusComputeAlgorithmActor : Message
    {
        /// <summary>
        /// 对焦靶环计算任务，用于对焦，将靶环调到清晰位置 task-2
        /// </summary>
        //public TargetRingFocusComputeTask TargetRingFocusComputeTask { get; set; }
        public List<float[]> FocusImages { get; set; }
        public TargetRingRadiusComputeAlgorithmActor(List<float[]> images)
        {
            FocusImages = images;
        }

        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleTargetRingFocusCompute(FocusImages);
            }
        }
    }

    /// <summary>
    /// 实时瞳孔跟踪消息
    /// </summary>
    public class RealTimeTrackComputeAlgorithmActor : Message
    {
        /// <summary>
        /// 实时瞳孔跟踪计算任务 task-3
        /// </summary>
        //public RealTimeTrackComputeTask RealTimeTrackComputeTask { get; set; }
        public float[] IrisImage { get; set; }
        /// <summary>
        /// 实时瞳孔跟踪计算任务
        /// </summary>
        /// <param name="task">虹膜图像计算任务</param>
        public RealTimeTrackComputeAlgorithmActor(float[] image)
        {
            IrisImage = image;
        }
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleRealTimeTrackCompute(IrisImage);
            }
        }
    }

    public class Reply_RealTimeTrackComputeAlgorithmActor : ReplyMessage<int[]>
    {
        /// <summary>
        /// 实时瞳孔跟踪计算任务 task-3
        /// </summary>
        //public RealTimeTrackComputeTask RealTimeTrackComputeTask { get; set; }
        public float[] IrisImage { get; set; }
        /// <summary>
        /// 实时瞳孔跟踪计算任务
        /// </summary>
        /// <param name="task">虹膜图像计算任务</param>
        public Reply_RealTimeTrackComputeAlgorithmActor(float[] image)
        {
            IrisImage = image;
        }
        public override int[] DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                var offset = algorithmActor.Reply_HandleRealTimeTrackCompute(IrisImage);
                return offset;
            }
            return null;
        }
    }

    public class SignalProcessComputeAlgorithmActor : Message
    {
        private DAQData _data;
        private Model.MeasureMode _measureMode;
        private Model.DeviceConfigPara _deviceConfigPara;
        public SignalProcessComputeAlgorithmActor(DAQData data, MeasureMode measureMode, DeviceConfigPara deviceConfigPara)
        {
            _data = data;
            _measureMode = measureMode;
            _deviceConfigPara = deviceConfigPara;
        }

        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleSignalProcessCompute(_data, _measureMode, _deviceConfigPara);
            }
        }
    }


    public class EyeMultiParameterComputeTaskAlgorithmActor : Message
    {
        private EyeMultiParameterComputeTask _task;
        public EyeMultiParameterComputeTaskAlgorithmActor(EyeMultiParameterComputeTask task)
        {
            _task = task;
        }
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleEyeMultiParameterCompute(_task);
            }
        }
    }
    public class EyeMultiParameterPacketsComputeTaskAlgorithmActor : Message
    {
        private EyeMultiParameterPacketsComputeTask _task;
        public EyeMultiParameterPacketsComputeTaskAlgorithmActor(List<EyeMultiParameterComputeTask> tasks)
        {
            _task = new EyeMultiParameterPacketsComputeTask
            {
                SignalTasks = tasks
            };
        }

        public EyeMultiParameterPacketsComputeTaskAlgorithmActor(EyeMultiParameterPacketsComputeTask task)
        {
            _task = task;
        }
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleEyeMultiParameterCompute(_task);
            }
        }
    }
    public class Reply_HandleSyncDataComputeAlgorithmActor : ReplyMessage<Case>
    {
        private SyncDataComputeTask _task;
        public Reply_HandleSyncDataComputeAlgorithmActor(SyncDataComputeTask task)
        {
            _task = task;
        }

        public override Case DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.Reply_HandleSyncDataCompute(_task);
            }
            return null;
        }
    }

    /// <summary>
    /// 蓝灯和白灯LED光斑数量计算
    /// </summary>
    public class LEDFaculaNumComputeAlgorithmActor : Message
    {
        public float[] IrisImage { get; set; }
        public LEDFaculaNumComputeAlgorithmActor(float[] irisImage)
        {
            IrisImage = irisImage;
        }

        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleLEDFaculaNumCompute(IrisImage);
            }
        }
    }

    public class CalculatePDAndWTWAlgorithmActor : Message
    {
        public float[] IrisImage { get; set; }
        public bool IsMockEye { get; set; }
        public CalculatePDAndWTWAlgorithmActor(float[] irisImage, bool isMockEye)
        {
            IrisImage = irisImage;
            IsMockEye = isMockEye;
        }
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleCalculatePDAndWTW(IrisImage, IsMockEye);
            }
        }
    }

    public class CalculatePSAndWTWPacketsAlgorithmActor : Message
    {
        private List<float[]> _irisImage { get; set; }
        private bool _isMockEye { get; set; }
        public CalculatePSAndWTWPacketsAlgorithmActor(List<float[]> irisImages, bool isMockEye)
        {
            _irisImage = irisImages;
            _isMockEye = isMockEye;
        }
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleCalculatePDAndWTW(_irisImage, _isMockEye);
            }
        }
    }



    public class CalculateThicknessAlgorithmActor : Message
    {
        private short[] _Channel1;
        private List<SignalPositionInfo> _Signals;
        private byte _index;
        public CalculateThicknessAlgorithmActor(short[] Channel1, List<SignalPositionInfo> Signals,byte index=0)
        {
            _Channel1 = Channel1;
            _Signals = Signals;
            _index = index;
        }
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleCalculateThickness(_Channel1, _Signals, _index);
            }
        }
    }


    /// <summary>
    /// 释放算法计算资源
    /// </summary>
    public class FreeAlgorithmComputeAlgorithmActor : Message
    {
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleFreeAlgorithmCompute();
            }
        }
    }

    /// <summary>
    /// 初始化算法计算资源
    /// </summary>
    public class InitAlgorithmComputeAlgorithmActor : Message
    {
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleInitAlgorithmCompute();
            }
        }
    }

    /// <summary>
    /// 设置算法计算配置
    /// </summary>
    public class SetAlgorithmComputeConfigAlgorithmActor : Message
    {
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.HandleReadAlgorithmComputeConfig();
            }
        }
    }

    public class FlushAlgorithmComputeAlgorithmActor : Message
    {
        public override void DoWork(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                algorithmActor.Flush();
            }
        }
    }

    public class GetCorneaToFocusDistanceAlgorithmActor : ReplyMessage<float>
    {
        short[] _chanel;

        int _position;
        byte _index;

        public GetCorneaToFocusDistanceAlgorithmActor(short[] chanel, int position,byte index=0)
        {
            _chanel = chanel;
            _position = position;
            _index = index;
        }
        public override float DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.HandleGetCorneaToFocusDistance(_chanel, _position, _index);
            }
            return -1;
        }
    }

    public class Reply_HandleSignalThresholdValueComputeAlgorithmActor : ReplyMessage<float>
    {
        private float[] _data;
        public Reply_HandleSignalThresholdValueComputeAlgorithmActor(float[] processedSignal)
        {
            _data = processedSignal;
        }

        public override float DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.Reply_HandleSignalThresholdValueCompute(_data);
            }
            return 0;
        }
    }


    public class IOLPreoperativeComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public IOLPreoperativeComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.IOLPreoperativeCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class HISTORYderivedComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public HISTORYderivedComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.HISTORYderivedCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class RefractionDerivedComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public RefractionDerivedComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.RefractionDerivedCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class Double_K_SRK_TComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public Double_K_SRK_TComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.Double_K_SRK_TCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class ShammasComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public ShammasComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.ShammasCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class ROSAComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public ROSAComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.ROSACompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class Haigis_LComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public Haigis_LComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.Haigis_LCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class MasketComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public MasketComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.MasketCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }

    public class ModifyMasketComputeAlgorithmActor : ReplyMessage<ValueTuple<float, float, float[], float[]>>
    {
        private IOLBasiceInputEntity _IOLBasiceInputEntity;
        public ModifyMasketComputeAlgorithmActor(IOLBasiceInputEntity input)
        {
            _IOLBasiceInputEntity = input;
        }
        public override (float, float, float[], float[]) DoCore(Actor actor)
        {
            if (actor is AlgorithmActor algorithmActor)
            {
                return algorithmActor.ModifyMasketCompute(_IOLBasiceInputEntity);
            }
            return (-1, -1, null, null);
        }
    }
}


