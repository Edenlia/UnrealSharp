> 2026-10-27 test 分支交接：本文保留原验证阶段记录。原始证据未随分支发布；跨电脑审查及复测见 [README](README.md) 和 [复现步骤](REPRODUCE.md)。功能 HEAD 仍是 fddb33ae，test 分支额外携带工具与文档。

# UE 5.6 / 5.7 兼容验证

## 结论

保持 `featre/csharp-package` 的 `1723b7ba619b8e4ee778dde672cf9983e63cd6a9` 不变进行了验证。**目前不能宣称这两个版本的自动打包均通过。**

| 检查 | UE 5.6.1 | UE 5.7.4 |
| --- | --- | --- |
| 项目既有 AutoBuild Editor | 通过，exit 0 | 失败，exit 6；UHT 导出异常 |
| 项目既有 AutoBuild Game / Development | 通过，exit 0 | 未执行；Editor 构建已失败 |
| UAT 自动发现插件 Automation | 通过，无项目桥接 | 通过，移走旧桥接后仍可发现 |
| 当前 staging handler 编译 | 按条件排除，Engine 没有该 API | 包含并编译成功 |
| 标准 Cook / Stage / Archive | UAT exit 0；托管文件缺失，自动发布功能不支持 | 被原生构建前置条件阻断，未执行 |
| 手工 PackageProject | exit 0，但误选 Engine Marketplace 插件；来源混合，不能算通过 | 未执行 |
| 打包游戏运行 | 未执行；Cook 有 C# 父类缺失且手工发布来源混合 | 未执行 |

上述是 Windows x64 Development 的实际结果；不能外推为 Mac 签名或其他配置已验证。5.7 的阻断发生在已有项目测试头文件的绑定导出阶段，不是 staging handler 的 API 编译失败，也不能据此断言所有 UE 5.7 项目都无法构建。

## 环境、命令和实现一致性

- UE 5.6：`C:/Program Files/Epic Games/UE_5.6`，5.6.1 / CL 44394996，Installed Build。
- UE 5.7：`C:/Program Files/Epic Games/UE_5.7`，5.7.4 / CL 51494982，Installed Build。
- 项目：`C:/Users/edenlia/Workspace/UEProjects/SharpTestProject56`、`SharpTestProject57`。`.uproject`、`project_env.config.txt`、既有 AutoBuild 的引擎绑定一致。没有改原生源码、构建入口、Target 或配置。
- 两项目均从同一精确 Git 提交的完整 archive 部署，共 827 个版本管理文件；最终再次逐个校验，均未变化。源仓库仍为干净工作树，未增加提交。
- native 仅运行项目原有 `AutoBuild.bat` / `AutoBuildGame.bat`，参数不变。没有在 5.7 失败后清缓存、强制重建、换引擎或启动 Editor。
- UAT 使用 `-ScriptsForProject=<uproject>`，没有 `-ScriptDir`、项目 Build 桥接或永久配置注册。两版 UAT 使用其自带 .NET 8；UnrealSharp 托管项目目标仍是 net10.0，这两个框架用途不同。
- 测试命令和退出码逐次保存在 [原始证据目录](compat-ue56-ue57-20261025)，包括 `*.command.json`、`*.result.json` 和日志。副本中的测试 runner 可复核完整参数，但有防覆盖检查，不应直接重复覆盖本次记录。
- 5.6 手工发布选错 Engine 插件后，在其 `Managed/.../bin`、`obj` 生成了构建输出；发现时该命令已经完成。没有手工修改或删除共享 Engine 插件，也没有把这次输出当作候选产物验证。此次预检最初只查直接 Engine 插件路径，遗漏 Marketplace，随后已递归检查并更正。

主要证据：

