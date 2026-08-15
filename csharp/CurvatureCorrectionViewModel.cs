using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using LJ.Lus.Backend.Acquire.Actors.Camera;
using LJ.Lus.Backend.Acquire.Actors.DAQ;
using LJ.Lus.Backend.Acquire.Actors.MCU;
using LJ.Lus.Backend.Basic;
using LJ.Lus.Backend.Compute.Actors.Algorithm;
using LJ.Lus.Backend.Compute.ComputeDataModel;
using LJ.Lus.Backend.Model;
using LJ.Lus.Backend.Tooling.Actors.Compute;
using LJ.Lus.DotnetDevices.DAQ;
using LJ.Lus.DotnetDevices.MCU.DataModel;
using LJ.Lus.TechLibrary.Helpers;
using LJ.Lus.ToolingDashboard.FullMachine.Model;
using LJ.Lus.ToolingDashboard.FullMachine.Tool;
using LJ.Lus.ToolingDashboard.FullMachine.View;
using LJ.Lus.ToolingDashboard.Tool;
using LJ.Lus.ToolingDashboard.ViewModel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlotSignal = LJ.Lus.ToolingDashboard.FullMachine.Tool.PlotSignal;
using ReminderWindowOfChangeArtificalEye = LJ.Lus.ToolingDashboard.FullMachine.View.ReminderWindowOfChangeArtificalEye;

namespace LJ.Lus.ToolingDashboard.FullMachine.ViewModel
{
    public class CornealComputeDataEntitys
    {
        public float[] Image;
        public int index;
        public int index2;
        public int cycleNumber;
        public int cycleNumber2;
        public int BatchToken;
    }

    public class CurvatureCorrectionViewModel : ViewModelBase
    {
        private const string CurvatureCalibrationUpdatedMessage = "CurvatureCalibrationUpdated";
        private const int FramesPerPosition = 9;
        private const int MotorSettlingDelayMilliseconds = 1000;
        private const int IrisPreviewWidth = 640;
        private const int IrisPreviewHeight = 480;

        #region 字段

        private CorrectFileConfigs _configs;
        private EventHandler<List<CornealComputeDataEntitys>> _filleDataEventHandler;
        private EventHandler _computeFinishEventHandler;
        private readonly object _fillDataSyncRoot = new object();
        private readonly object _motorSettlingDelaySyncRoot = new object();
        private readonly object _correctionImageSaveSyncRoot = new object();
        private readonly object _previewFrameSyncRoot = new object();
        private float _mockEyeSize = 0.0f;
        private string _correctionImageExportWorkingFolder;
        private bool _prepareCompute = false;
        private bool _canFillData = false;
        private bool _computeOriginPosition = false;
        private int _acquirePositionNumber = 21; // 扫描位置数量。
        private int _acquirePrePositionNumber = FramesPerPosition;
        private int _totalNumber = 400; // 角膜信号总采样数。
        private int _motorComputeMoveStep;
        private CorneaCurvatureDistanceCalculator _corneaCurvatureDistanceCalculator;
        private FunctionMethod _functionMethod;
        private bool _saveImg = false;
        private bool _startCompute = false;
        private int _positonCount = 0;
        private int _correctionGeneration = 0;
        private ReminderWindowOfChangeArtificalEye _reminderWindowOfChangeArtificalEye;
        private CancellationTokenSource _motorSettlingDelayCts;
        private WriteableBitmap _irisPreviewBitmap;
        private readonly byte[] _irisPreviewBuffer = new byte[IrisPreviewWidth * IrisPreviewHeight];
        private float[] _latestPreviewFrame;
        private int _previewRefreshQueued = 0;
        /// <summary>
        /// 如果方向反了，可将该值改为 1。
        /// </summary>
        private float _motorDirection = -1;
        private CorrectMotorControl _motorControl;

        // 像元尺寸，单位为毫米。
        float PIXEL_SIZE = 0.0075f;

        private float CorrectionCenterIndex = 0;

        // 出厂插值字典，以曲率 R 作为键。
        private Dictionary<string, CalibrationNode> _factoryInterpolationDict = new Dictionary<string, CalibrationNode>();
        private const string MockEyeCorrectionDataFileName = "correction_data.txt";
        private const string AllMockEyeCorrectionDataFileName = "all_mock_eye_correction_data.txt";
        private readonly Dictionary<string, string> _mockEyeCorrectionDataTexts = new Dictionary<string, string>();

        // 光斑数据（X 轴：f_mm）。
        private List<double> _LightSpotDistance = new List<double>();

        // 外环数据（Y 轴：模拟眼光斑尺寸）。
        private List<double> _OuterLongAxis = new List<double>();
        private List<double> _OuterShortAxis = new List<double>();

        // 内环数据。
        private List<double> _InnerLongAxis = new List<double>();
        private List<double> _InnerShortAxis = new List<double>();

        #endregion

        #region ctor

        public CurvatureCorrectionViewModel(CorrectFileConfigs configs)
        {
            _isNeedNewImage = true;
            _configs = configs;
            _functionMethod = new FunctionMethod();
            _corneaCurvatureDistanceCalculator = new CorneaCurvatureDistanceCalculator();
            _corneaCurvatureDistanceCalculator.Init(_configs, _acquirePrePositionNumber);
            _motorComputeMoveStep = (int)(_totalNumber * 128 / 4.8f / (_acquirePositionNumber - 1));
            _computeFinishEventHandler += ComputeFinishCallBack;
            _filleDataEventHandler += FilledDataCallback;
            ToolingComputeActor.Instance.ShowCurvatureClearValueComputeEventHandler += CurvatureClearValueComputeCallBack;
        }


        #endregion

        #region 属性

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

        private string clearValue;
        public string ClearValue
        {
            get { return clearValue; }
            set
            {

                clearValue = value;
                RaisePropertyChanged(nameof(ClearValue));
            }
        }

        private CorrectMotorControl _selectedMotorTypes;
        public CorrectMotorControl SelectedMotorTypes
        {
            get { return _selectedMotorTypes; }
            set
            {
                Set(ref _selectedMotorTypes, value);
                _motorControl = _selectedMotorTypes;
            }
        }

        private List<CorrectMotorControl> _motorTypes;
        public List<CorrectMotorControl> MotorTypes
        {
            get
            {
                if (_motorTypes == null)
                {
                    _motorTypes = new List<CorrectMotorControl>()
                    {
                        CorrectMotorControl.A80_1605_200,
                        CorrectMotorControl.A80_1610_200,
                    };

                    SelectedMotorTypes = CorrectMotorControl.A80_1605_200;
                    RaisePropertyChanged(nameof(SelectedMotorTypes));
                }
                return _motorTypes;
            }
            set
            {
                Set(ref _motorTypes, value);
            }
        }

