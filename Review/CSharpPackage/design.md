> 2026-10-27 test 分支交接：本文保留原验证阶段记录。原始证据未随分支发布；跨电脑审查及复测见 [README](README.md) 和 [复现步骤](REPRODUCE.md)。功能 HEAD 仍是 fddb33ae，test 分支额外携带工具与文档。

# 插件接入点评估

基于当前 upstream/main cb8577e3 与本机 UE 5.8.3 / CL 58210709 的文本源码。这里的时序结论是源码分析，运行结论另见 validation.md。

| 入口 | 插件内可实现 | 关键边界 | 决定 |
| --- | --- | --- | --- |
| uplugin PreBuildSteps | 是，UBT 展开 PluginDir、ProjectFile、TargetName、TargetPlatform、TargetConfiguration、TargetType | 在首次目标 UHT glue 生成前执行；跳过 Build 就不执行；没有直接 architecture 变量 | 不承载 publish |
| uplugin PostBuildSteps | 是，可从 receipt 补齐架构 | UBT 创建 PostBuildStep action，其 prerequisite 是 receipt，输出是 .ran；仅 C# 修改不保证 native receipt 过期；跳过 Build 不执行 | 不作为唯一入口 |
| ModuleRules + RuntimeDependencies | 是 | 规则构造与通配符枚举不是每次 staging 的钩子；首次发布产物尚不存在，新增托管依赖和缓存图有时序问题 | 不在规则构造器中运行发布或手改 receipt |
| UHT exporter | 是，已有基础设施 | 只有 UHT 被触发才运行；只改 C# 和 skip-build 不保证执行；发布职责与生成混合 | 保留现有生成职责 |
| Editor 打包按钮/额外命令 | 可以 | CI 与标准 Stage 不一致；仍需另外维护调用链 | 保留旧手工功能作为兼容入口 |
| CustomStageCopyHandler | 需要配置具体类型 | 单个 handler 配置容易与项目既有扩展冲突；只是复制阶段 | 不使用 |
| CustomStagingHandler.GetFilesToStage | 是 | UE API 可用且 Automation 工程已发现时才工作；有多 handler 共存和完整 DeploymentContext | 选择 |

## 实际数据流

标准 BuildCookRun -> 创建 DeploymentContext / 读取 StageTargets receipt -> 自动枚举 handler -> 根据 receipt 中 UnrealSharp.uplugin 激活 -> 使用原有 PackageProject 发布到 Intermediate/UnrealSharp/Publish/目标/平台/配置/架构 -> StageFiles NonUFS -> 平台映射/复制/封装/签名/归档。

不再从 archive 中猜 exe 名或追加 .app 内容，因而不依赖尚未合入的 #785。自包含运行时和程序集必须是 loose 文件，不能放进 UE pak 后让 hostfxr 直接读取。

## 自动发现的真实边界

`CompileScriptModules.InitializeScriptModules` 在 Installed 模式传入 GameDirectories；`Rules.FindAllRulesSourceFiles` 枚举插件并扫描 Source、Tests、Build；`CustomStagingHandler.GetCustomStagingHandlers` 扫描已加载 Automation assemblies。Editor 标准入口携带 ScriptsForProject；CI 对引擎目录外项目也必须传这个 UE 参数，单独的 `-project` 不负责脚本发现。

源码引擎当前分支把 GameFolders 设为空，但仍接受 AdditionalScriptsFolders。因此 source engine 的项目级安装使用 `-ScriptDir=<UnrealSharp>/Build/Scripts` 可以纯插件加载。此限制在 UAT 执行 BuildCookRun 之前发生，Pre/PostBuild 不能为同一进程补回已完成的程序集发现。旧实验生成项目 Build/UnrealSharp 桥接工程虽可绕过，但不符合本次减少项目侵入的要求，不采用。

本实现不修改引擎、不注入项目 csproj/Target/uproject/永久配置。原有 managed 构建正常产生 Script 下的 SDK 配置和 Intermediate/Binaries 输出，这不是新增 staging 注册机制。

## 支持范围

当前插件声明 Win64、Mac；新增 handler 使用这两个桌面平台的既有 JIT 发布布局。移动端需要各自运行时/AOT、文件布局和平台打包集成，不能仅登记 NonUFS 就声称支持。多个配置/目标同目录、Universal Mac 和 DLC 会明确拒绝，而不混合互不兼容的程序集。

Mac 的布局和平台包装时序可从 UE Apple/Mac Automation 源码追踪；Windows 环境无法证明实际 codesign、公证或 Mac runtime 加载。未执行的验证必须保留为限制。

## 参考

- [官方当前打包说明](https://www.unrealsharp.com/getting-started-and-fundamentals/packaging)
- [#786](https://github.com/UnrealSharp/UnrealSharp/issues/786)
- [#785](https://github.com/UnrealSharp/UnrealSharp/pull/785)
- UE 本机源码：Engine/Source/Programs/Shared/EpicGames.Build/System/{CompileScriptModules,Rules}.cs；UnrealBuildTool/Configuration/UEBuildTarget.cs；AutomationTool/AutomationUtils/{CustomStagingHandler,DeploymentContext}.cs；AutomationTool/Scripts/CopyBuildToStagingDirectory.Automation.cs。
