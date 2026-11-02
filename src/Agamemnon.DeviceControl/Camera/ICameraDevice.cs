using System;
using System.Collections.Generic;

namespace Agamemnon.DeviceControl;

/// <summary>相机帧：8 位灰度，行主序，长度 = Width × Height。帧内容属于设备数据，不是设备规格。</summary>
public sealed record CameraFrame(int WidthPx, int HeightPx, byte[] Gray8, long FrameIndex);

/// <summary>相机参数元数据：Key 稳定、范围与步进由实现声明；动态范围以运行时读取为准。</summary>
public sealed record CameraParameterInfo(string Key, string DisplayName, double Minimum, double Maximum, double Step, string Unit);

/// <summary>
/// 相机设备接口：连接/采集生命周期。语义对齐现行相机契约：未连接不允许采集；Start/Stop 幂等；
/// 断开停止采集并清空状态；保存真实传感器图像的能力由具体实现声明，仿真实现不得假装能存真实帧。
/// </summary>
public interface ICameraDevice : IDevice
{
    bool IsCapturing { get; }
    /// <summary>开始采集；已采集时幂等。需要已连接。</summary>
    void StartCapture();
    /// <summary>停止采集；未采集时幂等。</summary>
    void StopCapture();
    /// <summary>拉取一帧；需要正在采集。实现保证返回确定性数据并在成功时触发 <see cref="FrameArrived"/>。</summary>
    CameraFrame GrabFrame();
    /// <summary>每次成功取帧后触发（拉取模式）。</summary>
    event EventHandler<CameraFrame> FrameArrived;
    /// <summary>实现支持的参数目录（静态元数据）。</summary>
    IReadOnlyList<CameraParameterInfo> Parameters { get; }
    /// <summary>读取参数当前值；需要已连接。</summary>
    double ReadParameter(string key);
    /// <summary>写入参数（按目录范围与步进校验）；需要已连接。</summary>
    void WriteParameter(string key, double value);
}
