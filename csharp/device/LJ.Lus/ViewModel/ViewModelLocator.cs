using CommonServiceLocator;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Messaging;
using LJ.Lus.Backend.Compute.Actors.Algorithm;
using LJ.Lus.Backend.Model;
using LJ.Lus.DotnetDevices.DAQ;
using LJ.Lus.Helpers;
using LJ.Lus.TechLibrary.Helpers;
using System;
using System.IO;

namespace LJ.Lus.ViewModel
{
    /// <summary>
    /// This class contains static references to all the view models in the
    /// application and provides an entry point for the bindings.
    /// </summary>
    public class ViewModelLocator
    {
        private static void RegisterSingletonIfNeeded<T>() where T : class
        {
            if (!SimpleIoc.Default.IsRegistered<T>())
            {
                SimpleIoc.Default.Register<T>(true);
            }
        }

        private static void RegisterIfNeeded<T>() where T : class
        {
            if (!SimpleIoc.Default.IsRegistered<T>())
            {
                SimpleIoc.Default.Register<T>();
            }
        }

        /// <summary>
        /// Initializes a new instance of the ViewModelLocator class.
        /// </summary>
        public ViewModelLocator()
        {
            ServiceLocator.SetLocatorProvider(() => SimpleIoc.Default);

            #region 配置文件
            SimpleIoc.Default.Register<SignalFilterConfigs>(true);
            SimpleIoc.Default.Register<DeviceConfigPara>(true);
            SimpleIoc.Default.Register<CorrectFileConfigs>(true);
            SimpleIoc.Default.Register<DAQConfig>(true);
            SimpleIoc.Default.Register<KeyBoardViewModel>(true);
            SimpleIoc.Default.Register<DeviceIntrinsicParaModel>(true);
            string pathSignalFilterConfigs = Environment.CurrentDirectory + "\\SignalFilterConfigs.json";
            string pathDeviceConfigPara = Environment.CurrentDirectory + "\\DeviceConfigPara.json";
            string pathCorrectFileConfigs = Environment.CurrentDirectory + "\\CorrectFileConfigs.json";
            string pathDAQFileConfigs = Environment.CurrentDirectory + "\\DAQConfigs.json";
            string pathDeviceIntrinsicParaModels = Environment.CurrentDirectory + "\\DeviceIntrinsicPara.json";
            if (File.Exists(pathSignalFilterConfigs))
            {
                SignalFilterConfigs signalFilterConfig = ServiceLocator.Current.GetInstance<SignalFilterConfigs>();
                SignalFilterConfigs signalFilterConfigQ = JsonHelper.Load<SignalFilterConfigs>(pathSignalFilterConfigs);
                ClassCloneHelper.Copy(ref signalFilterConfig, signalFilterConfigQ);
            }
            if (File.Exists(pathDeviceConfigPara))
            {
                DeviceConfigPara DeviceConfigPara = ServiceLocator.Current.GetInstance<DeviceConfigPara>();
                DeviceConfigPara DeviceConfigParaQ = JsonHelper.Load<DeviceConfigPara>(pathDeviceConfigPara);
                //若偏振最佳位置为0，则默认转动。
                if (DeviceConfigParaQ.PolarizationMotorBestPosition == 0) DeviceConfigParaQ.IsPolarizationAdjust = true;
                //若个体商户公司名称为空，默认采用出厂商名称
                if (String.IsNullOrEmpty(DeviceConfigParaQ.CompanyName)) DeviceConfigParaQ.CompanyName = DeviceConfigParaQ.Manufacturer;
                ClassCloneHelper.Copy(ref DeviceConfigPara, DeviceConfigParaQ);
            }
            if (File.Exists(pathCorrectFileConfigs))
            {
                CorrectFileConfigs CorrectFileConfigs = ServiceLocator.Current.GetInstance<CorrectFileConfigs>();
                CorrectFileConfigs CorrectFileConfigsQ = JsonHelper.Load<CorrectFileConfigs>(pathCorrectFileConfigs);
                ClassCloneHelper.Copy(ref CorrectFileConfigs, CorrectFileConfigsQ);
            }
            if(File.Exists(pathDAQFileConfigs))
            {
                DAQConfig DAQConfig = ServiceLocator.Current.GetInstance<DAQConfig>();
                DAQConfig DAQConfigQ = JsonHelper.Load<DAQConfig>(pathDAQFileConfigs);
                switch (ServiceLocator.Current.GetInstance<DeviceConfigPara>().AcquisitionOption.AcquisitionMode)
                {
                    case AcquisitionMode.Single:
                        DAQConfigQ.AcquirePoints = (int)(1.375 * 1024 * 1024);
                        break;
                    case AcquisitionMode.Multi:
                        DAQConfigQ.AcquirePoints = (int)(3 * 1024 * 1024);
                        break;
                    default:
                        break;
                }
                ClassCloneHelper.Copy(ref DAQConfig, DAQConfigQ);
            }
            if (File.Exists(pathDeviceIntrinsicParaModels))
            {
                DeviceIntrinsicParaModel deviceIntrinsicParaModel = ServiceLocator.Current.GetInstance<DeviceIntrinsicParaModel>();
                DeviceIntrinsicParaModel deviceIntrinsicParaModelQ = JsonHelper.Load<DeviceIntrinsicParaModel>(pathDeviceIntrinsicParaModels);
                ClassCloneHelper.Copy(ref deviceIntrinsicParaModel, deviceIntrinsicParaModelQ);
            }
            #endregion

            SimpleIoc.Default.Register<DataDispatcher>(true);
            SimpleIoc.Default.Register<StartCheckViewModel>();
            SimpleIoc.Default.Register<UserLoginViewModel>(true);
            SimpleIoc.Default.Register<MainViewModel>(true);
            SimpleIoc.Default.Register<IOLViewModel>(true);
            SimpleIoc.Default.Register<AccountViewModel>();

            Messenger.Default.Register<string>(this, "StartMainWindow", s =>
            {
                RegisterSingletonIfNeeded<SignalThresholdSettingViewModel>();
                RegisterSingletonIfNeeded<SignalCorrectionViewModel>();
                RegisterSingletonIfNeeded<PersonAllInformationModel>();
                RegisterSingletonIfNeeded<InternetViewModel>();
                RegisterSingletonIfNeeded<PrintSimulateEyesResultPageViewModel>();
                RegisterSingletonIfNeeded<AdminSettingViewModel>();
                RegisterSingletonIfNeeded<PersonCardsViewModel>();
                RegisterSingletonIfNeeded<PersonResultShowViewModel>();
                RegisterSingletonIfNeeded<DataAnalysisViewModel>();
                RegisterSingletonIfNeeded<DataAnalysisCaseViewModel>();
                RegisterSingletonIfNeeded<PSWTWCorrectionViewModel>();
                RegisterSingletonIfNeeded<EvaluationViewModel>();
                RegisterSingletonIfNeeded<MultiplePrinterViewModel>();
                RegisterSingletonIfNeeded<DataCompareViewModel>();
                RegisterSingletonIfNeeded<Motor3DLoopRunViewModel>();
                RegisterSingletonIfNeeded<FirmwareUpgradesViewModel>();
                RegisterSingletonIfNeeded<AxisResultsAnalysisViewModel>();
                RegisterSingletonIfNeeded<RefractiveResultsAnalysisViewModel>();
                RegisterSingletonIfNeeded<PersonalDataAnalysisViewModel>();
                RegisterSingletonIfNeeded<CompensationCoefficientSettingsViewModel>();
                RegisterIfNeeded<PowerSettingViewModel>();
                RegisterIfNeeded<CameraConfigViewModel>();
                RegisterIfNeeded<FrequencyCurveCheckViewModel>();
            });
        }
        //public FirmwareUpgradesViewModel FirmwareUpgrades => ServiceLocator.Current.GetInstance<FirmwareUpgradesViewModel>();
        public FirmwareUpgradesViewModel FirmwareUpgrades
        {
            get
            {
                if (!SimpleIoc.Default.IsRegistered<FirmwareUpgradesViewModel>())
                {
                    SimpleIoc.Default.Register<FirmwareUpgradesViewModel>();
                }
                return ServiceLocator.Current.GetInstance<FirmwareUpgradesViewModel>();
            }
        }
        public DataCompareViewModel DataCompare => ServiceLocator.Current.GetInstance<DataCompareViewModel>();
        //public MainViewModel MainWindow => ServiceLocator.Current.GetInstance<MainViewModel>();
        public MainViewModel MainWindow
        {
            get
            {
                // 1. 检查容器里到底有没有 MainViewModel
                if (!SimpleIoc.Default.IsRegistered<MainViewModel>())
                {
                    // 2. 如果没有（报 Type not found 就是因为进到了这里），立刻注册它！
                    SimpleIoc.Default.Register<MainViewModel>();
                }

                // 3. 现在肯定有了，安全获取
                return ServiceLocator.Current.GetInstance<MainViewModel>();
            }
        }
        public MultiplePrinterViewModel MultiplePrinterPage => ServiceLocator.Current.GetInstance<MultiplePrinterViewModel>();
        public EvaluationViewModel Evaluation => ServiceLocator.Current.GetInstance<EvaluationViewModel>();
        public PSWTWCorrectionViewModel PSWTWCorrection => ServiceLocator.Current.GetInstance<PSWTWCorrectionViewModel>();
        public SignalCorrectionViewModel SignalCorrection => ServiceLocator.Current.GetInstance<SignalCorrectionViewModel>();
        public StartCheckViewModel StartCheck => ServiceLocator.Current.GetInstance<StartCheckViewModel>();
        public PrintSimulateEyesResultPageViewModel PrintSimulateEyesResultPageViewModel => ServiceLocator.Current.GetInstance<PrintSimulateEyesResultPageViewModel>();
        public PersonAllInformationModel PersonAllInformation => ServiceLocator.Current.GetInstance<PersonAllInformationModel>();
        //public AdminSettingViewModel AdminSetting => ServiceLocator.Current.GetInstance<AdminSettingViewModel>();
        public AdminSettingViewModel AdminSetting
        {
            get
            {
                // 1. 检查容器里到底有没有
                if (!SimpleIoc.Default.IsRegistered<AdminSettingViewModel>())
                {
                    // 2. 如果没有（报 Type not found 就是因为进到了这里），立刻注册它！
                    SimpleIoc.Default.Register<AdminSettingViewModel>();
                }

                // 3. 现在肯定有了，安全获取
                return ServiceLocator.Current.GetInstance<AdminSettingViewModel>();
            }
        }
        //public PersonCardsViewModel PersonCards => ServiceLocator.Current.GetInstance<PersonCardsViewModel>();
        public PersonCardsViewModel PersonCards
        {
            get
            {
                // 获取前先检查：如果没有注册，就立刻注册！
                if (!SimpleIoc.Default.IsRegistered<PersonCardsViewModel>())
                {
                    SimpleIoc.Default.Register<PersonCardsViewModel>();
                }

                return ServiceLocator.Current.GetInstance<PersonCardsViewModel>();
            }
        }

