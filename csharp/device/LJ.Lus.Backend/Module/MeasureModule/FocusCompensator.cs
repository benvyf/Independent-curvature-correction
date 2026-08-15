using LJ.Lus.Backend.Acquire.Actors.MCU;
using LJ.Lus.Backend.Basic;
using LJ.Lus.Backend.Compute.Actors.Algorithm;
using LJ.Lus.Backend.Model;
using LJ.Lus.Backend.Module.ModuleManager;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LJ.Lus.Backend.Module.MeasureModule
{
    /// <summary>
    /// 对焦补偿器：封装Y轴对焦补偿、角膜曲率图像缓存判断、异常距离图像缓存等对焦相关逻辑。
    /// 本身不是模块，由 CaptureDataModule 持有并在相机帧事件中调用。
    /// </summary>
    public class FocusCompensator
    {
        #region Constants
        private const float AcquireDistanceLimit = 0.4f;
        private const float SaveAbnormalCaptureDistanceLimit = 100f;
        private const int FocusFrameSampleCount = 5; // 5帧中位数，允许2帧异常
        private const float EnlageNum = 1.667f;
        #endregion

        #region Fields
        private readonly int _xMovePixelStep = (int)(44 * EnlageNum);
        private readonly int _zMovePixelStep = (int)(220 * EnlageNum);
        private readonly List<(float distance, int xDiff, int yDiff)> _focusFrameSamples = new List<(float, int, int)>();
        private int _offsetX = 0;
        private int _offsetZ = 0;
        private MeasureMode _measureMode;
        private bool _isCornealImageCaptureEnabled = false;
        // 坏帧过滤
        private const int CenterJumpThreshold = 30;
        private const int CenterRangeMin = 80;
        private const int CenterRangeMaxX = 560;
        private const int CenterRangeMaxY = 400;
        private const int MaxConsecutiveInvalidFrames = 15;
        private int _lastValidCenterX = 320;
        private int _lastValidCenterY = 240;
        private bool _hasLastValidCenter = false;
        private int _pendingBaselineCenterX = 320;
        private int _pendingBaselineCenterY = 240;
        private bool _hasPendingBaselineCenter = false;
        private int _consecutiveInvalidFrames = 0;
        #endregion

        #region Public State
        public volatile bool IsNeedNewImage = false;
        public volatile bool IsFocusStable = false;
        public float LastStableDistance = 0f;
        #endregion

        private static bool HasValidDistance(float distance) => distance != 0f;

        /// <summary>
        /// 重置对焦参数，在 CaptureDataModule.ParasReset 中调用。
        /// </summary>
        public void Reset(int offsetX, int offsetZ, MeasureMode measureMode)
        {
            _offsetX = offsetX;
            _offsetZ = offsetZ;
            _measureMode = measureMode;
            _focusFrameSamples.Clear();
            IsFocusStable = false;
            IsNeedNewImage = true;
            LastStableDistance = 0f;
            _isCornealImageCaptureEnabled = measureMode == MeasureMode.AK || measureMode == MeasureMode.AScan;
            _lastValidCenterX = 320;
            _lastValidCenterY = 240;
            _hasLastValidCenter = false;
            _pendingBaselineCenterX = 320;
            _pendingBaselineCenterY = 240;
            _hasPendingBaselineCenter = false;
            _consecutiveInvalidFrames = 0;
        }

        /// <summary>
        /// 禁用角膜图像缓存（采集完成或超时时调用）。
        /// </summary>
        public void DisableCornealImageCapture()
        {
            _isCornealImageCaptureEnabled = false;
        }

        /// <summary>
        /// 在相机帧事件中调用，执行对焦补偿逻辑。
        /// 包括：角膜曲率图像缓存、异常距离图像缓存、3帧中位数对焦决策。
        /// </summary>
        /// <param name="irisGrayImage">当前帧虹膜灰度图像</param>
        /// <param name="captureModule">CaptureDataModule 实例，用于缓存图像</param>
        /// <param name="isFinish">模块是否已完成</param>
        /// <param name="isBlueLEDMeasuring">是否正在蓝灯测量</param>
        public void ProcessFrame(float[] irisGrayImage, CaptureDataModule captureModule, bool isFinish, bool isBlueLEDMeasuring)
        {
            if (!IsNeedNewImage || MCUActor.Instance.IsMotor3dWorking || isFinish) return;
            IsNeedNewImage = false;

            // 每帧只计算一次光斑，结果同时用于角膜曲率图像缓存判断和对焦补偿
            int[] center = new int[2];
            var distance = MessageDispatcher.Send(new LightSpotComputeAlgorithmActor(irisGrayImage, center));

            // 坏帧过滤：中心超出有效范围 或 帧间跳变过大 则丢弃
            bool isFrameValid = true;
            if (center[0] < CenterRangeMin || center[0] > CenterRangeMaxX ||
                center[1] < CenterRangeMin || center[1] > CenterRangeMaxY)
            {
                isFrameValid = false;
            }
            else if (_hasLastValidCenter)
            {
                int jumpX = Math.Abs(center[0] - _lastValidCenterX);
                int jumpY = Math.Abs(center[1] - _lastValidCenterY);
                if (jumpX > CenterJumpThreshold || jumpY > CenterJumpThreshold)
                {
                    isFrameValid = false;
                }
            }

            if (!isFrameValid)
            {
                if (!_hasLastValidCenter && _hasPendingBaselineCenter)
                {
                    _hasPendingBaselineCenter = false;
                }
                _consecutiveInvalidFrames++;
                Console.WriteLine($"[FocusCompensator] Bad frame filtered. center=({center[0]},{center[1]}), lastValid=({_lastValidCenterX},{_lastValidCenterY}), consecutiveInvalid={_consecutiveInvalidFrames}");
                if (_consecutiveInvalidFrames >= MaxConsecutiveInvalidFrames)
                {
                    _hasLastValidCenter = false;
                    _hasPendingBaselineCenter = false;
                    _consecutiveInvalidFrames = 0;
                    _focusFrameSamples.Clear();
                    IsFocusStable = false;
                    LastStableDistance = 0f;
                    Console.WriteLine("[FocusCompensator] Too many consecutive invalid frames. Reset baseline and stable focus state.");
                }
                IsNeedNewImage = true;
                return;
            }

            if (!_hasLastValidCenter)
            {
                _consecutiveInvalidFrames = 0;
                if (!_hasPendingBaselineCenter)
                {
                    _pendingBaselineCenterX = center[0];
                    _pendingBaselineCenterY = center[1];
                    _hasPendingBaselineCenter = true;
                    Console.WriteLine($"[FocusCompensator] Baseline candidate captured. center=({center[0]},{center[1]})");
                    IsNeedNewImage = true;
                    return;
                }

                int pendingJumpX = Math.Abs(center[0] - _pendingBaselineCenterX);
                int pendingJumpY = Math.Abs(center[1] - _pendingBaselineCenterY);
                if (pendingJumpX > CenterJumpThreshold || pendingJumpY > CenterJumpThreshold)
                {
                    int previousCandidateX = _pendingBaselineCenterX;
                    int previousCandidateY = _pendingBaselineCenterY;
                    _pendingBaselineCenterX = center[0];
                    _pendingBaselineCenterY = center[1];
                    Console.WriteLine($"[FocusCompensator] Baseline candidate replaced. center=({center[0]},{center[1]}), previousCandidate=({previousCandidateX},{previousCandidateY})");
                    IsNeedNewImage = true;
                    return;
                }

                _lastValidCenterX = center[0];
                _lastValidCenterY = center[1];
                _hasLastValidCenter = true;
                _hasPendingBaselineCenter = false;
                Console.WriteLine($"[FocusCompensator] Baseline confirmed. center=({center[0]},{center[1]})");
            }
            else
            {
                _consecutiveInvalidFrames = 0;
                _lastValidCenterX = center[0];
                _lastValidCenterY = center[1];
                _hasLastValidCenter = true;
            }

            int xDiff = center[0] + _offsetX - 320;
            int yDiff = 240 + _offsetZ - center[1];

            // 角膜曲率图像缓存：焦距好时缓存
            TryCaptureCornealCurvatureImage(irisGrayImage, captureModule, isBlueLEDMeasuring, distance, xDiff, yDiff);

            // 异常距离图像缓存
            if (Math.Abs(distance) > SaveAbnormalCaptureDistanceLimit)
                captureModule.TryCacheAbnormalCaptureStageImage(irisGrayImage, distance);

            // Y轴对焦补偿：累积3帧取中位数再决策
            ProcessFocusCompensation(captureModule, distance, xDiff, yDiff);
        }

        /// <summary>
        /// 焦距好时缓存角膜曲率图像。
        /// </summary>
        private void TryCaptureCornealCurvatureImage(float[] irisGrayImage, CaptureDataModule captureModule, bool isBlueLEDMeasuring,
            float distance, int xDiff, int yDiff)
        {
            if ((_measureMode != MeasureMode.AK && _measureMode != MeasureMode.AScan) || !_isCornealImageCaptureEnabled || isBlueLEDMeasuring)
            {
                return;
            }
            if (!HasValidDistance(distance) || Math.Abs(distance) >= AcquireDistanceLimit || xDiff * xDiff + yDiff * yDiff > 5 * 5)
            {
                return;
            }

            var imageCopy = new float[irisGrayImage.Length];
            Buffer.BlockCopy(
                irisGrayImage, 0,
                imageCopy, 0,
                irisGrayImage.Length * sizeof(float)
            );

            captureModule.StoreCornealCurvatureImageTask(imageCopy, distance);
        }

        /// <summary>
        /// Y轴对焦补偿：累积3帧取中位数再决策，不依赖信号触发。
        /// </summary>
        private void ProcessFocusCompensation(CaptureDataModule captureModule, float distance, int xDiff, int yDiff)
        {
            _focusFrameSamples.Add((distance, xDiff, yDiff));
            if (_focusFrameSamples.Count < FocusFrameSampleCount)
            {
                IsNeedNewImage = true;
                return;
            }

            // 各维度独立取中位数，防止单帧异常污染其他维度
            int mid = FocusFrameSampleCount / 2;
            var medianDistance = _focusFrameSamples.OrderBy(s => s.distance).ToList()[mid].distance;
            var medianXDiff = _focusFrameSamples.OrderBy(s => s.xDiff).ToList()[mid].xDiff;
            var medianYDiff = _focusFrameSamples.OrderBy(s => s.yDiff).ToList()[mid].yDiff;
            _focusFrameSamples.Clear();

            bool hasValidMedianDistance = HasValidDistance(medianDistance);
            if (!hasValidMedianDistance || Math.Abs(medianDistance) >= AcquireDistanceLimit || medianXDiff * medianXDiff + medianYDiff * medianYDiff > 5 * 5)
            {
                IsFocusStable = false;
                LastStableDistance = 0f;
                if (!hasValidMedianDistance && medianXDiff * medianXDiff + medianYDiff * medianYDiff <= 5 * 5)
                {
                    IsNeedNewImage = true;
                    return;
                }
                var d = Math.Abs(medianDistance) > 3.5f ? 0 : medianDistance;
                Console.WriteLine("distance :" + medianDistance + " mm");
                int xSteps = medianXDiff * _xMovePixelStep;
                int zSteps = medianYDiff * _zMovePixelStep;
                int ySteps = -(int)((d * 6400) / (10f / 8));
                MCUActor.Instance.IsMotor3dWorking = true;
                MessageDispatcher.Send(new Motor3dMoveFiniteMCUActor(xSteps, ySteps, zSteps));
            }
            else
            {
                LastStableDistance = medianDistance;
                IsFocusStable = true;
            }
            IsNeedNewImage = true;
        }
    }
}

