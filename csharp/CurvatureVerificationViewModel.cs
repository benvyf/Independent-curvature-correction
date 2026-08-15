using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using LJ.Lus.Backend.Compute.Actors.Algorithm;
using LJ.Lus.Backend.Model;
using LJ.Lus.DotnetDevices.DAQ;
using System.Threading;
using LJ.Lus.ToolingDashboard.FullMachine.Tool;
using System.Drawing;
using LJ.Lus.Backend.Acquire.Actors.DAQ;
using LJ.Lus.Backend.Basic;
using LJ.Lus.Backend.Acquire.Actors.Camera;
using LJ.Lus.Backend.Acquire.Actors.MCU;
using LJ.Lus.TechLibrary.Helpers;
using LJ.Lus.Backend.Compute.ComputeDataModel;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using LJ.Lus.Backend.Tooling.Actors.Database.DatabaseConfigs;
using LJ.Lus.Backend.Tooling.Helpers;
using LJ.Lus.ToolingDashboard.Views;
using System.Windows.Controls;
using System.IO;
using LJ.Lus.TechLibrary.Image;
using LJ.Lus.Backend.Tooling.Actors.Compute;
using LJ.Lus.ToolingDashboard.FullMachine.Model;
using LJ.Lus.DotnetDevices.MCU.DataModel;
using LJ.Lus.TechLibrary.Interfaces;

namespace LJ.Lus.ToolingDashboard.FullMachine.ViewModel
{

    public struct CalibrationParams
    {
        /// <summary>
        /// 出厂插值字典（包含各模拟眼的 f0、分轴 d0 / k / Cf / C）
        /// </summary>
        public Dictionary<string, CalibrationNode> FactoryInterpolationDictionary;

        /// <summary>
        /// 旧版字段，保留兼容。
        /// </summary>
        public float HardwareResidualCoef;

        /// <summary>
        /// 像元尺寸 (mm/pixel)，默认 0.0075
        /// </summary>
        public double PixelSize;

        /// <summary>
        /// 旧版字段，保留兼容。
        /// </summary>
        public double ObliqueDotGain { get; set; }

        /// <summary>
        /// 标识当前传递的是内环还是外环，以便 Calculator 调取对应字段
        /// </summary>
        public bool IsInnerRing;



    }

    public struct EyeResult
    {
        public double RMajor;   // 长轴半径 (mm)
        public double RMinor;   // 短轴半径 (mm)
        public double KFlat;    // 平坦 K 值 (D)
        public double KSteep;   // 陡峭 K 值 (D)
        public double Cyl;      // 散光度数 (D)

        // 实时偏离焦点的物理位移 (mm)
        public double D_Shift;
    }

    public class CurvatureVerificationViewModel : ViewModelBase
    {
        private const string CurvatureCalibrationUpdatedMessage = "CurvatureCalibrationUpdated";
        private const int VerificationFrameIntervalMilliseconds = 100;
        private const int IrisPreviewWidth = 640;
        private const int IrisPreviewHeight = 480;

        #region 字段

        private CorrectFileConfigs _configs;
        private EventHandler<List<CornealComputeDataEntitys>> _filleDataEventHandler;
        private EventHandler<ComputeDataEntity> _filleWTWDataEventHandler;
        private bool _isExported = false;
        private volatile bool _isCompute = false;
        private bool _isWTWCompute = false;
        private int wtwindex = 0;
        List<CornealComputeDataEntitys> computeDataEntities = new List<CornealComputeDataEntitys>();
        private CorrectMotorControl _motorControl;
        private CornealCurvatureCalculator _eyeCalculator;
        private bool _saveImg = false;
        private readonly object _verificationLoopSyncRoot = new object();
        private readonly object _latestVerificationFrameSyncRoot = new object();
        private readonly object _measurementStateSyncRoot = new object();
        private readonly object _previewFrameSyncRoot = new object();
        private readonly AutoResetEvent _verificationFrameReadyEvent = new AutoResetEvent(false);
        private CancellationTokenSource _verificationProcessingCts;
        private Task _verificationProcessingTask;
        private float[] _latestVerificationFrame;
        private WriteableBitmap _irisPreviewBitmap;
        private readonly byte[] _irisPreviewBuffer = new byte[IrisPreviewWidth * IrisPreviewHeight];
        private float[] _latestPreviewFrame;
        private int _previewRefreshQueued = 0;
        private int _validationRequestToken = 0;
        // 定义两套参数
        public CalibrationParams _innerCalibrationParams;
        public CalibrationParams _outerCalibrationParams;

