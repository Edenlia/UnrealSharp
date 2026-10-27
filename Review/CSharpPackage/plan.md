> 2026-10-27 test 分支交接：本文保留原验证阶段记录。原始证据未随分支发布；跨电脑审查及复测见 [README](README.md) 和 [复现步骤](REPRODUCE.md)。功能 HEAD 仍是 fddb33ae，test 分支额外携带工具与文档。

# C# 自动打包计划

## 目标与范围

在 featre/csharp-package 保留插件内 staging 集成，移除独立的插件查找优先级修复。UE 5.7/5.8 使用标准 BuildCookRun 自动发布；UE 5.6 使用 BuildCookRunUnrealSharp。托管文件在平台封装与签名前登记为 NonUFS。不修改项目 Target、uproject、打包配置或创建项目 Automation 桥接；正常生成输出仍位于 Intermediate/Binaries/Saved。

重复安装插件的优先级问题属于另一份 PR，本分支沿用原始 resolver。当前测试环境只有项目级 UnrealSharp；staging 仍核对发布插件与 native receipt 的身份。UE 5.6 默认 Editor 打包按钮不会自动改用独立命令。

仅本地改写、验证和报告，不推送、不发布或更新 PR、不合并 main/cp-main。Mac 签名、移动端、NativeAOT、Universal Mac 和 DLC 不扩大验证或支持。

## 基线与提交

- 仓库：C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp；origin 为 Edenlia/UnrealSharp。
- 原始 PR 基线保持 cb8577e3a47b5bdc5454239a17f61a6282a50ae2，不引入其他 PR 或更新 main。
- 提交 1：1723b7ba619b8e4ee778dde672cf9983e63cd6a9，5.7/5.8 自动 staging。
- 提交 2：fddb33aefd598fa0d1eb5e324dc5ae36fb788a47，5.6 独立命令、共享 staging 与 UHT 按名解析命令。
- 改写前由 backup/csharp-package-before-drop-resolver-20261025 保存；历史映射记入工作日志。

## 依赖与设计约束

- 5.7/5.8 通过 CustomStagingHandler 自动发现，Installed 项目脚本上下文由 -ScriptsForProject 提供。源码引擎项目发现未扩大验证。
- 5.6 没有该 API，通过 ModifyDeploymentContextCallback 接入。命令按引擎 API 可用性条件编译，不在新版重复执行。
- PreBuild 早于 UHT，PostBuild 依赖 native action 状态，不能覆盖仅 C# 变化或 skip-build staging，不作为主入口。
- UHT 引用 Automation 程序集，因此按名称解析单个 BuildCommand，避免枚举所有类型时加载 UAT 专用依赖。
- 所有版本共享 receipt、目标/配置/架构验证和隔离发布目录。

## 验证步骤与验收

1. 已完成恢复引用与本地 rebase。range-diff 仅移除 resolver 提交，其余补丁等价；最终源码不包含测试和报告。
2. 使用既有专用项目：5.6 的 SharpTestProject56_1/56_2，5.7 的 SharpTestProject57_1/57_2，5.8 的 SharpTestProject/SharpTestProject1。核对当前 Engine Build.version、配置与插件身份；Engine 只读。
3. A 均使用原始 cb8577e3，B 均使用最终 fddb33ae。另在 5.6 同一 B 项目检查改写前后行为与成本，明确区分于原始基线 A/B。
4. 完整核对 fork 快照与全部插件源文件，保留输出缓存；仅替换变化源码并移除快照不存在的旧源码。原样 AutoBuild Editor/Game 成功后，执行官方 managed build、Cook/Stage/Archive 与包内 C# 探针，核对 Game/Development、内容和加载路径。
5. 原始 A 标准 staging 不含托管文件，手工 PackageProject 后运行成功；候选单次 staging 自动发布。5.6 原始 resolver 在无重复插件时必须选择项目插件，不再用附带 resolver 修复的版本冒充原始 A。
6. 候选执行 receipt 边界、插件身份不一致拒绝、仅 C# 修改、失败不覆盖归档、skipstage 和恢复检查。对比真实生成文本与发布/暂存/归档清单、哈希；历史证据仅在源码和环境等价时引用。
7. 相关 warm staging 测量预热后至少三次独立采样，保留离散范围。顺序构建和运行，不清缓存、不强制重建或修改 AutoBuild。
8. 更新中文 validation.md、时间顺序 work-log.md 和英文 pr.md，交付两提交历史与实际验证结果。当前：历史改写、三版新候选功能/边界/增量/失败恢复、产物与三轮 warm A/B 验证全部完成。最终 HEAD fddb33aefd598fa0d1eb5e324dc5ae36fb788a47，工作树干净，仅本地且未推送。

## 操作边界

构建前检查同一引擎/输出占用，不终止用户进程。测试工具仅放在已授权测试项目，持久证据放在外部报告目录 amend-drop-resolver-20261025。生成 glue 不手改。

## 2026-10-27 跨电脑审查交接

- 从最终功能 HEAD 创建 test/csharp-package-test；原 featre/csharp-package 保持不变。
- 工具、计划、工作日志、验证总结、PR 草稿与复现说明放入 Review/CSharpPackage。
- 按用户后续指示，不提交原始证据、日志、JSON 清单、二进制或压缩包。
- 校验脚本语法与提交范围，向 origin 发布此 test 分支；不创建或更新 PR。