        public PersonResultShowViewModel PersonResultShow => ServiceLocator.Current.GetInstance<PersonResultShowViewModel>();
        public InternetViewModel Internet => ServiceLocator.Current.GetInstance<InternetViewModel>();
        public SignalThresholdSettingViewModel SignalThresholdSetting => ServiceLocator.Current.GetInstance<SignalThresholdSettingViewModel>();
        //public DataAnalysisViewModel DataAnalysis => ServiceLocator.Current.GetInstance<DataAnalysisViewModel>();
        public DataAnalysisViewModel DataAnalysis
        {
            get
            {
                // 获取前先检查：如果没有注册，就立刻注册！
                if (!SimpleIoc.Default.IsRegistered<DataAnalysisViewModel>())
                {
                    SimpleIoc.Default.Register<DataAnalysisViewModel>();
                }

                return ServiceLocator.Current.GetInstance<DataAnalysisViewModel>();
            }
        }
        public DataAnalysisCaseViewModel DataAnalysisCase => ServiceLocator.Current.GetInstance<DataAnalysisCaseViewModel>();
        public IOLViewModel IOL => ServiceLocator.Current.GetInstance<IOLViewModel>();
        public KeyBoardViewModel KeyBoard => ServiceLocator.Current.GetInstance<KeyBoardViewModel>();
        public Motor3DLoopRunViewModel Motor3DLoopRun => ServiceLocator.Current.GetInstance<Motor3DLoopRunViewModel>();
        public AxisResultsAnalysisViewModel AxisResultsAnalysis => ServiceLocator.Current.GetInstance<AxisResultsAnalysisViewModel>();
        public RefractiveResultsAnalysisViewModel RefractiveResultsAnalysis => ServiceLocator.Current.GetInstance<RefractiveResultsAnalysisViewModel>();
        public PersonalDataAnalysisViewModel PersonalDataAnalysis => ServiceLocator.Current.GetInstance<PersonalDataAnalysisViewModel>();
        public UserLoginViewModel UserLogin => ServiceLocator.Current.GetInstance<UserLoginViewModel>();
        public CompensationCoefficientSettingsViewModel CompensationCoefficientSettings => ServiceLocator.Current.GetInstance<CompensationCoefficientSettingsViewModel>();
        public PowerSettingViewModel PowerSetting => ServiceLocator.Current.GetInstance<PowerSettingViewModel>();

        public AccountViewModel Account => ServiceLocator.Current.GetInstance<AccountViewModel>();
        public FrequencyCurveCheckViewModel FrequencyCurveCheck => ServiceLocator.Current.GetInstance<FrequencyCurveCheckViewModel>();

        public CameraConfigViewModel CameraConfig => ServiceLocator.Current.GetInstance<CameraConfigViewModel>();
        public static void Cleanup()
        {
            // TODO Clear the ViewModels
        }
    }
}

