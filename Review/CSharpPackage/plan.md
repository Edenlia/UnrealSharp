> 2026-10-28 test 分支交接：功能实现为 b59ff754；本文中的“仅本地、未推送”描述原验证阶段。此次按用户要求同步审查资料，不携带原始日志和证据。入口见 [README](README.md)，Client/Server 见 [专用说明](ClientServer/README.md)。

# C# 自动打包计划

## 目标与范围

在 featre/csharp-package 中完成插件自身的 managed staging 集成，并按引擎能力提供合适的 Editor 入口。托管程序集和自包含 runtime 在平台封装、签名前登记为 NonUFS，避免打包完成后再手工复制。

- 有 CustomStagingHandler 的引擎：普通 BuildCookRun 自动接入；移除 UnrealSharp 菜单中的重复 Package Project。实际覆盖 UE 5.7/5.8。
- 无该 API 的引擎：提供 BuildCookRunUnrealSharp；UnrealSharp > Package Project 调用完整 Cook/Stage/Package/Archive，使用主机平台的项目打包设置。实际覆盖 UE 5.6。
- UE 5.6 原生 Platforms 入口保持原行为，仍需选 UnrealSharp 入口或使用专用命令。
- 生产方案不修改项目 Target、uproject、打包配置，不创建项目 Automation 桥接；正常生成输出位于 Intermediate/Binaries/Saved。
- 重复安装插件的优先级修复属于另一 PR；本分支沿用原 resolver，验证环境仅安装项目级 UnrealSharp。
- 本次只本地提交与报告，不 push、不发布 PR、不修改 test/csharp-package-test、不合并 main/cp-main。

## 基线与完成的提交

仓库：<WORKSPACE>/CodeProjects/UnrealSharp，origin 为 Edenlia/UnrealSharp。

原始 PR base：cb8577e3a47b5bdc5454239a17f61a6282a50ae2。
最终 HEAD：b59ff754ada2ad10c02b65f590c5f18585f8271c。

| 步骤 | 最终 commit | 完整责任 | 验证 |
| --- | --- | --- | --- |
| 1，完成 | 1723b7ba619b8e4ee778dde672cf9983e63cd6a9 | UE 5.7/5.8 CustomStagingHandler、receipt 验证、隔离发布及 NonUFS 登记 | 原始基线 A/B、边界与包内运行；5.7/5.8 最新真实 Editor 打包回归 |
| 2，完成 | fddb33aefd598fa0d1eb5e324dc5ae36fb788a47 | UE 5.6 独立 BCR 命令、共享 staging、UHT 按名解析命令 | 原始基线 A/B、增量/失败/skipstage 回归及三轮性能；5.6 最新完整 UI 调用 |
| 3，完成 | b59ff754ada2ad10c02b65f590c5f18585f8271c | 按 API 能力控制 Editor 按钮；旧版由后置发布改为完整打包 | 三版本父提交/候选菜单 A/B；5.6 取消及空目录；三候选真实点击打包、包内运行、复制链 |

本轮只读 fetch 查到 upstream/main=115e6753b2f8c830f084a3e453b7ef3b1230eb33，新增 managed class deletion guard 与本任务无重叠。继续用户指定现有 PR 基线，不合入无关提交。移除 resolver 前的历史由 backup/csharp-package-before-drop-resolver-20261025 保留；历史映射见工作日志。

## 设计约束

1. PreBuild 早于 UHT，PostBuild 依赖 native action 是否执行，均不能覆盖 managed-only 和 skip-build staging，因此主入口仍是 staging。
2. 新版走 CustomStagingHandler，旧版走 ModifyDeploymentContextCallback；Automation 和 Editor 使用相同的 API 文件存在性判断，避免两种入口重复运行。
3. UHT 按名称解析单个 BuildCommand，避免枚举所有类型时加载旧版命令的 UAT 专用依赖。
4. 发布目标、配置、架构和插件身份来自 native receipt；混合目标/架构、DLC 等不支持组合明确拒绝。
5. 旧版 Editor 使用现有异步 UAT helper。Turnkey 的命令构造为私有实现，插件不调用私有接口、不通过反射修改它。读取常用 Packaging Settings 和平台 target/configuration/architecture，先保存脏包。
6. 旧版按钮限定主机平台；IfProjectHasCode 保守请求 native build，不复刻 promoted receipt 的跳过优化。自定义 DDC graph、ZenStore 等并非完整 Turnkey 参数等价实现。
7. 能力判断可涵盖没有 handler 的更早分支，但低于 UE 5.6 未实际构建，不宣称已验证兼容。

