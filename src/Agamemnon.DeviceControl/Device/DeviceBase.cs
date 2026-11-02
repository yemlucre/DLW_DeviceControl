using System;

namespace Agamemnon.DeviceControl;

/// <summary>
/// <see cref="IDevice"/> 的共用实现基座：统一的状态机、幂等连接/断开、释放后拒绝重连与状态事件。
/// 子类通过 OnConnecting/OnDisconnecting/OnBusy 执行实际逻辑；非法状态一律显式抛出。
/// </summary>
public abstract class DeviceBase : IDevice
{
    private readonly object _gate = new();
    private DeviceConnectionState _state = DeviceConnectionState.Disconnected;
    private DeviceStatus _last = new(DeviceConnectionState.Disconnected, "未连接", DateTimeOffset.UtcNow);
    private bool _disposed;

    protected DeviceBase(DeviceIdentity identity)
        => Identity = identity ?? throw new ArgumentNullException(nameof(identity));

    public DeviceIdentity Identity { get; }
    public string Id => Identity.Kind + ":" + Identity.Model;
    public DeviceConnectionState State { get { lock (_gate) return _state; } }
    public DeviceStatus LastStatus { get { lock (_gate) return _last; } }
    public bool IsConnected => State == DeviceConnectionState.Connected;
    public bool IsSimulated => Identity.IsSimulated;
    public abstract bool IsBusy { get; }
    public event EventHandler<DeviceStatus> StatusChanged;

    public void Connect()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(Id, "设备对象已释放，不能重新连接。");
            if (_state == DeviceConnectionState.Connected) return;
            _state = DeviceConnectionState.Connecting;
        }
        Publish(DeviceConnectionState.Connecting, "连接中");
        try { OnConnecting(); }
        catch
        {
            Publish(DeviceConnectionState.Faulted, "连接失败");
            throw;
        }
        Publish(DeviceConnectionState.Connected, "已连接");
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(Id, "设备对象已释放。");
            if (_state == DeviceConnectionState.Disconnected) return;
        }
        try { OnDisconnecting(); }
        finally { Publish(DeviceConnectionState.Disconnected, "已断开"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { OnDisconnecting(); } catch { /* 释放路径不再抛出 */ }
        lock (_gate) { if (_state != DeviceConnectionState.Disconnected) _state = DeviceConnectionState.Disconnected; }
    }

    /// <summary>校验未释放；释放后的任何操作拒绝。</summary>
    protected void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(Id, "设备对象已释放。");
    }
    /// <summary>校验已连接；未连接/故障状态的设备操作显式拒绝。</summary>
    protected void EnsureConnected()
    {
        EnsureNotDisposed();
        if (!IsConnected) throw new InvalidOperationException("设备未连接。");
    }
    /// <summary>发布新状态并触发事件；子类用它上报忙碌/故障变化。</summary>
    protected void Publish(DeviceConnectionState state, string message)
    {
        var status = new DeviceStatus(state, message, DateTimeOffset.UtcNow);
        lock (_gate) { _state = state; _last = status; }
        StatusChanged?.Invoke(this, status);
    }
    /// <summary>连接建立时执行（如打开通道）。抛出则进入 Faulted。</summary>
    protected virtual void OnConnecting() { }
    /// <summary>断开/释放前执行：必须停止在途动作并释放资源；不应抛出。</summary>
    protected virtual void OnDisconnecting() { }
}