        private float _motorDistance = 0.1f;
        public float MotorDistance
        {
            get { return _motorDistance; }
            set { Set(ref _motorDistance, value); }
        }

        private bool _canCaculate = false;
        public bool CanCaculate
        {
            get { return _canCaculate; }
            set { Set(ref _canCaculate, value); }
        }

        private int _currentTime = 0;
        public int CurrentTime
        {
            get { return _currentTime; }
            set
            {
                _currentTime = value;
                RaisePropertyChanged(nameof(CurrentTime));
            }
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


        private Visibility _singalCorrectMode = Visibility.Visible;
        public Visibility SingalCorrectMode
        {
            get { return _singalCorrectMode; }
            set
            {
                _singalCorrectMode = value;
                RaisePropertyChanged(nameof(SingalCorrectMode));
            }
        }

        private Visibility _doubleCorrectMode = Visibility.Hidden;
        public Visibility DoubleCorrectMode
        {
            get { return _doubleCorrectMode; }
            set
            {
                _doubleCorrectMode = value;
                RaisePropertyChanged(nameof(DoubleCorrectMode));
            }
        }

        private bool _firstStepBtnIsEnabeld;
        public bool FirstStepBtnIsEnabeld
        {
            get { return _firstStepBtnIsEnabeld; }
            set { Set(ref _firstStepBtnIsEnabeld, value); }
        }

        private bool _secondStepBtnIsEnabeld;
        public bool SecondStepBtnIsEnabeld
        {
            get { return _secondStepBtnIsEnabeld; }
            set { Set(ref _secondStepBtnIsEnabeld, value); }
        }

        private bool _lastStepBtnIsEnabeld;
        public bool LastStepBtnIsEnabeld
        {
            get { return _lastStepBtnIsEnabeld; }
            set { Set(ref _lastStepBtnIsEnabeld, value); }
        }

        private bool? _isFirstPass = null;
        public bool? IsFirstPass
        {
            get { return _isFirstPass; }
            set { Set(ref _isFirstPass, value); }
        }

        private bool? _isSecondPass = null;
        public bool? IsSecondPass
        {
            get { return _isSecondPass; }
            set { Set(ref _isSecondPass, value); }
        }

        private bool? _isLastPass = null;
        public bool? IsLastPass
        {
            get { return _isLastPass; }
            set { Set(ref _isLastPass, value); }
        }

        private bool? _isCloselMode;
        public bool? IsCloselMode
        {
            get { return _isCloselMode; }
            set
            {
                Set(ref _isCloselMode, value);
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
                        RegisterEvents();
                    }
                }
            }
        }

        #endregion

        #region 命令

        private RelayCommand<string> _computeCommand;
        public RelayCommand<string> ComputeCommand
        {
            get
            {
                if (_computeCommand == null) return new RelayCommand<string>((str) => ExcuteComputeCommand(str));
                return _computeCommand;
            }
            set { _computeCommand = value; }
        }


        private RelayCommand _saveLocationCommand;
        public RelayCommand SaveLocationCommand
        {
            get
            {
                if (_saveLocationCommand == null) return new RelayCommand(() => ExcuteSaveLocationCommand());
                return _saveLocationCommand;
            }
            set { _saveLocationCommand = value; }
        }

        private RelayCommand _reCaculatePresentMockEyeCommand;
        public RelayCommand ReCaculatePresentMockEyeCommand
        {
            get
            {
                if (_reCaculatePresentMockEyeCommand == null) return new RelayCommand(() => ExcuteReCaculatePresentMockEyeCommand());
                return _reCaculatePresentMockEyeCommand;
            }
            set { _reCaculatePresentMockEyeCommand = value; }
        }

        private RelayCommand _reCaculateCommand;
        public RelayCommand ReCaculateCommand
        {
            get
            {
                if (_reCaculateCommand == null) return new RelayCommand(() => ExcuteReCaculateCommand());
                return _reCaculateCommand;
            }
            set { _reCaculateCommand = value; }
        }

        private RelayCommand _nextStepCommand;
        public RelayCommand NextStepCommand =>
            _nextStepCommand ?? (_nextStepCommand = new RelayCommand(ExcuteNextStep));


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

        private RelayCommand _saveImgCommand;
        public RelayCommand SaveImgCommand =>
            _saveImgCommand ?? (_saveImgCommand = new RelayCommand(ExcuteSaveImg));

        #endregion

        #region 方法

        #region 命令处理方法

