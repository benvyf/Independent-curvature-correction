using CommonServiceLocator;
using LJ.Lus.Backend.Basic;
using LJ.Lus.Backend.Compute.Actors.Algorithm;
using LJ.Lus.Backend.Info.Actors.Database;
using LJ.Lus.Backend.Model;
using LJ.Lus.Backend.Module.MeasureModule;
using LJ.Lus.Backend.Module;
using LJ.Lus.Backend.Systems;
using LJ.Lus.TechLibrary.Helpers;
using LJ.Lus.TechLibrary.Signal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LJ.Lus.TechLibrary.Extend;
using LogHelper = LJ.Lus.TechLibrary.Helpers.LogHelper;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;

namespace LJ.Lus.Backend.Compute.Actors.DataPostProcess
{
    public class DataPostProcessActor : BasicActor
    {
        #region Singleton
        public DataPostProcessActor()
        {
            _dataClassifySystem = new List<DataClassifySystem>
            {
                new DataClassifySystem(0.025f),//AL
                new DataClassifySystem(0.04f),//ACD
                new DataClassifySystem(0.06f),//LT
                new DataClassifySystem(8.0f),//CCT
                new DataClassifySystem(0.02f),//K
                new DataClassifySystem(9f)//shaftAngle
            };
            LaunchActor();
        }

        private static readonly Lazy<DataPostProcessActor> lazy = new Lazy<DataPostProcessActor>(() => new DataPostProcessActor());
        public static DataPostProcessActor Instance { get { return lazy.Value; } }
        #endregion

        #region Fiedls
        protected List<DataClassifySystem> _dataClassifySystem;//分类器列表
        protected List<EyeMultiParameterComputeResult> _eyeMultiParameterComputeResults;
        protected DeviceConfigPara _deviceConfigPara;
        protected ResultShowModel _resultShowModel;
        protected EyeType _eyeType = EyeType.无;
        protected MeasureMode _measureMode;
        protected Guid _caseID;
        protected List<EyeMultiParameterComputeResult> _selectedCornealComputeResults = new List<EyeMultiParameterComputeResult>();
        protected float[] _blueLedEyeIrisImage;
        protected float[] _blueLedPDAndWTWComputeResult;
        protected bool _isPSAndWTWCalculateFinish = false;
        protected bool _isEyeMultiParameterPacketsComputeFinish = false;
        //测量轮次代数：每轮起点SetCurrentDataInfo自增，用于识别并丢弃中断重测后上一轮迟到的跨轮消息
        protected int _measurementGeneration = 0;
        //本轮期望的PSAndWTW结果所属代数：多参数计算加载本轮数据时记录当前代数；结果到达时代数不符=跨轮陈旧，丢弃
        protected int _pendingPSAndWTWGeneration = 0;
        protected const int CornealImageWidth = 640;
        protected const int CornealImageHeight = 480;
        #endregion

        #region Properties
        /// <summary>
        /// 数据后处理完成事件
        /// </summary>
        public virtual EventHandler<ResultShowModel> PostProcessDataEventHandler { get; set; }
        /// <summary>
        /// 数据保存完成事件
        /// </summary>
        public virtual EventHandler FinishSaveDataEventHandler { get; set; }
        #endregion

        //本actor的Become绝不冲刷队列：蓝灯PSAndWTW结果、下一轮SetCurrentDataInfo/SetPatientInfo等跨轮消息可能已入队，
        //冲掉会导致完成标志永不置位/下一轮消息丢失。跨轮陈旧消息改由代数比对显式丢弃，而非依赖Flush。
        protected override bool FlushOnBecome => false;


        #region MessageMethods
        /// <summary>
        /// 设置当前数据信息（数据来源）
        /// </summary>
        /// <param name="eyeType">眼睛类型：左眼/右眼</param>
        public void SetCurrentDataInfo(EyeType eyeType)
        {
            //新一轮测量起点：代数自增，使上一轮迟到的PSAndWTW结果/图像保存完成消息在比对时被识别为陈旧并丢弃
            _measurementGeneration++;
            _isPSAndWTWCalculateFinish = false;
            _isEyeMultiParameterPacketsComputeFinish = false;
            _blueLedEyeIrisImage = null;
            _blueLedPDAndWTWComputeResult = null;
            _eyeType = eyeType;
        }
        /// <summary>
        /// 设置当前病历信息
        /// </summary>
        /// <param name="caseID">病历ID</param>
        /// <param name="patientAge">病人年龄</param>
        /// <param name="measureMode">测量模式</param>
        public void SetPatientInfo(Guid caseID, int patientAge, MeasureMode measureMode)
        {
            _deviceConfigPara = ServiceLocator.Current.GetInstance<DeviceConfigPara>();
            _caseID = caseID;
            _measureMode = measureMode;
            _resultShowModel = new ResultShowModel();
            if (patientAge >= 4 && patientAge <= 18)
            {
                _dataClassifySystem.Clear();
                //小孩子筛选条件
                _dataClassifySystem.Add(new DataClassifySystem(0.03f));//AL
                _dataClassifySystem.Add(new DataClassifySystem(0.1f));//ACD
                _dataClassifySystem.Add(new DataClassifySystem(0.1f));//LT
                _dataClassifySystem.Add(new DataClassifySystem(15.0f));//CCT
                _dataClassifySystem.Add(new DataClassifySystem(0.02f));//K
                _dataClassifySystem.Add(new DataClassifySystem(9f));//shaftAngle
            }
            else
            {
                _dataClassifySystem.Clear();
                //成人筛选条件
                _dataClassifySystem.Add(new DataClassifySystem(0.025f));//AL
                _dataClassifySystem.Add(new DataClassifySystem(0.04f));//ACD
                _dataClassifySystem.Add(new DataClassifySystem(0.06f));//LT
                _dataClassifySystem.Add(new DataClassifySystem(8.0f));//CCT
                _dataClassifySystem.Add(new DataClassifySystem(0.02f));//K
                _dataClassifySystem.Add(new DataClassifySystem(9f));//shaftAngle
            }
        }

        protected List<EyeMultiParameterComputeResult> GetCornealComputeResults()
        {
            var cornealComputeResults = _eyeMultiParameterComputeResults?
                .Where(item => item != null && item.UseForCornealCurvature)
                .ToList() ?? new List<EyeMultiParameterComputeResult>();

            if (cornealComputeResults.Count == 0)
            {
                cornealComputeResults = _eyeMultiParameterComputeResults?
                    .Where(item => item != null)
                    .ToList() ?? new List<EyeMultiParameterComputeResult>();
            }

            return cornealComputeResults;
        }

        /// <summary>
        /// 处理多参数计算结果集合（多参数计算事件）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eyeMultiParameterComputeResults">多参数计算结果集合</param>
        public void EyeMultiParameterPacketsCompute(List<EyeMultiParameterComputeResult> eyeMultiParameterComputeResults)
        {
            Console.WriteLine("EyeMultiParameterPacketsCompute");
            if (eyeMultiParameterComputeResults == null) return;
            Become(Working);//本actor FlushOnBecome=false，不冲刷队列：蓝灯PSAndWTW结果消息可能已入队
            _selectedCornealComputeResults = GetSelectedCornealComputeResults(eyeMultiParameterComputeResults);
            _eyeMultiParameterComputeResults = eyeMultiParameterComputeResults;
            //记录本轮多参数数据所属代数：随后到达的PSAndWTW结果须与当前代数一致才允许写入本轮结果
            //（同轮内多参数计算消息必先于其PSAndWTW消息到达，故此处置位可靠覆盖本轮PSAndWTW的校验）
            _pendingPSAndWTWGeneration = _measurementGeneration;
            var cornealComputeResults = GetCornealComputeResults();
            var cornealResultsForSelection = _selectedCornealComputeResults?.Count > 0 ? _selectedCornealComputeResults : cornealComputeResults;
            //算法反馈需保证List列表是线程安全的。
            foreach (var item in eyeMultiParameterComputeResults)
            {
                _dataClassifySystem[0].Add(item.Case.AxialLength);
                _dataClassifySystem[1].Add(item.Case.AnteriorchamberDepth);
                _dataClassifySystem[2].Add(item.Case.LensThickness);
                _dataClassifySystem[3].Add(item.Case.CentralCornealThickness);
            }
            foreach (var item in cornealResultsForSelection)
            {
                _dataClassifySystem[4].Add(item.Case.LongAxisCornealCurvature);
                _dataClassifySystem[5].Add(item.Case.LongAxis_Axial_angle_mean);
            }

            //数据分类
            var ALList = _dataClassifySystem[0].GetSimilarDataList();
            var ACDList = _dataClassifySystem[1].GetSimilarDataList();
            var LTList = _dataClassifySystem[2].GetSimilarDataList();
            var CCTList = _dataClassifySystem[3].GetSimilarDataList();
            var K1List = _dataClassifySystem[4].GetSimilarDataList();
            var shaftAngleList = _dataClassifySystem[5].GetSimilarDataList();



            if (_measureMode == MeasureMode.AK)
            {
                //CCT、ACD、LT全赋异常值
                CCTList.Clear();
                ACDList.Clear();
                LTList.Clear();
            }


            //不够5组，则填充
            if (ALList.Count < 5)
            {
                var value = SignalFilteringHelper.GetClosestData(ALList.ToArray());
                var eyeMultiParameterComputeResult = eyeMultiParameterComputeResults.Find(item => item.Case.AxialLength == value);
                DataPopulate(ALList, value, eyeMultiParameterComputeResult);
            }
            if (CCTList.Count < 5)
            {
                //CCT
                var value = SignalFilteringHelper.GetClosestData(CCTList.ToArray());
                var eyeMultiParameterComputeResult = eyeMultiParameterComputeResults.Find(item => item.Case.CentralCornealThickness == value);
                DataPopulate(CCTList, value, eyeMultiParameterComputeResult);
            }

            if (ACDList.Count < 5)
            {
                //ACD
                var value = SignalFilteringHelper.GetClosestData(ACDList.ToArray());
                var eyeMultiParameterComputeResult = eyeMultiParameterComputeResults.Find(item => item.Case.AnteriorchamberDepth == value);
                DataPopulate(ACDList, value, eyeMultiParameterComputeResult);
            }
            if (LTList.Count < 5)
            {
                //LT
                var value = SignalFilteringHelper.GetClosestData(LTList.ToArray());
                var eyeMultiParameterComputeResult = eyeMultiParameterComputeResults.Find(item => item.Case.LensThickness == value);
                DataPopulate(LTList, value, eyeMultiParameterComputeResult);
            }
            if (K1List.Count < 5)
            {
                //K
                var value = SignalFilteringHelper.GetClosestData(K1List.ToArray());
                var eyeMultiParameterComputeResult = cornealResultsForSelection.Find(item => item.Case.LongAxisCornealCurvature == value);
                DataPopulate(K1List, value, eyeMultiParameterComputeResult);
            }
            if (shaftAngleList.Count < 5)
            {
                //K
                var value = SignalFilteringHelper.GetClosestData(shaftAngleList.ToArray());
                var eyeMultiParameterComputeResult = cornealResultsForSelection.Find(item => item.Case.LongAxis_Axial_angle_mean == value);
                DataPopulate(shaftAngleList, value, eyeMultiParameterComputeResult);
            }




            //存储数据并且通过事件反馈出去
            try
            {
                EyeMultiParameterPublish(ALList, CCTList, ACDList, LTList, K1List, shaftAngleList);
            }
            catch (Exception e)
            {
                LogHelper.Error("DataProcess异常" + e.Message + e.StackTrace);
                Clear();
            }

        }

