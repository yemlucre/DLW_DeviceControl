# DLW_DeviceControl（Agamemnon 设备控制包）

Agamemnon「Function 功能包分层」的第三个包：**设备控制域**——每台设备一个接口 + 统一连接/状态模型。从主项目 `Function/Agamemnon.DeviceControl` 于 2026-11-02 抽出为独立仓库；零依赖（纯 net8.0），接口语义对齐主项目已被千项回归验证的设备契约。

## 定位

四类设备接口 + 统一模型；包内提供**仿真实现**（只模拟状态语义，不代表硬件规格）。真实厂商驱动适配（Tucam 相机 / Agito 位移台）留在主项目 `Devices.*`，未来经适配器接入本包接口。

## 目录

```text
src/Agamemnon.DeviceControl/
├── Device/     IDevice + DeviceBase（统一连接/状态模型：状态机、幂等连接、
│               释放后拒绝重连、StatusChanged 事件、忙碌标志）
├── Camera/     ICameraDevice + SimulatedCamera（采集生命周期、确定性 Gray8 帧、
│               FrameArrived 事件、参数目录范围/步进校验）
├── Stage/      IStageController + SimulatedStage（轴快照 mm、可空未知态、
│               行程钳制运动、JOG、软件置零、上下电、回零显式拒绝）
├── LaserGate/  ILaserGateController + SimulatedLaserGate（位设置/读回，bool? 未知诚实性）
└── Processing/ IBufferedProcessingController + SimulatedBufferedProcessing
                （段式程序、干跑/出光标志、停止/取消如实报告部分完成）
tests/          独立无硬件检查（54 项，与主仓库同一检查集）
docs/           契约与边界说明（contract.md）
```

## 公开接口速览

```csharp
using ICameraDevice camera = new SimulatedCamera();
camera.Connect();
camera.StartCapture();
CameraFrame frame = camera.GrabFrame();        // 64×48 Gray8 字节 + FrameIndex（仿真图案）

IStageController stage = new SimulatedStage();
stage.Connect();
stage.SetAxisPower("X", true);
var move = await stage.MoveAbsoluteAsync(new Dictionary<string, double> { ["X"] = 15 });
// move.AnyClamped == true, move.EffectiveMm["X"] == 10（行程 ±10 mm 钳制并如实报告）

ILaserGateController gate = new SimulatedLaserGate();
var state = await gate.SetAsync(true);          // state.On == true；读回失败时 On == null（未知）
```

## 约定

- 单位：位置 mm、速度 mm/s、门宽数量 62.5 µs/个（只携带数量，产品策略属工艺层）。
- 诚实性：可空状态（Powered/InTarget/On）=null 表示**未知**，绝不当 false；拒绝消息为中文原因；停止的加工如实报告部分完成。
- 异常契约：非法参数 `ArgumentException` 族；非法状态（未连接/未上电/忙碌）`InvalidOperationException` 或 `Accepted=false` 结果对象。
- 仿真实现只模拟状态语义（JOG 不积分物理轨迹、行程 ±10/±10/0–10 mm 是演示值）。

## 构建与检查

```powershell
dotnet run --project tests\Agamemnon.DeviceControl.Checks.csproj -c Release   # 54 项检查
```

需要 .NET 8 SDK，跨平台、无硬件。

## 验证记录

- 2026-11-02：上游主仓库 Debug/Release 全量无硬件回归 1059 项通过（本包 54 项）；本仓库独立运行同一检查集通过。
- 已知边界：真实驱动适配未做；仿真不代表硬件规格。
