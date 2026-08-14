# Soma Dynamics｜形体动力学控制器 v1.0.3.5

Soma Dynamics 是 Koikatu / Koikatsu Party 的统一形体物理控制器，管理大腿、手臂、
腹部、胸部和臀部。界面以少量感知参数为主，同时保留逐骨高级调节。

内部 GUID、DLL、安装目录和角色卡数据键继续沿用 `ThighPhysicsController`，以兼容旧卡、
配置和现有安装。

## 1.0 至 1.0.3.5 修复总览

| 版本 | 用户可见修复 |
| --- | --- |
| 1.0.0.0 | 正式统一大腿、手臂、腹部、胸部与臀部管理；胸臀改为安全接管游戏原生碰撞链，移除会重复解算的实验 Spring。 |
| 1.0.1.0 | Studio 换角色、换动作时自动重置 Soma 运行状态，减少手动恢复姿态；关闭诊断后不再保留采样开销。 |
| 1.0.2.0 | Timeline 载入场景时不再把角色强制拉回卡片初始姿势；修复 Chain 使用旧旋转基准造成的大腿扭曲。 |
| 1.0.2.1 | 接入 Timeline/Studio 场景载入、导入和清空事件；修复外部骨骼写入后手臂自动旋转。 |
| 1.0.2.2 | 每帧剥离 Soma 自己上一帧的 Chain 输出，切断父子骨反馈；修复暂停或静止时大腿慢慢变形。 |
| 1.0.2.3 | Timeline 连续关键帧成为实时 Chain 基准，恢复播放时的弹性与惯性；不再用“关闭物理”回避形变。 |
| 1.0.2.4 | 检测角色整体的世界空间瞬移/大旋转并整链安全重锚；修复 Timeline 大位移时小腿和手臂严重拉伸变形。 |
| 1.0.2.7 | 面板新增按需 Timeline 安全弹簧开关；播放时 Chain 临时使用 Spring，暂停/停止自动恢复，不改角色卡模式。 |
| 1.0.3.0 | 预设一键保存/应用与默认预设自动套用；五部位默认启用开关（可全局覆盖）；Timeline 弹簧三档（关闭/手动/自动）与自定义快捷键。 |
| 1.0.3.1 | 默认开关修复（不再被 Force enable 覆盖）+ 中档 Thigh02 封顶；弹簧旋转采纳（自由H 不再扭曲）；检测到自由H 自动全部位切弹簧、退出恢复；面板一键【全部弹簧/全部链式】。 |
| 1.0.3.2 | 修复 1.0.3.1 自由H兜底检测在普通场景每帧执行全场景对象扫描造成的严重掉帧；改为场景加载/切换后仅扫描一次并缓存。 |
| 1.0.3.3 | 内置高档精确采用 MyPreset1；兼容 PushUp 胸型重算，并在体型刷新后重建 Soma 基准，修复拖动滑条导致四肢/身体变形。 |
| 1.0.3.4 | 修正 PushUp 协调：不再重采手臂/腹部，也不再用 setPtn 撤回胸型；只原位回写胸链物理字段。 |
| 1.0.3.5 | 按反编译的 BPC 提交流程补回胸链 ReSetup，并在 PushUp 写完整组胸型后只提交胸部基准。 |

完整逐项记录见 [`CHANGELOG.md`](CHANGELOG.md)。

## 物理结构

- 大腿、手臂、腹部：使用 Soma Dynamics 的 `Spring` 或 `Chain`，三个部位可自由组合。
- 胸部、臀部：使用游戏原生 `DynamicBone_Ver02` 碰撞链，由插件统一管理参数。
- 胸臀实验性独立 Spring 已根据实机结果完整移除；不会隐藏入口、保留双解算或写入无效字段。
- 胸臀实时应用不调用 `SetWeight` 或直接重置粒子位置；胸部只在整套参数/PushUp 胸型提交
  完成后调用一次游戏原生 `ReSetupDynamicBoneBust`，与 BPC 的提交顺序一致。

## 安装

首次安装、BPC 迁移、MMD 配套、防冲突与故障排查请阅读：

[`docs/USER_GUIDE.zh-CN.md`](docs/USER_GUIDE.zh-CN.md)

