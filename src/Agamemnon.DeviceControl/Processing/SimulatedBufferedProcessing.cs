using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Agamemnon.DeviceControl;

/// <summary>
/// 仿真缓冲加工（DEMO）：每段消耗极短时间后计为完成；支持 Stop/取消打断并如实报告完成段数。
/// 不驱动任何真实轴或激光；干跑/出光仅反映程序标志（LaserRequested），不证明物理出光。
/// </summary>
public sealed class SimulatedBufferedProcessing : DeviceBase, IBufferedProcessingController
{
    private readonly object _gate = new();
    private bool _running;
    private int _completed;
    private int _total;
    private bool _laserRequested;
    private bool _stopRequested;
    public const int MillisecondsPerSegment = 2;

    public SimulatedBufferedProcessing() : base(new DeviceIdentity("Processing", "DEMO-CNC", "simulated", true)) { }

    public ProcessingStatus Status
    {
        get
        {
            lock (_gate) return new ProcessingStatus(_running, _running && !_laserRequested, _completed, _total,
                _laserRequested, _running ? (_laserRequested ? "运行中（请求出光）" : "运行中（干跑）") : "空闲");
        }
    }
    public override bool IsBusy { get { lock (_gate) return _running; } }

    public async Task<ProcessingRunResult> RunAsync(BufferedProgram program, CancellationToken cancellation = default)
    {
        if (program == null) throw new ArgumentNullException(nameof(program));
        Validate(program);
        EnsureConnected();
        lock (_gate)
        {
            if (_running) throw new InvalidOperationException("已有程序在运行。");
            _running = true; _completed = 0; _total = program.Segments.Count; _laserRequested = program.LaserEnabled; _stopRequested = false;
        }
        Publish(DeviceConnectionState.Connected, _laserRequested ? "加工运行（请求出光）" : "加工运行（干跑）");
        try
        {
            foreach (var _ in program.Segments)
            {
                if (cancellation.IsCancellationRequested) return Finish(false, false, true, "程序已取消。");
                lock (_gate) if (_stopRequested) return Finish(false, true, false, "程序被停止。");
                await Task.Delay(MillisecondsPerSegment, CancellationToken.None).ConfigureAwait(false);
                lock (_gate) _completed++;
            }
            return Finish(true, false, false, null);
        }
        finally
        {
            lock (_gate) _running = false;
            Publish(DeviceConnectionState.Connected, "空闲");
        }
    }

    public bool Stop()
    {
        lock (_gate)
        {
            if (!_running) return false;
            _stopRequested = true;
            return true;
        }
    }

    private ProcessingRunResult Finish(bool completed, bool stopped, bool cancelled, string error)
    {
        int done, total;
        lock (_gate) { done = _completed; total = _total; }
        return new ProcessingRunResult(completed, stopped, cancelled, done, total, error);
    }

    private static void Validate(BufferedProgram program)
    {
        if (program.Segments == null || program.Segments.Count == 0) throw new ArgumentException("程序至少需要一段。", nameof(program));
        foreach (var (segment, index) in program.Segments.Select((s, i) => (s, i)))
        {
            ValidatePoints(segment.FromMm, program, index, "From");
            ValidatePoints(segment.ToMm, program, index, "To");
            if (segment.FromMm.Count != segment.ToMm.Count || segment.FromMm.Keys.Any(k => !segment.ToMm.ContainsKey(k)))
                throw new ArgumentException("第 " + (index + 1) + " 段起点/终点轴集合必须一致。", nameof(program));
            if (segment.GateCount < 0) throw new ArgumentException("第 " + (index + 1) + " 段门宽数量不能为负。", nameof(program));
            if (!double.IsFinite(segment.SpeedMmPerS) || segment.SpeedMmPerS <= 0)
                throw new ArgumentException("第 " + (index + 1) + " 段速度需为正的有限值（mm/s）。", nameof(program));
        }
    }

    private static void ValidatePoints(IReadOnlyDictionary<string, double> points, BufferedProgram program, int index, string label)
    {
        if (points == null || points.Count == 0)
            throw new ArgumentException("第 " + (index + 1) + " 段" + label + " 至少需要一个轴坐标。", nameof(program));
        foreach (var (axis, value) in points)
        {
            if (string.IsNullOrWhiteSpace(axis)) throw new ArgumentException("第 " + (index + 1) + " 段" + label + " 轴名不能为空。", nameof(program));
            if (!double.IsFinite(value)) throw new ArgumentException("第 " + (index + 1) + " 段" + label + " 轴 " + axis + " 坐标必须为有限值（mm）。", nameof(program));
        }
    }
}
