using System;
using System.Threading;
using System.Threading.Tasks;

namespace Agamemnon.DeviceControl;

/// <summary>激光门读回：On=null 表示未知（读取失败或端口不可读），绝不能当作关闭。Error 携带中文原因。</summary>
public sealed record LaserGateState(bool? On, int? RawPort, string Error);

/// <summary>
/// 激光门接口（控制器数字输出位）。语义对齐现行契约：置位/读回只证明输出位状态，
/// 不移动轴、也不证明实际出光；接线/极性须实机确认。未知状态必须显式报告。
/// </summary>
public interface ILaserGateController : IDevice
{
    /// <summary>设置输出位并读回。要求已连接。</summary>
    Task<LaserGateState> SetAsync(bool on, CancellationToken cancellation = default);
    /// <summary>只读回当前输出位状态。</summary>
    Task<LaserGateState> ReadAsync(CancellationToken cancellation = default);
    /// <summary>最近已知状态；未知为 null（断开后为 null）。</summary>
    bool? LastKnownOn { get; }
}
