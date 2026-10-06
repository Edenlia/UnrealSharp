# Balloon migration patch archive

This branch preserves the complete historical project-plugin implementation, including the later partial/hot-reload correction. It is an audit archive, not an approved replacement for Engine/Plugins/UnrealSharp. No changes were merged into main.

- Task-start plugin: 2b51b3b076b5cbe327add60d1f93ad7d48cfe12c
- Preserved plugin tip: f4d12067c450bd75a0380c61d65e677ce66a477b
- User-audited standalone main (unchanged): d7edf9dc624ebe7ae5228a4fc64c7b7e4c35f917
- Engine cp-main comparison: 49a7bda8e0ffbfef70940e30fb807997d119803c

The HTML contains the original R1–R9 evidence, full patches and reproduction proposals. original-patch-stack.patch preserves the complete historical delta. archive.json compares each touched source blob with the accepted engine plugin; differing blobs do not by themselves establish an unlanded bug, because an independently implemented fix may differ.

Accepted Engine commits include hostfxr character conversion aa5ab6f9ed7f, manager singleton export 08447b14572c and native struct emitted-name deduplication 49a7bda8e0ff. The project migration must avoid unaccepted split-reflection, parameter and Blueprint-workaround dependencies where supported project code is possible. Temporary Engine changes require a concrete failure, alternative analysis and a separately recorded branch commit.

The migration does not depend on this checkout. Do not merge this whole branch into main: it also preserves historical project-only Bind_BalloonRuntime, measurement instrumentation and workaround metadata for review.
