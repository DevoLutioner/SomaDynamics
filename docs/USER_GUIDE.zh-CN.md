# Soma Dynamics 1.0 完整使用教程

本文适用于 Koikatu / Koikatsu Party 的 BepInEx 5 环境，以及 CharaStudio 中使用
MMD Director 的场景。Soma Dynamics 管理角色形体物理；配套的
MMD DynamicBone Stabilizer 专门稳定 MMD 播放时的头发、衣摆、饰品和裙骨碰撞。

## 1. 两个插件分别负责什么

| 插件 | 作用范围 | 是否需要界面 |
| --- | --- | --- |
| Soma Dynamics | 大腿、手臂、腹部、胸部、臀部的形体物理 | 按 `Insert` 打开 |
| MMD DynamicBone Stabilizer | MMD 中的头发、衣摆、饰品、Unity Cloth 和 KKPE 裙骨碰撞 | 不需要，自动工作 |

两者可以同时安装，职责不会重复。Soma Dynamics 即使不使用 MMD 也能工作；稳定器只在
检测到 MMD Director 正在控制角色时介入，停止播放后会恢复临时调整。

## 2. 安装前检查

开始前请确认：

1. 已安装 BepInEx 5，并能正常进入游戏或 CharaStudio。
2. 使用 MMD 稳定器时，已安装 MMD Director 及其自带的 CharaAnime 2.8 或更高版本。
3. 完全关闭 `Koikatu.exe` 与 `CharaStudio.exe`。游戏运行时不要覆盖 DLL。
4. 检查 `BepInEx/plugins` 中是否存在旧版 FPC 或 BPC。一个插件只能保留一个以
   `.dll` 结尾的活动副本。

建议先备份整个 `BepInEx/plugins` 和 `BepInEx/config`。角色卡本身无需修改。

## 3. BPC 会不会冲突

会产生功能冲突，建议在安装 Soma Dynamics 前停用。

Soma Dynamics 已接管胸部和臀部的原生 `DynamicBone_Ver02` 参数，并包含原 BPC 的状态
管理能力。若 BPC 仍在运行，两个插件会在角色加载、换装、体型刷新或切换坐标时写入同一组
胸臀参数。它不一定造成启动崩溃，但可能出现以下问题：

- UI 数值与实际效果不一致；
- 换装后参数被另一个插件覆盖；
- 胸部碰撞后持续抖动；
- 不同坐标或裸身/胸罩/上衣状态的效果来回变化。

### 需要停用的旧插件

在 `BepInEx/plugins` 及其子目录中查找并停用：

- `BreastPhysicsController.dll`
- `BpcAutoDefault.dll`
- `DisableHipDynamicBones.dll`

推荐的可恢复停用方式是把扩展名改成 `.disabled`，例如：

```text
BreastPhysicsController.dll → BreastPhysicsController.disabled
BpcAutoDefault.dll          → BpcAutoDefault.disabled
DisableHipDynamicBones.dll  → DisableHipDynamicBones.disabled
```

也可以将 DLL 移到 `BepInEx/plugins` 之外的备份目录。不要同时保留改名后的文件和另一份活动
DLL。如果这些文件已经是 `.disabled`，无需继续处理。

### 不需要删除的内容

以下内容可以保留，便于以后回滚：

- `BepInEx/config/com.snw.bepinex.breastphysicscontroller.cfg`
- `BepInEx/config/codex.koikatumanager.bpcautodefault.cfg`
- `BreastPhysicsController/Presets` 中的 XML 预设
- 角色卡、场景和卡片中的旧 BPC 扩展数据

这些文件在 BPC DLL 停用后不会执行。Soma Dynamics 使用自己的 GUID 保存数据，不会破坏
旧 BPC 卡片字段。

## 4. 安装方法

### 推荐：二合一整合包

1. 下载 `SomaDynamics_1.0.0.0-with-MMD-Stabilizer-v1.1.0.zip`。
2. 打开压缩包，确认最外层直接包含 `BepInEx` 文件夹。
3. 将 `BepInEx` 合并到游戏根目录，也就是包含 `Koikatu.exe` 和
   `CharaStudio.exe` 的目录。
4. 同意覆盖旧版本文件。

安装完成后应存在：

```text
BepInEx/plugins/ThighPhysicsController/ThighPhysicsController.dll
BepInEx/plugins/MmdDynamicBoneStabilizer/MmdDynamicBoneStabilizer.dll
```

