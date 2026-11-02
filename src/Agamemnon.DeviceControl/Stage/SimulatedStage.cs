using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Agamemnon.DeviceControl;

/// <summary>
/// 仿真位移台（DEMO）：三轴 X/Y/Z，演示行程 X/Y ±10 mm、Z 0–10 mm（仿真值，不是任何设备规格）。
/// 只模拟状态语义：绝对/相对运动瞬时完成并报告钳制；JOG 只置位 Moving/速度，不积分物理轨迹；
/// 初始上电状态未知（Powered=null），运动前需 SetAxisPower(true)。硬件回零不可用。
/// </summary>
public sealed class SimulatedStage : DeviceBase, IStageController
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AxisSim> _axes = new()
    {
        ["X"] = new AxisSim(-10, 10), ["Y"] = new AxisSim(-10, 10), ["Z"] = new AxisSim(0, 10)
    };
    public const double MaxJogVelocityMmPerS = 50;

    private sealed class AxisSim
    {
        public double Position, Minimum, Maximum, Velocity;
        public bool? Powered; public bool? InTarget; public bool Moving;
        public AxisSim(double minimum, double maximum) { Minimum = minimum; Maximum = maximum; Position = minimum; InTarget = null; }
        public StageAxisState ToState(string id) => new(id, Position, Velocity, Powered, InTarget, Moving, null, Minimum, Maximum);
    }

    public SimulatedStage() : base(new DeviceIdentity("Stage", "DEMO-STAGE", "simulated", true)) { }

    public IReadOnlyList<string> AxisIds { get; } = new[] { "X", "Y", "Z" };
    public bool HardwareHomingAvailable => false;
    public override bool IsBusy { get { lock (_gate) return _axes.Values.Any(a => a.Moving); } }

    public StageSnapshot Snapshot()
    {
        EnsureNotDisposed();
        lock (_gate) return new(_axes.Keys.Select(id => _axes[id].ToState(id)).ToArray(), _axes.Values.Any(a => a.Moving), DateTimeOffset.UtcNow);
    }

    public async Task<StageMoveResult> MoveAbsoluteAsync(IReadOnlyDictionary<string, double> targetsMm, CancellationToken cancellation = default)
        => await MoveAsync(targetsMm, null, cancellation).ConfigureAwait(false);

    public async Task<StageMoveResult> MoveRelativeAsync(IReadOnlyDictionary<string, double> deltasMm, CancellationToken cancellation = default)
        => await MoveAsync(deltasMm, 1, cancellation).ConfigureAwait(false);

    private async Task<StageMoveResult> MoveAsync(IReadOnlyDictionary<string, double> values, int? mode, CancellationToken cancellation)
    {
        ValidateTargets(values);
        EnsureConnected();
        lock (_gate)
        {
            foreach (var id in values.Keys)
            {
                var axis = _axes[id];
                if (axis.Moving) return Reject("轴 " + id + " 正在 JOG，先停止再运动。");
                if (axis.Powered != true) return Reject("轴 " + id + " 未上电，不能运动。");
            }
            if (cancellation.IsCancellationRequested) return Reject("运动已取消。");
        }
        await Task.Delay(1).ConfigureAwait(false);
        lock (_gate)
        {
            var requested = new Dictionary<string, double>();
            var effective = new Dictionary<string, double>();
            bool clamped = false;
            foreach (var (id, value) in values)
            {
                var axis = _axes[id];
                double target = mode == null ? value : axis.Position + value;
                double bounded = Math.Clamp(target, axis.Minimum, axis.Maximum);
                if (bounded != target) clamped = true;
                requested[id] = target; effective[id] = bounded;
                axis.Position = bounded; axis.Velocity = 0; axis.InTarget = true; axis.Moving = false;
            }
            return new StageMoveResult(true, null, requested, effective, clamped, SnapshotLocked());
        }
    }

    public StageMoveResult JogStart(string axisId, double velocityMmPerS)
    {
        var axis = AxisOf(axisId);
        EnsureConnected();
        if (!double.IsFinite(velocityMmPerS) || velocityMmPerS == 0 || Math.Abs(velocityMmPerS) > MaxJogVelocityMmPerS)
            throw new ArgumentOutOfRangeException(nameof(velocityMmPerS), "JOG 速度需为非零有限值，绝对值不超过 " + MaxJogVelocityMmPerS + " mm/s。");
        lock (_gate)
        {
            if (axis.Powered != true) return Reject("轴 " + axisId + " 未上电，不能 JOG。");
            if (axis.Moving) return Reject("轴 " + axisId + " 已在 JOG。");
            axis.Moving = true; axis.Velocity = velocityMmPerS; axis.InTarget = false;
            return new StageMoveResult(true, null, Empty(), Empty(), false, SnapshotLocked());
        }
    }

    public StageMoveResult JogStop(string axisId)
    {
        var axis = AxisOf(axisId);
        lock (_gate)
        {
            axis.Moving = false; axis.Velocity = 0; axis.InTarget = true;
            return new StageMoveResult(true, null, Empty(), Empty(), false, SnapshotLocked());
        }
    }

    public StageMoveResult StopAll()
    {
        lock (_gate)
        {
            foreach (var axis in _axes.Values) { axis.Moving = false; axis.Velocity = 0; axis.InTarget = true; }
            return new StageMoveResult(true, null, Empty(), Empty(), false, SnapshotLocked());
        }
    }

    public StageMoveResult SetAxisPower(string axisId, bool on)
    {
        var axis = AxisOf(axisId);
        EnsureConnected();
        lock (_gate)
        {
            axis.Powered = on;
            if (!on) { axis.Moving = false; axis.Velocity = 0; }
            return new StageMoveResult(true, null, Empty(), Empty(), false, SnapshotLocked());
        }
    }

    public StageMoveResult SetSoftwareZero(string axisId)
    {
        var axis = AxisOf(axisId);
        EnsureConnected();
        lock (_gate)
        {
            double shift = axis.Position;
            axis.Minimum -= shift; axis.Maximum -= shift; axis.Position = 0; axis.InTarget = true;
            return new StageMoveResult(true, null, Empty(), Empty(), false, SnapshotLocked());
        }
    }

    public Task<StageMoveResult> HomeAsync(string axisId, CancellationToken cancellation = default)
    {
        AxisOf(axisId);
        throw new InvalidOperationException("仿真驱动不支持硬件回零（HardwareHomingAvailable=false）。");
    }

    private StageMoveResult Reject(string error)
        => new(false, error, Empty(), Empty(), false, SnapshotLocked());
    private static IReadOnlyDictionary<string, double> Empty() => new Dictionary<string, double>();
    private StageSnapshot SnapshotLocked()
        => new(_axes.Keys.Select(id => _axes[id].ToState(id)).ToArray(), _axes.Values.Any(a => a.Moving), DateTimeOffset.UtcNow);
    private void ValidateTargets(IReadOnlyDictionary<string, double> values)
    {
        if (values == null || values.Count == 0) throw new ArgumentException("至少需要一个轴目标。");
        foreach (var (id, value) in values)
        {
            if (!_axes.ContainsKey(id)) throw new ArgumentException("未知轴：" + id, nameof(values));
            if (!double.IsFinite(value)) throw new ArgumentException("轴 " + id + " 的目标必须为有限值。", nameof(values));
        }
    }
    private AxisSim AxisOf(string axisId)
        => _axes.TryGetValue(axisId, out var axis) ? axis : throw new ArgumentException("未知轴：" + axisId, nameof(axisId));

    protected override void OnDisconnecting()
    {
        lock (_gate)
        {
            foreach (var axis in _axes.Values) { axis.Moving = false; axis.Velocity = 0; axis.Powered = null; axis.InTarget = null; }
        }
    }
}
