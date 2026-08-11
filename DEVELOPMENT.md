# Soma Dynamics｜形体动力学控制器 开发交接文档

## 这是什么

BepInEx 5 插件。大腿、手臂、腹部使用自定义 Spring / Chain；胸部、臀部管理游戏原生
DynamicBone 碰撞链。显示品牌为 Soma Dynamics，内部标识沿用 ThighPhysicsController。

## 目录结构

```text
FleshPhysicsController\
├─ src\ThighPhysicsController\      插件 C# 源码（项目名沿用 ThighPhysicsController）
│  ├─ ThighPhysicsController.csproj  net20 目标，引用游戏程序集
│  ├─ FleshPart.cs                  三个自定义物理部位的骨骼定义
│  ├─ FleshParameterRanges.cs       UI/卡片/XML 的统一参数边界
│  ├─ ThighParams.cs                参数模型 + 卡片序列化（v61）
│  ├─ FleshTuning.cs                三项目标映射 + 低/中/高内部基线
│  ├─ NativeBodyParams.cs           胸臀原生链参数与胸部状态
│  ├─ NativeDynamicBoneBridge.cs    原生链实时应用（禁止粒子重置）
│  ├─ ThighController.cs            每角色控制器 + 预设 XML 读写
│  ├─ ThighFleshJiggle.cs           Transform 编排 + 两种模式入口
│  ├─ FleshPhysicsState.cs          Spring/Chain 运行状态
│  ├─ FleshStateReset.cs            Spring/Chain 统一状态复位
│  ├─ FleshSafetyGuard.cs           非有限骨骼状态检查
│  ├─ FleshSpringSolver.cs          弹簧写入策略
│  ├─ FleshChainSolver.cs           单/多粒子共享链积分
│  ├─ FleshSolverMath.cs            帧率无关标量映射
│  ├─ ThighFleshJiggle.Metrics.cs   运行指标采样与日志
│  ├─ ThighPhysicsControllerPlugin.cs  BepInEx 入口 + 面板
│  ├─ WindowsFileDialog.cs          Windows 保存/打开对话框（P/Invoke）
│  └─ Presets\                      用户 XML 预设的可选发布目录（默认不内置）
├─ tests\ParameterModel.Tests\      无游戏运行时的参数模型测试
├─ tools\Summarize-FleshPerformance.ps1  按部位/求解器汇总 CPU 微秒基线
├─ tools\Build-ThighPhysicsController.ps1  构建 + 打包 + SHA-256
├─ packaging\SomaDynamics_1.0.2.0\ 发行目录（含 README.zh-CN.md、CHANGELOG.md）
├─ packaging\SomaDynamics_1.0.2.0.zip + .sha256
├─ README.md                        用户/功能说明
├─ CHANGELOG.md                     更新日志
└─ DEVELOPMENT.md                   本文档
```

## 关键标识（不要乱改）

- GUID：`codex.koikatumanager.thighphysicscontroller`（旧卡数据兼容，不能变）
- 插件版本：1.0.2.0（`BepInPlugin`）
- 显示名：Soma Dynamics（中文名“形体动力学控制器”）
- 卡片数据版本：61（v60 为已移除的胸臀实验 Spring；v54 为已归档坏版本）
- XML 版本：4
- 依赖：KKAPI `marco.kkapi`（不限制最低版本）、ExtensibleSaveFormat、0Harmony

## 部位与骨骼

部位定义在 `FleshPart.cs`：

| 部位 | 锚点 | 肉感骨（链序） | 说明 |
| --- | --- | --- | --- |
| Thigh | cf_j_thigh00_L/R | cf_s_thigh01/02/03 → cf_s_leg02 | 4 骨/侧，共 8 骨 |
| Arm | cf_j_arm00_L/R | cf_s_arm01/02/03 | 3 骨/侧，共 6 骨 |
| Belly | cf_j_spine03 | cf_s_waist01 | 1 骨（spine03 为刚性骨，已移除） |
| Breast Native | 游戏原生 | cf_j_bust01/02/03 | 按服装与坐标保存，保留碰撞 |
| Butt Native | 游戏原生 | cf_d_siri01 + cf_j_siri_L/R_01 | 共享参数，保留碰撞 |

**重要**：不要给 Belly 加回 `cf_s_waist02`——它是承载双腿（cf_s_leg_L/R）的结构骨，
跳舞时位移会导致身体皮肤撕裂/消失（0.8.0 已修复并加了 NaN 防护）。

