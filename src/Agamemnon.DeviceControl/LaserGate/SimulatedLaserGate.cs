using System;
using System.Threading;
using System.Threading.Tasks;

namespace Agamemnon.DeviceControl;

/// <summary>
/// 仿真激光门（DEMO）：单个输出位，置位/读回一致。提供测试用故障注入开关
/// SimulateReadbackFailure=true 后读回报告未知（On=null + Error），用于验证调用方不把未知当关闭。
/// </summary>
public sealed class SimulatedLaserGate : DeviceBase, ILaserGateController
{
    private readonly object _gate = new();
    private bool _bit;
    /// <summary>测试钩子：置 true 后所有读回报告未知。</summary>
    public bool SimulateReadbackFailure { get; set; }

    public SimulatedLaserGate() : base(new DeviceIdentity("LaserGate", "DEMO-GATE", "bit0", true)) { }

    public bool? LastKnownOn { get { lock (_gate) return IsConnected && !SimulateReadbackFailure ? _bit : null; } }
    public override bool IsBusy => false;

    public Task<LaserGateState> SetAsync(bool on, CancellationToken cancellation = default)
    {
        EnsureConnected();
        if (cancellation.IsCancellationRequested) return Task.FromResult(new LaserGateState(null, null, "设置已取消。"));
        lock (_gate) _bit = on;
        return ReadAsync(cancellation);
    }

    public Task<LaserGateState> ReadAsync(CancellationToken cancellation = default)
    {
        EnsureConnected();
        lock (_gate)
        {
            if (SimulateReadbackFailure) return Task.FromResult(new LaserGateState(null, null, "仿真读回失败：状态未知。"));
            return Task.FromResult(new LaserGateState(_bit, _bit ? 1 : 0, null));
        }
    }

    protected override void OnDisconnecting()
    {
        lock (_gate) _bit = false;
    }
}