        /// <summary>
        /// 处理瞳孔和白到白结果集合（闪烁蓝灯后的独立计算事件）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="PSAndWTWComputeResults">瞳孔、白到白处理结果集合</param>
        public void PSAndWTWCalculateResult(List<InnerAndOuterDiameterComputeResult> PSAndWTWComputeResults)
        {
            Console.WriteLine("PSAndWTW");
            //丢弃跨轮陈旧或重复的PSAndWTW结果：
            //  代数不符=中断重测后上一轮迟到的结果（会把旧虹膜图PD/WTW写进新一轮Case、提前置完成标志触发误发布/误保存）；
            //  已完成=本轮或回Idle态后重复到达的结果（会二次发布/二次SaveImageDataAndInvoke）。
            if (_pendingPSAndWTWGeneration != _measurementGeneration || _isPSAndWTWCalculateFinish)
            {
                LogHelper.Warn($"[CONTRAST] 丢弃陈旧/重复PSAndWTW结果 pendingGen={_pendingPSAndWTWGeneration} curGen={_measurementGeneration} psFinished={_isPSAndWTWCalculateFinish}");
                return;
            }
            if (_resultShowModel.Case == null)
            {
                _resultShowModel.Case = new Case();
            }
            //WTW系数补偿
            foreach (var item in PSAndWTWComputeResults)
            {
                item.PDDiameter *= _deviceConfigPara.WTWSamplingTime;
                item.WTWDiameter *= _deviceConfigPara.WTWSamplingTime;
            }
            //去掉异常情况(不用遍历的方式，遍历方式删除元素会导致集合修改异常)
            PSAndWTWComputeResults.RemoveAll(o => o.PDDiameter < 2);
            PSAndWTWComputeResults.RemoveAll(o => o.WTWDiameter < 9.5);
            PSAndWTWComputeResults.RemoveAll(o => o.WTWDiameter > 13.5);
            //选取瞳孔最小
            if (PSAndWTWComputeResults.Count != 0)
            {
                int num = (int)Math.Floor(PSAndWTWComputeResults.Count * 0.1);
                //去掉最小的几个，可能由于眨眼引起的
                for (int i = 0; i < num; ++i)
                {
                    var minPSValue = PSAndWTWComputeResults.Select(o => o.PDDiameter).Min();
                    var minPS = PSAndWTWComputeResults.Find(o => o.PDDiameter == minPSValue);
                    PSAndWTWComputeResults.Remove(minPS);
                }
                var list = PSAndWTWComputeResults.Select(o => (o.PDDiameter)).ToList();
                var listWTW = PSAndWTWComputeResults.Select(o => (o.WTWDiameter)).ToList();
                var min = PSAndWTWComputeResults.Select(o => (o.PDDiameter)).ToList().Min();
                int index = list.FindIndex(item => item.Equals(min));
                _resultShowModel.Case.PupillaryDiameter = min;
                _resultShowModel.Case.WhiteToWhite = SignalFilteringHelper.FindMidSort(listWTW);
                _resultShowModel.PDAndWTWComputeResult = PSAndWTWComputeResults[index].PDAndWTWComputeResult;
                _resultShowModel.EyeIrisImage = PSAndWTWComputeResults[index].IrisImageForSHow;
                _blueLedPDAndWTWComputeResult = _resultShowModel.PDAndWTWComputeResult;
                _blueLedEyeIrisImage = _resultShowModel.EyeIrisImage;

                //异常数据处理
                WTWAndPSExceptionDataProcess(_resultShowModel.Case);

                var CircleInfos = new List<CircleInfo>()
                {
                    new CircleInfo()//PS
                    {
                        CenterX=(int)_resultShowModel.PDAndWTWComputeResult[0],
                        CenterY=(int)_resultShowModel.PDAndWTWComputeResult[1],
                        Radius=(int)(_resultShowModel.PDAndWTWComputeResult[2]/2)
                    },
                    new CircleInfo()//WTW
                    {
                        CenterX=(int)_resultShowModel.PDAndWTWComputeResult[3],
                        CenterY=(int)_resultShowModel.PDAndWTWComputeResult[4],
                        Radius=(int)(_resultShowModel.PDAndWTWComputeResult[5]/2)
                    }
                };
                foreach (var item in _resultShowModel.EyeParameters)
                {
                    item.CircleInfos = CircleInfos;
                }
            }
            _isPSAndWTWCalculateFinish = true;
            if (IsAbleToInvoke())
            {
                //数据传出
                this.PostProcessDataEventHandler?.BeginMulticastInvoke(this, _resultShowModel);
                SaveImageDataAndInvoke(_resultShowModel.EyeIrisImage, _resultShowModel.Case);
                Console.WriteLine("PSAndWTW计算返回");
            }
        }

        #endregion


        #region Methods
        protected void Clear()
        {
            //释放筛选过程的内存
            foreach (var item in _dataClassifySystem)
            {
                item.Clear();
            }
            _eyeMultiParameterComputeResults?.Clear();
            _selectedCornealComputeResults?.Clear();
            _blueLedEyeIrisImage = null;
            _blueLedPDAndWTWComputeResult = null;
            //复位完成标志与待处理代数：避免回Idle后残留的完成标志被陈旧/重复PSAndWTW借用触发二次发布；
            //两标志仅在活动轮次内经IsAbleToInvoke读取、下一轮SetCurrentDataInfo会重置，此处复位无副作用
            _isPSAndWTWCalculateFinish = false;
            _isEyeMultiParameterPacketsComputeFinish = false;
            _pendingPSAndWTWGeneration = 0;
            //释放采集过程的内存
            (ModuleFactory.Instance.GetModule(ModuleOptions.CaptureData) as CaptureDataModule)?.MemoryClear();
        }

        protected void ApplyBlueLedDisplayResultIfAvailable()
        {
            if (!_deviceConfigPara.IsBlueLEDMeasure || _blueLedEyeIrisImage == null)
            {
                return;
            }

            _resultShowModel.EyeIrisImage = _blueLedEyeIrisImage;
            if (_blueLedPDAndWTWComputeResult != null)
            {
                _resultShowModel.PDAndWTWComputeResult = _blueLedPDAndWTWComputeResult;
            }
        }