        #endregion

        #region 构造

        public CurvatureVerificationViewModel(CorrectFileConfigs configs)
        {
            _configs = configs;
            _eyeCalculator = new CornealCurvatureCalculator();
            _filleWTWDataEventHandler += FilledWTWDataCallback;
            MessageInit();
            LoadedFiles();
            UpdateModeVisibility();
        }

        #endregion

        #region 属性

        private string _realTimeD = "0.000";
        /// <summary>
        /// 实时对焦偏移量 (mm)
        /// </summary>
        public string RealTimeD
        {
            get { return _realTimeD; }
            set { Set(ref _realTimeD, value); }
        }

        private string _clearLocationValue;
        public string ClearLocationValue
        {
            get { return _clearLocationValue; }
            set
            {
                _clearLocationValue = value;
                RaisePropertyChanged(nameof(ClearLocationValue));
            }
        }

        private Visibility _exportVisibility = Visibility.Hidden;
        public Visibility ExportVisibility
        {
            get { return _exportVisibility; }
            set
            {
                _exportVisibility = value;
                RaisePropertyChanged(nameof(ExportVisibility));
            }
        }

        private Visibility _nextVisibility = Visibility.Visible;
        public Visibility NextVisibility
        {
            get { return _nextVisibility; }
            set
            {
                _nextVisibility = value;
                RaisePropertyChanged(nameof(NextVisibility));
            }
        }


        private string _fileName;
        public string FileName
        {
            get { return _fileName; }
            set { Set(ref _fileName, value); }
        }

        private Visibility _newParamsVisibility = Visibility.Collapsed;
        public Visibility NewParamsVisibility
        {
            get { return _newParamsVisibility; }
            set
            {
                _newParamsVisibility = value;
                RaisePropertyChanged(nameof(NewParamsVisibility));
            }
        }

        private bool? _isCloselMode;
        public bool? IsCloselMode
        {
            get { return _isCloselMode; }
            set
            {
                Set(ref _isCloselMode, value);
                UpdateModeVisibility();
                if (value ?? true)
                {
                    MainWindowViewModel.cameraStatue = false;
                    Messenger.Default.Send<object>(null, "ControlButtonToCorrectionModeState");
                    IrisImgVisibility = Visibility.Collapsed;
                    MessageDispatcher.Send(new CloseIrisMFCameraActor());
                    UnregisterEvents();
                }
                else
                {
                    if (MessageDispatcher.Send(new CheckMFCameraActor()))
                    {
                        Messenger.Default.Send<object>(null, "ControlButtonToCorrectionModeState");
                        if (MessageDispatcher.Send(new OpenIrisMFCameraActor())) MessageDispatcher.Send(new StartIrisMFCameraActor());
                        IrisImgVisibility = Visibility.Visible;
                        LoadedFiles();
                        RegisterEvents();
                    }
                }
            }
        }

        private Visibility _TwoFaceKERDataVisibility = Visibility.Hidden;
        public Visibility TwoFaceKERDataVisibility
        {
            get { return _TwoFaceKERDataVisibility; }
            set
            {
                _TwoFaceKERDataVisibility = value;
                RaisePropertyChanged(nameof(TwoFaceKERDataVisibility));
            }
        }

        private string sDR;
        public string SDR
        {
            get { return sDR; }
            set
            {
                sDR = value;
                RaisePropertyChanged(nameof(SDR));
            }
        }

        private string _ker1;
        public string KER1
        {
            get { return _ker1; }
            set { Set(ref _ker1, value); }
        }

        private string _ker2;
        public string KER2
        {
            get { return _ker2; }
            set { Set(ref _ker2, value); }
        }

        private string _meanRadiusOfCurvature;
        public string MeanRadiusOfCurvature
        {
            get { return _meanRadiusOfCurvature; }
            set { Set(ref _meanRadiusOfCurvature, value); }
        }

        private string _longAxis;
        public string LongAxis
        {
            get { return _longAxis; }
            set { Set(ref _longAxis, value); }
        }

        private string _shortAxis;
        public string ShortAxis
        {
            get { return _shortAxis; }
            set { Set(ref _shortAxis, value); }
        }

