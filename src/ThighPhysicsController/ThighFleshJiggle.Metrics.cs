using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using static ThighPhysicsController.FleshSafetyGuard;

namespace ThighPhysicsController;

/// <summary>Runtime sampling and log emission, isolated from solver integration.</summary>
public sealed partial class ThighFleshJiggle
{
    private delegate long AllocatedBytesReader();

    private static readonly AllocatedBytesReader AllocationReader = CreateAllocationReader();
    private static readonly string MemoryMeasurementSource = AllocationReader == null
        ? "mono_heap_delta"
        : "thread_allocated";

    private float _metricElapsed;
    private int _metricSamples;
    private double _metricSum;
    private double _metricSumSquares;
    private double _metricSumX;
    private double _metricSumY;
    private double _metricSumZ;
    private float _metricPeak;
    private int _metricSafetyResets;
    private int _metricReanchors;
    private float _metricWarmupRemaining;
    private float _performanceElapsed;
    private int _performanceSamples;
    private double _performanceTotalMicroseconds;
    private double _performanceMaxMicroseconds;
    private long _performanceAllocatedBytes;
    private long _performanceMaxAllocatedBytes;
    private int _performanceAllocationSamples;

    private static AllocatedBytesReader CreateAllocationReader()
    {
        try
        {
            MethodInfo method = typeof(GC).GetMethod(
                "GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            return method == null
                ? null
                : (AllocatedBytesReader)Delegate.CreateDelegate(typeof(AllocatedBytesReader), method);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static long ReadAllocatedBytes()
    {
        if (AllocationReader != null)
        {
            return AllocationReader();
        }
        try
        {
            // Old Koikatu Mono lacks GetAllocatedBytesForCurrentThread. Sampling
            // immediately around the solver still provides a useful heap-delta
            // trend, but it is explicitly labelled global rather than exact allocation.
            return Profiler.GetMonoUsedSizeLong();
        }
        catch (Exception)
        {
            return -1L;
        }
    }

    private void RecordSolverDuration(long started, long allocatedBefore, string mode)
    {
        long allocatedAfter = ReadAllocatedBytes();
        if (!ThighPhysicsControllerPlugin.DebugCollectMetrics.Value ||
            _metricWarmupRemaining > 0f)
        {
            ResetPerformanceWindow();
            return;
        }
        double microseconds = (Stopwatch.GetTimestamp() - started) * 1000000d /
                              Stopwatch.Frequency;
        if (microseconds < 0d || double.IsNaN(microseconds) || double.IsInfinity(microseconds))
        {
            return;
        }
        _performanceSamples++;
        _performanceTotalMicroseconds += microseconds;
        if (microseconds > _performanceMaxMicroseconds)
        {
            _performanceMaxMicroseconds = microseconds;
        }
        if (allocatedBefore >= 0L && allocatedAfter >= allocatedBefore)
        {
            long allocated = allocatedAfter - allocatedBefore;
            _performanceAllocationSamples++;
            _performanceAllocatedBytes += allocated;
            if (allocated > _performanceMaxAllocatedBytes)
            {
                _performanceMaxAllocatedBytes = allocated;
            }
        }
        _performanceElapsed += Mathf.Min(Time.deltaTime, 0.05f);
        if (_performanceElapsed < 5f)
        {
            return;
        }
        double mean = _performanceTotalMicroseconds / Math.Max(1, _performanceSamples);
        double allocatedPerFrame = _performanceAllocationSamples == 0
            ? -1d
            : (double)_performanceAllocatedBytes / _performanceAllocationSamples;
        ThighPhysicsControllerPlugin.LogRuntime("FPC_PERF part=" + FleshPartDef.Get(_partId).DisplayName +
            " mode=" + mode +
            " seconds=" + _performanceElapsed.ToString("F2") +
            " samples=" + _performanceSamples +
            " mean_us=" + mean.ToString("F3") +
            " max_us=" + _performanceMaxMicroseconds.ToString("F3") +
            " memory_source=" + MemoryMeasurementSource +
            " memory_bpf=" + allocatedPerFrame.ToString("F3") +
            " max_memory_bytes=" + (_performanceAllocationSamples == 0
                ? "-1"
                : _performanceMaxAllocatedBytes.ToString()));
        ResetPerformanceWindow();
    }

    private void RecordMetric(Vector3 value)
    {
        if (_metricWarmupRemaining > 0f || IsNan(value))
        {
            return;
        }
        float magnitude = value.magnitude;
        if (!FleshValue.IsFinite(magnitude))
            return;
        _metricSamples++;
        _metricSum += magnitude;
        _metricSumSquares += magnitude * magnitude;
        _metricSumX += value.x;
        _metricSumY += value.y;
        _metricSumZ += value.z;
        if (magnitude > _metricPeak)
        {
            _metricPeak = magnitude;
        }
    }

    private void FlushMetrics(float dt, string mode)
    {
        if (!ThighPhysicsControllerPlugin.DebugCollectMetrics.Value)
        {
            ResetMetricWindow();
            return;
        }
        if (_metricWarmupRemaining > 0f)
        {
            _metricWarmupRemaining = Mathf.Max(0f, _metricWarmupRemaining - dt);
            ResetMetricWindow();
            return;
        }
        _metricElapsed += dt;
        if (_metricElapsed < 5f)
        {
            return;
        }
        double divisor = Math.Max(1, _metricSamples);
        double mean = _metricSum / divisor;
        double rms = Math.Sqrt(_metricSumSquares / divisor);
        double meanX = _metricSumX / divisor;
        double meanY = _metricSumY / divisor;
        double meanZ = _metricSumZ / divisor;
        double bias = Math.Sqrt(meanX * meanX + meanY * meanY + meanZ * meanZ);
        double dynamicRms = Math.Sqrt(Math.Max(0d, rms * rms - bias * bias));
        float strength = FleshTuning.GetStrength(ParamsRef);
        float softness = FleshTuning.GetSoftness(ParamsRef, _partId);
        ThighPhysicsControllerPlugin.LogRuntime("FPC_METRIC part=" + FleshPartDef.Get(_partId).DisplayName +
            " mode=" + mode +
            " seconds=" + _metricElapsed.ToString("F2") +
            " samples=" + _metricSamples +
            " mean=" + mean.ToString("F6") +
            " rms=" + rms.ToString("F6") +
            " bias=" + bias.ToString("F6") +
            " dynamic=" + dynamicRms.ToString("F6") +
            " peak=" + _metricPeak.ToString("F6") +
            " resets=" + _metricSafetyResets +
            " reanchors=" + _metricReanchors +
            " strength=" + strength.ToString("F3") +
            " softness=" + softness.ToString("F3") +
            " motion_target=" + FleshTuning.GetMotionTarget(ParamsRef).ToString("F3") +
            " motion_raw=" + ParamsRef.MotionGain.ToString("F3"));
        ResetMetricWindow();
    }

    private void ResetMetricWindow()
    {
        _metricElapsed = 0f;
        _metricSamples = 0;
        _metricSum = 0d;
        _metricSumSquares = 0d;
        _metricSumX = 0d;
        _metricSumY = 0d;
        _metricSumZ = 0d;
        _metricPeak = 0f;
        _metricSafetyResets = 0;
        _metricReanchors = 0;
    }

    private void ResetPerformanceWindow()
    {
        _performanceElapsed = 0f;
        _performanceSamples = 0;
        _performanceTotalMicroseconds = 0d;
        _performanceMaxMicroseconds = 0d;
        _performanceAllocatedBytes = 0L;
        _performanceMaxAllocatedBytes = 0L;
        _performanceAllocationSamples = 0;
    }
}
