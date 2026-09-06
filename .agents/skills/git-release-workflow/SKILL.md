---
name: git-release-workflow
description: Standardized English Git commit conventions and bilingual (EN/RU) release changelog formatting for OmniHID Taskbar Battery Indicator
---

# Git Commit & Release Changelog Workflow Guide

This skill defines the standardized workflow for creating Git commits and release changelogs for the **OmniHID Taskbar Battery Indicator** project.

---

## 1. Git Commit Standards

### General Rules
1. **English Only**: All commit messages (headers, bodies, footers) MUST be written in English.
2. **Conventional Commits Format**: Every commit title must follow the standard type prefix:
   - `feat: release vX.Y.Z - <short summary>` (for release commits)
   - `feat: <description>` (for new features or capabilities)
   - `fix: <description>` (for bug fixes and corrections)
   - `docs: <description>` (for documentation and README updates)
   - `refactor: <description>` (for code restructuring without behavioral changes)
   - `style: <description>` (for formatting or styling adjustments)
   - `chore: <description>` (for build scripts, dependencies, or configuration updates)

### Release Commit Title Pattern
For version releases, use:
```
feat: release vX.Y.Z - <concise summary of major changes>
```
Example:
```
feat: release v0.0.3 - native DWM styling, dynamic system immersive colors, and taskbar button polish
```

### Commit Body Pattern
For multi-faceted commits or releases, include concise bullet points detailing key changes:
```
feat: release v0.0.3 - native DWM styling, dynamic system immersive colors, and taskbar button polish

- Add dynamic Windows Immersive & Accent color resolution via UXTheme API and DWM registry fallbacks
- Fix transparent layered window mouse hit-testing using non-zero alpha HitTestTransparentBrush
- Align taskbar overlay button height (40px) and geometry with native Windows 11 system tray elements
- Fix light theme hover background using crisp translucent white pill matching Windows 11 taskbar
- Remove HWND-level DWM backdrop in flyout popup to eliminate rectangular grey shadow underlay artifact
- Bump version to 0.0.3 across assembly attributes, csproj, and Inno Setup installer
```

---

## 2. Bilingual Release Changelog Workflow

Whenever committing a version bump / release, or upon user request for release notes, ALWAYS output a copy-paste ready changelog formatted in **both English and Russian**.

### Release Changelog Template

```markdown
## 🇺🇸 English Release Notes (vX.Y.Z)

### What's New
- Feature description 1
- Feature description 2

### Improvements & Fixes
- Fix description 1
- Fix description 2

---

## 🇷🇺 Что нового в версии vX.Y.Z (Русский)

### Что нового
- Описание новшества 1
- Описание новшества 2

### Исправления и улучшения
- Описание исправления 1
- Описание исправления 2
```