        private string _axisAngle;
        public string AxisAngle
        {
            get { return _axisAngle; }
            set { Set(ref _axisAngle, value); }
        }

        private string _lightSpotDist;
        public string LightSpotDist
        {
            get { return _lightSpotDist; }
            set { Set(ref _lightSpotDist, value); }
        }

        private ImageSource _irisImg;
        public ImageSource IrisImg
        {
            get { return _irisImg; }
            set { Set(ref _irisImg, value); }
        }

        private Visibility _irisImgVisibility = Visibility.Hidden;
        public Visibility IrisImgVisibility
        {
            get { return _irisImgVisibility; }
            set
            {
                _irisImgVisibility = value;
                RaisePropertyChanged(nameof(IrisImgVisibility));
            }
        }

        private float _WTWVerityValue = 0;
        public float WTWVerityValue
        {
            get { return _WTWVerityValue; }
            set
            {
                _WTWVerityValue = value;
                RaisePropertyChanged(nameof(WTWVerityValue));
            }
        }

        private float _WTWSamplingTime = 1;
        public float WTWSamplingTime
        {
            get { return _WTWSamplingTime; }
            set
            {
                _WTWSamplingTime = value;
                RaisePropertyChanged(nameof(WTWSamplingTime));
            }
        }

        #endregion

        #region 命令

        private RelayCommand _saveImgCommand;
        public RelayCommand SaveImgCommand =>
            _saveImgCommand ?? (_saveImgCommand = new RelayCommand(ExcuteSaveImg));

        private RelayCommand _validationCommand;
        public RelayCommand ValidationCommand =>
            _validationCommand ?? (_validationCommand = new RelayCommand(ExcuteValidationCommand));

        private RelayCommand _exportCommand;
        public RelayCommand ExportCommand =>
            _exportCommand ?? (_exportCommand = new RelayCommand(ExcuteExportCommand));

        private RelayCommand _nextStepCommand;
        public RelayCommand NextStepCommand =>
            _nextStepCommand ?? (_nextStepCommand = new RelayCommand(ExcuteNextStepCommand));

        private RelayCommand _continueMoveStopCommand;
        public RelayCommand ContinueMoveStopCommand =>
            _continueMoveStopCommand ?? (_continueMoveStopCommand = new RelayCommand(ExcuteContinueMoveStop));

        private RelayCommand _motorLeftCommand;
        public RelayCommand MotorLeftCommand =>
            _motorLeftCommand ?? (_motorLeftCommand = new RelayCommand(ExcuteMotorLeft));

        private RelayCommand _leftContinueMoveStartCommand;
        public RelayCommand LeftContinueMoveStartCommand =>
            _leftContinueMoveStartCommand ?? (_leftContinueMoveStartCommand = new RelayCommand(ExcuteLeftContinueMoveStart));

        private RelayCommand _motorRightCommand;
        public RelayCommand MotorRightCommand =>
            _motorRightCommand ?? (_motorRightCommand = new RelayCommand(ExcuteMotorRight));

        private RelayCommand _rightContinueMoveStartCommand;
        public RelayCommand RightContinueMoveStartCommand =>
            _rightContinueMoveStartCommand ?? (_rightContinueMoveStartCommand = new RelayCommand(ExcuteRightContinueMoveStart));

        private RelayCommand _validationWTWCommand;
        public RelayCommand ValidationWTWCommand =>
            _validationWTWCommand ?? (_validationWTWCommand = new RelayCommand(ExcuteValidationWTWCommand));


        #endregion

        #region 方法

        private void ExcuteContinueMoveStop()
        {
            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(1, _motorControl));
        }