`ThighPhysicsController.dll` 是为兼容旧卡保留的内部文件名，界面显示名仍是
Soma Dynamics。

### 独立安装

不使用 MMD 时，只安装 `SomaDynamics_1.0.0.0.zip` 即可。只需要修复 MMD 头发或裙摆时，
也可以单独安装 `MmdDynamicBoneStabilizer-v1.1.0.zip`。

## 5. 第一次启动

1. 启动游戏或 CharaStudio，并加载一个角色。
2. 按 `Insert` 打开 Soma Dynamics。
3. 初次使用建议选择全身预设 `中`。
4. 普通摆姿或日常动画可为大腿、手臂和腹部选择 `Spring`；MMD 舞蹈可按部位选择
   `Chain`。三个部位可以自由组合。
5. 播放一次 MMD，观察头发和裙摆。MMD 稳定器无需打开界面。

启动日志位于 `BepInEx/LogOutput.log`。正常加载时可搜索：

```text
Loading [Soma Dynamics 1.0.0.0]
Loading [MMD DynamicBone Stabilizer 1.1.0]
MMD DynamicBone stabilization active
```

最后一条只会在 MMD 实际控制角色时出现。

## 6. 三档预设怎么选

| 档位 | 适合场景 | 特点 |
| --- | --- | --- |
| 低 | 静态展示、轻动作、贴身服装 | 保留肉感，但减少拖尾和碰撞放大 |
| 中 | 日常通用、初次安装 | 采用作者常用 `MyPreset` Amp，平衡清晰度和稳定性 |
| 高 | 幅度较大的 MMD、需要明显肉感 | 提高整体响应，并保留防抖与位移安全限制 |

胸部摆动强度经过碰撞实测限制：中档不超过 `0.50`，高档不超过 `0.60`。只有高档大腿
`Thigh02 Amp` 使用 `0.50` 的防塌陷值；中档保持 `MyPreset` 原值。

预设不会锁定求解模式。选择中档后仍可把大腿改成 Chain、手臂改成 Spring，或使用任何
其他组合。预设会同时准备 Spring 与 Chain 的对应参数，因此切换后不会回到另一套旧数值。

## 7. 三个基础参数

### 摆动强度 Swing

控制形体物理的总体存在感。数值越高，肉体与骨骼原动作之间的偏移越明显。胸臀涉及游戏
碰撞链，不建议仅为了追求幅度把此项直接拉满。

### 柔顺度 Softness

控制回弹的松软程度。低值更紧实、回位更快；高值更柔软、拖尾更长。它不是单纯的幅度
开关，过高时动作可能显得迟缓。

### 动作响应 Motion response

控制动画运动有多少能量进入物理。MMD 骨骼旋转和 Studio 方向轴拖动采用不同的输入方式，
Soma Dynamics 会将它们换算到统一的距离模型。需要增强舞蹈效果时优先逐步增加此项，
不要同时把所有高级参数拉满。

## 8. Spring 与 Chain

- `Spring`：适合普通动画、摆姿和较小动作，回弹直接，容易理解。
- `Chain`：适合连续舞蹈和明显的骨骼旋转，能表现沿骨链传递的滞后。

模式是按部位保存的，不是全身锁定。胸部和臀部始终使用游戏原生 DynamicBone 碰撞链，
不会显示实验性 Spring。

## 9. 保存、复位和高级参数

- 参数实时生效，并随角色卡保存。
- XML 导出会保存五个部位、模式和逐骨高级参数，适合分享完整方案。
- `恢复推荐基线 Reset` 只复位当前部位。
- `恢复姿态 Restore pose` 只清除当前形变和速度，不改参数。

高级页面向需要逐骨调节的用户。日常使用通常只需要三档预设和三个基础参数。若必须调整
Amp、轴向、Damping 或 Inert，建议一次只改一个值，并保留 XML 备份。

## 10. MMD 防抖插件的工作范围

MMD DynamicBone Stabilizer 会在 MMD 写完角色姿势后再计算头发、衣服和饰品物理，并在
切歌或拖动进度时清除陈旧速度。它还会识别 Unity Cloth，以及裙骨使用的 KKPE 球形
碰撞体；MMD 停止后会恢复临时参数。

默认配置已经按零调参设计。配置文件首次启动后生成在：

```text
BepInEx/config/codex.koikatu.mmddynamicbonestabilizer.cfg
```