        protected List<EyeMultiParameterComputeResult> GetSelectedCornealComputeResults(List<EyeMultiParameterComputeResult> eyeMultiParameterComputeResults)
        {
            var selectedResults = eyeMultiParameterComputeResults?
                .Where(item => item != null && item.UseForCornealCurvature)
                .ToList() ?? new List<EyeMultiParameterComputeResult>();

            if (selectedResults.Count == 0)
            {
                selectedResults = eyeMultiParameterComputeResults?
                    .Where(item => item != null)
                    .ToList() ?? new List<EyeMultiParameterComputeResult>();
            }

            return selectedResults
                .OrderBy(item => Math.Abs(item?.EyeMultiParameterComputeTask?.CornealCurvatureDistance ?? float.MaxValue))
                .Take(5)
                .ToList();
        }
        /// <summary>
        /// 眼睛多参数数据筛选并发布（分析评估界面显示）
        /// </summary>
        /// <param name="ALList">AL数据集合</param>
        /// <param name="CCTList">CCT数据集合</param>
        /// <param name="ACDList">ACD数据集合</param>
        /// <param name="LTList">LT数据集合</param>
        protected void EyeMultiParameterPublish(List<float> ALList, List<float> CCTList, List<float> ACDList, List<float> LTList, List<float> K1List, List<float> shaftAngleList)
        {
            ALList = ALList.GetRange(0, 5);
            CCTList = CCTList.GetRange(0, 5);
            ACDList = ACDList.GetRange(0, 5);
            LTList = LTList.GetRange(0, 5);
            K1List = K1List.GetRange(0, 5);
            shaftAngleList = shaftAngleList.GetRange(0, 5);
            int length = new int[4] { ALList.Count, CCTList.Count, ACDList.Count, LTList.Count }.Min();

            //var @cases = _eyeMultiParameterComputeResults.Select(item => item.Case).ToList();
            //var signals = _eyeMultiParameterComputeResults.Select(item => item.EyeMultiParameterComputeTask.SignalProcessComputeResult).ToList();

            var @cases = new List<Case>();
            var signals = new List<SignalProcessComputeResult>();
            int startIndex = 0;
            //这里应该确保ALList[i]一定能在_eyeMultiParameterComputeResults.Case.AxialLength中被找到。
            //也就是ALList[i]一定是从_eyeMultiParameterComputeResults.Case.AxialLength中拿出来的，不能是新计算的，否则会陷入死循环。
            for (int i = 0; i < length;)
            {
                //防止多个重复值，find一直找到第一个就返回。
                //需确保ALList的存储顺序和_rawMultiParameterComputeResult存储顺序一样
                var index = (int)(_eyeMultiParameterComputeResults?.FindIndex(startIndex, t => t.Case.AxialLength == ALList[i]));
                if (index != -1)//找到对应的数据条
                {
                    startIndex = index;//指针指向开始位置
                    @cases.Add(_eyeMultiParameterComputeResults[startIndex].Case);
                    signals.Add(_eyeMultiParameterComputeResults[startIndex].EyeMultiParameterComputeTask.SignalProcessComputeResult);
                    i++;
                }
                else//没有对应的数据条
                {
                    startIndex = 0;//指针复位
                }
                if (startIndex != _eyeMultiParameterComputeResults.Count - 1)
                {
                    startIndex++;//指针后移一位
                }
                else
                {
                    startIndex = 0;//指针复位，重新查找
                }
            }


            //将筛选完的数据形成一个Case，并将眼表参数根据K1提取，得到K2
            var @case = DatasToOneCase(ALList, CCTList, ACDList, LTList, K1List, out var K2List, shaftAngleList);
            //根据K1、K2得到D1、D2
            var cornealCurvatureD1List = new List<float>();
            var cornealCurvatureD2List = new List<float>();
            var shortAxisShaftAngleList = new List<float>();
            for (int i = 0; i < K1List.Count; ++i)
            {
                if (K1List[i] == 0)
                {
                    cornealCurvatureD1List.Add(0);
                }
                else
                {
                    var value = (float)(1000 * (1.3375f - 1) / K1List[i]);
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        cornealCurvatureD1List.Add(0);
                    }
                    else
                    {
                        cornealCurvatureD1List.Add(value);
                    }
                }
                if (K2List[i] == 0)
                {
                    cornealCurvatureD2List.Add(0);
                }
                else
                {
                    var value = (float)(1000 * (1.3375f - 1) / K2List[i]);
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        cornealCurvatureD2List.Add(0);
                    }
                    else
                    {
                        cornealCurvatureD2List.Add(value);
                    }
                }
                shortAxisShaftAngleList.Add(GetDisplayShortAxisAngle((int)shaftAngleList[i]));
            }

            _resultShowModel.MeasureMode = _measureMode;
            var previousCase = _resultShowModel.Case;
            _resultShowModel.Case = @case;
            if (_blueLedPDAndWTWComputeResult != null && previousCase != null)
            {
                //蓝灯结果先到时其PS/WTW写在旧Case上，替换Case后迁移过来；
                //DatasToOneCase在蓝灯已写值时跳过了主路径中位数填充，不迁移则两头都不填、0值会被异常处理加密成假异常值
                //（迁移值可能已被加密，WTWAndPSExceptionDataProcess的幂等守卫保证不会二次加密还原）
                @case.PupillaryDiameter = previousCase.PupillaryDiameter;
                @case.WhiteToWhite = previousCase.WhiteToWhite;
            }
            _resultShowModel.OrginDatas = @cases;
            var cornealComputeResults = GetCornealComputeResults();
            var cornealDisplayResult = (_selectedCornealComputeResults?.FirstOrDefault())
                ?? cornealComputeResults.FirstOrDefault()
                ?? _eyeMultiParameterComputeResults.FirstOrDefault();
            _resultShowModel.EyeIrisImage = cornealDisplayResult?.EyeMultiParameterComputeTask.IrisImage;//传入一张图片用于显示
            _resultShowModel.PDAndWTWComputeResult = cornealDisplayResult?.PSAndWTWComputeResult;
            ApplyBlueLedDisplayResultIfAvailable();
            var CircleInfos = new List<CircleInfo>();
            if (_resultShowModel.PDAndWTWComputeResult != null && _resultShowModel.PDAndWTWComputeResult.Length >= 6)
            {
                CircleInfos.Add(new CircleInfo()//PS
                {
                    CenterX = (int)_resultShowModel.PDAndWTWComputeResult[0],
                    CenterY = (int)_resultShowModel.PDAndWTWComputeResult[1],
                    Radius = (int)(_resultShowModel.PDAndWTWComputeResult[2] / 2)
                });
                CircleInfos.Add(new CircleInfo()//WTW
                {
                    CenterX = (int)_resultShowModel.PDAndWTWComputeResult[3],
                    CenterY = (int)_resultShowModel.PDAndWTWComputeResult[4],
                    Radius = (int)(_resultShowModel.PDAndWTWComputeResult[5] / 2)
                });
            }
            //为数据包添加 筛选后的眼睛参数原始数值，用于信号拖动，个人历史查询
            List<float[]> reBuildSignals = new List<float[]>();
            _resultShowModel.EyeParameters = new List<EyeParameters>();

            if (_measureMode == Backend.Model.MeasureMode.AScan)
            {
                int AL_startIndex = 0;
                int CCT_startIndex = 0;
                int ACD_startIndex = 0;
                int LT_startIndex = 0;

                for (int i = 0; i < length; i++)
                {
                    //因为后面信号会拿去拼接，所以无所谓拿的信号是否是同一根了。
                    AL_startIndex = _eyeMultiParameterComputeResults.FindIndex(AL_startIndex, t => t.Case.AxialLength == ALList[i]);
                    CCT_startIndex = _eyeMultiParameterComputeResults.FindIndex(CCT_startIndex, t => t.Case.CentralCornealThickness == CCTList[i]);
                    ACD_startIndex = _eyeMultiParameterComputeResults.FindIndex(ACD_startIndex, t => t.Case.AnteriorchamberDepth == ACDList[i]);
                    LT_startIndex = _eyeMultiParameterComputeResults.FindIndex(LT_startIndex, t => t.Case.LensThickness == LTList[i]);

                    var AL = _eyeMultiParameterComputeResults[AL_startIndex];
                    var CCT = _eyeMultiParameterComputeResults[CCT_startIndex];
                    var ACD = _eyeMultiParameterComputeResults[ACD_startIndex];
                    var LT = _eyeMultiParameterComputeResults[LT_startIndex];
                    var vitreousThickness = ClinicalValueHelper.VitreousThicknessOrError(ALList[i], CCTList[i], ACDList[i], LTList[i]);//行内任一分量为哨兵值时VT置ErrorValue，避免约+1000mm或偏差+1mm的假值进入分析页
                    //var AL = _eyeMultiParameterComputeResults[i];
                    //var CCT = _eyeMultiParameterComputeResults[i];
                    //var ACD = _eyeMultiParameterComputeResults[i];
                    //var LT = _eyeMultiParameterComputeResults[i];
                    //var vitreousThickness = ALList[i] - CCTList[i] / 1000f - ACDList[i] - LTList[i];
                    //添加筛选后的数据
                    _resultShowModel.EyeParameters.Add(new EyeParameters
                    {
                        //轴向参数
                        AxialLength = ALList[i],
                        CentralCornealThickness = CCTList[i],
                        AnteriorchamberDepth = ACDList[i],
                        LensThickness = LTList[i],
                        VitreousThickness = vitreousThickness,
                        //眼表参数
                        LongAxisCornealCurvature = K1List[i],
                        ShortAxisCornealCurvature = K2List[i],
                        LongAxis_Axial_angle_mean = (int)shaftAngleList[i],
                        ShortAxis_Axial_angle_mean = (int)shortAxisShaftAngleList[i],
                        CornealCurvatureD1 = cornealCurvatureD1List[i],
                        CornealCurvatureD2 = cornealCurvatureD2List[i],
                        EyeType = @case.EyeType,
                        SignalPositionInfo = new List<SignalPositionInfo>
                        {
                            AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[0],
                            AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[4],
                            CCT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[0],
                            CCT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[1],
                            ACD?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[1],
                            ACD?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[2],
                            LT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[2],
                            LT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[3],
                        },
                        SignalInfos = new List<SignalPositionInfo>()
                        {
                            AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[0],
                            CCT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[1],
                            ACD?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[2],
                            LT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[3],
                            AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[4],
                        },
                        CircleInfos = CircleInfos,

                    });
                    //拼接信号
                    var indexArrayAL = AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos.Select(o => (o.Position)).ToArray();
                    var indexArrayCCT = CCT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos.Select(o => (o.Position)).ToArray();
                    var indexArrayACD = ACD?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos.Select(o => (o.Position)).ToArray();
                    var indexArrayLT = LT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos.Select(o => (o.Position)).ToArray();
                    var reBuildSignal = SignalFilteringHelper.ReBuildSignal(AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.ProcessedSignal,
                        CCT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.ProcessedSignal,
                        ACD?.EyeMultiParameterComputeTask.SignalProcessComputeResult.ProcessedSignal,
                        LT?.EyeMultiParameterComputeTask.SignalProcessComputeResult.ProcessedSignal
                        , indexArrayAL, indexArrayCCT, indexArrayACD, indexArrayLT);
                    //reBuildSignals.Add(CCT.EyeMultiParameterComputeTask.SignalProcessComputeResult.ProcessedSignal);
                    reBuildSignals.Add(reBuildSignal);
                }
            }
            else
            {
                int AL_startIndex = 0;

                for (int i = 0; i < length; i++)
                {
                    AL_startIndex = _eyeMultiParameterComputeResults.FindIndex(AL_startIndex, t => t.Case.AxialLength == ALList[i]);

                    var AL = _eyeMultiParameterComputeResults[AL_startIndex];

                    if (AL_startIndex != _eyeMultiParameterComputeResults.Count - 1) AL_startIndex++;
                    _resultShowModel.EyeParameters.Add(new EyeParameters
                    {
                        //轴向参数
                        AxialLength = ALList[i],
                        CentralCornealThickness = EyeParameters.ErrorValue,
                        AnteriorchamberDepth = EyeParameters.ErrorValue,
                        LensThickness = EyeParameters.ErrorValue,
                        VitreousThickness = EyeParameters.ErrorValue,
                        //眼表参数
                        LongAxisCornealCurvature = K1List[i],
                        ShortAxisCornealCurvature = K2List[i],
                        LongAxis_Axial_angle_mean = (int)shaftAngleList[i],
                        ShortAxis_Axial_angle_mean = (int)shortAxisShaftAngleList[i],
                        CornealCurvatureD1 = cornealCurvatureD1List[i],
                        CornealCurvatureD2 = cornealCurvatureD2List[i],
                        EyeType = @case.EyeType,
                        SignalPositionInfo = new List<SignalPositionInfo>
                        {
                             AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[0],
                             AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[1],
                        },
                        SignalInfos = new List<SignalPositionInfo>()
                        {
                            AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[0],
                            AL?.EyeMultiParameterComputeTask.SignalProcessComputeResult.SignalPositionInfos[1],
                        },
                        CircleInfos = CircleInfos,
                    });
                    reBuildSignals.Add(AL.EyeMultiParameterComputeTask.SignalProcessComputeResult.ProcessedSignal);
                }
            }

