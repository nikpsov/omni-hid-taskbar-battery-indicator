---
name: csharp-commenting-style
description: Standardized English commenting and XML-documentation guidelines for C# codebase in OmniHID Taskbar Battery Indicator
---

# C# Commenting & Documentation Style Guide

This skill defines the uniform commenting and documentation standards for the OmniHID Taskbar Battery Indicator project. All code written or updated in this repository MUST adhere to these rules.

## Core Principles

1. **English Only**: All comments, documentation strings, parameter descriptions, remarks, and commit notes MUST be written in clear English.
2. **Explain the "Why", Not Just the "What"**: Comments should explain intent, Win32 Shell quirks, DWM composition behavior, DPI conversion mathematics, taskbar docking lifecycle, and hardware telemetry handling rather than merely paraphrasing the code.
3. **Professional XML Documentation**: Every public and internal type, method, constructor, property, event, and enum member must have XML doc comments.

---

## 1. XML Documentation (`///`)

### Class and Interface Documentation
Include `<summary>` and optionally `<remarks>` for UI architecture, Win32 interop, or lifecycle details:

```csharp
/// <summary>
/// Frameless overlay window docked dynamically beside the Windows system tray.
/// </summary>
/// <remarks>
/// Tracks the <c>TrayNotifyWnd</c> child window inside <c>Shell_TrayWnd</c> via Win32 P/Invoke,
/// dynamically sizing and anchoring to display live telemetry beside system icons.
/// </remarks>
public class OverlayWindow : Window
```

### Methods and Constructors
Include `<summary>`, `<param>`, `<returns>`, and `<exception>` where appropriate:

```csharp
/// <summary>
/// Re-evaluates and repositions the overlay window relative to the Windows taskbar tray area.
/// </summary>
/// <remarks>
/// Performs physical-to-logical DIP conversions and suppresses window activation to avoid stealing focus.
/// </remarks>
public void UpdatePosition()
```

### Properties and Enum Members
Every property and enum value must have a concise, accurate description:

```csharp
/// <summary>
/// Gets whether the current Windows system theme is set to dark mode.
/// </summary>
public bool IsDarkTheme { get; }
```

---

## 2. Visual Section Dividers

For classes with multiple logical phases (e.g., Win32 Interop, UI Construction, Event Handlers, Lifecycle), use standardized 75-character divider lines:

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// Win32 Interop & Shell APIs
// ═══════════════════════════════════════════════════════════════════════════
```

---

## 3. Inline Technical Comments

When interacting with Win32 APIs, DWM, or layout math:
- Document P/Invoke flags and structures:
  ```csharp
  // SWP_NOACTIVATE (0x0010) prevents stealing focus from foreground gaming windows
  ```
- Document DPI transformations and coordinates:
  ```csharp
  // Convert physical monitor pixel coordinates to WPF device-independent pixels (DIPs)
  double workBottom = mi.rcWork.Bottom / dpiY;
  ```
- Explain layout offsets:
  ```csharp
  // 14 DIP gap matches Fluent Flyout spacing (8 DIP work area margin + 6 DIP card margin)
  ```

---

## 4. Anti-Patterns to Avoid

- ❌ Avoid redundant comments that repeat identifier names: `// gets or sets the id`
- ❌ Do NOT use `#region` / `#endregion` blocks (reduces code scannability)
- ❌ Do NOT leave empty XML doc tags (`<param name="foo"></param>`)
- ❌ Do NOT mix languages; avoid non-English text in code comments