## 验证与验收

专用六槽：5.6 的 SharpTestProject56_1/56_2，5.7 的 SharpTestProject57_1/57_2，5.8 的 SharpTestProject/SharpTestProject1。均为对应 Installed Engine，Win64 x64 Development Game，项目级插件，Engine 只读。

- 原始基线与前两提交最终实现的功能、54 项边界、生成文件与性能比较保存在 amend-drop-resolver-20261025；第三提交未改变这部分 UAT/Managed/exporter 源码，按等价范围复用。
- 本轮增量 A=fddb33ae，B=b59ff754；原始 base 与 fddb 的 Editor 源码完全相同，当前菜单前后对照也覆盖原始入口差异。没有把 A 说成重新部署的 cb8577e3。
- 六槽均原样执行 canonical AutoBuild Editor/Game，成功后再 managed build、打开 Editor；UI 测试配置 Build=Never，避免 Editor 占用时再进行 native 编译。保留缓存，使用新空 Archive。
- A56 点击旧按钮要求已有 exe；B56 取消不启动 UAT，选择空目录完整打包成功。A57/A58 有旧按钮，B57/B58 无按钮且原生 Platforms 打包成功。
- 三候选实际执行包内 C# 探针，验证内容、Game/Development、归档程序集和 hostfxr 路径；220/220/225 个文件的 Publish/Stage/Archive 路径、大小、SHA256 一致。
- 保留既有每版各一次预热、三次独立 warm staging 样本。最新 UI 完整流程各运行一次，单独记录耗时，不拿失败的旧按钮或不同 Cook 工作量做速度比较。
- 最新六槽每槽 829 个源码文件、15 个已加载插件模块与部署身份一致；AutoBuild、项目配置、probe 等已登记输入在执行中未变化。
- 中文 validation、英文 pr 草稿及工作日志已同步最终三提交。源码提交只含生产插件文件。

## 边界与最终状态

所有本任务 Editor、Cook、游戏已退出。六槽保留 A=fddb33ae、B=b59ff754；未自动恢复成其他插件版本。历史插件备份移至报告目录，防止 UAT 递归发现。

未执行 Mac 签名、移动端、NativeAOT、Universal Mac、DLC、Shipping 或 UE 5.5 及更早组合。Client/Server 的 UE 5.8 source-engine 追加验证见下方；其余组合仍未执行。本机通过不外推为这些平台通过。测试工具保留在已授权项目 Tools，持久报告在报告目录，未进入源码提交。

## 追加验证：UE 5.8 Engine-level Client/Server（完成，无新提交）

2026-10-28，按 unrealsharp-verification 单实现流程验证同一 b59ff754。用户批准创建独立项目及 canonical AutoBuild，并批准整体迁至 UnrealEngine2/USharpTests/CsPkg 解决 ISPC 长路径限制。保持 Target/构建参数/缓存，恢复标准默认插件配置后 Editor/Client/Server/UnrealPak/managed 构建均成功。

实际操作 Editor 官方 Platforms > Package Project，分别选择 CSharpPackageClient 和 CSharpPackageDedicated，Win64/x64/Development。两者均自动发布并打包成功，包内 C# 分别确认 Client/Server 条件及归档 runtime，进程 exit 0，各 220 个 managed 文件复制链一致。没有产品修改、额外 commit 或 push。PR 草稿新增验证及有条件 supersede #785 的说明，明确 Mac 未验证、旧 CLI 参数并未逐项移植。

过程中的长路径构建失败、精简插件配置 LevelEditor 加载顺序断言、排除 Editor 内容导致的 Landmass Cook 失败，均保留在[独立验证报告](ClientServer/verification.md)。最终标准测试配置下两项通过，不将失败尝试隐藏为首次成功。

[验证总结](validation.md) · [工作日志](work-log.md) · [英文 PR 草稿](pr.md)