1. 关闭 `CharaStudio.exe` 和 `Koikatu.exe`。
2. 将压缩包中的 `BepInEx\plugins\ThighPhysicsController\` 合并到游戏同名目录。
3. 启动游戏或 Studio，加载角色后按 `Insert` 打开面板。

配置文件位于：

`BepInEx\config\codex.koikatumanager.thighphysicscontroller.cfg`

## 界面逻辑

### 全身控制

顶部只保留一组全身控制，统一作用于五个部位：

- `摆动强度 Swing`
- `柔顺度 Softness`
- `动作响应 Motion response`

三项范围均为 `0–2`：`0–1` 是常用区，`1–2` 是受安全限幅保护的增强区。

`低 / 中 / 高` 不改写各部位的求解模式。中档逐骨 Amp 采用原有 `MyPreset` 基准，低档为
该基准的 0.75 倍；高档则精确采用 `MyPreset1.xml` 中 Spring/Chain 两套参数、逐骨 Amp
和轴向值，胸部/臀部三项目标也使用同一预设中的数值。
因此应用任意档位后，仍可独立组合三个部位的 Spring / Chain，且档位
差异不会在切换模式后丢失。

### 部位控制

- 大腿、手臂、腹部：独立启用，使用明确的 Spring / Chain 动作按钮。
- Timeline 兼容开关为三档：关闭 / 手动（全部角色）/ 自动（仅 Timeline 实际驱动的角色
  在播放期间临时切换 Spring）；可设自定义快捷键一键翻转。
- 胸部、臀部：`启用参数接管` 只决定是否由插件覆盖原生链参数；关闭后恢复游戏原值，
  不会关闭游戏本身的碰撞物理。
- 基础页只显示三项目标参数和当前部位复位。
- 高级页显示求解器、逐骨、轴向和配置文件导入/导出。

### 默认值与预设

- 「默认值 Defaults」区提供五个部位的默认启用开关；勾选「应用到所有角色」后对所有
  角色生效（例如腹部偏瘦可全局默认关闭腹部），否则只对新加载且无卡数据的角色生效。
- 配置文件区新增【保存当前设置 Save】一键直写预设与【应用所选 Apply】一键应用；
  【设为默认】后，所有无卡数据的角色加载时自动套用该预设——多角色共用一套参数只需
  调整一次，身高/胸部差异由骨骼锚定的 Chain 基准自动适配。

参数实时生效并随角色卡保存。高级 XML 配置保存完整的五部位组合。

### 复位

- `恢复推荐基线 Reset`：只重置当前部位。
- `恢复姿态 Restore pose`：清除当前物理形变，不改参数。
- Studio/Timeline 换角色、换服装、切换或重播动作时只撤销 Soma 自身形变，并在新姿态
  连续稳定两帧后将其采纳为 Chain 基准，不会强制恢复角色卡初始姿势。

## 高级参数范围

| 参数 | 范围 | 说明 |
| --- | ---: | --- |
| Weight | 0–2 | 求解器总体作用量 |
| Gravity | -0.4–0.4 | 自定义 Spring / Chain 重力 |
| Motion gain | 0–10 | 动作输入原始增益 |
| Damping / Elasticity / Stiffness | 0–1 | 阻尼、弹性和刚性 |
| Inert | 0–1.5 | 自定义求解器惯性；原生链保持 0–1 |
| Jitter frequency | 0–5 | 响应频率 |
| Motion smooth | 0.05–1 | Spring 动作平滑 |
| Per-bone Amp | 0–4 | 单骨位移幅度 |
| Axis / Rotation | 0–2 | 单骨轴向与旋转倍率 |
| Native Gravity | -0.003–0.003 | 胸臀原生链重力 |

所有卡片、XML 和 UI 输入都会执行有限值检查与范围钳制。

## 胸部状态

胸部按游戏实际状态保存：裸身、胸罩、上衣，以及 7 个坐标槽。高级页的
`应用到全部胸部状态` 可将当前胸部参数复制到全部状态。臀部使用一套共享参数。

推荐基线来源于原 BPC Auto Default 的 Bust / Hip Soft 参数，但界面和存档不再暴露
BPC 开发术语。完成迁移后，不需要同时启用以下旧插件：

- BreastPhysicsController
- BPC Auto Default
- DisableHipDynamicBones

## MMD 与防抖

- Chain 将骨骼旋转换算为世界空间切向位移，使 MMD 动作和 Studio 方向轴拖动采用一致的
  距离语义。
- 高帧率输入使用三采样中值保护去除孤立尖峰，不对正常舞蹈频段持续低通。
- 保留最短角度、速度、位移、旋转、leash 和非有限状态保护。
- 腹部只控制 `cf_s_waist01`；结构骨 `cf_s_waist02` 不参与形体物理。

## 兼容与数据版本

- 插件版本：`1.0.3.5`
- 卡片数据版本：`61`
- XML 版本：`4`
- GUID：`codex.koikatumanager.thighphysicscontroller`

v60 的实验胸臀 Spring 键会被安全忽略。旧卡的原生胸臀参数和 v1/v2/v3 XML 会继续迁移；
v54 是已归档的坏版本，不应回滚使用。

## 构建

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-ThighPhysicsController.ps1
```

正式构建会运行参数模型测试、胸臀实时应用安全契约、品牌/UI 字符串烟测，并生成：

- `packaging\SomaDynamics_1.0.3.5\`
- `packaging\SomaDynamics_1.0.3.5.zip`
- `packaging\SomaDynamics_1.0.3.5.zip.sha256`
