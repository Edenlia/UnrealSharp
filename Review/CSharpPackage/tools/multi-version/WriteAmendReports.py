import json
import statistics
from pathlib import Path
from AmendPackaging import BASE, HEAD, PRE, R, ROOT, save

audit = json.loads((R / 'artifact-audit.json').read_text())
copies = json.loads((R / 'final-copy-chain.json').read_text())
assemblies = json.loads((R / 'final-assembly-summary.json').read_text())
sources = json.loads((R / 'final-source-verification.json').read_text())
symbols = json.loads((R / 'symbol-summary.json').read_text())
calls = json.loads((R / 'publisher-call-counts.json').read_text())
env = json.loads((R / 'environment.json').read_text())
stats = {}
rows = []
for version in ('56', '57', '58'):
    data = json.loads((R / f'{version}-performance.json').read_text())
    assert len(data) == 8
    stats[version] = {}
    for slot in ('1', '2'):
        values = [x['seconds'] for x in data if x['slot'] == slot and not x['warmup']]
        assert len(values) == 3
        stats[version][slot] = {'values': values, 'median': statistics.median(values), 'min': min(values), 'max': max(values)}
        s = stats[version][slot]
        rows.append(f'| UE 5.{version[1]} | {"A 原始基线：Stage + Manual" if slot == "1" else "B 最终候选：自动 Stage"} | {" / ".join(f"{v:.6f}" for v in values)} | {s["median"]:.6f} | {s["min"]:.6f}–{s["max"]:.6f} |')
    a, b = [stats[version][s]['median'] for s in ('1', '2')]
    stats[version]['delta_seconds'] = b - a
    stats[version]['delta_percent'] = (b - a) / a * 100
pre_values = [x['seconds'] for x in json.loads((R / 'pre-amend-performance.json').read_text()) if not x['warmup']]
stats['pre-amend-56'] = {'revision': PRE, 'values': pre_values, 'median': statistics.median(pre_values), 'min': min(pre_values), 'max': max(pre_values)}
save('performance-summary.json', stats)

for version in ('56', '57', '58'):
    assert audit[version + '-boundaries'] == 18
    assert not audit[version + '-generated-plugin']['differences']
    assert not audit[version + '-generated-project']['differences']
    equivalence = json.loads((R / 'final-equivalence.json').read_text())
    assert not equivalence[version]['differences']
    regression = json.loads((R / f'{version}-regressions.json').read_text())
    assert regression['failure_preserved_archive'] and regression['before'] == regression['after']
    for e in assemblies[version]:
        if e['file'].startswith('UnrealSharp'): assert e['equal_api_il'], e

