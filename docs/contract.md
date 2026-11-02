# Agamemnon.DeviceControl 设备控制包契约

检查日期：2026-11-02。能力 ID：`device.control_package`（implemented，agentExposed=false）。本文件是 `Function/Agamemnon.DeviceControl`（现独立仓库 DLW_DeviceControl）的接口契约与边界记录。

## 定位与边界

- **职责**：**每台设备一个接口 + 统一连接/状态模型**。四类设备接口：相机、位移台、激光门、缓冲加工执行；统一 `IDevice`。
- **依赖**：net8.0 纯 BCL，零其他引用；无 UI/厂商 SDK。
- **实现策略**：提供**仿真实现**，只模拟状态语义、不模拟物理轨迹，不代表任何硬件规格；真实厂商驱动适配（Tucam/Agito）属后续任务。

## 公开接口

| 接口/类型 | 实现 | 说明 |
|---|---|---|
| `IDevice` + `DeviceBase` | 基座 | `DeviceConnectionState`(Disconnected/Connecting/Connected/Faulted)、`DeviceIdentity`、`DeviceStatus`(状态+消息+时间)、`StatusChanged`；Connect 幂等、释放后拒绝重连、断开停止在途动作 |
| `ICameraDevice` | `SimulatedCamera` | 采集生命周期（未连接拒绝、幂等启停）、`GrabFrame()` 确定性 Gray8 帧 + `FrameArrived`、参数目录（范围/步进校验）；断开清空采集状态 |
| `IStageController` | `SimulatedStage` | 轴快照（mm；Powered/InTarget 可空=未知）、绝对/相对运动（行程钳制 + `AnyClamped`）、JOG（≤50 mm/s）、StopAll、软件置零（行程同步平移）、上下电、`HardwareHomingAvailable=false` 时回零显式拒绝 |
| `ILaserGateController` | `SimulatedLaserGate` | 置位+读回（`bool? On`，未知=显式 null 不当 false）；断开后状态未知；`SimulateReadbackFailure` 测试钩子 |
| `IBufferedProcessingController` | `SimulatedBufferedProcessing` | 段式程序（From/To mm + 门宽数量 + 速度）；干跑/出光标志、Stop/取消如实报告部分完成、运行中拒绝新程序、程序结构校验 |

## 单位、坐标系与诚实性约定

- 位移/加工坐标一律 **mm**、速度 mm/s；门宽数量单位 62.5 µs/个，**只携带数量**（像素模式、16 倍数门宽等产品策略属工艺流程层，本包不校验）。
- 可空状态（Powered/InTarget/On）=null 表示**未知**，不得当作 false。
- 非法参数抛 `ArgumentException` 族；非法状态抛 `InvalidOperationException` 或 `Accepted=false` 结果对象；运动/拒绝结果始终携带快照。
- 激光门置位/读回只证明输出位，不证明实际出光；仿真行程 X/Y ±10、Z 0–10 mm 是演示值；仿真 JOG 只置位标志不积分轨迹。

## 验证

2026-11-02：上游主仓库根 `test.ps1` Debug 与 Release 全量无硬件回归 **1059 项通过**（本包 54 项：包隔离、统一模型事件/幂等/释放语义、相机 13、位移台 15（钳制/未知上电/JOG 互斥/软件置零/取消/断开拒绝）、激光门 6（未知诚实性）、加工 11（停止部分完成/干跑/出光标志/程序校验））；本独立仓库运行同一检查集。无设备操作、无 SDK 加载。
