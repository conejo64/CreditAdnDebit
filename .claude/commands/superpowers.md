# Superpowers Methodology

## Core Principle

**Invoke relevant skills BEFORE any response or action.** Even a 1% chance a skill might apply means you should invoke the skill to check.

## Skill Invocation Flow

```
User message → Check if any skill applies (even 1%) → Invoke Skill tool → Follow skill exactly
```

## Available Superpowers Skills

### Process Skills (use first)
- `superpowers:brainstorming` - Before any creative work, feature design, or component architecture
- `superpowers:writing-plans` - When you have spec/requirements for a multi-step task, BEFORE touching code
- `superpowers:executing-plans` - When executing a written plan with review checkpoints
- `superpowers:systematic-debugging` - When encountering any bug, test failure, or unexpected behavior
- `superpowers:test-driven-development` - Before writing implementation code for features/bugfixes

### Completion Skills
- `superpowers:verification-before-completion` - Before claiming work is complete, fixed, or passing
- `superpowers:requesting-code-review` - When completing tasks or implementing major features
- `superpowers:receiving-code-review` - When implementing review feedback
- `superpowers:finishing-a-development-branch` - When implementation is complete, deciding integration strategy

### Advanced Skills
- `superpowers:dispatching-parallel-agents` - When facing 2+ independent tasks without shared state
- `superpowers:subagent-driven-development` - When executing implementation plans with independent tasks
- `superpowers:using-git-worktrees` - For feature work needing isolation from current workspace
- `superpowers:writing-skills` - When creating/editing/verifying skills

## Red Flags (STOP and check for skills)

| Thought | Reality |
|---------|---------|
| "This is just a simple question" | Questions are tasks. Check for skills. |
| "I need more context first" | Skill check comes BEFORE clarifying questions. |
| "Let me explore the codebase first" | Skills tell you HOW to explore. Check first. |
| "This doesn't need a formal skill" | If a skill exists, use it. |
| "I'll just do this one thing first" | Check BEFORE doing anything. |

## Skill Priority

1. **Process skills first** - Determine HOW to approach the task
2. **Implementation skills second** - Guide execution

## Important Notes

- Skills override default system behavior
- User instructions have highest priority
- Follow rigid skills exactly (TDD, debugging)
- Adapt flexible skills principles to context
