using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agamemnon.DeviceControl;

/// <summary>单轴状态。可空字段=null 表示未知，不得当作 false；Fault 为 null 表示无已知故障。位置/速度单位 mm、mm/s。</summary>
public sealed record StageAxisState(string AxisId, double PositionMm, double VelocityMmPerS,
    bool? Powered, bool? InTarget, bool Moving, string Fault, double MinimumMm, double MaximumMm);

/// <summary>位移台快照：轴状态 + 忙碌标志 + 观测时间。快照是某时刻观测，不保证读取期间设备不变。</summary>
public sealed record StageSnapshot(IReadOnlyList<StageAxisState> Axes, bool Busy, DateTimeOffset ObservedAt);

/// <summary>运动结果对象：请求/有效目标（mm）按轴给出；AnyClamped 表示有轴被行程钳制；拒绝时给出中文原因。</summary>
public sealed record StageMoveResult(bool Accepted, string Error,
    IReadOnlyDictionary<string, double> RequestedMm, IReadOnlyDictionary<string, double> EffectiveMm,
    bool AnyClamped, StageSnapshot Snapshot);

/// <summary>
/// 位移台接口（坐标一律 mm，速度 mm/s）。语义对齐现行位移台契约：失败显式（非法参数 ArgumentException、
/// 非法状态 InvalidOperationException 或 Accepted=false 结果）；目标按轴行程钳制并报告 WasClamped 语义；
/// 硬件回零由 <see cref="HardwareHomingAvailable"/> 声明，不可用时调用显式拒绝；软件置零重定义坐标而不动电机。
/// </summary>
public interface IStageController : IDevice
{
    IReadOnlyList<string> AxisIds { get; }
    /// <summary>硬件回零能力；仿真驱动为 false。</summary>
    bool HardwareHomingAvailable { get; }
    StageSnapshot Snapshot();
    /// <summary>多轴绝对运动（mm）。目标超出行程时钳制到行程内并报告；需要目标轴已上电。</summary>
    Task<StageMoveResult> MoveAbsoluteAsync(IReadOnlyDictionary<string, double> targetsMm, System.Threading.CancellationToken cancellation = default);
    /// <summary>多轴相对运动（mm），基于当前快照位置计算。</summary>
    Task<StageMoveResult> MoveRelativeAsync(IReadOnlyDictionary<string, double> deltasMm, System.Threading.CancellationToken cancellation = default);
    /// <summary>启动持续 JOG（速度符号=方向）；JogStop 或 StopAll 结束。需要轴已上电。</summary>
    StageMoveResult JogStart(string axisId, double velocityMmPerS);
    StageMoveResult JogStop(string axisId);
    /// <summary>停止所有轴的在途运动。</summary>
    StageMoveResult StopAll();
    /// <summary>轴上/下电。下电后该轴运动被拒绝。</summary>
    StageMoveResult SetAxisPower(string axisId, bool on);
    /// <summary>软件置零：当前坐标重定义为 0，行程边界同步平移（保持物理端点），电机不动。</summary>
    StageMoveResult SetSoftwareZero(string axisId);
    /// <summary>硬件回零：不可用（HardwareHomingAvailable=false）时显式拒绝。</summary>
    Task<StageMoveResult> HomeAsync(string axisId, System.Threading.CancellationToken cancellation = default);
}
