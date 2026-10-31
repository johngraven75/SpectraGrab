# Repository Engineering Standard

Mandatory for all AI-assisted engineering. Before code define product goal, target user, measurable success criteria, non-goals, assumptions, and missing requirements. Propose architecture first including platform constraints, security, dependency direction, contracts, persistence, lifecycle, observability, backward compatibility, upgrades and rollback. Organize work/reporting as **Frontend**, **Connector / integration**, and **Backend** where applicable.

Break substantial work into independently testable vertical slices suitable for isolated git worktrees/branches. For every slice define exact files, public interfaces/contracts, error handling, logging/telemetry, security implications, and definition of done. Keep commits atomic and conventional; never merge around failing required checks.

Ship fresh, idiomatic, repository-specific production code following SOLID, DRY, explicit typing, immutable data where practical, dependency injection, narrow interfaces, secure defaults, validation, null safety, cancellation/disposal, bounded resources, and separation of concerns. No TODOs, placeholders, mock production data, fabricated integrations, credentials, or incomplete production paths. Preserve accepted functionality and backward compatibility unless migration is explicitly approved.

Every slice requires appropriate unit, real OS/filesystem integration, contract, security, performance-budget, and manual UI/installer QA validation. Run formatting, lint/static analysis, type checks, unit/integration/E2E, packaging, and user-flow validation. Self-review races, deadlocks, leaks, disposal, locks, retries, lifecycle cleanup, privileges, interrupted operations, rollback and compatibility; fix issues before completion.

Production readiness requires diff summary, changelog/release notes, migration/installer notes, API/config/environment docs, rollback plan, post-release monitoring, and test/checksum evidence. Commit every change before building; rerun after fixes; never claim completion/publication/signing/test success without evidence.

For Windows desktop components explicitly handle long paths, UAC/least privilege, registry ownership/cleanup, file locks, process lifecycle, MSBuild/toolchain behavior, UI threading, crash recovery, MSI/MSIX compatibility where supported, silent install, updater safety, and backward-compatible upgrades. Build with `build.cmd` when present and pass all existing tests before merge/release.
