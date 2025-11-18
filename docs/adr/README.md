# Architecture Decision Records (ADR)

This directory contains Architecture Decision Records (ADRs) for the LiMount project.

## What is an ADR?

An Architecture Decision Record (ADR) is a document that captures an important architectural decision made along with its context and consequences.

**Why write ADRs?**
- **Historical Context**: Understand why a decision was made months/years later
- **Onboarding**: New team members can quickly understand architectural choices
- **Avoid Revisiting**: Don't waste time re-discussing decisions that have already been made
- **Learning**: Document what worked and what didn't

## When to Write an ADR

Create an ADR when making decisions that:
- Affect the structure or behavior of the application significantly
- Are difficult or expensive to reverse
- Require agreement from multiple stakeholders
- Have multiple viable options with trade-offs

**Examples**:
- ✅ "Use Result Objects instead of Exceptions for expected failures"
- ✅ "Use Orchestrator Pattern for multi-step workflows"
- ✅ "Use Configuration-First approach for all tunable values"
- ❌ "Use PascalCase for class names" (coding standard, not architecture)
- ❌ "Use blue color for primary buttons" (UI detail, not architecture)

## How to Write an ADR

1. **Copy the template**: `000-template.md`
2. **Number it sequentially**: ADR-001, ADR-002, etc.
3. **Give it a descriptive title**: "Use Result Objects Instead of Exceptions"
4. **Fill in all sections**:
   - Context: What problem are we solving?
   - Decision Drivers: What factors influenced the decision?
   - Considered Options: What alternatives did we evaluate?
   - Decision Outcome: What did we choose and why?
   - Consequences: What are the positive and negative effects?
5. **Be honest**: Document trade-offs and risks
6. **Be specific**: Reference actual code/files
7. **Commit it**: ADRs are immutable once accepted

## ADR Lifecycle

### Status Values

- **Proposed**: Decision is under discussion
- **Accepted**: Decision has been made and implemented
- **Deprecated**: Decision is no longer recommended (but not superseded)
- **Superseded by ADR-XXX**: Decision has been replaced by a newer decision

### Changing Decisions

**Don't edit old ADRs** (except to mark them as superseded). Instead:
1. Create a new ADR explaining the new decision
2. Reference the old ADR and explain why it's being superseded
3. Update the old ADR's status to "Superseded by ADR-XXX"

This preserves the historical context of why the original decision was made.

## ADR Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [000](000-template.md) | Template for ADRs | - | - |
| [001](001-result-objects-over-exceptions.md) | Use Result Objects Instead of Exceptions | Accepted | 2025-11-18 |
| [002](002-orchestrator-pattern-for-workflows.md) | Use Orchestrator Pattern for Multi-Step Workflows | Accepted | 2025-11-18 |
| [003](003-configuration-first-approach.md) | Configuration-First Approach for All Tunable Values | Accepted | 2025-11-18 |

## Tips for Writing Good ADRs

### Do's

✅ **Be concise**: 1-2 pages is ideal, 3-4 pages maximum
✅ **Be specific**: Reference actual code, files, classes
✅ **Show trade-offs**: No decision is perfect, document the cons
✅ **List alternatives**: Show what you considered and rejected
✅ **Include validation**: How do we know this decision is working?
✅ **Link to code**: Point to implementations
✅ **Update index**: Keep README.md table up to date

### Don'ts

❌ **Don't be vague**: "Use a better pattern" is not an ADR
❌ **Don't skip alternatives**: Show you considered other options
❌ **Don't hide downsides**: Every decision has trade-offs
❌ **Don't make it a tutorial**: ADR documents decisions, not how to implement them
❌ **Don't edit accepted ADRs**: Create a new ADR to supersede it

## Example ADR Structure

Here's a condensed example:

```markdown
# ADR-004: Use State Service for Mount Persistence

**Status**: Accepted
**Date**: 2025-11-18

## Context and Problem Statement

Users want to know what's mounted after restarting the application.
Storing state in ViewModel loses it when app closes.

## Decision Drivers

- State must survive application restart
- Need to detect orphaned mounts (app crashed, mount still active)
- Must support concurrent access (multiple app instances)

## Considered Options

1. Store in ViewModel (lost on restart) ❌
2. Store in Registry (Windows-specific, complex permissions) ❌
3. Store in JSON file (simple, portable, human-readable) ✅

## Decision Outcome

**Chosen**: JSON file in %LocalAppData%\LiMount\mount-state.json

**Rationale**: Simple, testable, human-readable, portable.

**Consequences**:
- Positive: State persists across restarts
- Negative: No built-in locking (multiple instances could conflict)
- Mitigation: Document "don't run multiple instances"
```

## Related Documentation

- [CLAUDE.md](../../CLAUDE.md): Architectural patterns and principles
- [AGENTS.md](../../AGENTS.md): Guidance for AI assistants
- [TODO.md](../../TODO.md): Planned work and milestones

## Further Reading

- [ADR GitHub Organization](https://adr.github.io/)
- [Documenting Architecture Decisions by Michael Nygard](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- [ADR Tools](https://github.com/npryce/adr-tools)

---

**Maintainer**: Development Team
**Last Updated**: 2025-11-18