generated_rows = '\n'.join(f'| 5.{v[1]} | {audit[v+"-generated-plugin"]["A"]:,} | {audit[v+"-generated-project"]["A"]:,} | 完全一致 | {copies[v+"_2"]["counts"]["publish"]} |' for v in ('56','57','58'))
performance_rows = '\n'.join(rows)
delta_text = '；'.join(f'5.{v[1]}：{stats[v]["delta_seconds"]:+.3f} 秒（{stats[v]["delta_percent"]:+.2f}%）' for v in ('56','57','58'))
report = f'''# C# 自动打包：移除 resolver 提交后的验证

## 结论与交付

可以移除第二个提交。用户移除 UE 5.6 Engine 安装中的 UnrealSharp 后，原始插件 resolver 在实际 UAT 中正确选择项目插件；移除修复后的候选在 UE 5.6/5.7/5.8 均完成 Win64 x64 Development Game 打包和包内 C# 运行。此次 amendment 的必需验证已完成；不把下述未测平台或 UI 入口算作通过。

- 分支：`featre/csharp-package`，仓库 `C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp`。
- 原始基线保持 `{BASE}`；最终 HEAD `{HEAD}`。
- 本地保留 `backup/csharp-package-before-drop-resolver-20261025` 指向 `{PRE}`。
- 仅本地修改；没有推送、创建/修改 GitHub PR、合并 main/cp-main 或引入另一份 PR 的代码。

| 原提交 | 当前状态 | 责任与证据 |
| --- | --- | --- |
| 1723b7ba619b8e4ee778dde672cf9983e63cd6a9 | 原样保留 | 5.7/5.8 CustomStagingHandler；原先该提交的真实 A/B 与当前标准入口回归 |
| 89707cded9b453751d11525170354b0c03133025 | 已删除 | 插件优先级修复完全移出此次贡献；ProjectUtilities.cs 与原始基线一致 |
| 777b14237afbcfea740458d1643f433ef7769751 | 重放为 {HEAD} | 5.6 独立命令、共享 staging、UHT 按名解析；补丁等价，三版新候选验证 |

`range-diff` 只显示中间提交删除，其余两项补丁等价。最终贡献为 6 个插件 C#/csproj 文件，157 行新增、22 行删除；改写前后仅 ProjectUtilities.GetUnrealSharpUPlugin 不同。没有测试、文档、计划或项目文件进入源码提交。

## 保留的行为和边界

| 版本 | 实际验证入口 | 使用限制 |
| --- | --- | --- |
| 5.6 | `RunUAT -ScriptsForProject=<project.uproject> BuildCookRunUnrealSharp <打包参数>` | 默认 Editor 按钮和普通 BuildCookRun 不自动改用该命令 |
| 5.7/5.8 | `RunUAT -ScriptsForProject=<project.uproject> BuildCookRun <打包参数>` | 插件自动发现 staging handler |

发布、NonUFS 登记发生在平台封装和签名前。目标/配置/架构来自 native receipt，输出隔离于 Intermediate/UnrealSharp/Publish。保留发布插件与 receipt 不一致时明确失败的检查。

重复安装的优先级问题不由此分支修复。本轮三个 Installed Engine/Plugins 均没有 UnrealSharp.uplugin，六槽使用项目级插件。若再次存在多份同名插件，旧 resolver 仍可能选错；另一 PR 的修复未合入本分支，也没有把该场景标为已解决。

PreBuild 早于 UHT，PostBuild 依赖 native action 是否执行，不能覆盖 managed-only 或 skip-build staging，因此仍采用 staging。无项目 Automation 桥接或 Target/uproject 配置更改。源码引擎项目发现可能仍需 ScriptDir，本轮不扩大源码引擎矩阵。

## 来源与环境

源仓库 origin=Edenlia/UnrealSharp，upstream=UnrealSharp/UnrealSharp；本次只读 ls-remote 显示 upstream/main 仍为原始基线，没有移动 base。

| 版本 | 实际安装 | A 原始基线 / B 最终候选 |
| --- | --- | --- |
| 5.6 | 5.6.1 / CL 44394996 / UAT .NET 8.0.300 | SharpTestProject56_1 / SharpTestProject56_2 |
| 5.7 | 5.7.4 / CL 51494982 / UAT .NET 8.0.412 | SharpTestProject57_1 / SharpTestProject57_2 |
| 5.8 | 5.8.3 / CL 58210709 / UAT .NET 10 | SharpTestProject / SharpTestProject1 |

三个 A 均为原始 `{BASE}`，三个 B 均为 `{HEAD}`。A 各 826 个、B 各 829 个源文件与 fork Git archive 一致；无多余旧源码。保留原有构建输出与缓存，仅替换差异源码。六个项目的 AutoBuild、Game AutoBuild、project_env.config.txt 和 uproject 与本次开始时一致。

Windows 11、AMD Ryzen 9 9900X、约 64 GiB RAM；managed SDK 10.0.400。使用同机串行执行，每次开始前检查引擎和输出占用。没有修改 Engine、清理缓存、强制重建或绕过 AutoBuild。

额外的改写前后对照在 56_2 内执行：源为 `{PRE}`，环境同样已移除 Engine 插件，完成 Cook/Stage/Archive/运行和三轮 warm 采样。它与原始基线 A/B 分开记录。初始日志前缀 `56_2-final-*` 实际属于改写前；`pre-amend-source.json` 明确标注，最终候选一律用 `amended` 前缀。

## 新执行结果

| 检查 | 5.6 | 5.7 | 5.8 |
| --- | --- | --- | --- |
| A/B canonical Editor + Game | 4 次通过 | 4 次通过 | 4 次通过 |
| A 实际 resolver / Automation 加载路径 | 项目插件 | 项目插件 | 项目插件 |
| A 标准 staging / 手工发布 / 包内运行 | Stage 无 managed，手工发布后运行 | 同左 | 同左 |
| B Cook/Stage/Archive/包内运行 | 独立入口通过 | 标准入口通过 | 标准入口通过 |
| B receipt/命令边界 | 18/18 | 18/18 | 18/18 |
| 仅 C# 与内容更新 | v1→v2 | v1→v2 | v4→v5 |
| C# 错误阻止发布、保留旧归档 | 通过 | 通过 | 通过 |
| skipstage + skippak 保留旧包 | 运行 v2 | 运行 v2 | 运行 v5 |
| 源码恢复后正常 staging | 运行 v1 | 运行 v1 | 运行 v4 |
| native / Editor 输出不受 staging 改写 | 四组哈希一致 | 四组哈希一致 | 四组哈希一致 |

包内 probe 核对 Game/Development、正确版本及内容、Archive/Windows 下的用户程序集路径；5.6/5.7 另由 JSON 核对 runtime 与实际进程路径，5.8 的运行日志记录包内 hostfxr/程序集路径。每轮性能样本也运行归档游戏验证有效工作，但游戏启动耗时不计入 staging 指标。

18 项边界包含空 context、Editor、插件未参与、多目标/配置、Client/Server receipt 激活、Universal Mac、Android/iOS、错误项目、DLC、缺失命令及新增插件身份不匹配拒绝。这些是 synthetic receipt/命令检查，不是这些平台或 target 的完整打包。foreign/foreigncode 的既有拒绝证据仍适用，因为重放补丁及其先于 resolver 的控制流未变。

三个 Failure 日志是预期注入语法错误，均非零退出且旧归档哈希保持不变。当前夹具均已恢复。本轮没有非预期的生产构建或打包失败；准备脚本和诊断工具的修正保留在工作日志中。

## 真实产物与既有证据适用性

| 版本 | 插件 UHT 文本数 | 项目 UHT 文本数 | A/B 结果 | B managed/runtime 文件数 |
| --- | ---: | ---: | --- | ---: |
{generated_rows}

共 94,905 个真实 UHT 文本文件逐项匹配，各槽改写前后的清单也相同。native AutoBuild 本轮判断 up-to-date，自动重编译受影响的 UBT C# 插件；未强制重新导出 UHT。沿用之前真实 UHT 执行证据的依据是 native/generator 源码、输入与引擎不变，加上当前完整生成文件哈希相同。新候选的官方 managed build、发布、staging 与包内运行均实际重新执行；不把缓存文本核对称为本轮重新生成。

候选每个发布文件在 Publish/Stage/Archive 的路径、大小、SHA256 一致。性能采样后再次核对复制链，三版均一致。最终 B56/B57/B58 共 665 个文件全部与改写前的对应候选逐字节相同（220/220/225），详见 final-equivalence.json。初次产物审计期间，B58 曾出现 UnrealSharp.Log.dll/PDB 的 RID/非 RID 差异，之后 warm 重发布重新选择 RID 产物。A58 的 UnrealSharp.dll/PDB 同样在 warm 发布时从非 RID 产物切换；这是未修改源码时也可发生的既有依赖构建缓存选择，未将这些过渡快照混为最终输出。

5.8 过渡态 Log 的 API/IL 记录、9 个完整方法体及字段初始数据相同；CodeView 路径和 PDB 明确对应 obj/Release/net10.0/win-x64 与无 RID 目录；编译选项的平台字段分别是 X64 与 AnyCPU，不能将其称为仅文件路径变化。当前 x64 托管宿主均可运行这两种 IL 程序集。PDB 的 4 个文档仅路径不同，源内容 hash、序列点、作用域、局部变量相同，解码后的 import 语义相同。保留原始 DLL/PDB、MVID、完整 metadata 和差异记录，不宣称二进制字节一致。

5.6 原始 A 相对旧手工对照仅 UnrealSharp.pdb 不同，DLL 字节相同。两个 PDB 精确匹配当前 RID/非 RID 构建目录的文件哈希；522 个文档仅 RID 路径变化，序列点等一致，82 个 import blob 的编码差异解码后语义一致。编译选项中的 X64/AnyCPU 差异也完整保存。这属于既有 publish 构建产物来源变化，不归因于 staging 或 resolver 的运行时行为修改。

最终 A/B 重新检查公共 UnrealSharp DLL 的 API/IL，其记录均一致。探针仅有 file-local registrar 名差异。5.6/5.7 RuntimeGlue 源码一致，程序集差异是各自项目名；5.8 A 还预先存在 AssetIds.Map._Game_Maps_SharpTestMap 常量，B 没有，因此其 RuntimeGlue API/IL 不完全相同。完整源码、程序集 diff 已保留。两者本次均运行 /Engine/Maps/Entry 和相同探针，不使用该常量；两槽 RuntimeGlue DLL 从首轮到最终没有变化，B 也与改写前字节一致。没有将既有项目内容差异归因于本次 amendment，性能也不宣称完全相同项目的通用基准。

## 性能

每版 A/B 各一次预热，再按 A-B、B-A、A-B 取三轮独立 UAT 进程。A 指标为 Stage+手工 PackageProject 总 wall time，B 为自动 Stage wall time，单位秒。排除 native build、cook、包内运行；没有清缓存或制造 cold 条件。下表不是冷启动、完整项目构建或游戏帧率基准。

| 版本 | 流程 | 三次原始值（秒） | 中位数 | 范围 |
| --- | --- | --- | ---: | --- |
{performance_rows}

中位差（B-A）：{delta_text}。小夹具、三轮样本仅描述本机观测，不推断通用提速。5.8 staging 包含引擎 Zen/IoStore 准备和封装工作，阶段日志完整保留，不把所有 wall time 归因于 resolver。

同一 56_2 改写前后：pre-amend 三次为 {' / '.join(f'{v:.6f}' for v in pre_values)} 秒，中位 {statistics.median(pre_values):.6f}；amended 中位 {stats['56']['2']['median']:.6f}。采样分为先后两组，存在缓存/时间漂移；没有证据表明删除 resolver 提交导致可见性能退化，也不据此宣称优化。

## 证据索引与复现

本轮证据统一在 [amend-drop-resolver-20261025](amend-drop-resolver-20261025/)：

- `identity.json`、`environment.json`、`deployment.json`、`final-source-verification.json`：base/head、引擎、快照及最终源码身份。
- `range-diff.diff`、`amend.diff`、`final.diff`：两提交历史与完整变化。
- `*-amended-*`，其 command/result/log 保存原样命令、工作目录、时间、退出码和日志；`*-Identity-identity.json` 记录实际 Automation DLL 和插件描述符。
- `*-regressions.json`、`*-regression-*`：增量、失败、skipstage、恢复、四组输出哈希。
- `artifact-audit.json`、`*-generated-*.json/.diff`、`*-final-managed.json`、`final-copy-chain.json`：完整生成与复制链；`*-final-api-il/` 和 `58-pre-amend-api-il/`：程序集记录和完整文本差异。
- `symbols/`、`symbol-summary.json`、`58-log-pe-metadata/`：原始 PDB、所有已变 metadata 记录及解码依据。
- `*-performance.json`、`performance-summary.json`：预热与全部原始样本；`publisher-call-counts.json` 核对 {len(calls)} 个 staging/skipstage 调用次数。
- `harness/`、`fixtures/`：本轮工具与夹具快照；AutoBuild 和配置原文也保存在 environment.json。

复现时先读对应项目的 AutoBuild/AutoBuildGame 与配置，检查进程占用并顺序成功执行，再用保存的 CookStage/Stage/Manual command.json。UAT 参数沿用既有测试命令：Win64 Development、skipbuild、nocompileeditor、stage、pak、archive；完整路径与参数不能靠本文缩写重建 native 编译命令。

## 限制与最终状态

没有实际点击 Editor 打包按钮；5.7/5.8 UI 调用链与本轮 UAT 相同项目脚本上下文，UE 5.6 默认按钮仍不自动接入。没有测试 Mac 签名、移动端、NativeAOT、Universal Mac、DLC、完整 Client/Server/Shipping 或源码引擎组合。

最终六槽保持 A=原始基线、B=最终候选，probe 5.6/5.7 为 v1，5.8 为 v4；未自动恢复其他插件内容。仅在指定测试项目和外部报告目录保存夹具与证据。源码仓库保持两提交历史，工作树干净；所有改变本地且未推送。

[当前计划](plan.md) · [时间顺序工作日志](work-log.md) · [本地英文 PR 草稿](pr.md)
'''
(ROOT / 'validation.md').write_text(report, encoding='utf-8')

