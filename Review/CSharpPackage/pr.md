# Publish managed assemblies during Unreal staging, including UE 5.6

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

Each engine had one warmup and three independent samples per side, comparing Stage plus manual publish with automatic Stage. Median UAT wall times were 9.904/10.086 s on 5.6, 9.847/10.140 s on 5.7, and 42.160/42.523 s on 5.8. Native build, cook and runtime verification are excluded. These small fixture measurements are not a general performance guarantee.

## Limits

The path is restricted to Win64 and Mac JIT; Mac packaging/signing was not executed on this Windows host. Editor button clicks, full Client/Server/Shipping packaging and source-engine combinations were not executed. Mixed targets/configurations, universal architectures and DLC are rejected; mobile and NativeAOT support are not added.

Addresses [#786](https://github.com/UnrealSharp/UnrealSharp/issues/786) and the [documented packaging workflow](https://www.unrealsharp.com/getting-started-and-fundamentals/packaging). Related [#785](https://github.com/UnrealSharp/UnrealSharp/pull/785) remains separate; this change does not depend on its archive target-name detection.
