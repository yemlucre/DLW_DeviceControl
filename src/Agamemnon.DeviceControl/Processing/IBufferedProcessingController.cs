using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agamemnon.DeviceControl;

/// <summary>
/// 缓冲加工的一段：起点/终点（各轴目标，mm）+ 门宽数量（单位 62.5 µs/个，只携带数量；
/// 像素模式、16 倍数门宽等产品策略属于工艺流程层，本接口不校验）+ 速度 mm/s。
/// </summary>
public sealed record BufferedSegment(IReadOnlyDictionary<string, double> FromMm, IReadOnlyDictionary<string, double> ToMm,
    int GateCount, double SpeedMmPerS);

/// <summary>缓冲加工程序：段序列 + LaserEnabled。LaserEnabled=false 为干跑（不请求激光输出）。</summary>
public sealed record BufferedProgram(IReadOnlyList<BufferedSegment> Segments, bool LaserEnabled);

/// <summary>加工执行状态：运行标志、干跑标志、完成段数/总段数与状态文字。</summary>
public sealed record ProcessingStatus(bool Running, bool DryRun, int CompletedSegments, int TotalSegments,
    bool LaserRequested, string State);

/// <summary>一次运行的结果：Completed=全部完成；Stopped=被 Stop 打断；Cancelled=取消；完成段数如实报告。</summary>
public sealed record ProcessingRunResult(bool Completed, bool Stopped, bool Cancelled,
    int CompletedSegments, int TotalSegments, string Error);

/// <summary>
/// 缓冲加工执行接口。语义对齐现行加工执行契约：默认干跑；真实激光输出需程序 LaserEnabled=true；
/// 运行中拒绝新程序；Stop 是请求停止并让在途 RunAsync 如实返回；无部分成功伪装。
/// </summary>
public interface IBufferedProcessingController : IDevice
{
    ProcessingStatus Status { get; }
    /// <summary>执行整个程序并等待完成；运行中、程序非法或未连接时显式拒绝。</summary>
    Task<ProcessingRunResult> RunAsync(BufferedProgram program, CancellationToken cancellation = default);
    /// <summary>请求停止当前运行；返回是否确有运行被打断。</summary>
    bool Stop();
}