draft = f'''# Publish managed assemblies during Unreal staging, including UE 5.6

UnrealSharp packaging currently requires a separate managed publish after Unreal packaging. This change publishes assemblies and the self-contained runtime while UAT constructs the staging manifest, allowing platform packaging and signing to include them through the normal pipeline. A publish failure stops staging before archiving.

The plugin reuses PackageProject, takes the target/configuration/architecture from the native receipt, publishes into an isolated Intermediate directory, and registers the full output as NonUFS. No project Target, descriptor, packaging configuration, or Automation bridge changes are required. The selected publishing plugin must match the native receipt.

## Engine entry points

- UE 5.7/5.8: CustomStagingHandler participates in standard BuildCookRun with `-ScriptsForProject=<project.uproject>`.
- UE 5.6: BuildCookRunUnrealSharp derives from BuildCookRun and attaches the same staging implementation through ModifyDeploymentContextCallback. Ordinary UE 5.6 Editor packaging does not automatically opt in. Foreign sample creation is rejected; this command is excluded when CustomStagingHandler exists.
- Source-engine project-plugin discovery may require `-ScriptDir=<plugin>/Build/Scripts`; that environment is outside the executed matrix.

PreBuild/PostBuild cannot reliably cover managed-only changes when native actions are skipped. Command dispatch resolves only the requested managed command type so that UHT does not eagerly load the legacy command's UAT-only dependency. Plugin discovery precedence is unchanged; handling duplicate engine/project installations is outside this contribution.

## Validation

Fresh original-base versus final-candidate checks passed on Installed UE 5.6.1, 5.7.4 and 5.8.3, Win64 x64 Development Game. Both slots on each engine used project-level UnrealSharp without a duplicate engine installation. Canonical Editor/Game builds, Cook/Stage/Archive, and packaged managed-runtime probes passed. Baseline standard staging omitted managed files and required manual publishing; the candidate staged them automatically.

- Candidate publish/stage/archive hashes matched for all 220/220/225 managed and runtime files.
- Managed-only edits, expected compiler failure without archive replacement, skipped staging and recovery passed on all three engines; native and Editor managed output hashes remained unchanged.
- Eighteen receipt/command boundaries passed per engine, including publishing/native-plugin identity mismatch rejection.
- 94,905 existing generated UHT text artifacts matched across A/B and remained unchanged. Exporter/native inputs were unchanged; canonical builds reused native outputs. Managed publishing and runtime verification were executed afresh.
- Runtime assembly API/IL records matched. Transient RID/non-RID platform/debug-artifact differences and pre-existing fixture differences are retained and explained in the local report. All 665 final candidate published files matched the pre-amend output byte for byte.

Each engine had one warmup and three independent samples per side, comparing Stage plus manual publish with automatic Stage. Median UAT wall times were {stats['56']['1']['median']:.3f}/{stats['56']['2']['median']:.3f} s on 5.6, {stats['57']['1']['median']:.3f}/{stats['57']['2']['median']:.3f} s on 5.7, and {stats['58']['1']['median']:.3f}/{stats['58']['2']['median']:.3f} s on 5.8. Native build, cook and runtime verification are excluded. These small fixture measurements are not a general performance guarantee.

## Limits

The path is restricted to Win64 and Mac JIT; Mac packaging/signing was not executed on this Windows host. Editor button clicks, full Client/Server/Shipping packaging and source-engine combinations were not executed. Mixed targets/configurations, universal architectures and DLC are rejected; mobile and NativeAOT support are not added.

Addresses [#786](https://github.com/UnrealSharp/UnrealSharp/issues/786) and the [documented packaging workflow](https://www.unrealsharp.com/getting-started-and-fundamentals/packaging). Related [#785](https://github.com/UnrealSharp/UnrealSharp/pull/785) remains separate; this change does not depend on its archive target-name detection.
'''
(ROOT / 'pr.md').write_text(draft, encoding='utf-8')
plan = (ROOT / 'plan.md').read_text(encoding='utf-8-sig')
plan = plan.replace('当前：历史改写完成，新行为验证进行中。', f'当前：历史改写、三版新候选功能/边界/增量/失败恢复、产物与三轮 warm A/B 验证全部完成。最终 HEAD {HEAD}，工作树干净，仅本地且未推送。')
(ROOT / 'plan.md').write_text(plan, encoding='utf-8')
with (ROOT / 'work-log.md').open('a', encoding='utf-8') as f:
    f.write(f'\n\n### 2026-10-25：amend 最终交付\n\n- 两提交最终 HEAD {HEAD}；base 保持 {BASE}。只移除 89707cde，旧 HEAD 由本地 backup 引用保留。\n- 三版原始基线/候选的新功能、54 项边界、增量/失败保护/skipstage/恢复均完成；94,905 个既有真实 UHT 文本一致，生成器输入未改变，没有强制重新导出。\n- 5.6/5.7 候选各 220 文件与改写前逐字节一致；5.8 Log DLL/PDB 的 RID 调试差异已做 API/IL、方法体与 Portable PDB 解析，完整数据保留。最终复制链一致，公共 runtime DLL 的新 A/B API/IL 检查一致。\n- 三版 warm 采样中位差：{delta_text}。全部样本与预热保留，未删异常较慢值。\n- 最终源码快照 A 各 826/B 各 829 文件匹配，构建入口与 uproject 未变，publisher 调用次数 {len(calls)} 项核对通过。\n- validation.md、pr.md 和当前计划已更新，旧版本保留在本轮 evidence 目录。未推送、未发布/更新远端 PR、未合并其他分支。\n')
print(json.dumps(stats, indent=2), flush=True)
