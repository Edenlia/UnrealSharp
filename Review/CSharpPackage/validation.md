> 2026-10-28 test 分支交接：功能实现为 b59ff754；本文中的“仅本地、未推送”描述原验证阶段。此次按用户要求同步审查资料，不携带原始日志和证据。入口见 [README](README.md)，Client/Server 见 [专用说明](ClientServer/README.md)。

# C# 自动打包及 Editor 入口验证

## 结论

本地第三个提交 b59ff754 已完成。三版本均实际通过电脑操作打开 Editor 菜单，并与父提交对照；三个候选均从 Editor 点击发起打包，自动带入 C#，归档游戏启动并执行 C# 探针成功。

| 引擎 | 父提交 A 的 UnrealSharp 菜单 | 最终候选 B 的实际入口与结果 |
| --- | --- | --- |
| UE 5.6.1 | 有 Package Project；选择空目录提示缺少已有 exe，不启动 UAT | UnrealSharp > Package Project → BuildCookRunUnrealSharp；空目录完整打包、包内运行成功 |
| UE 5.7.4 | 有 Package 分组和旧按钮 | 移除分组/按钮；Platforms > Package Project → 普通 BuildCookRun 自动带入 C#，包内运行成功 |
| UE 5.8.3 | 有 Package 分组和旧按钮 | 移除分组/按钮；Platforms > Package Project → 普通 BuildCookRun 自动带入 C#，包内运行成功 |

5.6 的原生 Platforms 按钮没有被替换，仍不会自动选用专用命令。无 handler 时保留的 UnrealSharp 入口限定主机平台；5.7/5.8 使用原生平台菜单。

## 版本、提交与证据边界

- 仓库：<WORKSPACE>/CodeProjects/UnrealSharp，分支 featre/csharp-package。
- 原始 PR base：cb8577e3a47b5bdc5454239a17f61a6282a50ae2。
- 最终 HEAD：b59ff754ada2ad10c02b65f590c5f18585f8271c。
- upstream/main 本轮只读 fetch 为 115e6753b2f8c830f084a3e453b7ef3b1230eb33，新增内容与本任务无重叠，没有合入。
- 三个有序 commit：1723b7ba619b8e4ee778dde672cf9983e63cd6a9；fddb33aefd598fa0d1eb5e324dc5ae36fb788a47；b59ff754ada2ad10c02b65f590c5f18585f8271c。
- 新 commit 只修改三个 Editor 文件，98 行新增、19 行删除。完整 base..HEAD 为 9 个生产插件文件，255 行新增、41 行删除。没有测试、计划、报告或项目配置进入提交。
- 没有推送、发布/更新 PR、修改 test/csharp-package-test 或合并 main/cp-main。

本轮增量 A=fddb33ae、B=b59ff754，六槽均为完整 fork 快照，不能把本轮 A 称为原始 cb8577e3。原始 base 到 fddb 的 Editor 源码没有变化，因此本轮真实菜单 A/B 同时说明原始入口到最终入口的差异。原始 base 到最终组合的 UAT/publisher、边界、生成输出及三轮性能结果，复用 [2026-10-25 完整 A/B](editor-menu-20261028/validation-before.md) 的等价部分；第三提交只改变 Editor 入口，没有改变 Build/Managed/exporter 实现。该范围经过 Git diff 及最新发布文件核对。没有把历史测试描述为今天重新运行。

## 每项提交的责任和验证

1. **1723b7ba：Publish managed code during Unreal staging**
   从 native receipt 取得目标、配置、架构和插件身份，调用现有 PackageProject 发布到隔离 Intermediate 目录，以 NonUFS 登记完整输出。CustomStagingHandler 在平台封装/签名前执行。原始基线 staging 缺少托管文件、手工发布后成功；候选自动 staging 和包内运行成功。最新 5.7/5.8 原生 UI 路径再次覆盖。
2. **fddb33ae：Add managed staging to the UE 5.6 packaging command**
   没有 handler 时编译 BuildCookRunUnrealSharp，通过 ModifyDeploymentContextCallback 复用 staging。拒绝 foreign/foreigncode；按名称查找命令，避免 UHT 枚举 UAT 专用类型。旧版命令、边界、失败保护和仅 managed 修改回归通过；最新 5.6 UI 也完整执行。之前独立 resolver 修复已移出，本提交不改变插件发现优先级。
3. **b59ff754：Align Editor packaging actions with staging support**
   Build.cs 使用与 Automation csproj 一致的 CustomStagingHandler.cs 存在性判断。无 API 时保留菜单、注册和绑定，按钮由后置发布改为全链打包；保存脏包，读取平台 target/configuration/architecture 及常用 Packaging Settings。有 API 时不注册、不绑定、不显示该按钮。三版本新 Editor 编译与真实菜单 A/B、三个候选完整打包和运行均通过。

## 环境、构建和加载身份

| 版本 | Engine 身份 | A / B 项目 |
| --- | --- | --- |
| 5.6.1 | Installed，CL 44394996 | SharpTestProject56_1 / SharpTestProject56_2 |
| 5.7.4 | Installed，CL 51494982 | SharpTestProject57_1 / SharpTestProject57_2 |
| 5.8.3 | Installed，CL 58210709 | SharpTestProject / SharpTestProject1 |

