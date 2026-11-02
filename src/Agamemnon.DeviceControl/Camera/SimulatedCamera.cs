using System;
using System.Collections.Generic;
using System.Linq;

namespace Agamemnon.DeviceControl;

/// <summary>
/// 仿真相机（DEMO）：只模拟连接/采集/参数状态语义，帧为确定性合成图案，不代表任何传感器规格。
/// </summary>
public sealed class SimulatedCamera : DeviceBase, ICameraDevice
{
    private readonly object _gate = new();
    private bool _capturing;
    private long _frameIndex;
    private readonly Dictionary<string, double> _values = new()
    { ["exposure.ms"] = 10, ["gain.db"] = 0 };
    private static readonly CameraParameterInfo[] Catalog =
    {
        new("exposure.ms", "曝光时间", 0.1, 1000, 0.1, "ms"),
        new("gain.db", "增益", 0, 24, 0.1, "dB")
    };

    public SimulatedCamera() : base(new DeviceIdentity("Camera", "DEMO-CAM", "simulated", true)) { }

    public bool IsCapturing { get { lock (_gate) return _capturing; } }
    public override bool IsBusy => IsCapturing;
    public event EventHandler<CameraFrame> FrameArrived;
    public IReadOnlyList<CameraParameterInfo> Parameters => Catalog;

    public void StartCapture()
    {
        EnsureConnected();
        lock (_gate) _capturing = true;
        Publish(DeviceConnectionState.Connected, "采集中");
    }

    public void StopCapture()
    {
        lock (_gate) _capturing = false;
        if (IsConnected) Publish(DeviceConnectionState.Connected, "已停止采集");
    }

    public CameraFrame GrabFrame()
    {
        EnsureConnected();
        bool capturing;
        long index;
        lock (_gate) { capturing = _capturing; if (!capturing) throw new InvalidOperationException("采集未开始，不能取帧。"); index = ++_frameIndex; }
        const int width = 64, height = 48;
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = (byte)((x + y * 3 + index * 7) % 256);
        var frame = new CameraFrame(width, height, pixels, index);
        FrameArrived?.Invoke(this, frame);
        return frame;
    }

    public double ReadParameter(string key)
    {
        EnsureConnected();
        lock (_gate) return ValueOf(key);
    }

    public void WriteParameter(string key, double value)
    {
        EnsureConnected();
        var info = InfoOf(key);
        if (!double.IsFinite(value) || value < info.Minimum || value > info.Maximum ||
            Math.Abs(value / info.Step - Math.Round(value / info.Step)) > 1e-6)
            throw new ArgumentOutOfRangeException(nameof(key), $"{info.DisplayName} 需在 {info.Minimum}–{info.Maximum} {info.Unit} 内且符合步进 {info.Step}。");
        lock (_gate) _values[info.Key] = value;
    }

    private double ValueOf(string key)
    {
        var info = InfoOf(key);
        return _values.TryGetValue(info.Key, out var value) ? value : throw new ArgumentException("未知相机参数：" + key, nameof(key));
    }
    private CameraParameterInfo InfoOf(string key)
        => Catalog.FirstOrDefault(p => p.Key == key) ?? throw new ArgumentException("未知相机参数：" + key, nameof(key));

    protected override void OnDisconnecting()
    {
        lock (_gate) _capturing = false;
    }
}
