using System;
using System.Collections.Generic;

namespace Agamemnon.DeviceControl;

/// <summary>统一连接状态。Faulted 表示设备报错但对象仍可尝试重新 Connect。</summary>
public enum DeviceConnectionState { Disconnected, Connecting, Connected, Faulted }

/// <summary>设备身份：类别/型号/地址；IsSimulated=true 表示仿真实现（只模拟状态语义，不代表硬件规格）。</summary>
public sealed record DeviceIdentity(string Kind, string Model, string Address, bool IsSimulated);

/// <summary>最近一次状态观测：状态 + 消息 + 时间。消息为中文原因，未知信息显式出现在 Message 而不是被省略。</summary>
public sealed record DeviceStatus(DeviceConnectionState State, string Message, DateTimeOffset ObservedAt);

/// <summary>
/// 统一设备接口（每台设备一个对象）：连接生命周期 + 状态模型。语义约定与现行 Core 契约一致：
/// Connect 在已连接时幂等、对象释放后拒绝重连（ObjectDisposedException）；Disconnect 幂等并停止在途动作；
/// 实现必须显式失败（非法参数 ArgumentException、非法状态 InvalidOperationException），不得假装成功。
/// 阻塞调用不应在 UI 线程执行。
/// </summary>
public interface IDevice : IDisposable
{
    /// <summary>稳定设备标识（Kind:Model）。</summary>
    string Id { get; }
    DeviceIdentity Identity { get; }
    DeviceConnectionState State { get; }
    /// <summary>最近一次状态观测（含断开/故障原因）。</summary>
    DeviceStatus LastStatus { get; }
    bool IsConnected { get; }
    bool IsSimulated { get; }
    /// <summary>是否有在途动作（采集/运动/加工）。只读观测，不用于同步。</summary>
    bool IsBusy { get; }
    void Connect();
    void Disconnect();
    /// <summary>任何状态转换（含连接、断开、故障、忙碌变化）后触发。</summary>
    event EventHandler<DeviceStatus> StatusChanged;
}