        private void ExcuteContinueMoveStop()
        {
            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(1, _motorControl));
        }

        private void ExcuteMotorLeft() // 向回退方向移动。
        {

            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(0.3f * _motorDirection, _motorControl));
        }

        private void ExcuteLeftContinueMoveStart()
        {

            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(0.3f * _motorDirection * 1000, _motorControl));

        }

        private void ExcuteMotorRight() // 向校正步进方向移动。
        {

            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(-0.3f * _motorDirection, _motorControl));

        }

        private void ExcuteRightContinueMoveStart()
        {

            MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(-0.3f * _motorDirection * 1000, _motorControl));

        }

        private void ExcuteSaveImg()
        {
            if (!_saveImg) _saveImg = true;
        }

        private void ExcuteComputeCommand(string str)
        {
            ResetCurrentCorrectionAcquisitionState();
            double targetValue = Convert.ToDouble(str, CultureInfo.InvariantCulture);

            if (Math.Abs(targetValue - 6.668) < 0.0001)
            {
                _mockEyeSize = 6.668f;
                _prepareCompute = true;
            }
            else if (Math.Abs(targetValue - 7.943) < 0.0001)
            {
                _mockEyeSize = 7.943f;
                _prepareCompute = true;

            }
            else if (Math.Abs(targetValue - 9.320) < 0.0001)
            {
                _mockEyeSize = 9.320f;
                _prepareCompute = true;
            }
            else
            {
                return;
            }

            InvalidateCalibrationResultsFrom(_mockEyeSize);
            ResetPassIndicatorsFrom(_mockEyeSize);
            ResetMockEyeCorrectionImageFolder(_mockEyeSize);
            _canFillData = true;
        }

        private void ExcuteSaveLocationCommand()
        {
            ResetCurrentCorrectionAcquisitionState();
            CurrentTime = 0;
            ResetCorrectionDataExportFiles();
            _canFillData = true;
            IsFirstPass = null;   // 第一只模拟眼。
            IsSecondPass = null;  // 第二只模拟眼。
            IsLastPass = null;    // 第三只模拟眼。
            _computeOriginPosition = true;  // 先计算初始位置。
            _mockEyeSize = 7.943f; // 从第一只模拟眼开始。
        }

        private void ExcuteReCaculatePresentMockEyeCommand()
        {
            ResetCurrentCorrectionAcquisitionState();
            InvalidateCalibrationResultsFrom(_mockEyeSize);
            ResetPassIndicatorsFrom(_mockEyeSize);
            switch (_mockEyeSize)
            {
                case 6.668f:
                    FirstStepBtnIsEnabeld = false;
                    SecondStepBtnIsEnabeld = true;
                    LastStepBtnIsEnabeld = false;
                    break;
                case 7.943f:
                    FirstStepBtnIsEnabeld = true;
                    SecondStepBtnIsEnabeld = false;
                    LastStepBtnIsEnabeld = false;
                    break;
                case 9.320f:
                    FirstStepBtnIsEnabeld = false;
                    SecondStepBtnIsEnabeld = false;
                    LastStepBtnIsEnabeld = true;
                    break;
            }
        }

        private void ExcuteReCaculateCommand()
        {
            ResetCurrentCorrectionAcquisitionState();
            ResetCorrectionDataExportFiles();
            IsFirstPass = null;
            IsSecondPass = null;
            IsLastPass = null;
            FirstStepBtnIsEnabeld = false;
            SecondStepBtnIsEnabeld = false;
            LastStepBtnIsEnabeld = false;
        }

        private void ExcuteNextStep()
        {
            if (!MainWindowViewModel.cameraStatue)
            {
                if (IsLastPass != true)
                {
                    ReminderIsCloseWindow win = new ReminderIsCloseWindow("模拟眼校正未完成，是否继续下一步？");
                    if (!win.ShowDialog() ?? false)
                    {
                        return;
                    }
                }
                IsCloselMode = true;
                IsFirstPass = null;   // 第一只模拟眼。
                IsSecondPass = null;  // 第二只模拟眼。
                IsLastPass = null;    // 第三只模拟眼。
                Messenger.Default.Send<object>(null, "ExportVisibility");
                Messenger.Default.Send(new Message.Switch_InterfaceMessage.CurvatureVerification());
            }
            else
            {
                Messenger.Default.Send<object>(null, "ReminderCloseCamera");
            }
        }

        #endregion

        int count = 0;
        List<CornealComputeDataEntitys> computeDataEntities = new List<CornealComputeDataEntitys>(FramesPerPosition);

        double[] outer = new double[12];
        List<float> tempsdr_list = new List<float>(FramesPerPosition);
        List<float> tempclearvalue_list = new List<float>(FramesPerPosition);
        int countsdr = 0;
        private void CurvatureClearValueComputeCallBack(object sender, float[] e)
        {
            if (IsFinitePositive(e[1])) tempsdr_list.Add(e[1]);
            if (IsFinitePositive(e[0])) tempclearvalue_list.Add(e[0]);
            countsdr++;
            if (countsdr == FramesPerPosition)
            {
                SDR = GetStablePositiveMean(tempsdr_list.Select(value => (double)value).ToArray()).ToString("F4"); // 点径比。
                ClearValue = GetStablePositiveMean(tempclearvalue_list.Select(value => (double)value).ToArray()).ToString("F3"); // 清晰位置值。
                tempsdr_list.Clear();
                tempclearvalue_list.Clear();
                countsdr = 0;
            }

            _isNeedNewImage = true;
        }
        bool _isNeedNewImage = false;
        private void CurvatureCorrect(object sender, float[] image)
        {
            if (_isNeedNewImage)
            {
                _isNeedNewImage = false;
                MessageDispatcher.Send(new CornealCurvatureClearValueComputeToolingComputeActor(image));
            }

            List<CornealComputeDataEntitys> filledDataBatch = null;
            if (_canFillData)
            {
                lock (_fillDataSyncRoot)
                {
                    if (_canFillData)
                    {
                        CornealComputeDataEntitys computeDataEntitys = new CornealComputeDataEntitys();

                        computeDataEntitys.Image = image;
                        computeDataEntitys.BatchToken = Volatile.Read(ref _correctionGeneration);
                        computeDataEntities.Add(computeDataEntitys);
                        CurrentTime = count + 1;
                        count++;

                        if (count >= _acquirePrePositionNumber)
                        {
                            _canFillData = false;
                            count = 0;
                            filledDataBatch = computeDataEntities;
                            computeDataEntities = new List<CornealComputeDataEntitys>(_acquirePrePositionNumber);
                        }
                    }
                }

                if (filledDataBatch != null)
                {
                    _filleDataEventHandler.BeginInvoke(this, filledDataBatch, null, null);
                }
            }
        }

        // 为 FilledDataCallback 复用缓冲区，减少频繁分配。
        private double[] _innerSemiLongShaftPool = new double[FramesPerPosition];
        private double[] _innerSemiShortShaftPool = new double[FramesPerPosition];
        private double[] _outerSemiLongShaftPool = new double[FramesPerPosition];
        private double[] _outerSemiShortShaftPool = new double[FramesPerPosition];
        private double[] _indexesPool = new double[FramesPerPosition];
        private double[] _sdrPool = new double[FramesPerPosition];
        private double[] _innerRingPool = new double[5];
        private double[] _outerRingPool = new double[6];

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private double GetStablePositiveMean(double[] values)
        {
            if (values == null)
            {
                return 0;
            }

            double[] validValues = values.Where(IsFinitePositive).ToArray();
            if (validValues.Length == 0)
            {
                return 0;
            }

            return _functionMethod.GetFrontIndexMean(validValues);
        }

        private void ClearCurrentCorrectionSamples()
        {
            _InnerLongAxis.Clear();
            _InnerShortAxis.Clear();
            _OuterLongAxis.Clear();
            _OuterShortAxis.Clear();
            _LightSpotDistance.Clear();
        }

        private void ResetCurrentCorrectionAcquisitionState()
        {
            CancelMotorSettlingDelay();
            Interlocked.Increment(ref _correctionGeneration);

            lock (_fillDataSyncRoot)
            {
                computeDataEntities = new List<CornealComputeDataEntitys>(_acquirePrePositionNumber);
                count = 0;
                _canFillData = false;
            }

            _startCompute = false;
            _prepareCompute = false;
            _positonCount = 0;
            CurrentTime = 0;
            ClearCurrentCorrectionSamples();
        }

        private static bool IsSameMockEye(float mockEyeSize, float expectedMockEyeSize)
        {
            return Math.Abs(mockEyeSize - expectedMockEyeSize) < 0.001f;
        }

        private void ResetPassIndicatorsFrom(float mockEyeSize)
        {
            if (IsSameMockEye(mockEyeSize, 7.943f))
            {
                IsFirstPass = null;
                IsSecondPass = null;
                IsLastPass = null;
                return;
            }

            if (IsSameMockEye(mockEyeSize, 6.668f))
            {
                IsSecondPass = null;
                IsLastPass = null;
                return;
            }

            if (IsSameMockEye(mockEyeSize, 9.320f))
            {
                IsLastPass = null;
            }
        }

        private float[] GetMockEyesToInvalidate(float mockEyeSize)
        {
            if (IsSameMockEye(mockEyeSize, 7.943f))
            {
                return new[] { 7.943f, 6.668f, 9.320f };
            }

            if (IsSameMockEye(mockEyeSize, 6.668f))
            {
                return new[] { 6.668f, 9.320f };
            }

            if (IsSameMockEye(mockEyeSize, 9.320f))
            {
                return new[] { 9.320f };
            }

            return new[] { mockEyeSize };
        }

        private void InvalidateCalibrationResultsFrom(float mockEyeSize)
        {
            if (mockEyeSize <= 0)
            {
                return;
            }

            float[] mockEyesToInvalidate = GetMockEyesToInvalidate(mockEyeSize);
            lock (_correctionImageSaveSyncRoot)
            {
                foreach (float invalidMockEyeSize in mockEyesToInvalidate)
                {
                    string mockEyeFolderName = GetMockEyeFolderName(invalidMockEyeSize);
                    _factoryInterpolationDict.Remove(mockEyeFolderName);
                    _mockEyeCorrectionDataTexts.Remove(mockEyeFolderName);

                    if (!string.IsNullOrWhiteSpace(_correctionImageExportWorkingFolder) && Directory.Exists(_correctionImageExportWorkingFolder))
                    {
                        string mockEyeFolderPath = Path.Combine(_correctionImageExportWorkingFolder, mockEyeFolderName);
                        if (Directory.Exists(mockEyeFolderPath))
                        {
                            Directory.Delete(mockEyeFolderPath, true);
                        }

                        Directory.CreateDirectory(mockEyeFolderPath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(_correctionImageExportWorkingFolder) && Directory.Exists(_correctionImageExportWorkingFolder))
                {
                    string combinedFilePath = Path.Combine(_correctionImageExportWorkingFolder, AllMockEyeCorrectionDataFileName);
                    if (File.Exists(combinedFilePath))
                    {
                        File.Delete(combinedFilePath);
                    }
                }
            }
        }

        private static double EnsureFinitePositiveCalibrationValue(double value, string valueName)
        {
            if (!IsFinitePositive(value))
            {
                throw new InvalidOperationException($"{valueName} is invalid and calibration parameters cannot be generated for the current standard eye.");
            }

            return value;
        }

        private bool IsCurrentCorrectionGeneration(int batchToken)
        {
            return batchToken == Volatile.Read(ref _correctionGeneration);
        }

        private void ShowCorrectionCalibrationError(string message)
        {
            Action showMessage = () => MessageBox.Show(message, "Curvature Correction");
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                showMessage();
                return;
            }

            dispatcher.BeginInvoke(showMessage);
        }

        private double[] FitRobustLinearFinite(double[] x, double[] y, string seriesName)
        {
            List<double> validX = new List<double>();
            List<double> validY = new List<double>();

            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                if (IsFinite(x[i]) && IsFinite(y[i]))
                {
                    validX.Add(x[i]);
                    validY.Add(y[i]);
                }
            }

            if (validX.Count < 2)
            {
                throw new InvalidOperationException($"{seriesName} does not have enough valid points to complete calibration for the current standard eye.");
            }

            return _functionMethod.RobustPolynomialFit(validX.ToArray(), validY.ToArray(), 1, 1.5);
        }

        private double[] FitRobustLinearFinite(double[] x, double[] primary, string seriesName, params double[][] requiredFiniteSeries)
        {
            List<double> validX = new List<double>();
            List<double> validY = new List<double>();

            int count = Math.Min(x.Length, primary.Length);
            foreach (var series in requiredFiniteSeries)
            {
                count = Math.Min(count, series.Length);
            }

            for (int i = 0; i < count; i++)
            {
                bool allValid = IsFinite(x[i]) && IsFinite(primary[i]);
                if (allValid)
                {
                    foreach (var series in requiredFiniteSeries)
                    {
                        if (!IsFinite(series[i]))
                        {
                            allValid = false;
                            break;
                        }
                    }
                }

                if (allValid)
                {
                    validX.Add(x[i]);
                    validY.Add(primary[i]);
                }
            }

            if (validX.Count < 2)
            {
                throw new InvalidOperationException($"{seriesName} does not have enough valid points to complete calibration for the current standard eye.");
            }

            return _functionMethod.RobustPolynomialFit(validX.ToArray(), validY.ToArray(), 1, 1.5);
        }

        private double[] FitRobustLinearPositive(double[] x, double[] y, string seriesName)
        {
            List<double> validX = new List<double>();
            List<double> validY = new List<double>();

            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                if (IsFinite(x[i]))
                {
                    if (IsFinitePositive(y[i]))
                    {
                        validX.Add(x[i]);
                        validY.Add(y[i]);
                    }
                }
            }

            if (validX.Count >= 2)
            {
                return _functionMethod.RobustPolynomialFit(validX.ToArray(), validY.ToArray(), 1, 1.5);
            }

            Console.WriteLine($"{seriesName} does not have enough positive points, falling back to finite-point robust fitting.");
            return FitRobustLinearFinite(x, y, seriesName);
        }

        private double[] FitRobustLinearPositive(double[] x, double[] primary, string seriesName, params double[][] requiredPositiveSeries)
        {
            List<double> validX = new List<double>();
            List<double> validY = new List<double>();

            int count = Math.Min(x.Length, primary.Length);
            foreach (var series in requiredPositiveSeries)
            {
                count = Math.Min(count, series.Length);
            }

            for (int i = 0; i < count; i++)
            {
                bool allValid = IsFinite(x[i]) && IsFinitePositive(primary[i]);
                if (allValid)
                {
                    foreach (var series in requiredPositiveSeries)
                    {
                        if (!IsFinitePositive(series[i]))
                        {
                            allValid = false;
                            break;
                        }
                    }
                }

                if (allValid)
                {
                    validX.Add(x[i]);
                    validY.Add(primary[i]);
                }
            }

            if (validX.Count >= 2)
            {
                return _functionMethod.RobustPolynomialFit(validX.ToArray(), validY.ToArray(), 1, 1.5);
            }

            Console.WriteLine($"{seriesName} does not have enough positive points, falling back to finite-point robust fitting.");
            return FitRobustLinearFinite(x, primary, seriesName, requiredPositiveSeries);
        }

        private void FilledDataCallback(object sender, List<CornealComputeDataEntitys> computeDataEntities)
        {
            int batchToken = 0;
            try
            {
                if (computeDataEntities == null || computeDataEntities.Count == 0)
                {
                    return;
                }

                batchToken = computeDataEntities[0].BatchToken;
                if (!IsCurrentCorrectionGeneration(batchToken))
                {
                    return;
                }

                if (computeDataEntities.Count != _acquirePrePositionNumber)
                {
                    Console.WriteLine($"FilledDataCallback received {computeDataEntities.Count} frames, expected {_acquirePrePositionNumber}.");
                }

                // 当前帧批次使用的临时数组，优先复用池化缓冲区。
                int sampleCount = computeDataEntities.Count;
                double[] inner_semi_longShaft = sampleCount == _innerSemiLongShaftPool.Length ? _innerSemiLongShaftPool : new double[sampleCount];
                double[] inner_semi_shortShaft = sampleCount == _innerSemiShortShaftPool.Length ? _innerSemiShortShaftPool : new double[sampleCount];
                double[] outer_semi_longShaft = sampleCount == _outerSemiLongShaftPool.Length ? _outerSemiLongShaftPool : new double[sampleCount];
                double[] outer_semi_shortShaft = sampleCount == _outerSemiShortShaftPool.Length ? _outerSemiShortShaftPool : new double[sampleCount];
                double[] Indexes = sampleCount == _indexesPool.Length ? _indexesPool : new double[sampleCount];
                double[] sdr = sampleCount == _sdrPool.Length ? _sdrPool : new double[sampleCount];
                float currentMockEyeSize = _mockEyeSize;
                bool isPrepareStage = _prepareCompute;
                bool isScanStage = _startCompute;
                int currentPositionIndex = _positonCount;
                int count = 0;

                foreach (var item in computeDataEntities)
                {
                    double[] innerRing = _innerRingPool;
                    double[] outerRing = _outerRingPool;


                    // 调用曲率计算 Actor。
                    MessageDispatcher.Send(new CornealCurvatureComputeToolingComputeActor(item.Image, innerRing, outerRing));

                    // 填充内环数据。
                    inner_semi_longShaft[count] = innerRing[2];
                    inner_semi_shortShaft[count] = innerRing[3];

                    // 填充外环数据。
                    double outerLongAxis = outerRing[2];
                    double outerShortAxis = outerRing[3];
                    outer_semi_longShaft[count] = outerLongAxis;
                    outer_semi_shortShaft[count] = outerShortAxis;

                    // 公共数据。
                    double lightSpotDistance = outerRing[5];
                    Indexes[count] = lightSpotDistance; // 光斑距离，单位为像素。

                    // 基于外环数据计算 SDR。
                    sdr[count] = (IsFinitePositive(outerLongAxis) && IsFinitePositive(lightSpotDistance))
                        ? lightSpotDistance / (outerLongAxis * 2)
                        : 0;
                    count++;


                }

                if (!IsCurrentCorrectionGeneration(batchToken))
                {
                    return;
                }

                if (isScanStage)
                {
                    SaveCorrectionPositionImage(currentMockEyeSize, currentPositionIndex, computeDataEntities);
                }

                // 1. 查找并记录系统初始清晰位置与标准 SDR。
                if (_computeOriginPosition)
                {
                    double[] validSdr = sdr.Where(IsFinitePositive).ToArray();
                    if (validSdr.Length == 0)
                    {
                        Console.WriteLine("No valid SDR was found for the origin position; waiting for the next image group.");
                        _computeOriginPosition = true;
                        _canFillData = true;
                        CurrentTime = 0;
                        return;
                    }

                    _computeOriginPosition = false;

                    CorrectionCenterIndex = (float)_functionMethod.GetFrontIndexMean(validSdr);
                    _configs.CorrectionCenterIndex = CorrectionCenterIndex;
                    _configs.StandardTargetSDR = (float)validSdr.Average();
                    ClearLocationValue = CorrectionCenterIndex.ToString();

                    CanCaculate = true;
                    FirstStepBtnIsEnabeld = true;
                    CurrentTime = 0;
                    return;
                }

                // ==========================================================
                // 2. 准备阶段：从当前清晰位置退回到扫描起点。
                // 260331 标定不再锁定单点零位，而是依赖全扫描线性回归。
                // ==========================================================
                if (_prepareCompute)
                {
                    if (MainWindowViewModel.correctionMethod == CorrectionMethod.Tooling)
                    {
                        MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(MotorDistance * _motorDirection * (_acquirePositionNumber - 1) / 2f, _motorControl));
                    }
                    else
                    {
                        MessageDispatcher.Send(new Motor3dOneAxisFiniteMCUActor(RotationAxis.Y, (int)(_motorComputeMoveStep * _motorDirection * (_acquirePositionNumber - 1) / 2)));
                    }

                    return;
                }

                // 3. 电机扫描过程中的数据采集阶段。
                _InnerLongAxis.Add(GetStablePositiveMean(inner_semi_longShaft));
                _InnerShortAxis.Add(GetStablePositiveMean(inner_semi_shortShaft));

                _OuterLongAxis.Add(GetStablePositiveMean(outer_semi_longShaft));
                _OuterShortAxis.Add(GetStablePositiveMean(outer_semi_shortShaft));

                _LightSpotDistance.Add(GetStablePositiveMean(Indexes));

                // ==========================================================
                // 4. 全部扫描位置采集完成后，执行完整的 260331 标定流程。
                // ==========================================================
                if (_LightSpotDistance.Count == _acquirePositionNumber)
                {
                    // 动态构造 D_array，例如 21 个点，范围为 [-1.0, 1.0]。
                    double[] D_array = new double[_acquirePositionNumber];
                    double step = 2.0 / (_acquirePositionNumber - 1);
                    for (int i = 0; i < _acquirePositionNumber; i++)
                    {
                        D_array[i] = -1.0 + i * step;
                    }

                    // 将扫描得到的尺寸从像素转换为毫米（px -> mm）。
                    double[] f_mm = _LightSpotDistance.Select(x => x * PIXEL_SIZE).ToArray();
                    double[] dL_in_mm = _InnerLongAxis.Select(x => 2 * x * PIXEL_SIZE).ToArray();
                    double[] dS_in_mm = _InnerShortAxis.Select(x => 2 * x * PIXEL_SIZE).ToArray();
                    double[] dL_out_mm = _OuterLongAxis.Select(x => 2 * x * PIXEL_SIZE).ToArray();
                    double[] dS_out_mm = _OuterShortAxis.Select(x => 2 * x * PIXEL_SIZE).ToArray();

                    // 光斑距离只与当前扫描位置有关，因此内外环共用同一条 f(D) 直线。
                    double[] pf = FitRobustLinearPositive(D_array, f_mm, "光点距离");
                    double f0 = EnsureFinitePositiveCalibrationValue(pf[1], "f0");

                    CalibrationNode node = new CalibrationNode
                    {
                        R = currentMockEyeSize,
                        f0 = (float)f0
                    };

                    void CalibrateRing(double[] dL_mm, double[] dS_mm, bool isInner)
                    {
                        // 260331：分别对 dL 和 dS 做全点一次回归，得到零位 d0 和位移灵敏度 k。
                        double[] pL = FitRobustLinearPositive(D_array, dL_mm, isInner ? "内环长轴" : "外环长轴", dS_mm, f_mm);
                        double[] pS = FitRobustLinearPositive(D_array, dS_mm, isInner ? "内环短轴" : "外环短轴", dL_mm, f_mm);

                        string ringName = isInner ? "InnerRing" : "OuterRing";
                        double d0_L = EnsureFinitePositiveCalibrationValue(pL[1], $"{ringName} d0_L");
                        double d0_S = EnsureFinitePositiveCalibrationValue(pS[1], $"{ringName} d0_S");
                        double k_L = pL[0];
                        double k_S = pS[0];
                        double Cf_L = EnsureFinitePositiveCalibrationValue(d0_L / f0, $"{ringName} Cf_L");
                        double Cf_S = EnsureFinitePositiveCalibrationValue(d0_S / f0, $"{ringName} Cf_S");
                        double C_L = EnsureFinitePositiveCalibrationValue(d0_L / currentMockEyeSize, $"{ringName} C_L");
                        double C_S = EnsureFinitePositiveCalibrationValue(d0_S / currentMockEyeSize, $"{ringName} C_S");

                        // 复用 CalibrationNode 的旧字段保存 260331 参数，避免修改底层模型结构。
                        // C=d0_L, scale_L=d0_S, scale_S=Cf_L, kL2=Cf_S, kL1=k_L, kS2=k_S, kS1=C_L, kk2=C_S
                        if (isInner)
                        {
                            node.Inner_C = (float)d0_L;
                            node.Inner_scale_L = (float)d0_S;
                            node.Inner_scale_S = (float)Cf_L;
                            node.Inner_kL2 = (float)Cf_S;
                            node.Inner_kL1 = (float)k_L;
                            node.Inner_kS2 = (float)k_S;
                            node.Inner_kS1 = (float)C_L;
                            node.Inner_kk2 = (float)C_S;
                            node.Inner_kk1 = EyeCalculator.CalibrationModelMarker260331;
                            node.Inner_res_coef_L = 0f;
                            node.Inner_res_coef_S = 0f;
                        }
                        else
                        {
                            node.Outer_C = (float)d0_L;
                            node.Outer_scale_L = (float)d0_S;
                            node.Outer_scale_S = (float)Cf_L;
                            node.Outer_kL2 = (float)Cf_S;
                            node.Outer_kL1 = (float)k_L;
                            node.Outer_kS2 = (float)k_S;
                            node.Outer_kS1 = (float)C_L;
                            node.Outer_kk2 = (float)C_S;
                            node.Outer_kk1 = EyeCalculator.CalibrationModelMarker260331;
                            node.Outer_res_coef_L = 0f;
                            node.Outer_res_coef_S = 0f;
                        }
                    }

                    // 分别标定内环和外环。
                    CalibrateRing(dL_in_mm, dS_in_mm, true);
                    CalibrateRing(dL_out_mm, dS_out_mm, false);

                    // 将结果写入插值字典。
                    if (!IsCurrentCorrectionGeneration(batchToken))
                    {
                        return;
                    }

                    _factoryInterpolationDict[currentMockEyeSize.ToString("F3")] = node;
                    SaveCurrentMockEyeCorrectionData(currentMockEyeSize);

                    // 最后一只模拟眼完成后，落盘保存整套标定结果。
                    if (Math.Abs(currentMockEyeSize - 9.320f) < 0.001f)
                    {
                        if (_configs.FactoryInterpolationDictionary == null)
                        {
                            _configs.FactoryInterpolationDictionary = new Dictionary<string, CalibrationNode>();
                        }
                        else
                        {
                            _configs.FactoryInterpolationDictionary.Clear();
                        }

                        foreach (var pair in _factoryInterpolationDict.OrderBy(pair => pair.Key))
                        {
                            _configs.FactoryInterpolationDictionary[pair.Key] = pair.Value;
                        }

                        SaveCombinedCorrectionData();
                        _canFillData = false;
                        string path = Environment.CurrentDirectory + "\\CorrectFileConfigs.json";
                        JsonHelper.Save(_configs, path);
                        Messenger.Default.Send<object>(null, CurvatureCalibrationUpdatedMessage);
                        TryFinalizeCorrectionImageExportFolder();
                    }

                    CanCaculate = false;

                    // 清空缓冲，为下一只模拟眼做准备。
                    ClearCurrentCorrectionSamples();
                }

                _computeFinishEventHandler.Invoke(this, null);
            }
            catch (Exception ex)
            {
                if (!IsCurrentCorrectionGeneration(batchToken))
                {
                    return;
                }

                Console.WriteLine($"Curvature correction calibration failed: {ex.Message}");
                ResetCurrentCorrectionAcquisitionState();
                ShowCorrectionCalibrationError($"Curvature correction failed: {ex.Message}");
            }
            finally
            {
                //// 如果后续重新启用 GDI 清理，可在这里释放图像资源。
                //foreach (var item in computeDataEntities)
                //{
                //    item.Image?.Dispose();
                //}
            }
        }


        private void ComputeFinishCallBack(object sender, EventArgs e)
        {
            if (_startCompute)
            {
                if (_positonCount == _acquirePositionNumber - 1)
                {
                    _startCompute = false;
                    _positonCount = 0;

                    if (MainWindowViewModel.correctionMethod == CorrectionMethod.Tooling)
                    {
                        MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(MotorDistance * _motorDirection * (_acquirePositionNumber - 1) / 2f, _motorControl));
                    }
                    else
                    {
                        MessageDispatcher.Send(new Motor3dOneAxisFiniteMCUActor(RotationAxis.Y, (int)(_motorComputeMoveStep * _motorDirection * (_acquirePositionNumber - 1) / 2)));
                    }

                    switch (_mockEyeSize)
                    {
                        case 6.668f:
                            Application.Current.Dispatcher.Invoke(new Action(() =>
                            {
                                CurrentTime = 0;
                                IsSecondPass = true;
                                FirstStepBtnIsEnabeld = false;
                                SecondStepBtnIsEnabeld = false;
                                LastStepBtnIsEnabeld = true;
                                _reminderWindowOfChangeArtificalEye = new ReminderWindowOfChangeArtificalEye();
                                _reminderWindowOfChangeArtificalEye.ShowDialog();
                            }));

                            break;
                        case 7.943f:
                            Application.Current.Dispatcher.Invoke(new Action(() =>
                            {
                                CurrentTime = 0;
                                IsFirstPass = true;
                                FirstStepBtnIsEnabeld = false;
                                SecondStepBtnIsEnabeld = true;
                                LastStepBtnIsEnabeld = false;
                                _reminderWindowOfChangeArtificalEye = new ReminderWindowOfChangeArtificalEye();
                                _reminderWindowOfChangeArtificalEye.ShowDialog();
                            }));
                            break;
                        case 9.320f:
                            Application.Current.Dispatcher.Invoke(new Action(() =>
                            {
                                CurrentTime = 0;
                                IsLastPass = true;
                                FirstStepBtnIsEnabeld = false;
                                SecondStepBtnIsEnabeld = false;
                                LastStepBtnIsEnabeld = false;
                            }));
                            break;
                        default:
                            break;
                    }
                    return;
                }

                if (MainWindowViewModel.correctionMethod == CorrectionMethod.Tooling)
                {
                    MessageDispatcher.Send(new CorrectionMotorMoveToMCUActor(-MotorDistance * _motorDirection, _motorControl));
                }
                else
                {
                    MessageDispatcher.Send(new Motor3dOneAxisFiniteMCUActor(RotationAxis.Y, (int)(-_motorComputeMoveStep * _motorDirection)));
                }

                _positonCount++;
            }
        }

        /// <summary>
        /// 实时接收图像数据。
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
                    // 确保目标目录存在。
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    // 查找下一个可用文件名。
                    int fileNumber = 0;
                    string fullPath;
                    do
                    {
                        fullPath = Path.Combine(directoryPath, $"{fileNumber}.png");
                        fileNumber++;
                    } while (File.Exists(fullPath));

                    Bitmap bitmap = BitmapHelpers.ToGrayBitmap(image, 640, 480);
                    // 以 PNG 格式保存图像。
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

        private void ResetMockEyeCorrectionImageFolder(float mockEyeSize)
        {
            if (mockEyeSize <= 0)
            {
                return;
            }

            lock (_correctionImageSaveSyncRoot)
            {
                string rootFolder = EnsureCorrectionImageExportWorkingFolder();
                string mockEyeFolderName = GetMockEyeFolderName(mockEyeSize);
                string mockEyeFolderPath = Path.Combine(rootFolder, mockEyeFolderName);
                _mockEyeCorrectionDataTexts.Remove(mockEyeFolderName);

                if (Directory.Exists(mockEyeFolderPath))
                {
                    Directory.Delete(mockEyeFolderPath, true);
                }

                Directory.CreateDirectory(mockEyeFolderPath);
            }
        }

        private void ResetCorrectionDataExportFiles()
        {
            _factoryInterpolationDict.Clear();
            _mockEyeCorrectionDataTexts.Clear();

            lock (_correctionImageSaveSyncRoot)
            {
                if (string.IsNullOrWhiteSpace(_correctionImageExportWorkingFolder) || !Directory.Exists(_correctionImageExportWorkingFolder))
                {
                    return;
                }

                string combinedFilePath = Path.Combine(_correctionImageExportWorkingFolder, AllMockEyeCorrectionDataFileName);
                if (File.Exists(combinedFilePath))
                {
                    File.Delete(combinedFilePath);
                }

                foreach (string mockEyeFolderName in new[] { "6.668", "7.943", "9.32" })
                {
                    string mockEyeDataPath = Path.Combine(_correctionImageExportWorkingFolder, mockEyeFolderName, MockEyeCorrectionDataFileName);
                    if (File.Exists(mockEyeDataPath))
                    {
                        File.Delete(mockEyeDataPath);
                    }
                }
            }
        }

        private void SaveCorrectionPositionImage(float mockEyeSize, int positionIndex, List<CornealComputeDataEntitys> imageBatch)
        {
            if (mockEyeSize <= 0 || positionIndex < 0 || imageBatch == null || imageBatch.Count < 3)
            {
                return;
            }

            try
            {
                string rootFolder = EnsureCorrectionImageExportWorkingFolder();
                string mockEyeFolderPath = Path.Combine(rootFolder, GetMockEyeFolderName(mockEyeSize));
                Directory.CreateDirectory(mockEyeFolderPath);
                string imagePath = Path.Combine(mockEyeFolderPath, $"{positionIndex}.png");
                using (Bitmap bitmap = BitmapHelpers.ToGrayBitmap(imageBatch[2].Image, 640, 480))
                {
                    bitmap.Save(imagePath, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存曲率校正中间图失败: {ex.Message}");
            }
        }

        private void SaveCurrentMockEyeCorrectionData(float mockEyeSize)
        {
            if (mockEyeSize <= 0)
            {
                return;
            }

            try
            {
                string mockEyeFolderName = GetMockEyeFolderName(mockEyeSize);
                string correctionData = BuildMockEyeCorrectionDataText(mockEyeSize);

                lock (_correctionImageSaveSyncRoot)
                {
                    string rootFolder = EnsureCorrectionImageExportWorkingFolder();
                    string mockEyeFolderPath = Path.Combine(rootFolder, mockEyeFolderName);
                    Directory.CreateDirectory(mockEyeFolderPath);
                    File.WriteAllText(Path.Combine(mockEyeFolderPath, MockEyeCorrectionDataFileName), correctionData, Encoding.UTF8);
                    _mockEyeCorrectionDataTexts[mockEyeFolderName] = correctionData;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存模拟眼校正数据失败: {ex.Message}");
            }
        }

        private void SaveCombinedCorrectionData()
        {
            try
            {
                lock (_correctionImageSaveSyncRoot)
                {
                    if (_mockEyeCorrectionDataTexts.Count == 0)
                    {
                        return;
                    }

                    string rootFolder = EnsureCorrectionImageExportWorkingFolder();
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("MockEyeCorrectionDataSummary");
                    builder.AppendLine($"GeneratedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                    foreach (var item in _mockEyeCorrectionDataTexts.OrderBy(item => double.Parse(item.Key, CultureInfo.InvariantCulture)))
                    {
                        builder.AppendLine();
                        builder.AppendLine($"=== MockEye {item.Key} ===");
                        builder.AppendLine(item.Value.TrimEnd());
                    }

                    File.WriteAllText(Path.Combine(rootFolder, AllMockEyeCorrectionDataFileName), builder.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存全部模拟眼校正数据失败: {ex.Message}");
            }
        }

        private string BuildMockEyeCorrectionDataText(float mockEyeSize)
        {
            int pointCount = new[]
            {
                _LightSpotDistance.Count,
                _InnerLongAxis.Count,
                _InnerShortAxis.Count,
                _OuterLongAxis.Count,
                _OuterShortAxis.Count
            }.Min();

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"MockEyeSize={GetMockEyeFolderName(mockEyeSize)}");
            builder.AppendLine($"PointCount={pointCount}");
            builder.AppendLine($"_LightSpotDistance={FormatCorrectionSeries(_LightSpotDistance)}");
            builder.AppendLine($"_InnerLongAxis={FormatCorrectionSeries(_InnerLongAxis)}");
            builder.AppendLine($"_InnerShortAxis={FormatCorrectionSeries(_InnerShortAxis)}");
            builder.AppendLine($"_OuterLongAxis={FormatCorrectionSeries(_OuterLongAxis)}");
            builder.AppendLine($"_OuterShortAxis={FormatCorrectionSeries(_OuterShortAxis)}");
            builder.AppendLine();
            builder.AppendLine("Index\t_LightSpotDistance\t_InnerLongAxis\t_InnerShortAxis\t_OuterLongAxis\t_OuterShortAxis");

            for (int i = 0; i < pointCount; i++)
            {
                builder.AppendLine(string.Join("\t", new[]
                {
                    i.ToString(CultureInfo.InvariantCulture),
                    FormatCorrectionValue(_LightSpotDistance[i]),
                    FormatCorrectionValue(_InnerLongAxis[i]),
                    FormatCorrectionValue(_InnerShortAxis[i]),
                    FormatCorrectionValue(_OuterLongAxis[i]),
                    FormatCorrectionValue(_OuterShortAxis[i])
                }));
            }

            return builder.ToString();
        }

        private string FormatCorrectionSeries(IEnumerable<double> values)
        {
            return string.Join(", ", values.Select(FormatCorrectionValue));
        }

        private string FormatCorrectionValue(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private string EnsureCorrectionImageExportWorkingFolder()
        {
            lock (_correctionImageSaveSyncRoot)
            {
                if (!string.IsNullOrWhiteSpace(_correctionImageExportWorkingFolder) && Directory.Exists(_correctionImageExportWorkingFolder))
                {
                    return _correctionImageExportWorkingFolder;
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string folderBaseName = $"曲率校正-进行中{DateTime.Now:yyyyMMdd-HHmmss}";
                string folderPath = Path.Combine(desktopPath, folderBaseName);
                int suffix = 1;

                while (Directory.Exists(folderPath))
                {
                    folderPath = Path.Combine(desktopPath, $"{folderBaseName}_{suffix}");
                    suffix++;
                }

                Directory.CreateDirectory(folderPath);
                Directory.CreateDirectory(Path.Combine(folderPath, "6.668"));
                Directory.CreateDirectory(Path.Combine(folderPath, "7.943"));
                Directory.CreateDirectory(Path.Combine(folderPath, "9.32"));
                _correctionImageExportWorkingFolder = folderPath;
                return _correctionImageExportWorkingFolder;
            }
        }

        private void TryFinalizeCorrectionImageExportFolder()
        {
            lock (_correctionImageSaveSyncRoot)
            {
                if (string.IsNullOrWhiteSpace(_correctionImageExportWorkingFolder) || !Directory.Exists(_correctionImageExportWorkingFolder))
                {
                    return;
                }

                try
                {
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string folderBaseName = $"曲率校正-{DateTime.Now:yyyyMMdd-HHmmss}";
                    string targetFolderPath = Path.Combine(desktopPath, folderBaseName);
                    int suffix = 1;

                    while (Directory.Exists(targetFolderPath))
                    {
                        targetFolderPath = Path.Combine(desktopPath, $"{folderBaseName}_{suffix}");
                        suffix++;
                    }

                    Directory.Move(_correctionImageExportWorkingFolder, targetFolderPath);
                    _correctionImageExportWorkingFolder = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"重命名曲率校正导出目录失败: {ex.Message}");
                }
            }
        }

        private string GetMockEyeFolderName(float mockEyeSize)
        {
            if (Math.Abs(mockEyeSize - 6.668f) < 0.001f)
            {
                return "6.668";
            }

            if (Math.Abs(mockEyeSize - 7.943f) < 0.001f)
            {
                return "7.943";
            }

            if (Math.Abs(mockEyeSize - 9.320f) < 0.001f)
            {
                return "9.32";
            }

            return mockEyeSize.ToString("0.###");
        }

        #region 基础方法

        private void CancelMotorSettlingDelay()
        {
            CancellationTokenSource delayCts;
            lock (_motorSettlingDelaySyncRoot)
            {
                delayCts = _motorSettlingDelayCts;
                _motorSettlingDelayCts = null;
            }

            if (delayCts == null)
            {
                return;
            }

            try
            {
                delayCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                delayCts.Dispose();
            }
        }

        private void StartMotorSettlingDelay(bool startScanAfterDelay)
        {
            CancelMotorSettlingDelay();

            CancellationTokenSource delayCts = new CancellationTokenSource();
            lock (_motorSettlingDelaySyncRoot)
            {
                _motorSettlingDelayCts = delayCts;
            }
            _ = EnableCaptureAfterMotorSettlingAsync(startScanAfterDelay, delayCts.Token);
        }

        private async Task EnableCaptureAfterMotorSettlingAsync(bool startScanAfterDelay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(MotorSettlingDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (startScanAfterDelay)
            {
                CurrentTime = 0;
                _startCompute = true;
                _prepareCompute = false;
            }

            _canFillData = true;
        }

        private void MotorMoveFinished1(object sender, RotationAxis e)
        {
            Console.WriteLine($"Motor move feedback _prepareCompute{_prepareCompute},_startCompute{_startCompute}");
            if (_prepareCompute)
            {
                StartMotorSettlingDelay(true);
                return;
            }

            if (_startCompute)
            {
                StartMotorSettlingDelay(false);
            }
        }

        private void MotorMoveFinished(object sender, MotorPositions e)
        {
            Console.WriteLine($"Tooling move feedback _prepareCompute{_prepareCompute},_startCompute{_startCompute}");
            if (_prepareCompute)
            {
                StartMotorSettlingDelay(true);
                return;
            }

            if (_startCompute)
            {
                StartMotorSettlingDelay(false);
            }
        }

        private void RegisterEvents()
        {
            MainWindowViewModel.cameraStatue = true;
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler += ShowIrisImage;
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler += CurvatureCorrect;

            if (MainWindowViewModel.correctionMethod == CorrectionMethod.Tooling)
            {
                MCUActor.Instance.CorrectionMotorMoveToFinishedEventHandler += MotorMoveFinished;
            }
            else
            {
                MCUActor.Instance.ShowMCUYAxisMoveFinishEventHandler += MotorMoveFinished1;
            }

        }

        private void UnregisterEvents()
        {
            CancelMotorSettlingDelay();


            MFCameraActor.Instance.UpdateMFByteArrayEventHandler -= ShowIrisImage;
            MFCameraActor.Instance.UpdateMFByteArrayEventHandler -= CurvatureCorrect;
            if (MainWindowViewModel.correctionMethod == CorrectionMethod.Tooling)
            {
                MCUActor.Instance.CorrectionMotorMoveToFinishedEventHandler -= MotorMoveFinished;
            }
            else
            {
                MCUActor.Instance.ShowMCUYAxisMoveFinishEventHandler -= MotorMoveFinished1;
            }


        }

        public override void Cleanup()
        {
            base.Cleanup();
            ToolingComputeActor.Instance.ShowCurvatureClearValueComputeEventHandler -= CurvatureClearValueComputeCallBack;
            _computeFinishEventHandler -= ComputeFinishCallBack;
            _filleDataEventHandler -= FilledDataCallback;
            UnregisterEvents();
        }

        #endregion 

        #endregion

    }
}