            //错误数据处理。@case和_resultShowModel里的EyeParameters会被修改
            ErrorDataProcess(@case);
            //异常数据处理，@case和_resultShowModel里的EyeParameters会被修改（先错误处理，再异常处理。不可颠倒）
            ExceptionDataProcess(@case);

            //根据外部模式，对后处理的结果进行发出invoke
            if (!_deviceConfigPara.IsCricleRun)//非烤机模式
            {
                //保存信号数据
                SaveSignalData(reBuildSignals, @case, signals);
                _isEyeMultiParameterPacketsComputeFinish = true;
                if (IsAbleToInvoke())
                {
                    Console.WriteLine("多参数计算传出");
                    //计算结果传出，界面两边显示
                    this.PostProcessDataEventHandler?.BeginMulticastInvoke(this, _resultShowModel);
                    //保存图像数据并传出，收到消息后才可以显示测量完成界面
                    SaveImageDataAndInvoke(_resultShowModel.EyeIrisImage, @case);

                }
            }
            else//烤机模式，烤机采集过程中不会闪烁蓝灯，由此计算事件传出
            {
                Console.WriteLine("多参数计算传出");
                this.PostProcessDataEventHandler?.BeginMulticastInvoke(this, _resultShowModel);
                //清除
                Clear();
                Become(Idle);//本actor FlushOnBecome=false，不冲刷队列：下一轮测量的SetCurrentDataInfo/SetPatientInfo等消息可能已入队
            }
        }
        /// <summary>
        /// 多组眼睛参数数据形成一组数据（主界面显示）
        /// </summary>
        /// <param name="ALList">AL数据集合</param>
        /// <param name="CCTList">CCT数据集合</param>
        /// <param name="ACDList">ACD数据集合</param>
        /// <param name="LTList">LT数据集合</param>
        /// <returns>一组多参数数据（最终结果）</returns>
        protected Case DatasToOneCase(List<float> ALList, List<float> CCTList, List<float> ACDList, List<float> LTList, List<float> K1List, out List<float> K2List, List<float> shaftAngleList)
        {
            var cornealComputeResults = GetCornealComputeResults();
            var cornealResultsForAverage = _selectedCornealComputeResults?.Count > 0 ? _selectedCornealComputeResults : cornealComputeResults;
            var psList = cornealResultsForAverage.Select(o => o.Case.PupillaryDiameter).ToList();
            var wtwList = cornealResultsForAverage.Select(o => o.Case.WhiteToWhite).ToList();
            var ixList = cornealResultsForAverage.Select(o => o.Case.Ix).ToList();
            var iyList = cornealResultsForAverage.Select(o => o.Case.Iy).ToList();
            var cwcLenList = cornealResultsForAverage.Select(o => o.Case.CWC_Length).ToList();
            var cwcAngleList = cornealResultsForAverage.Select(o => (float)o.Case.CWC_Angle).ToList();
            K2List = new List<float>();
            //遍历K1所在的计算结果，提取出K2。并为其全部添加补偿因子。（补偿因子应写在correctFileConfig里，但是改了之后，以前出去的机器就无法更新了）
            for (int i = 0; i < K1List.Count; ++i)
            {
                if (K1List[i] == EyeParameters.ErrorValue || float.IsNaN(K1List[i]) || float.IsInfinity(K1List[i]))
                {
                    K2List.Add(EyeParameters.ErrorValue);
                    continue;
                }
                K2List.Add(((cornealResultsForAverage.Find(o => o.Case.LongAxisCornealCurvature == K1List[i])?.Case.ShortAxisCornealCurvature) ?? 0) + _deviceConfigPara.CornealCurvatureCompensationFactor);
                K1List[i] += _deviceConfigPara.CornealCurvatureCompensationFactor;
            }

            Case @case = new Case();
            @case.CaseId = _caseID;
            //多参数
            @case.AxialLength = (float)Math.Round(SignalFilteringHelper.GetClosestDataMean(ALList.GetRange(0, 5).ToArray()), 2);
            @case.CentralCornealThickness = (float)Math.Round(SignalFilteringHelper.GetClosestDataMean(CCTList.GetRange(0, 5).ToArray()));
            @case.AnteriorchamberDepth = (float)Math.Round(SignalFilteringHelper.GetClosestDataMean(ACDList.GetRange(0, 5).ToArray()), 2);
            @case.LensThickness = (float)Math.Round(SignalFilteringHelper.GetClosestDataMean(LTList.GetRange(0, 5).ToArray()), 2);
            @case.VitreousThickness = ClinicalValueHelper.VitreousThicknessOrError(@case.AxialLength, @case.CentralCornealThickness, @case.AnteriorchamberDepth, @case.LensThickness);//任一分量为哨兵值时VT置ErrorValue，避免-1000参与算术产出伪值
            ApplyAveragedCornealResult(@case, cornealResultsForAverage, K1List, K2List, shaftAngleList);
            float D1 = 0; float D2 = 0;
            if (IsValidCornealCurvatureValue(@case.ShortAxisCornealCurvature) && IsValidCornealCurvatureValue(@case.LongAxisCornealCurvature))
            {
                D1 = (float)(1000 * (1.3375f - 1) / @case.ShortAxisCornealCurvature);
                D2 = (float)(1000 * (1.3375f - 1) / @case.LongAxisCornealCurvature);
            }
            if (float.IsNaN(D1) || float.IsInfinity(D1))
            {
                D1 = 0;
            }
            if (float.IsNaN(D2) || float.IsInfinity(D2))
            {
                D2 = 0;
            }
            float cornealAstigmatism = D2 - D1;//AST
            @case.CornealAstigmatism = cornealAstigmatism;
            @case.CornealCurvatureD1 = D1;
            @case.CornealCurvatureD2 = D2;
            //瞳孔和白到白（蓝灯已写出有效PS/WTW时不覆盖；蓝灯未到或未产出有效值时用采集帧中位数兜底）
            if (_blueLedPDAndWTWComputeResult == null)
            {
                @case.PupillaryDiameter = SignalFilteringHelper.FindMidSort(psList) * _deviceConfigPara.WTWSamplingTime;
                @case.WhiteToWhite = SignalFilteringHelper.FindMidSort(wtwList) * _deviceConfigPara.WTWSamplingTime;
            }
            //轴率比（AL或K均值为哨兵值时置ErrorValue，避免-1000参与除法产出垃圾比值）
            @case.AxialToCornealCurvatureRatio = ClinicalValueHelper.RatioOrError(@case.AxialLength, @case.CornealCurvatureMeanValue);
            //屈光度均值
            @case.CornealCurvatureMeanValue = (D1 + D2) / 2;
            //Alpha角 Ix Iy
            @case.Ix = SignalFilteringHelper.FindMidSort(ixList);
            @case.Iy = SignalFilteringHelper.FindMidSort(iyList);
            //kappa角 CWC
            @case.CWC_Angle = (int)Math.Round(SignalFilteringHelper.FindMidSort(cwcAngleList));
            @case.CWC_Length = SignalFilteringHelper.FindMidSort(cwcLenList);
            //测量模式
            @case.Model = _measureMode;
            //左眼或右眼
            @case.EyeType = _eyeType;
            return @case;
        }

        protected void ApplyAveragedCornealResult(
            Case @case,
            List<EyeMultiParameterComputeResult> cornealComputeResults,
            List<float> K1List,
            List<float> K2List,
            List<float> shaftAngleList)
        {
            var averagedLongAxisValues = cornealComputeResults?
                .Select(item => item?.Case == null ? EyeParameters.ErrorValue : item.Case.LongAxisCornealCurvature + _deviceConfigPara.CornealCurvatureCompensationFactor)
                .Where(IsValidCornealCurvatureValue)
                .ToList() ?? new List<float>();
            var averagedShortAxisValues = cornealComputeResults?
                .Select(item => item?.Case == null ? EyeParameters.ErrorValue : item.Case.ShortAxisCornealCurvature + _deviceConfigPara.CornealCurvatureCompensationFactor)
                .Where(IsValidCornealCurvatureValue)
                .ToList() ?? new List<float>();

            if (averagedLongAxisValues.Count > 0 && averagedShortAxisValues.Count > 0)
            {
                @case.LongAxisCornealCurvature = averagedLongAxisValues.Average();
                @case.ShortAxisCornealCurvature = averagedShortAxisValues.Average();
            }
            else
            {
                var validK1List = K1List?.Where(IsValidCornealCurvatureValue).ToList() ?? new List<float>();
                var validK2List = K2List?.Where(IsValidCornealCurvatureValue).ToList() ?? new List<float>();
                if (validK1List.Count > 0 && validK2List.Count > 0)
                {
                    @case.LongAxisCornealCurvature = validK1List.Average();
                    @case.ShortAxisCornealCurvature = validK2List.Average();
                }
                else
                {
                    @case.LongAxisCornealCurvature = EyeParameters.ErrorValue;
                    @case.ShortAxisCornealCurvature = EyeParameters.ErrorValue;
                }
            }

            var averagedAxialAngles = cornealComputeResults?
                .Select(item => item?.Case == null ? EyeParameters.ErrorValue : item.Case.LongAxis_Axial_angle_mean)
                .Where(IsValidAxialAngle)
                .Select(item => (float)item)
                .ToList() ?? new List<float>();
            if (averagedAxialAngles.Count == 0)
            {
                averagedAxialAngles = shaftAngleList?
                    .Where(IsValidAxialAngle)
                    .Select(item => NormalizeAxialAngle(item))
                    .ToList() ?? new List<float>();
            }

            @case.LongAxis_Axial_angle_mean = averagedAxialAngles.Count == 0 ? (int)EyeParameters.ErrorValue : GetAverageAxialAngle(averagedAxialAngles);
            if (IsValidCornealCurvatureValue(@case.LongAxisCornealCurvature) && IsValidCornealCurvatureValue(@case.ShortAxisCornealCurvature))
            {
                if (Math.Abs(@case.LongAxisCornealCurvature - @case.ShortAxisCornealCurvature) <= 0.03f)
                {
                    float cornealCurvatureMeanValue = (@case.LongAxisCornealCurvature + @case.ShortAxisCornealCurvature) / 2f;
                    @case.LongAxisCornealCurvature = cornealCurvatureMeanValue;
                    @case.ShortAxisCornealCurvature = cornealCurvatureMeanValue;
                    @case.LongAxis_Axial_angle_mean = 0;
                }
                @case.CornealCurvatureMeanValue = (@case.LongAxisCornealCurvature + @case.ShortAxisCornealCurvature) / 2f;
            }
            else
            {
                @case.CornealCurvatureMeanValue = EyeParameters.ErrorValue;
            }
            @case.ShortAxis_Axial_angle_mean = GetDisplayShortAxisAngle(@case.LongAxis_Axial_angle_mean);
        }

        protected bool IsValidCornealCurvatureValue(float value)
        {
            return value != EyeParameters.ErrorValue && !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
        }

        protected bool IsValidAxialAngle(float value)
        {
            return value != EyeParameters.ErrorValue && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
        }

        protected float NormalizeAxialAngle(float angle)
        {
            if (!IsValidAxialAngle(angle))
            {
                return 0;
            }

            angle %= 180f;
            if (angle < 0)
            {
                angle += 180f;
            }
            return angle;
        }

        protected int GetAverageAxialAngle(List<float> angles)
        {
            if (angles == null || angles.Count == 0)
            {
                return 0;
            }

            double x = 0;
            double y = 0;
            foreach (var angle in angles)
            {
                double radians = NormalizeAxialAngle(angle) * Math.PI / 90.0;
                x += Math.Cos(radians);
                y += Math.Sin(radians);
            }

            if (Math.Abs(x) < 0.000001 && Math.Abs(y) < 0.000001)
            {
                return (int)Math.Round(NormalizeAxialAngle(angles[0]));
            }

            double averageAngle = Math.Atan2(y, x) * 90.0 / Math.PI;
            if (averageAngle < 0)
            {
                averageAngle += 180.0;
            }

            return (int)Math.Round(averageAngle) % 180;
        }

        protected int GetDisplayShortAxisAngle(int angle)
        {
            if (angle == (int)EyeParameters.ErrorValue)
            {
                return (int)EyeParameters.ErrorValue;
            }

            int normalizedAngle = (int)Math.Round(NormalizeAxialAngle(angle));
            return normalizedAngle == 0 ? 180 : 180 - normalizedAngle;
        }

        /// <summary>
        /// 错误数据处理f(x)=-1000
        /// </summary>
        /// <param name="case"></param>
        protected void ErrorDataProcess(Case @case)
        {
            //角膜曲率D
            if (@case.LongAxisCornealCurvature == EyeParameters.ErrorValue)
            {
                @case.CornealCurvatureD2 = EyeParameters.ErrorValue;
                @case.CornealCurvatureMeanValue = EyeParameters.ErrorValue;
                @case.AxialToCornealCurvatureRatio = EyeParameters.ErrorValue;
                foreach (var item in _resultShowModel.EyeParameters)//5组全赋异常值
                {
                    item.CornealCurvatureD2 = EyeParameters.ErrorValue;
                    item.LongAxis_Axial_angle_mean = (int)EyeParameters.ErrorValue;
                }
            }
            if (@case.ShortAxisCornealCurvature == EyeParameters.ErrorValue)
            {
                @case.CornealCurvatureD1 = EyeParameters.ErrorValue;
                @case.CornealCurvatureMeanValue = EyeParameters.ErrorValue;
                @case.AxialToCornealCurvatureRatio = EyeParameters.ErrorValue;
                foreach (var item in _resultShowModel.EyeParameters)//5组全赋异常值
                {
                    item.CornealCurvatureD1 = EyeParameters.ErrorValue;
                    item.ShortAxis_Axial_angle_mean = (int)EyeParameters.ErrorValue;
                }
            }
            //轴率比
            if (@case.AxialLength == EyeParameters.ErrorValue || @case.CornealCurvatureMeanValue == EyeParameters.ErrorValue)
            {
                @case.AxialToCornealCurvatureRatio = EyeParameters.ErrorValue;
            }
            //VT
            if (@case.AxialLength == EyeParameters.ErrorValue || @case.AnteriorchamberDepth == EyeParameters.ErrorValue || @case.CentralCornealThickness == EyeParameters.ErrorValue || @case.CentralCornealThickness == EyeParameters.ErrorValue)
            {
                @case.VitreousThickness = EyeParameters.ErrorValue;
                foreach (var item in _resultShowModel.EyeParameters)
                {
                    item.VitreousThickness = EyeParameters.ErrorValue;
                }
            }
        }
        /// <summary>
        /// 异常数据处理 f(x)=-x+b ,令f(-1000)=-1000，求得b=-2000
        /// f(x)=-x-2000
        /// </summary>
        /// <param name="case">一组多参数数据（最终结果）</param>
        protected void ExceptionDataProcess(Case @case)
        {
            //多参数，五组都需要异常处理
            //AL@case.AxialLength < 15.0f || @case.AxialLength > 34.0f
            if (@case.AxialLength < 15.0f || @case.AxialLength > 34.0f)//@case.AxialLength > 15.0f && @case.AxialLength < 34.0f
            {
                @case.AxialLength = ExceptionDataEncrypt(@case.AxialLength);
                @case.AxialToCornealCurvatureRatio = ExceptionDataEncrypt(@case.AxialToCornealCurvatureRatio);
                foreach (var item in _resultShowModel.EyeParameters)
                {
                    item.AxialLength = ExceptionDataEncrypt(item.AxialLength);
                }
            }
            //ACD
            if (@case.AnteriorchamberDepth < 2.0f || @case.AnteriorchamberDepth > 4.0f)
            {
                @case.AnteriorchamberDepth = ExceptionDataEncrypt(@case.AnteriorchamberDepth);
                foreach (var item in _resultShowModel.EyeParameters)
                {
                    item.AnteriorchamberDepth = ExceptionDataEncrypt(item.AnteriorchamberDepth);
                }
            }
            //LT
            if (@case.LensThickness < 2.5f || @case.LensThickness > 6.0f)
            {
                @case.LensThickness = ExceptionDataEncrypt(@case.LensThickness);
                foreach (var item in _resultShowModel.EyeParameters)
                {
                    item.LensThickness = ExceptionDataEncrypt(item.LensThickness);
                }
            }
            //CCT
            if (@case.CentralCornealThickness < 400.0f || @case.CentralCornealThickness > 700.0f)
            {
                @case.CentralCornealThickness = ExceptionDataEncrypt(@case.CentralCornealThickness);
                foreach (var item in _resultShowModel.EyeParameters)
                {
                    item.CentralCornealThickness = ExceptionDataEncrypt(item.CentralCornealThickness);
                }
            }
            //VT
            if (@case.AxialLength <= EyeParameters.ExceptionValue || @case.AnteriorchamberDepth <= EyeParameters.ExceptionValue || @case.LensThickness <= EyeParameters.ExceptionValue || @case.CentralCornealThickness <= EyeParameters.ExceptionValue)
            {
                @case.VitreousThickness = ExceptionDataEncrypt(@case.VitreousThickness);
                foreach (var item in _resultShowModel.EyeParameters)
                {
                    item.VitreousThickness = ExceptionDataEncrypt(item.VitreousThickness);
                }
            }
            //角膜曲率
            if (@case.LongAxisCornealCurvature < 6.5 || @case.LongAxisCornealCurvature > 9.5)// @case.LongAxisCornealCurvature < 6.5 || @case.LongAxisCornealCurvature > 9.5
            {
                @case.LongAxisCornealCurvature = ExceptionDataEncrypt(@case.LongAxisCornealCurvature);
                @case.CornealCurvatureD2 = ExceptionDataEncrypt(@case.CornealCurvatureD2);
                @case.CornealCurvatureMeanValue = EyeParameters.ErrorValue;
                @case.AxialToCornealCurvatureRatio = EyeParameters.ErrorValue;
                foreach (var item in _resultShowModel.EyeParameters)//5组全赋异常值
                {
                    item.LongAxisCornealCurvature = ExceptionDataEncrypt(item.LongAxisCornealCurvature);
                    item.CornealCurvatureD2 = ExceptionDataEncrypt(item.CornealCurvatureD2);
                    item.LongAxis_Axial_angle_mean = (int)ExceptionDataEncrypt(item.LongAxis_Axial_angle_mean);
                }
            }
            if (@case.ShortAxisCornealCurvature < 6.5 || @case.ShortAxisCornealCurvature > 9.5)//@case.ShortAxisCornealCurvature < 6.5 || @case.ShortAxisCornealCurvature > 9.5
            {
                @case.ShortAxisCornealCurvature = ExceptionDataEncrypt(@case.ShortAxisCornealCurvature);
                @case.CornealCurvatureD1 = ExceptionDataEncrypt(@case.CornealCurvatureD1);
                @case.CornealCurvatureMeanValue = EyeParameters.ErrorValue;
                @case.AxialToCornealCurvatureRatio = EyeParameters.ErrorValue;
                foreach (var item in _resultShowModel.EyeParameters)//5组全赋异常值
                {
                    item.ShortAxisCornealCurvature = ExceptionDataEncrypt(item.ShortAxisCornealCurvature);
                    item.CornealCurvatureD1 = ExceptionDataEncrypt(item.CornealCurvatureD1);
                    item.ShortAxis_Axial_angle_mean = (int)ExceptionDataEncrypt(item.ShortAxis_Axial_angle_mean);
                }
            }
            //白到白和瞳孔直径
            WTWAndPSExceptionDataProcess(@case);
        }
        /// <summary>
        /// WTW、PS参数异常数据处理
        /// </summary>
        /// <param name="case">一组多参数数据（最终结果）</param>
        protected void WTWAndPSExceptionDataProcess(Case @case)
        {
            //幂等加密：已是-2000-x异常编码或-1000错误值的原样保留，重复调用不会二次加密把超范围值还原成"正常"真值
            @case.WhiteToWhite = ClinicalValueHelper.EncryptExceptionIfOutOfRange(@case.WhiteToWhite, 9.5f, 13.5f);
            @case.PupillaryDiameter = ClinicalValueHelper.EncryptExceptionIfOutOfRange(@case.PupillaryDiameter, 2f, 6.5f);
        }
        /// <summary>
        /// f(x)=-x+b (b=-2000)
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private float ExceptionDataEncrypt(float data)
        {
            return -data + EyeParameters.ExceptionValue;
        }
        /// <summary>
        /// 异步保存数据并发布
        /// </summary>
        /// <param name="eyeIrisImage">眼睛图片</param>
        /// <param name="processedSignals">处理后的信号</param>
        /// <param name="case">一组多参数数据（最终结果）</param>
        /// <param name="signals">多组信号</param>
        protected virtual async void SaveSignalData(List<float[]> processedSignals, Case @case, List<SignalProcessComputeResult> signals)
        {
            //async void：磁盘满/目录占用等IO异常若漏出会作为线程池未处理异常直接杀进程，必须整体兜底
            try
            {
            if (processedSignals == null || signals == null || signals.Count == 0)
            {
                return;
            }
            string Path = ServiceLocator.Current.GetInstance<DeviceConfigPara>().SaveDataPath;
            List<short[]> Channel1List = signals.Select(o => o.DAQData.Channel1).ToList();
            List<short[]> Channel2List = signals.Select(o => o.DAQData.Channel2).ToList();
            List<float[]> ProcessedSignalList = processedSignals;
            await Task.Run(() =>
            {
                if (@case.EyeType == EyeType.右眼)//右眼
                {
                    SaveDateToFileHelper.Save(Channel1List, Path + @case.CaseId, "RightEyeReferenceRulerOriginSignalDate");
                    SaveDateToFileHelper.Save(Channel2List, Path + @case.CaseId, "RightEyeOriginSignal");
                    SaveDateToFileHelper.Save(ProcessedSignalList, Path + @case.CaseId, "RightEyeProcessedSignal");

                    //if (@case.Patient == null)
                    //{
                    //    SaveDataToDICOM.Save(ProcessedSignalList, Path + @case.CaseId, "ODProcessedSignalDICOM", "NULL", "00000000", "NULL", "OD");
                    //}
                    //else
                    //{
                    //    SaveDataToDICOM.Save(ProcessedSignalList, Path + @case.CaseId, "ODProcessedSignalDICOM", @case.Patient.PatientName, @case.Patient.PatientBrith.ToString("yyyyMMdd"), @case.Patient.PatientSex.ToString(), "OD");
                    //}
                }
                else
                {
                    SaveDateToFileHelper.Save(Channel1List, Path + @case.CaseId, "LeftEyeReferenceRulerOriginSignalDate");
                    SaveDateToFileHelper.Save(Channel2List, Path + @case.CaseId, "LeftEyeOriginSignal");
                    SaveDateToFileHelper.Save(ProcessedSignalList, Path + @case.CaseId, "LeftEyeProcessedSignal");

                    //if (@case.Patient == null)
                    //{
                    //    SaveDataToDICOM.Save(ProcessedSignalList, Path + @case.CaseId, "OSProcessedSignalDICOM", "NULL", "00000000", "NULL", "OS");
                    //}
                    //else
                    //{
                    //    SaveDataToDICOM.Save(ProcessedSignalList, Path + @case.CaseId, "OSProcessedSignalDICOM", @case.Patient.PatientName, @case.Patient.PatientBrith.ToString("yyyyMMdd"), @case.Patient.PatientSex.ToString(), "OS");
                    //}
                }
            });
            }
            catch (Exception ex)
            {
                LogHelper.Error("信号数据保存异常(不影响参数入库)", ex);
            }
        }

        protected virtual async void SaveImageDataAndInvoke(float[] eyeIrisImage, Case @case)
        {
            //在actor线程同步捕获本轮代数：保存任务在线程池执行期间若已开始下一轮测量，代数会自增，
            //收尾时据此判定该次保存属于哪一轮，避免清掉新一轮正在采集的数据
            var generationSnapshot = _measurementGeneration;
            //async void：IO异常若漏出会作为线程池未处理异常直接杀进程，必须整体兜底
            try
            {
            var resultShowModel = _resultShowModel;
            var selectedCornealComputeResults = _selectedCornealComputeResults?.ToList() ?? new List<EyeMultiParameterComputeResult>();
            string Path = ServiceLocator.Current.GetInstance<DeviceConfigPara>().SaveDataPath;
            List<float[]> IrisImagesList = eyeIrisImage == null ? new List<float[]>() : new List<float[]> { eyeIrisImage };
            await Task.Run(() =>
            {
                if (eyeIrisImage != null)
                {
                    if (@case.EyeType == EyeType.右眼)//右眼
                    {
                        SaveDateToFileHelper.Save(IrisImagesList, Path + @case.CaseId, "RightEyeIrisImages");

                        if (@case.Patient == null)
                        {
                            SaveDataToDICOM.Save(IrisImagesList, Path + @case.CaseId, "ODIrisDICOM", "NULL", "00000000", "NULL", "OD");
                        }
                        else
                        {
                            SaveDataToDICOM.Save(IrisImagesList, Path + @case.CaseId, "ODIrisDICOM", @case.Patient.PatientName, @case.Patient.PatientBrith.ToString("yyyyMMdd"), @case.Patient.PatientSex.ToString(), "OD");
                        }
                    }
                    else
                    {
                        SaveDateToFileHelper.Save(IrisImagesList, Path + @case.CaseId, "LeftEyeIrisImages");

                        if (@case.Patient == null)
                        {
                            SaveDataToDICOM.Save(IrisImagesList, Path + @case.CaseId, "OSIrisDICOM", "NULL", "00000000", "NULL", "OS");
                        }
                        else
                        {
                            SaveDataToDICOM.Save(IrisImagesList, Path + @case.CaseId, "OSIrisDICOM", @case.Patient.PatientName, @case.Patient.PatientBrith.ToString("yyyyMMdd"), @case.Patient.PatientSex.ToString(), "OS");
                        }
                    }
                }

                SaveCornealCurvatureDataPackage(@case, resultShowModel, selectedCornealComputeResults);
            });
            }
            catch (Exception ex)
            {
                LogHelper.Error("图像数据保存异常(不影响流程收尾)", ex);
            }
            finally
            {
                //保存任务在线程池执行，Clear/Become等Actor状态清理必须回到自身消息队列串行完成，
                //避免与actor线程正在处理的SetPatientInfo/多参数计算并发修改_dataClassifySystem导致崩溃
                ReadSelfEnqueue().Enqueue(new ImageDataSaveCompletedDataPostProcessActor(generationSnapshot));
            }
        }

        /// <summary>
        /// 图像保存完成后的收尾（在actor自身线程串行执行）
        /// </summary>
        internal void CompleteImageDataSave(int generationSnapshot)
        {
            //FinishSaveData在保存完成时触发，语义不变（无论是否为陈旧轮次都照常通知，保持既有行为）
            this.FinishSaveDataEventHandler?.BeginInvoke(this, null, null, null);
            Console.WriteLine("多参数保存完成");
            //期间若已开始下一轮测量（代数已自增），不能清掉新一轮的数据/状态。
            //代数在SetCurrentDataInfo即自增，比原ReferenceEquals(结果集合)更早覆盖
            //“新一轮已SetPatientInfo/开始采集、但多参数计算结果尚未到达（引用未替换）”的窗口
            if (generationSnapshot != _measurementGeneration)
            {
                return;
            }
            Clear();
            Become(Idle);//本actor FlushOnBecome=false，不冲刷队列：下一轮测量的消息可能已入队
        }

        protected void SaveCornealCurvatureDataPackage(Case @case, ResultShowModel resultShowModel, List<EyeMultiParameterComputeResult> selectedCornealComputeResults)
        {
            if (@case == null || !_deviceConfigPara.SaveCornealCurvatureData || selectedCornealComputeResults == null || selectedCornealComputeResults.Count == 0)
            {
                return;
            }

            try
            {
                string eyeFolderName = @case.EyeType == EyeType.右眼 ? "RightEyeCornealCurvatureData" : "LeftEyeCornealCurvatureData";
                string targetFolderPath = Path.Combine(
                    _deviceConfigPara.SaveDataPath,
                    @case.CaseId.ToString(),
                    eyeFolderName,
                    DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture));
                var abnormalCaptureStageImageTasks = (ModuleFactory.Instance.GetModule(ModuleOptions.CaptureData) as CaptureDataModule)?
                    .GetAbnormalCaptureStageImageTasks() ?? new List<CornealCurvatureImageTask>();

                Directory.CreateDirectory(targetFolderPath);

                for (int i = 0; i < selectedCornealComputeResults.Count; i++)
                {
                    var irisImage = selectedCornealComputeResults[i]?.EyeMultiParameterComputeTask?.IrisImage;
                    if (irisImage == null || irisImage.Length != CornealImageWidth * CornealImageHeight)
                    {
                        continue;
                    }

                    using (Bitmap bitmap = BitmapHelpers.ToGrayBitmap(irisImage, CornealImageWidth, CornealImageHeight))
                    {
                        bitmap.Save(Path.Combine(targetFolderPath, $"Frame{i + 1:00}.png"), ImageFormat.Png);
                    }
                }

                for (int i = 0; i < abnormalCaptureStageImageTasks.Count; i++)
                {
                    var irisImage = abnormalCaptureStageImageTasks[i]?.IrisImage;
                    if (irisImage == null || irisImage.Length != CornealImageWidth * CornealImageHeight)
                    {
                        continue;
                    }

                    using (Bitmap bitmap = BitmapHelpers.ToGrayBitmap(irisImage, CornealImageWidth, CornealImageHeight))
                    {
                        bitmap.Save(
                            Path.Combine(
                                targetFolderPath,
                                $"CaptureDistanceGT100_{i + 1:00}_D{Math.Abs(abnormalCaptureStageImageTasks[i].Distance).ToString("0.000", CultureInfo.InvariantCulture)}.png"),
                            ImageFormat.Png);
                    }
                }

                File.WriteAllText(
                    Path.Combine(targetFolderPath, "CornealCurvatureReport.txt"),
                    BuildCornealCurvatureReport(@case, resultShowModel, selectedCornealComputeResults),
                    Encoding.UTF8);
            }
            catch (Exception e)
            {
                LogHelper.Error("SaveCornealCurvatureDataPackage异常" + e.Message + e.StackTrace);
            }
        }

        protected string BuildCornealCurvatureReport(Case @case, ResultShowModel resultShowModel, List<EyeMultiParameterComputeResult> selectedCornealComputeResults)
        {
            var builder = new StringBuilder();
            builder.AppendLine("角膜曲率数据导出");
            builder.AppendLine(new string('=', 80));
            AppendReportItem(builder, "保存时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            AppendReportItem(builder, "CaseId", @case.CaseId.ToString());
            AppendReportItem(builder, "眼别", @case.EyeType.ToString());
            AppendReportItem(builder, "测量模式", @case.Model.ToString());
            AppendReportItem(builder, "最终入选图像数", selectedCornealComputeResults.Count.ToString(CultureInfo.InvariantCulture));
            AppendSelectedCornealFramesSection(builder, selectedCornealComputeResults);

            return builder.ToString();
        }

        protected void AppendCaseSection(StringBuilder builder, string title, Case caseData)
        {
            builder.AppendLine();
            builder.AppendLine($"[{title}]");
            builder.AppendLine(new string('-', 80));
            if (caseData == null)
            {
                builder.AppendLine("无");
                return;
            }

            AppendReportItem(builder, "AL(mm)", FormatMeasureValue(caseData.AxialLength, "0.00"));
            AppendReportItem(builder, "CCT(um)", FormatMeasureValue(caseData.CentralCornealThickness, "0"));
            AppendReportItem(builder, "ACD(mm)", FormatMeasureValue(caseData.AnteriorchamberDepth, "0.00"));
            AppendReportItem(builder, "LT(mm)", FormatMeasureValue(caseData.LensThickness, "0.00"));
            AppendReportItem(builder, "VT(mm)", FormatMeasureValue(caseData.VitreousThickness, "0.00"));
            AppendReportItem(builder, "WTW(mm)", FormatMeasureValue(caseData.WhiteToWhite, "0.00"));
            AppendReportItem(builder, "PS(mm)", FormatMeasureValue(caseData.PupillaryDiameter, "0.00"));
            AppendReportItem(builder, "K1(D/mm/@)", $"{FormatMeasureValue(caseData.CornealCurvatureD2, "0.00")} / {FormatMeasureValue(caseData.LongAxisCornealCurvature, "0.00")} / {FormatMeasureValue(caseData.LongAxis_Axial_angle_mean, "0")}");
            AppendReportItem(builder, "K2(D/mm/@)", $"{FormatMeasureValue(caseData.CornealCurvatureD1, "0.00")} / {FormatMeasureValue(caseData.ShortAxisCornealCurvature, "0.00")} / {FormatMeasureValue(caseData.ShortAxis_Axial_angle_mean, "0")}");
            AppendReportItem(builder, "AVG_K(D)", FormatMeasureValue(caseData.CornealCurvatureMeanValue, "0.00"));
            AppendReportItem(builder, "AL/CR", FormatMeasureValue(caseData.AxialToCornealCurvatureRatio, "0.00"));
            AppendReportItem(builder, "AST(D)", FormatMeasureValue(caseData.CornealAstigmatism, "0.00"));
            AppendReportItem(builder, "Ix(mm)", FormatMeasureValue(caseData.Ix, "0.00"));
            AppendReportItem(builder, "Iy(mm)", FormatMeasureValue(caseData.Iy, "0.00"));
            AppendReportItem(builder, "CWC_Length(mm)", FormatMeasureValue(caseData.CWC_Length, "0.00"));
            AppendReportItem(builder, "CWC_Angle(deg)", FormatMeasureValue(caseData.CWC_Angle, "0"));
        }

        protected void AppendEyeParametersSection(StringBuilder builder, string title, List<EyeParameters> eyeParameters)
        {
            builder.AppendLine();
            builder.AppendLine($"[{title}]");
            builder.AppendLine(new string('-', 80));
            if (eyeParameters == null || eyeParameters.Count == 0)
            {
                builder.AppendLine("无");
                return;
            }

            for (int i = 0; i < eyeParameters.Count; i++)
            {
                var item = eyeParameters[i];
                builder.AppendLine($"第{i + 1}组");
                AppendReportItem(builder, "AL(mm)", FormatMeasureValue(item.AxialLength, "0.00"));
                AppendReportItem(builder, "CCT(um)", FormatMeasureValue(item.CentralCornealThickness, "0"));
                AppendReportItem(builder, "ACD(mm)", FormatMeasureValue(item.AnteriorchamberDepth, "0.00"));
                AppendReportItem(builder, "LT(mm)", FormatMeasureValue(item.LensThickness, "0.00"));
                AppendReportItem(builder, "VT(mm)", FormatMeasureValue(item.VitreousThickness, "0.00"));
                AppendReportItem(builder, "K1(D/mm/@)", $"{FormatMeasureValue(item.CornealCurvatureD2, "0.00")} / {FormatMeasureValue(item.LongAxisCornealCurvature, "0.00")} / {FormatMeasureValue(item.LongAxis_Axial_angle_mean, "0")}");
                AppendReportItem(builder, "K2(D/mm/@)", $"{FormatMeasureValue(item.CornealCurvatureD1, "0.00")} / {FormatMeasureValue(item.ShortAxisCornealCurvature, "0.00")} / {FormatMeasureValue(item.ShortAxis_Axial_angle_mean, "0")}");
                AppendReportItem(builder, "峰位信息", FormatSignalInfos(item.SignalInfos));
                AppendReportItem(builder, "拼接峰位信息", FormatSignalInfos(item.SignalPositionInfo));
                builder.AppendLine(new string('-', 80));
            }
        }

        protected void AppendOriginCasesSection(StringBuilder builder, string title, List<Case> originCases)
        {
            builder.AppendLine();
            builder.AppendLine($"[{title}]");
            builder.AppendLine(new string('-', 80));
            if (originCases == null || originCases.Count == 0)
            {
                builder.AppendLine("无");
                return;
            }

            for (int i = 0; i < originCases.Count; i++)
            {
                var item = originCases[i];
                builder.AppendLine($"第{i + 1}组原始Case");
                AppendReportItem(builder, "AL(mm)", FormatMeasureValue(item.AxialLength, "0.00"));
                AppendReportItem(builder, "CCT(um)", FormatMeasureValue(item.CentralCornealThickness, "0"));
                AppendReportItem(builder, "ACD(mm)", FormatMeasureValue(item.AnteriorchamberDepth, "0.00"));
                AppendReportItem(builder, "LT(mm)", FormatMeasureValue(item.LensThickness, "0.00"));
                AppendReportItem(builder, "VT(mm)", FormatMeasureValue(item.VitreousThickness, "0.00"));
                AppendReportItem(builder, "WTW(mm)", FormatMeasureValue(item.WhiteToWhite, "0.00"));
                AppendReportItem(builder, "PS(mm)", FormatMeasureValue(item.PupillaryDiameter, "0.00"));
                AppendReportItem(builder, "K1(D/mm/@)", $"{FormatMeasureValue(item.CornealCurvatureD2, "0.00")} / {FormatMeasureValue(item.LongAxisCornealCurvature, "0.00")} / {FormatMeasureValue(item.LongAxis_Axial_angle_mean, "0")}");
                AppendReportItem(builder, "K2(D/mm/@)", $"{FormatMeasureValue(item.CornealCurvatureD1, "0.00")} / {FormatMeasureValue(item.ShortAxisCornealCurvature, "0.00")} / {FormatMeasureValue(item.ShortAxis_Axial_angle_mean, "0")}");
                AppendReportItem(builder, "Ix(mm)", FormatMeasureValue(item.Ix, "0.00"));
                AppendReportItem(builder, "Iy(mm)", FormatMeasureValue(item.Iy, "0.00"));
                AppendReportItem(builder, "CWC_Length(mm)", FormatMeasureValue(item.CWC_Length, "0.00"));
                AppendReportItem(builder, "CWC_Angle(deg)", FormatMeasureValue(item.CWC_Angle, "0"));
                builder.AppendLine(new string('-', 80));
            }
        }

        protected void AppendSelectedCornealFramesSection(StringBuilder builder, List<EyeMultiParameterComputeResult> selectedCornealComputeResults)
        {
            builder.AppendLine();
            builder.AppendLine("[最终入选5帧图像原始数据]");
            builder.AppendLine(new string('-', 80));
            if (selectedCornealComputeResults == null || selectedCornealComputeResults.Count == 0)
            {
                builder.AppendLine("无");
                return;
            }

            for (int i = 0; i < selectedCornealComputeResults.Count; i++)
            {
                var result = selectedCornealComputeResults[i];
                var task = result?.EyeMultiParameterComputeTask;
                var cornealCurvatureComputeResult = result?.CornealCurvatureComputeResult;
                bool hasRawCornealCurvatureData = result != null && result.LightSpotDistance.HasValue;
                builder.AppendLine($"Frame{i + 1:00}.png");
                AppendReportItem(builder, "D(mm)", FormatSignedValue(task?.CornealCurvatureDistance));
                AppendReportItem(builder, "光点距离", hasRawCornealCurvatureData ? FormatRawValue(result.LightSpotDistance, "0.000") : "-");
                AppendReportItem(builder, "内环长轴半径(mm)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.InnerCornealCurvatureParses.LongAxis, "0.000") : "-");
                AppendReportItem(builder, "内环短轴半径(mm)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.InnerCornealCurvatureParses.ShortAxis, "0.000") : "-");
                AppendReportItem(builder, "内环长轴(px)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.InnerCornealCurvatureParses.SemiLongAxisPixel * 2, "0.000") : "-");
                AppendReportItem(builder, "内环短轴(px)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.InnerCornealCurvatureParses.SemiShortAxisPixel * 2, "0.000") : "-");
                AppendReportItem(builder, "内环轴位角(deg)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.InnerCornealCurvatureParses.Xtheta, "0.000") : "-");
                AppendReportItem(builder, "外环长轴半径(mm)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.OuterCornealCurvatureParses.LongAxis, "0.000") : "-");
                AppendReportItem(builder, "外环短轴半径(mm)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.OuterCornealCurvatureParses.ShortAxis, "0.000") : "-");
                AppendReportItem(builder, "外环长轴(px)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.OuterCornealCurvatureParses.SemiLongAxisPixel * 2, "0.000") : "-");
                AppendReportItem(builder, "外环短轴(px)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.OuterCornealCurvatureParses.SemiShortAxisPixel * 2, "0.000") : "-");
                AppendReportItem(builder, "外环轴位角(deg)", hasRawCornealCurvatureData ? FormatRawValue(cornealCurvatureComputeResult.OuterCornealCurvatureParses.Xtheta, "0.000") : "-");
                builder.AppendLine(new string('-', 80));
            }
        }

        protected void AppendReportItem(StringBuilder builder, string label, string value)
        {
            builder.AppendLine($"{label.PadRight(24)}: {value}");
        }

        protected string FormatSignalInfos(List<SignalPositionInfo> signalInfos)
        {
            if (signalInfos == null || signalInfos.Count == 0)
            {
                return "-";
            }

            var validSignalInfos = signalInfos.Where(item => item != null).ToList();
            if (validSignalInfos.Count == 0)
            {
                return "-";
            }

            return string.Join(", ", validSignalInfos
                .Select((item, index) => $"P{index + 1}:{item.Position}/{item.Value.ToString("0.##", CultureInfo.InvariantCulture)}"));
        }

        protected string FormatSignedValue(float? value)
        {
            if (!value.HasValue)
            {
                return "-";
            }

            return value.Value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        }

        protected string FormatRawValue(float? value, string format)
        {
            if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
            {
                return "-";
            }

            return value.Value.ToString(format, CultureInfo.InvariantCulture);
        }

        protected string FormatRawValue(double value, string format)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "-";
            }

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        protected string FormatMeasureValue(float value, string format)
        {
            if (value == EyeParameters.ErrorValue)
            {
                return "ERR";
            }

            if (value <= EyeParameters.ExceptionValue)
            {
                return (EyeParameters.ExceptionValue - value).ToString(format, CultureInfo.InvariantCulture) + "*";
            }

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        protected string FormatMeasureValue(int value, string format)
        {
            return FormatMeasureValue((float)value, format);
        }

        /// <summary>
        /// 数据集合填充
        /// </summary>
        /// <param name="datas">数据集合</param>
        /// <param name="value">需填充的值</param>
        /// <param name="multiParameterComputeResult">填充值所在的多参数集合</param>
        protected void DataPopulate(List<float> datas, float value, EyeMultiParameterComputeResult multiParameterComputeResult)
        {
            if (datas.Count < 5 && datas.Count >= 1)//1≤x<5，填充相似数据
            {
                for (int i = datas.Count; i < 5; i++)
                {
                    datas.Add(value);
                    _eyeMultiParameterComputeResults.Add(multiParameterComputeResult);
                }
            }
            else if (datas.Count == 0)//x==0，填充异常数据
            {
                for (int i = 0; i < 5; i++)
                {
                    datas.Add(EyeParameters.ErrorValue);
                }
            }
            else//x>=5
            {
                //do nothing
            }
        }

        protected bool IsAbleToInvoke()
        {
            return _deviceConfigPara.IsBlueLEDMeasure ? (_isEyeMultiParameterPacketsComputeFinish && _isPSAndWTWCalculateFinish) : _isEyeMultiParameterPacketsComputeFinish;
        }

        #endregion
    }
}