## 构建

需要 .NET 8 SDK，以及游戏根目录（默认 `Z:\Koikatu`，可用 `KOIKATU_BUILD_GAME_ROOT` 覆盖）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-ThighPhysicsController.ps1
```

构建默认先运行 `tests\ParameterModel.Tests`，验证三个部位的基线、简单参数往返、
单调性、卡片限幅与旧字段清理；只排查构建工具时可临时传 `-SkipTests`。

插件不包含自动载入场景或动作驱动。静态参数测试随正式构建执行；运行日志可用现有离线
汇总脚本分析。旧 Mono 不提供精确的逐线程分配计数，因此不能把观测到的零堆增量表述为
“绝对零分配”。

产物：`packaging\SomaDynamics_1.0.2.0\`、ZIP、SHA-256。构建脚本会做版本、UI、
胸臀 Spring 清除和原生链实时应用安全契约检查。

## 安装与测试

- 安装：把 `packaging\SomaDynamics_1.0.2.0\BepInEx\plugins\ThighPhysicsController\`
  覆盖到游戏 `BepInEx\plugins\ThighPhysicsController\`。
- 测试：启动 `CharaStudio.exe`，Insert 打开面板，加载角色卡。
- 日志：`Z:\Koikatu\output_log.txt`（插件 Debug 日志也写到这）。
- 调试配置：`Z:\Koikatu\BepInEx\config\codex.koikatumanager.thighphysicscontroller.cfg`
  - `Log flesh physics = true`：每 2 秒输出弹簧/链式偏移；
  - `Dump skeleton bones = true`：启动时转储骨骼层级。

## 日志格式速查

```text
Loading [Soma Dynamics 1.0.2.0]
Flesh physics initialized: bones=8 part=Thigh
Flesh physics initialized: bones=6 part=Arm
Flesh physics initialized: bones=2 part=Belly
Flesh chain params: weight=... damping=... elasticity=... stiffness=... inert=... motionGain=...
Flesh physics [cf_s_thigh02_L]: chain applied=(x,y,z) mag=... off=... anchor=... amp=... axis=(...) rc=0 rcRot=(...)
Flesh physics [cf_s_thigh01_L]: applied=(x,y,z) mag=... rot=(...)
```

- 弹簧模式行含 `rot=`；链式模式行含 `anchor/amp/axis/rc/rcRot`；
- `off` = 粒子相对基准的偏移，`mag` = 实际写入位移；
- `chain re-anchored` 只在瞬移/大换姿势时出现。

## 当前状态与决策记录

- 弹簧模式是主推手感（用户认可）；链式模式保留为“跳舞增强”选项。
- 弹簧/链式参数完全独立：共享参数 `Chain`/`ChainBones` vs 弹簧 `Thigh00`/`Bones`；
  面板按部位（Thigh/Arm/Belly）切换编辑，控件 ID 带模式前缀避免缓冲串扰。
- Dance response（MotionGain）：弹簧/链式统一倍数
  `gain × (weight/0.8) × ((0.25+inert)/0.6)`，默认参数下 gain=1 即 1.0x；
  链式参考系数 0.000384（gain=1 约等于旧版 0.001 驱动），弹簧沿用原有各项系数，
  两边默认手感与之前一致，滑条语义统一；0.8.5 起上限放开到 5
  （UI/卡片/预设三处同步）。当前弹簧中点：Damping 0.18/Elasticity 0.10/
  Stiffness 0.12/Inert 0.35，
  加速度平滑 0.45→0.25、X/Z 增益 1.25、驱动 0.00025；弹簧阻尼保留完整
  0~1 语义并按时间步合成，动态状态围绕重力平衡点积分；
  链式增强：Weight 0.7/Damping 0.30/Inert 0.40，舞蹈驱动系数 0.0006，
  切向限幅 0.05、速度限幅 0.22。
- 多角色（0.8.5）：角色列表按女性/男性分组（`#序号 名字`），选中按 InstanceID 记忆，
  点击行直接切换；性别映射 KK `sex==0` 为男；
  参数按 `fullname|sex|personality` 会话内记忆（`FleshProfile`），
  同名同性别角色共享同一参数对象实现自动同步；`OnReload` 尊重 `maintainState`；
  记忆仅限本次会话，卡片保存仍写回卡内。
- 兼容（0.8.5）：`[BepInDependency("marco.kkapi")]` 去掉最低版本号，
  只用 KKAPI 1.x 长期稳定的 API（RegisterExtraBehaviour / CharaCustomFunctionController）。