- [5.6 Editor](compat-ue56-ue57-20261025/56-BuildEditor.result.json)、[Game](compat-ue56-ue57-20261025/56-BuildGame.result.json)、[标准打包](compat-ue56-ue57-20261025/56-CookStage.log)、[打包后缺失检查](compat-ue56-ue57-20261025/56-before-manual.json)。
- [5.6 手工发布选错插件](compat-ue56-ue57-20261025/56-Manual.log)、[原生 receipt 对应的 NonUFS 清单](compat-ue56-ue57-20261025/56-CookStage-FinalCopyWin64_NonUFSFiles.txt)。
- [5.7 AutoBuild 失败](compat-ue56-ue57-20261025/57-BuildEditor.log)、[详细 UBT 日志](compat-ue56-ue57-20261025/57-BuildEditor-UBT.log)、[UAT 命令发现](compat-ue56-ue57-20261025/57-List.log)、[包含 handler 的 Automation 构建记录](compat-ue56-ue57-20261025/57-Automation-build-record.json)。
- [部署一致性](compat-ue56-ue57-20261025/deployment-equivalence.json)、[测试后源码完整性](compat-ue56-ue57-20261025/source-integrity.json)。

## 问题一：5.6 缺少自动 staging 扩展

5.6 没有 `AutomationUtils/CustomStagingHandler.cs`。候选工程的 `Compile Remove="Staging\\*.cs"` 条件生效，因此 Automation 和原生构建可以成功，但没有 handler 被加载。真实打包输出中，Stage、Archive 的 `Binaries/Managed` 以及 `Intermediate/UnrealSharp/Publish` 均不存在；这与该条件排除完全一致。

5.7 有该 API，其 UAT 构建记录确实包含 `Staging/UnrealSharpStagingHandler.cs`。因此当前实现的自动 staging API 下限是 5.7，而“旧版本仍可编译”不等于“旧版本具备自动发布”。

### 适合的兼容方案

推荐保持 **5.7+ 使用 CustomStagingHandler；5.6 使用插件提供的一步 UAT 命令**。后者可继承标准 `BuildCookRun`，通过 `SetupParams()` 得到参数，组合 `ModifyDeploymentContextCallback`，在回调里按 receipt 发布并调用 `SC.StageFiles(NonUFS, ...)`，最后仍交给 `DoBuildCookRun()` 完成平台流程。已有 callback 应组合保留；新版避免同时触发两次发布。

