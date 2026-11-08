# Issue #15：KKS 1.0.3.10 加载修复

2026-11-08，针对 [Issue #15](https://github.com/DevoLutioner/SomaDynamics/issues/15)。

## 原因与修复

原 v1.0.3.10 Release 只有 `SomaDynamics_1.0.3.10.zip`，其中 DLL 是 KK 构建：
引用 KKAPI、ExtensibleSaveFormat、mscorlib 2 和旧版 UnityEngine。KKS 则需要
KKSAPI、KKS_ExtensibleSaveFormat、mscorlib 4 和 Unity 2018 模块化程序集。
相同的命名空间、GUID 和 DLL 文件名不代表二进制可互换。

本次只修改构建、打包和文档，从同一份 1.0.3.10 插件源码补充 KKS 产物；
未更改物理解算、默认值、卡片数据 v61 或 XML v4。

## 实际 Unity 加载对照

`tools/Test-FleshPluginLoad.ps1` 复制可执行文件、BepInEx、最小 KKS 依赖和待测插件，
为每次测试建立独立配置及 UserData。游戏资源与 Mono 目录通过目录联接读取。
脚本只终止自己启动的进程，不安装插件到原游戏目录。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FleshPluginLoad.ps1 -GameRoot H:\KKS\KKS -PluginDll <待测DLL>
```

| 待测 DLL | 实际 KKS CharaStudio 结果 |
| --- | --- |
| 正式发布的 KK 1.0.3.10 | 两次稳定复现原始实例化失败，脚本退出码 1 |
| 旧 KKS 1.0.3.8 | 初始化、Harmony 注册成功，退出码 0 |
| 新 KKS 1.0.3.10 | 初始化、Harmony 注册成功，退出码 0；`disableMales=True` |

失败信号与原报告完全一致：

```text
The script 'ThighPhysicsController.ThighPhysicsControllerPlugin' could not be instantiated!
(Filename: Line: 227)
```

新构建成功信号：

```text
Loading [Soma Dynamics 1.0.3.10]
Soma Dynamics initialized (... disableMales=True ...)
Native breast and Studio pose-change patches installed.
SOMA_RUNTIME_TICK controllers=0 collectMetrics=False
```

## 构建与回归

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-ThighPhysicsController.ps1 -Game KK -Force
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-ThighPhysicsController.ps1 -Game KKS -Force
```

- 两种 Release 构建均为 0 警告、0 错误，并可在同一工作区先后构建。
- 参数模型 1,178 条断言、25 个正式控制器/求解器集成场景、性能源码和服装状态门禁通过。
- 二进制检查器对 KK DLL 按 KKS 检查时失败，对各自匹配的 DLL 通过；打包时始终执行，
  `-SkipTests` 不会跳过此检查。
- `bin`、`obj` 和 NuGet 锁文件按游戏隔离；所有版本的生成源码均排除出编译输入。
- Windows PowerShell 5.1 已执行构建与加载脚本验证。

原始日志和每次运行的 SHA-256 保存在本地忽略目录 `artifacts/issue15-load-smoke/`。
此次实机验证覆盖启动加载、补丁注册和插件更新，不包含角色视觉、保存重载或 FPS 验收。

## 用户恢复步骤

关闭 KKS/Studio，将 `SomaDynamics_KKS_1.0.3.10.zip` 中的 `BepInEx` 合并到 KKS
根目录，覆盖同路径旧 DLL，保留配置及用户预设。确认安装了 KKSAPI 和 KKS 版
ExtensibleSaveFormat，并且只保留一份活动的 `ThighPhysicsController.dll`。