- 复位与 RC（0.8.5.1）：部位开关关闭时 `LateUpdate` 调用 `ClearDeformation`
  恢复姿态；链式第二遍禁用骨直接复位位置+旋转；`RemoveFlesh` 先复位再销毁，
  防止“清除无效/禁用后再启用把变形当原始姿态”；RC 默认全开
  （`PerBoneAmount.RotCalc`、代码预设），
  链式 RC 跳过 `GetAmp<=0` 的骨骼。
- 弹簧防积累（0.8.5.1）：重锚检测从世界空间改为父空间局部判定
  （`localPosition - (BaseLocal + LastAppliedLocal)`，阈值 0.005m），
  父骨旋转不再误触发重锚、不再把自身偏移固化进 BaseLocal；弹簧 RC 改为
  基准旋转 `baseWorldRot * RestDirLocal` + ±12° 限幅，消除子骨反馈；
  `Auto fix spring drift`（默认 true）后台看门狗：基准偏移 >0.005m 持续 2 秒、
  且 1 秒内无外部重锚时，按每帧 0.0005m 把 BaseLocal 拉回 PristineLocal。
- 开放参数（0.8.6）：弹簧 `JitterFreq`（缩放 springVel 与弹性回中）与
  `MotionSmooth`（替换硬编码的 0.25/0.3 滤波系数）；链式 `JitterFreq`
  （缩放轴向刚度与垂直弹性修正）；卡片键 `jf/ms/c_jf`，预设 XML
  `JitterFreq/MotionSmooth`，UI 三个新滑条均直接作用于物理。
- 小肚子单骨链（0.8.6.2）：`cf_s_spine03` 为刚性骨已从小肚子链移除，
  仅保留 `cf_s_waist01`；`BuildChain` 允许单粒子链，
  `UpdateSingleParticleChain` 按“子粒子”方式积分单粒子（不回头读骨骼，
  避免自反馈），RC 因无子骨可瞄准而自动退化为纯位移。
- 链式“橡皮泥”历史问题：根因是长度约束过松 + 参考系漂移；现已用
  BaseLocal 父空间基准 + 切向/径向弹簧 + 写入/leash 限幅解决。
- RC（RotCalc）：基于基准旋转做瞄准约束（不累积），旋转限幅 12°；非 RC 骨骼的 Rot
  用偏移驱动平滑旋转，默认 0.25。
- 兼容性：ABMX（KKABMX）会写这些骨骼，链式每帧按当前骨骼刷新静止方向/长度跟随；
  BPC 不直接冲突，但会放大骨盆运动，链式已对 anchorMove 限幅 0.30m。
- 日志洪水：`rotation overwritten by game` 已节流（每骨每 2 秒 1 条，仅真正写旋转时）。

## 未完成/注意点

- 类名/程序集名/插件目录仍是 ThighPhysicsController（显示名与打包名已改为 Soma Dynamics，
  内部标识未全量重命名，避免破坏安装路径、预设目录与旧配置）；
- 0.8.7.0 已删除 `ThighBoneParams` 中不参与物理的旧字段；旧卡中的额外键会被忽略；
- 0.8.8.0 已移除三个旧内置 XML；1.0 UI 将内部基线简化为低/中/高，且不接管模式；
- 0.8.9.0 已拆出状态、安全、指标和链积分模块，并为 Chain/Spring 分别建立实机门禁；
- 0.8.10.0 已统一状态复位、完成两求解器五点柔软度门禁与 30/60/高帧率归一化；
- 1.0.0.0 已整合胸臀原生链，并沿用 BPC Auto Default 的 Soft 参数；
- 1.0.1.0 已加入工作室换角色/换动作自动 Pose 复位，并移除关闭诊断时的采样开销；
- 1.0.2.0 已将 Chain 自动复位改为保留 Timeline 姿势的延迟重锚，并修复首粒子外部旋转
  未被识别导致的载入扭曲；
- 0.8.11.0 已明确简单 UI 的求解器选择、修复 Stable 预设模式，并建立跨卡体型矩阵；
- 旧版本打包（0.4.10~0.7.2）仍留在 `Z:\Koikatu\DeepSeekEdition\KoikatuManager\packaging\` 作为历史存档。
- 0.9.0（碰撞体系统）为坏版本，已归档到
  `Z:\Koikatu\DeepSeekEdition\_broken_archive\FleshPhysicsController_0.9.0_20260806\`，不要回滚使用。