Windows 同一主机、Win64 x64 Development Game；Engine 根分别为 C:/Program Files/Epic Games/UE_5.6、UE_5.7、UE_5.8。UAT 使用各自 Engine 自带的 .NET；managed 项目为 .NET 10。六槽都是 Project-level UnrealSharp，没有 Engine 同名副本。

六槽先检查占用，严格执行本项目原有 AutoBuild.bat、AutoBuildGame.bat，成功后官方 BuildUserSolution，再打开 Editor。没有改 native options 或引擎，没有 Live Coding、强制重建或清理缓存。B 的 Editor 本轮编译 41 actions，UBT 分别 18.83 / 19.56 / 23.00 秒；Game 复用 up-to-date native 输出。测试打包配置统一为 Build=Never、Development、Entry、Pak/IoStore，以避免 Editor 持有 DLL 时再次 native 编译；不等于测试了所有 -build/clean 分支。

A58 的既有 DuplicateTransientProbe.cpp、GasFreeParameterProbe.cpp、MaintainerParameterProbe.cpp 在 up-to-date 扫描中出现首个头文件不匹配文字；UBT 明确 Result: Succeeded、exit 0，未重编译或修复这些无关夹具。保留日志，不声称这些旧夹具经过全量重编译。

每槽 829 个插件源文件在部署和最终核对均匹配快照；每个 Editor 的 15 个 UnrealSharp 模块均来自对应项目 Plugins/UnrealSharp。Automation build record 指向同一项目的 Build/Scripts。八项登记输入（AutoBuild、配置、uproject、probe 等）执行前后哈希未变。新 commit 的三个源码文件在提交前与已测试 candidate 逐字节相同，提交后工作树干净。

## 本轮实际执行的功能检查

- A56：旧按钮选新空目录，出现缺少项目 exe 的预期错误，未启动 UAT。此为有效负例，不能用于性能比较。
- B56：取消目录选择，不启动 UAT；再次选新空目录，命令为 BuildCookRunUnrealSharp，包含 cook/stage/package/archive 和正确项目、target、Win64/Development、Pak/IoStore 等参数。UAT exit 0。
- A57/A58：实际检查旧 Package 菜单存在。本轮不重复它们的完整 native package；先前相同实现的 UI 打包记录见 [前轮 UI 总结](../editor-package-matrix-20261028/verification.md)。
- B57/B58：实际检查插件 Package 分组消失，其他菜单保留；点击 Platforms > Package Project > Package Project，选择 Windows、Use Project Setting (Development)，输出到新空 Archive。日志确认 Turnkey + 普通 BuildCookRun，自动执行 managed publish，UAT exit 0。
- 三个 B 均在关闭本任务 Editor 后运行新归档内 exe，参数为 nullrhi/unattended/nosound/ExecCmds=Quit，exit 0。这是包内 C# 初始化/加载/内容探针验证，不是交互式 gameplay 或渲染测试。

| 候选 | 探针内容 | 目标/配置 | 加载路径 | Publish/Stage/Archive 文件数 |
| --- | --- | --- | --- | ---: |
| B56 | version=2856，package-content-ui-56-20261028 | Game/Development | 新 Archive 内程序集、runtime、exe | 220 |
| B57 | version=2857，package-content-ui-57-20261028 | Game/Development | 新 Archive 内程序集、runtime、exe | 220 |
| B58 | version=28-ui-20261028，package-content-ui-20261028 | Game/Development | 新 Archive 内程序集、hostfxr、exe | 225 |

三处相对路径、文件数量、大小、SHA256 全部一致，未通过后置手工发布补齐新包。

## 累积回归、产物与复用依据

前两提交的完整原始基线比较已覆盖每引擎 18 项、共 54 项 receipt/命令边界：包括缺失/冲突目标、平台/架构、项目与插件身份、DLC 和旧版 foreign 拒绝。三版 managed-only 修改、预期 C# 编译失败不覆盖旧归档、skipstage 保留旧版本及恢复均通过；native/Editor managed 输出不受这些 staging 操作影响。新 commit 不改这部分实现，复用其证据。

历史真实 UHT 输出比较共 94,905 个文本文件一致（5.6：28,430；5.7：29,254；5.8：37,197 插件 + 24 项目）。本轮不是一次新的全量生成比对；Editor 菜单改动未进入 exporter/managed 生成路径，保留既有生成器执行、清单、API/IL 证据及其范围限制。

最新 B 发布产物相对前轮最终 B：
- 5.6 的 217/220、5.7 的 217/220、5.8 的 222/225 文件逐字节相同。
- 每版仅 package-stage.txt、PackageStageProbe.dll、PackageStageProbe.pdb 改变，均对应本次更新的探针版本/内容输入；没有增删发布文件。
- 所有 656 个其余文件，包括生产托管程序集、自包含 runtime 和原生依赖，均与此前最终候选一致。这支持未变发布路径和 runtime 回归证据的复用。