        private void ExcuteMotorLeft() //回头方向
        {
            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(0.3f, _motorControl));
        }

        private void ExcuteLeftContinueMoveStart()
        {
            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(0.3f * 1000, _motorControl));
        }

        private void ExcuteMotorRight() //校正步进方向
        {

            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(-0.3f, _motorControl));

        }

        private void ExcuteRightContinueMoveStart()
        {
            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(-0.3f * 1000, _motorControl));
        }

        private void ExcuteNextStepCommand()
        {
            if (!MainWindowViewModel.cameraStatue)
            {
                if (MainWindowViewModel.cornaelVerificationMockEyeType == CornaelVerificationMockEyeType.Convex)
                {
                    IsCloselMode = true;
                    MainWindowViewModel.cornaelVerificationMockEyeType = CornaelVerificationMockEyeType.Concave;

                    Messenger.Default.Send(new Message.Switch_InterfaceMessage.CurvatureCorrection());
                }
            }
            else
            {
                Messenger.Default.Send<object>(null, "ReminderCloseCamera");
            }
        }

        private void ExcuteSaveImg()
        {
            if (!_saveImg) _saveImg = true;
        }

        private void ExcuteValidationCommand()
        {
            lock (_measurementStateSyncRoot)
            {
                _validationMeasureBuffer.Clear();
                _validationRequestToken++;
                _isCompute = true;
            }
        }

        private void ExcuteValidationWTWCommand()
        {
            WTWVerityValue = 0;

            if (WTWSamplingTime == 0)
            {
                WTWSamplingTime = 1;
            }

            _isWTWCompute = true;

            _configs.WTWSamplingTime = WTWSamplingTime;
            string path = Environment.CurrentDirectory + "\\CorrectFileConfigs.json";
            JsonHelper.Save(_configs, path);
        }

        private void ExcuteExportCommand()
        {

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (FileName == null)
            {
                FileName = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            }

            string folderPath = Path.Combine(desktopPath, FileName);
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CorrectFileConfigs.json"), Path.Combine(folderPath, "CorrectFileConfigs.json"), true);
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeviceConfigPara.json"), Path.Combine(folderPath, "DeviceConfigPara.json"), true);
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DAQConfigs.json"), Path.Combine(folderPath, "DAQConfigs.json"), true);
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TotalNumssCycleNum.txt"), Path.Combine(folderPath, "TotalNumssCycleNum.txt"), true);
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SignalFilterConfigs.json"), Path.Combine(folderPath, "SignalFilterConfigs.json"), true);

            if (MainWindowViewModel.collectNumberFaces == CollectNumberFaces.TwoFaces)
            {
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CorrectFileConfigs2.json"), Path.Combine(folderPath, "CorrectFileConfigs2.json"), true);
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TotalNumssCycleNum2.txt"), Path.Combine(folderPath, "TotalNumssCycleNum2.txt"), true);
            }

            _isExported = true;
            MessageBox.Show("导出成功！");
        }

        private void FilledWTWDataCallback(object sender, ComputeDataEntity computeDataEntities)
        {
            try
            {
                WTWVerityValue = MessageDispatcher.Send(new Reply_CalculateWTWCoefficientToolingComputeActor(BitmapHelpers.BitmapToFloat(computeDataEntities.Image))) * WTWSamplingTime;
            }
            finally
            {
                computeDataEntities.Image?.Dispose();
            }
        }

        /// <summary>
        /// 实时获得图像数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="image"></param>
        private void ShowIrisImage(object sender, float[] image)
        {
            if (_saveImg)
            {
                _saveImg = false;
                string directoryPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + "\\图片保存";

                try
                {
                    // 确保目录存在
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    // 查找下一个可用的文件名
                    int fileNumber = 0;
                    string fullPath;
                    do
                    {
                        fullPath = Path.Combine(directoryPath, $"{fileNumber}.png");
                        fileNumber++;
                    } while (File.Exists(fullPath));

                    // 保存图片为PNG格式
                    Bitmap bitmap = BitmapHelpers.ToGrayBitmap(image, 640, 480);
                    // 保存图片为PNG格式
                    bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);

                }
                catch (Exception ex)
                {

                }

            }
            _saveImg = false;

            QueueLatestPreviewFrame(image);
        }

        private void QueueLatestPreviewFrame(float[] image)
        {
            lock (_previewFrameSyncRoot)
            {
                _latestPreviewFrame = image;
            }

            if (Interlocked.Exchange(ref _previewRefreshQueued, 1) != 0)
            {
                return;
            }

            Application.Current.Dispatcher.BeginInvoke(new Action(ProcessPendingPreviewFrame));
        }

        private void ProcessPendingPreviewFrame()
        {
            try
            {
                while (true)
                {
                    float[] image;
                    lock (_previewFrameSyncRoot)
                    {
                        image = _latestPreviewFrame;
                        _latestPreviewFrame = null;
                    }

                    if (image == null)
                    {
                        break;
                    }

                    if (_irisPreviewBitmap == null)
                    {
                        _irisPreviewBitmap = CreateGray8PreviewBitmap();
                        IrisImg = _irisPreviewBitmap;
                    }

                    WritePreviewFrameToBitmap(image);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _previewRefreshQueued, 0);

                lock (_previewFrameSyncRoot)
                {
                    if (_latestPreviewFrame != null && Interlocked.Exchange(ref _previewRefreshQueued, 1) == 0)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(ProcessPendingPreviewFrame));
                    }
                }
            }
        }

        private WriteableBitmap CreateGray8PreviewBitmap()
        {
            return new WriteableBitmap(IrisPreviewWidth, IrisPreviewHeight, 96, 96, PixelFormats.Gray8, BitmapPalettes.Gray256);
        }

        private void WritePreviewFrameToBitmap(float[] image)
        {
            int pixelCount = IrisPreviewWidth * IrisPreviewHeight;
            for (int i = 0; i < pixelCount; i++)
            {
                float value = image[i];
                if (value < 0) value = 0;
                if (value > 255) value = 255;
                _irisPreviewBuffer[i] = (byte)value;
            }

            _irisPreviewBitmap.WritePixels(
                new Int32Rect(0, 0, IrisPreviewWidth, IrisPreviewHeight),
                _irisPreviewBuffer,
                IrisPreviewWidth,
                0);
        }


        // 用于保存单帧数据的结构体
        private class MeasureData
        {
            // Inner Data
            public float InnerLongShaft { get; set; }
            public float InnerShortShaft { get; set; }
            public float InnerAngle { get; set; }

            // Outer Data
            public float OuterLongShaft { get; set; }
            public float OuterShortShaft { get; set; }
            public float OuterAngle { get; set; }

            // Common Data
            public float LightSpotDistance { get; set; }
            public float Sdr { get; set; }
        }

        private class ValidationDisplayResult
        {
            public string Ker1 { get; set; }
            public string Ker2 { get; set; }
            public string MeanRadiusOfCurvature { get; set; }
            public string LongAxis { get; set; }
            public string ShortAxis { get; set; }
            public string AxisAngle { get; set; }
            public string LightSpotDist { get; set; }
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private static bool IsValidMeasurement(MeasureData data)
        {
            return IsFinitePositive(data.LightSpotDistance)
                && IsFinitePositive(data.InnerLongShaft)
                && IsFinitePositive(data.InnerShortShaft)
                && IsFinitePositive(data.OuterLongShaft)
                && IsFinitePositive(data.OuterShortShaft);
        }

        private static bool IsValidOuterRealtimeMeasurement(MeasureData data)
        {
            return IsFinitePositive(data.LightSpotDistance)
                && IsFinitePositive(data.OuterLongShaft)
                && IsFinitePositive(data.OuterShortShaft);
        }

        private static bool IsValidEyeResult(EyeResult result)
        {
            return IsFinitePositive(result.RMajor) && IsFinitePositive(result.RMinor);
        }

        // SDR 使用连续批次，验证使用点击后新开启的独立缓冲，避免混入点击前旧帧。
        private readonly List<MeasureData> _measureBuffer = new List<MeasureData>(5);
        private readonly List<MeasureData> _validationMeasureBuffer = new List<MeasureData>(5);

        // 性能优化：Curvatureverify 回调专用缓冲池，免除每帧创建临时对象引起的 GC 异常
        private double[] _innerRingPool = new double[5];
        private double[] _outerRingPool = new double[6];

        private void Curvatureverify(object sender, float[] image)
        {
            SetLatestVerificationFrame(image);
            _verificationFrameReadyEvent.Set();
        }

        private void StartVerificationProcessing()
        {
            lock (_verificationLoopSyncRoot)
            {
                if (_verificationProcessingTask != null && !_verificationProcessingTask.IsCompleted)
                {
                    return;
                }

                CancellationTokenSource verificationProcessingCts = new CancellationTokenSource();
                _verificationProcessingCts = verificationProcessingCts;
                _verificationProcessingTask = Task.Run(() => VerificationProcessingLoop(verificationProcessingCts.Token));
            }
        }

        private void StopVerificationProcessing()
        {
            CancellationTokenSource processingCts;
            Task processingTask;

            lock (_verificationLoopSyncRoot)
            {
                processingCts = _verificationProcessingCts;
                processingTask = _verificationProcessingTask;
                _verificationProcessingCts = null;
                _verificationProcessingTask = null;
            }

            if (processingCts == null)
            {
                return;
            }

            try
            {
                processingCts.Cancel();
                _verificationFrameReadyEvent.Set();
                processingTask?.Wait(1000);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is TaskCanceledException || inner is OperationCanceledException))
            {
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                processingCts.Dispose();
                SetLatestVerificationFrame(null);
                lock (_measurementStateSyncRoot)
                {
                    _measureBuffer.Clear();
                    _validationMeasureBuffer.Clear();
                    _validationRequestToken++;
                    _isCompute = false;
                }
            }
        }

        private void VerificationProcessingLoop(CancellationToken cancellationToken)
        {
            DateTime lastProcessedAtUtc = DateTime.MinValue;

            while (!cancellationToken.IsCancellationRequested)
            {
                _verificationFrameReadyEvent.WaitOne();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan remaining = TimeSpan.FromMilliseconds(VerificationFrameIntervalMilliseconds) - (DateTime.UtcNow - lastProcessedAtUtc);
                if (remaining > TimeSpan.Zero && cancellationToken.WaitHandle.WaitOne(remaining))
                {
                    break;
                }

                float[] image = TakeLatestVerificationFrame();
                if (image == null)
                {
                    continue;
                }

                try
                {
                    ProcessVerificationFrame(image);
                    lastProcessedAtUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Curvature verification frame processing failed: {ex.Message}");
                }
            }
        }

        private void SetLatestVerificationFrame(float[] image)
        {
            lock (_latestVerificationFrameSyncRoot)
            {
                _latestVerificationFrame = image;
            }
        }

        private float[] TakeLatestVerificationFrame()
        {
            lock (_latestVerificationFrameSyncRoot)
            {
                float[] image = _latestVerificationFrame;
                _latestVerificationFrame = null;
                return image;
            }
        }

        private void ProcessVerificationFrame(float[] image)
        {
            double[] innerRing = _innerRingPool;
            double[] outerRing = _outerRingPool;
            CalibrationParams innerCalibrationParams = _innerCalibrationParams;
            CalibrationParams outerCalibrationParams = _outerCalibrationParams;
            MessageDispatcher.Send(new CornealCurvatureComputeToolingComputeActor(image, innerRing, outerRing));

            var currentData = new MeasureData
            {
                // Inner (0-4)
                InnerLongShaft = (float)innerRing[2],
                InnerShortShaft = (float)innerRing[3],
                InnerAngle = (float)innerRing[4],

                // Outer (5-9)
                OuterLongShaft = (float)outerRing[2],
                OuterShortShaft = (float)outerRing[3],
                OuterAngle = (float)outerRing[4],

                // Common
                LightSpotDistance = (float)outerRing[5],
                Sdr = (float)((outerRing[2] > 0.001) ? outerRing[5] / (outerRing[2] * 2) : 0)
            };

            // ==========================================================
            // [单帧极速辅助] 实时计算 D 位移，辅助操作人员对焦
            // ==========================================================
            if (IsValidOuterRealtimeMeasurement(currentData)
                && outerCalibrationParams.FactoryInterpolationDictionary != null
                && outerCalibrationParams.FactoryInterpolationDictionary.Count > 0)
            {
                try
                {
                    var rtResult = _eyeCalculator.Calculate(
                        currentData.OuterLongShaft, currentData.OuterShortShaft,
                        currentData.LightSpotDistance, currentData.OuterAngle, outerCalibrationParams);

                    if (IsValidEyeResult(rtResult))
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            RealTimeD = rtResult.D_Shift > 0 ? $"+{rtResult.D_Shift:F3}" : $"{rtResult.D_Shift:F3}";
                        }));
                    }
                }
                catch { }
            }

            string sdr = null;
            List<MeasureData> validationBatch = null;
            int validationToken = 0;

            lock (_measurementStateSyncRoot)
            {
                _measureBuffer.Add(currentData);
                if (_measureBuffer.Count >= 5)
                {
                    sdr = CalculateClusteredSdr(_measureBuffer);
                    _measureBuffer.Clear();
                }

                if (_isCompute && IsValidMeasurement(currentData))
                {
                    _validationMeasureBuffer.Add(currentData);
                    if (_validationMeasureBuffer.Count >= 5)
                    {
                        validationBatch = new List<MeasureData>(_validationMeasureBuffer);
                        validationToken = _validationRequestToken;
                        _validationMeasureBuffer.Clear();
                        _isCompute = false;
                    }
                }
            }

            if (sdr != null)
            {
                BeginInvokeOnUiThread(() => SDR = sdr);
            }

            if (validationBatch != null)
            {
                ValidationDisplayResult validationResult = TryBuildValidationResult(validationBatch, innerCalibrationParams, outerCalibrationParams);
                if (validationResult != null)
                {
                    bool shouldApplyResult;
                    lock (_measurementStateSyncRoot)
                    {
                        shouldApplyResult = validationToken == _validationRequestToken;
                    }

                    if (shouldApplyResult)
                    {
                        BeginInvokeOnUiThread(() => ApplyValidationResult(validationResult));
                    }
                }
            }
        }

        private string CalculateClusteredSdr(IReadOnlyList<MeasureData> measurements)
        {
            List<float> sdrList = measurements.Select(x => x.Sdr).ToList();
            sdrList.Sort();

            float GetVar(float v1, float v2, float v3)
            {
                float avg = (v1 + v2 + v3) / 3f;
                return (v1 - avg) * (v1 - avg) + (v2 - avg) * (v2 - avg) + (v3 - avg) * (v3 - avg);
            }

            float var1 = GetVar(sdrList[0], sdrList[1], sdrList[2]);
            float var2 = GetVar(sdrList[1], sdrList[2], sdrList[3]);
            float var3 = GetVar(sdrList[2], sdrList[3], sdrList[4]);

            float finalSdr = (var1 <= var2 && var1 <= var3) ? (sdrList[0] + sdrList[1] + sdrList[2]) / 3f :
                             (var2 <= var1 && var2 <= var3) ? (sdrList[1] + sdrList[2] + sdrList[3]) / 3f :
                                                              (sdrList[2] + sdrList[3] + sdrList[4]) / 3f;
            return finalSdr.ToString("F4");
        }

        private ValidationDisplayResult TryBuildValidationResult(
            IReadOnlyList<MeasureData> measurements,
            CalibrationParams innerCalibrationParams,
            CalibrationParams outerCalibrationParams)
        {
            var framePairs = new List<(EyeResult result, MeasureData data)>();

            foreach (var data in measurements)
            {
                if (!IsValidMeasurement(data))
                {
                    continue;
                }

                var innerResult = _eyeCalculator.Calculate(
                    data.InnerLongShaft, data.InnerShortShaft, data.LightSpotDistance, data.InnerAngle, innerCalibrationParams);

                var outerResult = _eyeCalculator.Calculate(
                    data.OuterLongShaft, data.OuterShortShaft, data.LightSpotDistance, data.OuterAngle, outerCalibrationParams);

                double fusedMajor = (innerResult.RMajor + outerResult.RMajor) / 2.0;
                double fusedMinor = (innerResult.RMinor + outerResult.RMinor) / 2.0;
                if (!IsFinitePositive(fusedMajor) || !IsFinitePositive(fusedMinor))
                {
                    continue;
                }

                framePairs.Add((new EyeResult { RMajor = fusedMajor, RMinor = fusedMinor }, data));
            }

            if (framePairs.Count < 3) return null;

            var sortedPairs = framePairs.OrderBy(p => (p.result.RMajor + p.result.RMinor) / 2.0).ToList();

            double GetResultVar((EyeResult result, MeasureData data) p1,
                               (EyeResult result, MeasureData data) p2,
                               (EyeResult result, MeasureData data) p3)
            {
                double m1 = (p1.result.RMajor + p1.result.RMinor) / 2.0;
                double m2 = (p2.result.RMajor + p2.result.RMinor) / 2.0;
                double m3 = (p3.result.RMajor + p3.result.RMinor) / 2.0;
                double avg = (m1 + m2 + m3) / 3.0;
                return (m1 - avg) * (m1 - avg) + (m2 - avg) * (m2 - avg) + (m3 - avg) * (m3 - avg);
            }

            var bestThree = sortedPairs.GetRange(0, 3);
            double minVar = GetResultVar(bestThree[0], bestThree[1], bestThree[2]);

            for (int i = 1; i <= sortedPairs.Count - 3; i++)
            {
                double curVar = GetResultVar(sortedPairs[i], sortedPairs[i + 1], sortedPairs[i + 2]);
                if (curVar < minVar)
                {
                    minVar = curVar;
                    bestThree = sortedPairs.GetRange(i, 3);
                }
            }

            double finalRMajor = bestThree.Average(p => p.result.RMajor);
            double finalRMinor = bestThree.Average(p => p.result.RMinor);

            return new ValidationDisplayResult
            {
                Ker1 = finalRMajor.ToString("F3"),
                Ker2 = finalRMinor.ToString("F3"),
                MeanRadiusOfCurvature = ((finalRMajor + finalRMinor) / 2.0).ToString("F3"),
                LongAxis = bestThree.Average(p => (double)p.data.OuterLongShaft).ToString("F2"),
                ShortAxis = bestThree.Average(p => (double)p.data.OuterShortShaft).ToString("F2"),
                AxisAngle = bestThree.Average(p => (double)p.data.OuterAngle).ToString("F2"),
                LightSpotDist = bestThree.Average(p => (double)p.data.LightSpotDistance).ToString("F2")
            };
        }

        private void ApplyValidationResult(ValidationDisplayResult validationResult)
        {
            KER1 = validationResult.Ker1;
            KER2 = validationResult.Ker2;
            MeanRadiusOfCurvature = validationResult.MeanRadiusOfCurvature;
            LongAxis = validationResult.LongAxis;
            ShortAxis = validationResult.ShortAxis;
            AxisAngle = validationResult.AxisAngle;
            LightSpotDist = validationResult.LightSpotDist;
        }

        private void BeginInvokeOnUiThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action);
        }

        #region 基础方法

        private void BindCalibrationParams()
        {
            if (_configs == null) return;

            ClearLocationValue = _configs.CorrectionCenterIndex.ToString("F4");

            // 1. 初始化内环参数
            _innerCalibrationParams = new CalibrationParams
            {
                FactoryInterpolationDictionary = _configs.FactoryInterpolationDictionary,
                PixelSize = 0.0075,
                ObliqueDotGain = 0.005,
                IsInnerRing = true
            };

            // 2. 初始化外环参数
            _outerCalibrationParams = new CalibrationParams
            {
                FactoryInterpolationDictionary = _configs.FactoryInterpolationDictionary,
                PixelSize = 0.0075,
                ObliqueDotGain = 0.005,
                IsInnerRing = false
            };
        }

        private void UpdateModeVisibility()
        {
            bool isConvexVerification = MainWindowViewModel.cornaelVerificationMockEyeType == CornaelVerificationMockEyeType.Convex;

            NextVisibility = isConvexVerification ? Visibility.Visible : Visibility.Hidden;
            ExportVisibility = isConvexVerification ? Visibility.Hidden : Visibility.Visible;
            NewParamsVisibility = isConvexVerification ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MessageInit()
        {
            Messenger.Default.Register<object>(this, "ExportVisibility", msg =>
            {
                UpdateModeVisibility();
            });

            Messenger.Default.Register<object>(this, CurvatureCalibrationUpdatedMessage, msg =>
            {
                LoadedFiles();
                RealTimeD = "0.000";
            });
        }

        private void RegisterEvents()
        {
            MainWindowViewModel.cameraStatue = true;
            StartVerificationProcessing();
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler += ShowIrisImage;
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler += Curvatureverify;
        }

        private void UnregisterEvents()
        {
            StopVerificationProcessing();
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler -= ShowIrisImage;
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler -= Curvatureverify;
        }

        private void LoadedFiles()
        {
            string configPath = Environment.CurrentDirectory + "\\CorrectFileConfigs.json";
            if (File.Exists(configPath))
            {
                _configs = JsonHelper.Load<CorrectFileConfigs>(configPath);
            }
            else
            {
                _configs = new CorrectFileConfigs();
            }

            // 进入验证或收到校正完成通知后，都重新绑定最新校正参数。
            BindCalibrationParams();
        }

        public override void Cleanup()
        {
            base.Cleanup();
            _filleWTWDataEventHandler -= FilledWTWDataCallback;
            UnregisterEvents();
        }

        #endregion

        #endregion


    }
}

