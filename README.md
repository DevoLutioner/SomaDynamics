# Soma Dynamics｜形体动力学控制器 v1.0.0.0

Soma Dynamics 是 Koikatu / Koikatsu Party 的统一形体物理控制器，管理大腿、手臂、
腹部、胸部和臀部。界面以少量感知参数为主，同时保留逐骨高级调节。

内部 GUID、DLL、安装目录和角色卡数据键继续沿用 `ThighPhysicsController`，以兼容旧卡、
配置和现有安装。

## 物理结构

- 大腿、手臂、腹部：使用 Soma Dynamics 的 `Spring` 或 `Chain`，三个部位可自由组合。
- 胸部、臀部：使用游戏原生 `DynamicBone_Ver02` 碰撞链，由插件统一管理参数。
- 胸臀实验性独立 Spring 已根据实机结果完整移除；不会隐藏入口、保留双解算或写入无效字段。
- 胸臀实时应用不会调用 `ReSetupDynamicBoneBust`、`SetWeight` 或粒子位置重置，避免碰撞后
  二次激振。

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

`低 / 中 / 高` 不改写各部位的求解模式，并同步调整 Spring 与 Chain 两套逐骨 Amp：
中档逐骨 Amp 精确采用作者常用 `MyPreset`，低档为该基准的 0.75 倍，高档统一为
1.30 倍；只有高档大腿 `Thigh02 Amp` 是防塌陷例外，Spring 与 Chain 均固定为
`0.50`，中档仍保持 `MyPreset` 原值。
胸部中/高档 `摆动强度 Swing` 分别封顶 `0.50 / 0.60`，避免碰撞链被过度激发。三档同时调整强度、
柔软度与运动响应，避免中高档仅因参数饱和而体感接近。
因此应用任意档位后，仍可独立组合三个部位的 Spring / Chain，且档位
差异不会在切换模式后丢失。

### 部位控制

- 大腿、手臂、腹部：独立启用，使用明确的 Spring / Chain 动作按钮。
- 胸部、臀部：`启用参数接管` 只决定是否由插件覆盖原生链参数；关闭后恢复游戏原值，
  不会关闭游戏本身的碰撞物理。
- 基础页只显示三项目标参数和当前部位复位。
- 高级页显示求解器、逐骨、轴向和配置文件导入/导出。

参数实时生效并随角色卡保存。高级 XML 配置保存完整的五部位组合。

### 复位

- `恢复推荐基线 Reset`：只重置当前部位。
- `恢复姿态 Restore pose`：清除当前物理形变，不改参数。

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

- 插件版本：`1.0.0.0`
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

- `packaging\SomaDynamics_1.0.0.0\`
- `packaging\SomaDynamics_1.0.0.0.zip`
- `packaging\SomaDynamics_1.0.0.0.zip.sha256`
