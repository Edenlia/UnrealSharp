# C# packaging review handoff

此目录仅用于 `test/csharp-package-test` 跨电脑审查，不属于正式功能提交。

## 审查顺序

1. [当前验证结论](validation.md)：UE 5.6/5.7/5.8 的结果、限制和已移除的 resolver 提交。
2. [复现步骤](REPRODUCE.md)：迁移工具、构建入口、打包及回归检查。
3. [计划](plan.md)、[工作日志](work-log.md)、[英文 PR 草稿](pr.md)。
4. [设计分析](design.md)、[历史版本兼容性分析](compatibility-ue56-ue57.md)。历史分析中的中间方案以当前 validation.md 为准。

功能基线为 `cb8577e3a47b5bdc5454239a17f61a6282a50ae2`，验证过的功能 HEAD 为 `fddb33aefd598fa0d1eb5e324dc5ae36fb788a47`。两个实现提交是 `1723b7ba` 和 `fddb33ae`；本 test 分支在其后附加审查资料。

```sh
git fetch origin test/csharp-package-test
git switch --track origin/test/csharp-package-test
git diff cb8577e3 fddb33ae -- Build
git show 1723b7ba
git show fddb33ae
```

若本地已存在同名分支，直接切换该分支并检查其跟踪状态，不重复创建。

## 结果摘要

- 三版 Win64 x64 Development 完整打包、包内 C# 执行、managed-only 更新、错误发布失败、skipstage 与恢复均通过。
- 三版各 18 项边界检查通过；最终 665 个 managed/runtime 文件与删除中间提交前的候选一致。
- 5.6 使用 `BuildCookRunUnrealSharp`。默认 Editor 打包按钮没有自动接入。
- 5.7/5.8 使用标准 `BuildCookRun`，传入项目脚本上下文；本次验证实际运行 UAT，没有点击 Editor 按钮。
- 重复插件安装的优先级修复已移除，另一个 PR 未合入。测试引擎内没有重复安装的 UnrealSharp。
- Mac 签名、移动端、NativeAOT 等未实测；不扩大支持声明。

## 工具目录

- `tools/amend-harness/`：最终 amendment 使用的脚本快照、三版身份/边界检查、程序集及 PDB 分析工具。
- `tools/fixtures/`：六个槽的 managed probe 源码、项目文件与内容文本。
- `tools/multi-version/`：5.6/5.7/5.8 历史验证、部署和报告生成工具。
- `tools/initial-58/`：最初 5.8 验证工具。
- `tools/ManagedAssemblyCompare/`：完整方法体与字段数据对照工具。

这些是原执行脚本，保留历史路径、提交号与运行目录；不是可直接在任意机器启动的一键测试套件。审查时不必执行。复测前按 REPRODUCE.md 迁移，尤其不要直接运行历史 deploy/prepare 或报告生成脚本。

按照用户 2026-10-27 的要求，本分支不携带原始日志、JSON 证据、哈希清单、二进制、压缩包或完整测试项目/Engine。文档保留历史证据名称与本机路径作为出处，相关文件仅存原电脑，不是缺失的 Git 下载内容。文档中“仅本地、未推送”描述的是原功能验证阶段；此次授权只发布 test 分支的代码、工具和说明，不发布 GitHub PR，也不改写功能分支。