5.6 的回调位于清单生成后、目录映射前，后续仍按 UE 流程 Stage、Package、Archive。文件因此可在封装和签名前进入平台流程，符合 [Epic 对 Build/Cook/Stage/Package 阶段的说明](https://dev.epicgames.com/documentation/en-us/unreal-engine/build-operations-cooking-packaging-deploying-and-running-projects-in-unreal-engine)。这条方案不需要往项目写 Target、配置或 Automation 桥接工程，但 **5.6 需要使用插件的打包按钮或对应 CI 命令**，不能声称原生 Package Project 按钮零配置就会自动调用它。该方案本次只做源码分析，未实现和运行。

其他入口的具体限制：

| 入口 | 5.6 实际能力与限制 |
| --- | --- |
| CustomDeploymentHandler | `WinPlatform` 读 WindowsTargetSettings.CustomDeployment 或 `-customdeployment`。不是自动启用，且只容纳一个 handler；Mac 未覆写该选择入口，Platform 默认返回 null。可做 Windows 显式接入，不能直接当作统一自动方案。 |
| CustomStageCopyHandler | 需要 ProjectPackagingSettings.CustomStageCopyHandler 配置；主要处理已入清单文件的复制，不负责可靠生成完整发布清单。 |
| uplugin PreBuildSteps | 插件支持，但在 Target.BuildAsync / UHT 前运行，首次可能没有必要 glue；跳过 native build 的 Stage 不执行它。 |
| uplugin PostBuildSteps | 是依赖 receipt、产出 `.ran` 的缓存 action；只改 C# 不一定使该 action 过期，skip-build 也不会执行。 |
| RuntimeDependencies / ModuleRules | 依赖发现发生在构建图阶段，不能可靠覆盖后生成的完整 publish 文件集及仅 C# 的增量发布。 |

若必须同时满足“5.6 原生按钮不变、无需配置/额外命令、项目目录无额外持久内容”，当前已核实的公开扩展点无法完整满足；需要放宽入口要求，或另行考虑 Engine 侧回补扩展。不能用 Pre/PostBuild 假装覆盖所有增量及跳过构建路径。

本地源码定位：

- [5.6 回调与目录映射](<C:/Program Files/Epic Games/UE_5.6/Engine/Source/Programs/AutomationTool/Scripts/CopyBuildToStagingDirectory.Automation.cs:2035>)；[BuildCookRun 的标准执行链](<C:/Program Files/Epic Games/UE_5.6/Engine/Source/Programs/AutomationTool/Scripts/BuildCookRun.Automation.cs:244>)。
- [Windows handler 选择](<C:/Program Files/Epic Games/UE_5.6/Engine/Source/Programs/AutomationTool/Win/WinPlatform.Automation.cs:397>)；[默认 Platform 无 handler](<C:/Program Files/Epic Games/UE_5.6/Engine/Source/Programs/AutomationTool/AutomationUtils/Platform.cs:764>)。
- [PreBuild 执行顺序](<C:/Program Files/Epic Games/UE_5.6/Engine/Source/Programs/UnrealBuildTool/Modes/BuildMode.cs:1119>)；[PostBuild 的 receipt/.ran 依赖](<C:/Program Files/Epic Games/UE_5.6/Engine/Source/Programs/UnrealBuildTool/Configuration/UEBuildTarget.cs:2882>)。

## 问题二：5.7 测试项目触发已有的 UHT 默认值导出缺陷

原样 AutoBuild 报：`Internal Compiler Error: System.ArgumentOutOfRangeException ... (Parameter 'index')`。发生在 UnrealSharp 生成 C# bindings 时，早于原生编译和 Stage。

项目现有 `DefaultGuardProbe.h` 带有嵌套 `FTransform` 的 `CPP_Default_Value`，包含 Rotation、Translation、Scale3D。当前 `SimpleTypePropertyTranslator.ExportDefaultStructParameter` 直接 `Split(',')` 后按 `ScriptStruct.Children[i]` 取字段；该文本被切成 10 段，而 FTransform 只有三个 UHT 字段，索引 3 即越界。`FSoftClassPath` 顶层路径也需要专门处理，不能当普通字段列表。

这是**静态源码确认的越界路径，与本次异常类型和发生阶段一致**。原始 UHT 日志没有完整异常堆栈；本次没有改 exporter 加日志或更换测试头文件重跑，故不把“具体哪个参数最先抛错”写成已动态确认。导出器及项目头文件均不在本次三个打包文件的改动中，不能将此直接归为 UE 5.7 或 staging API 不兼容。

建议独立处理默认值解析，保留默认参数语义，覆盖嵌套括号、字符串中的分隔符、字段名匹配、路径类型和边界验证。不要只截断越界索引或把复杂默认值静默改为零值。仓库已有 `origin/bugfix/default-value-parser` 的 `ab0f42a8c7ed5fe96a18a309171a91cb77a6a309`，可作为待审查、待重新验证的独立修复来源；本次没有合入。项目里的旧 Fab 测试期望某些默认值回退为零，后续如采用保真解析，也应独立校正该夹具预期，不把这项测试历史混入打包功能。

定位：[测试头文件](C:/Users/edenlia/Workspace/UEProjects/SharpTestProject57/Source/SharpTestProject57/DefaultGuardProbe.h:13)、[直接逗号拆分](C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp/Source/UnrealSharpManagedGlue/PropertyTranslators/SimpleTypePropertyTranslator.cs:93)、[按索引取字段](C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp/Source/UnrealSharpManagedGlue/PropertyTranslators/SimpleTypePropertyTranslator.cs:115)、[FTransform 的三个字段](<C:/Program Files/Epic Games/UE_5.7/Engine/Source/Runtime/CoreUObject/Public/UObject/NoExportTypes.h:2114>)、[输入拆分分析](compat-ue56-ue57-20261025/57-default-value-analysis.json)。

## 问题三：5.6 重复安装时，手工发布选错插件

实际同时存在项目插件及 `UE_5.6/Engine/Plugins/Marketplace/UnrealSharp`。原生 receipt 指向项目插件，Cook 日志也明确记录项目插件优先。可是 `GetUnrealSharpUPlugin()` 取 `PluginsBase.EnumeratePlugins(Project)` 的第一个同名项；Engine 的枚举实现先列 Engine，再列 Project，所以发布选中了 Engine 副本。日志中的 `dotnet publish on ...Engine/Plugins/Marketplace/UnrealSharp/Managed/UnrealSharp` 是直接证据。

这也是基线已有的问题。候选 5.7+ handler 有 receipt 与 resolver 路径一致性检查，可以阻止静默混用，但不会替代 resolver 的正确选择；若同样环境放到 5.7+，应预期明确失败，而非自动解决重复插件。

建议采用与 UBT 一致的过滤、覆盖规则，并让发布使用与 receipt 一致的插件身份。仓库已有独立 `bugfix/plugin-find` 提交 `932f2bfcce17c58c2a3964c7b2e0b69f5f6d048f`，将该方法接到既有 `ReadAvailablePlugins`；这是比自行删除用户 Engine 插件更合适的修复方向。本次仅检查，不合入，也不对其他正在进行的分支验证作结论。

定位：[当前 resolver](C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp/Build/Scripts/Utilities/ProjectUtilities.cs:118)、[Engine 优先的原始枚举](<C:/Program Files/Epic Games/UE_5.6/Engine/Source/Programs/Shared/EpicGames.Build/System/EnumeratePlugins.cs:41>)、[候选的路径一致性检查](C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp/Build/Scripts/Staging/UnrealSharpStagingHandler.cs:64)。

## 测试准备问题、最终状态及后续验证顺序

5.6 初次 Cook 的 C# Blueprint 父类缺失，属于此次准备流程未完成用户程序集初始化：移走旧托管输出后，native AutoBuild 生成 bindings，但 `BuildUserSolution()` 遇到 `FApp::IsUnattended()` 会直接返回。不能只启动带 unattended 的空 Python commandlet，就认为已经构建用户程序集。后续应在 Cook 前用插件官方 `BuildUserSolution` 等命令建立正确的 Editor 用户程序集/load-order，并确认 C# 类加载成功；本次因重复插件身份问题未继续写错源。该失败保留，不算打包运行成功。

所有测试进程均已退出。两个测试项目保留本次候选完整插件；旧插件、托管输出、5.7 旧项目桥接保存在各自 `Saved/PackageCompatibility-20261025/before`，未自动恢复。5.6 Archive 是混合来源的诊断产物，不能拿来发布或作为候选验证包；5.7 没有新的验证包。

官方 UHT 在 5.7 自动更新了 `StageCSharpValidation.csproj` 的项目绑定引用及 `ManagedSharpTestProject57.sln.projects.json`；其余预先记录的 Source/Config/AutoBuild/uproject 输入未变。见两个版本的 `*-input-changes.json`。没有手工修改夹具 C#/C++ 或资产，未解析二进制资产。

建议后续先独立确认默认值解析和插件发现修复，再按原样 AutoBuild → 官方 Editor managed 构建 → Cook → Stage → Archive → 游戏探针复验 5.7。若确定要支持 5.6 一步打包，再实现插件 UAT wrapper 并验证相同的首次/增量/skip-build/失败传播路径。以上均为后续方案，本次按用户要求保持实现不变。