除非某个特殊动作仍在切换瞬间爆甩，否则不建议修改 Seek 阈值或裙骨碰撞缩放。

## 11. 兼容性速查

| 插件或系统 | 是否可共存 | 说明 |
| --- | --- | --- |
| MMD Director / CharaAnime | 是 | 稳定器的运行依赖 |
| KK_Fix_DynamicBones | 是 | 稳定器只补充 MMD 手动物理时序 |
| KKPE | 是 | 支持其裙骨球形碰撞体；仍应避免不合理的超大碰撞球 |
| Dynamic Bone Editor | 是 | 可以编辑链，但不要在播放时持续覆盖同一参数 |
| Cloth Colliders Support | 是 | 与 Unity Cloth 稳定逻辑职责不同 |
| BreastPhysicsController | 不建议 | 与 Soma Dynamics 同时写胸臀链 |
| BPC Auto Default | 不建议 | 会在加载时再次覆盖胸臀参数 |
| DisableHipDynamicBones | 不建议 | 会阻止 Soma Dynamics 管理臀部原生链 |
| 旧版 FPC / ThighPhysicsController | 不可重复 | 只保留最新 DLL |

## 12. 常见问题

### 按 Insert 没有界面

先确认角色已经加载，然后检查 `LogOutput.log` 是否出现 Soma Dynamics 的加载行。若完全
没有，通常是 ZIP 解压层级错误、DLL 仍在双层目录，或同时存在多个旧 DLL。

### 中档与高档看起来接近

确认当前部位是否启用、选择的是 Spring 还是 Chain，并观察动作本身是否足够激发物理。
MMD 旋转与方向轴拖动的输入不同，静态拖动不能完全代表舞蹈效果。

### 大腿变形或塌陷

先重新选择预设并点击该部位的 `恢复推荐基线`。高档已经为 Thigh02 提供独立安全值；若
导入了旧 XML，请检查逐骨 Amp 是否覆盖了预设。

### 胸部碰撞后抖动

确认 BPC 三个旧插件均已停用，不要让另一个工具在播放时反复重设胸部 DynamicBone。
随后恢复胸部推荐基线，并从中档开始测试。

### 裙摆仍然抖动

确认日志中 `MMD DynamicBone stabilization active` 后的 `skirtChains` 和
`skirtSpheres` 大于零。若只在特定角色发生，检查 KKPE 裙摆碰撞球是否过大、互相重叠，
或深度穿入裙骨。稳定器可以降低反复穿入/推出，但无法让错误碰撞体尺寸变得合理。

### 头发稳定但衣服仍抖

衣服可能使用 Unity Cloth 而不是 DynamicBone。保持稳定器的 `Stabilize Unity Cloth` 开启，
并在切换歌曲后重新观察。如果只有一件服装异常，优先检查该服装自己的 Cloth 和碰撞设置。

## 13. 升级、卸载与回滚

升级时关闭游戏，直接覆盖同路径 DLL。不要把新 DLL 改名后与旧 DLL 一起放在
`BepInEx/plugins` 内。

卸载 Soma Dynamics：

1. 关闭游戏和 Studio。
2. 删除或移走
   `BepInEx/plugins/ThighPhysicsController/ThighPhysicsController.dll`。
3. 配置和预设可以保留；若要彻底清理，再删除对应配置文件与插件目录。

卸载 MMD 稳定器：删除或移走
`BepInEx/plugins/MmdDynamicBoneStabilizer/MmdDynamicBoneStabilizer.dll`。它不写角色卡，
卸载后无需迁移数据。

若要恢复 BPC，必须先停用 Soma Dynamics，再把三个 `.disabled` 文件按需改回 `.dll`。
不要让 Soma Dynamics 与 BPC 同时恢复运行。

## 14. 推荐的首次测试顺序

1. 关闭 BPC，安装二合一包。
2. 加载角色，选择 `中`，先播放没有碰撞的普通动作。
3. 测试胸部和臀部碰撞，确认没有持续振荡。
4. 播放常用 MMD，观察大腿、手臂和腹部。
5. 再观察头发、衣摆及裙骨碰撞。
6. 只有确认中档稳定后才切换到高档。
7. 保存满意的角色卡，并导出一份 XML 作为可恢复预设。

项目主页：

- Soma Dynamics: <https://github.com/DevoLutioner/FleshPhysicsController>
- MMD DynamicBone Stabilizer: <https://github.com/DevoLutioner/MmdDynamicBoneStabilizer>