历史 RID/非 RID DLL/PDB 过渡差异、5.8 A 预存 AssetIds 常量差异及完整 API/IL 分析留在 [前轮报告](editor-menu-20261028/validation-before.md) 和对应原始记录中。未把这些已有项目或调试信息差异伪称为二进制全部相同。本次 656 文件比较直接使用最终归档哈希。

## 性能范围与结果

本次只改变 Editor 发起命令和菜单可见性，不增加游戏运行时逻辑。沿用前两提交在同机各一次预热、每侧三次独立进程的相关 warm staging 测量。A=原始基线 Stage + 手工 PackageProject；B=自动 Stage。单位秒，排除 native build、Cook、包内运行；没有 cold-cache 条件。

| 版本 | A 三次 | B 三次 | A / B 中位数 | B-A |
| --- | --- | --- | --- | --- |
| 5.6 | 10.097692 / 9.904136 / 9.584307 | 10.085889 / 10.040240 / 10.324300 | 9.904136 / 10.085889 | +0.182 s，+1.84% |
| 5.7 | 9.847054 / 9.683689 / 10.046692 | 10.231676 / 10.140036 / 10.043577 | 9.847054 / 10.140036 | +0.293 s，+2.98% |
| 5.8 | 42.159666 / 41.527461 / 43.512242 | 42.625145 / 42.523196 / 41.413025 | 42.159666 / 42.523196 | +0.364 s，+0.86% |

这组历史数据描述原 staging 改动的成本，没有将其冒充第三提交新的三轮端到端测量。最新 UI 各一次完整成功运行：5.6 UAT 约 32 秒（Cook 16.49，Stage 13.85）；5.7 BCR 33.85 秒（Cook 20.89，Stage 11.28）；5.8 BCR 42.54 秒（Cook 25.09，Stage 15.87）。新 UI 值包含 Cook、缓存状态和封装差异，与上表分母不同，只用于复现观测。n=1 不支持速度结论；不宣称稳定提速，也未测菜单展开延迟。

## 可复现步骤

1. 选对应版本的专用项目；核对 Engine、项目插件快照以及 AutoBuild.bat、AutoBuildGame.bat、project_env.config.txt。关闭/等待占用相同输出的本任务进程，顺序执行 canonical Editor、Game 构建并检查退出码；再执行项目 managed build。不得用本文简写替换项目规定的 native 命令。
2. 本测试配置 Build=Never、Development、Entry、Pak/IoStore；打开该项目 Editor。5.6 点击 UnrealSharp > Package Project；5.7/5.8 点击 Platforms > Package Project，在 Windows/Development 下执行 Package Project。
3. 选一个新的空归档目录，等 UAT 成功。日志应在 Stage 中出现 Publishing managed code，不需要再执行 PackageProject。
4. 运行归档 exe。当前夹具通过 PACKAGE_STAGE_PROBE_RESULT 环境变量写出 C# 结果，核对 Game/Development、内容标记、程序集/hostfxr 来自归档目录。
5. 若用 CI：5.6 用 BuildCookRunUnrealSharp；5.7/5.8 用 BuildCookRun。保留 -ScriptsForProject 和实际平台的原有打包参数。源码引擎 discovery 可能另需 ScriptDir，未在本矩阵执行。

本轮可复现工具在 SharpTestProject/Tools/UiPackageMatrix.ps1、FinalizeUiPackageEvidence.ps1。Prepare 会部署插件和测试配置，不应在未知项目上直接运行。Tools 没有进入生产 commit。

## 记录、限制与最终状态

精简汇总和必要复现记录保存在 [editor-menu-20261028](editor-menu-20261028/)：final-verification.json、published-output-diff.json，以及各槽 source-manifest、inputs、loaded-modules、Automation build record、构建/Editor/runtime 记录和最终复制清单。旧完整三轮数据在 [amend-drop-resolver-20261025](amend-drop-resolver-20261025/)；不要求审阅者阅读大日志才能理解上述结论。

限制：
- 真正执行的矩阵为 Windows x64 Development Game。Mac 签名、移动端、NativeAOT、Universal Mac、DLC、完整 Client/Server/Shipping、源码引擎没有因此获得验证。
- UE 5.5 及更早仅按 API 能力选择旧入口，未实际编译；不能保证所有更早 Editor API 均兼容。
- 5.6 插件入口复制常用包装参数，不是 Turnkey 全部选项的等价实现：不转发自定义 DDC graph/ZenStore；IfProjectHasCode 保守执行 native build，未复刻 promoted receipt 优化。
- 取消目录已经实测；脏包保存取消、Shipping/Client/Server、HTTP chunks、非默认 architecture 等命令分支未在本轮 UI 执行。
- 重复 Engine/Project 插件优先级仍属于另一 PR。当前环境无重复副本，不能声称修复了重复安装。

任务 Editor/Cook/游戏进程均已退出；六槽最终 A=fddb33ae，B=b59ff754。B 保留新 probe 和新归档，历史插件备份移到报告目录，避免重复发现；没有自动恢复无关项目状态。生产工作树干净，只有上述三个有序本地提交，远端未更新。

[当前计划](plan.md) · [时间顺序工作日志](work-log.md) · [英文 PR 草稿](pr.md)
