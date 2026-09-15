# Universal Walkthrough Examples: Polyglot Decoupling & Senior Refactoring

This guide provides concrete, real-world comparative walkthroughs demonstrating how to eliminate platform coupling, establish clean unit hierarchies, and swap UI layouts across diverse languages and frameworks.

---

## Pattern 1: Eliminating Inline Platform Directives (`#if PLATFORM` / Runtime OS Checks)

### Architectural Principle
Shared business logic, state containers, and presentation shells must never contain scattered compile-time preprocessor guards (`#if WINDOWS`, `#if ANDROID`, `#if DARWIN`) or sprawling runtime OS branching (`if (os == 'android')`, `Platform.OS === ...`, `DeviceInfo.Platform == ...`). Instead, isolate platform divergence behind a common contract with OS-specific compilation/bundling units.

#### Direct vs. Indirect Platform Checks
Coupling manifests in two forms across client architectures:
1. **Direct Platform Conditionals**:
   - Explicit preprocessor guards: `#if WINDOWS`, `#if ANDROID`, `#[cfg(target_os = "windows")]`, `//go:build windows`.
   - Explicit runtime checks: `Platform.OS === 'ios'`, `DeviceInfo.Platform == DevicePlatform.WinUI`, `runtime.GOOS == "darwin"`.
2. **Indirect / Leaked Platform Conditionals**:
   - Checking platform-tied hardware or runtime features to infer the OS: e.g., `if (window.chrome?.webview)` (inferring Windows), `if (DeviceInfo.Idiom == DeviceIdiom.Desktop)` (inferring Desktop/Windows instead of layout capability), or querying native user-agent strings.
   - Guarding UI logic behind pointer/mouse availability: e.g. `if (e.PointerDeviceType == ...)` or inspecting hover capability in shared view models.
   - Asymmetric capability null-checks: e.g., `if (trayIcon != null)` scattered throughout application lifecycle.

**Core Invariant**: Both direct and indirect platform conditions in shared code must be eradicated. If a platform-specific API, quirk, or capability is required, it must be resolved via dedicated compilation units (`X.Windows.cs`, `X.native.tsx`, etc.) or encapsulated in an explicit `PlatformCapability<T>` container.

---

### 1. TypeScript / React Native (Metro Platform Extensions)

#### ❌ Anti-Pattern: Inline OS branching inside shared component
```tsx
// Card.tsx - Fragile runtime branching inside shared component
import React from 'react';
import { View, Platform, StyleSheet, Vibration } from 'react-native';

export const Card = ({ children }: { children: React.ReactNode }) => {
  const handlePress = () => {
    if (Platform.OS === 'ios') {
      // iOS specific haptic
      require('react-native-haptic-feedback').trigger('impactLight');
    } else if (Platform.OS === 'android') {
      Vibration.vibrate(10);
    } else if (Platform.OS === 'windows') {
      // Windows desktop specific pointer logic
      window.chrome?.webview?.postMessage('card-clicked');
    }
  };

  return (
    <View style={[styles.card, Platform.OS === 'windows' ? styles.desktopCard : styles.mobileCard]}>
      {children}
    </View>
  );
};
```

#### ✅ Polyglot Standard: Metro Platform Suffixes (`Card.types.ts`, `Card.ios.tsx`, `Card.android.tsx`, `Card.windows.tsx`)

> **Naming Conventions & Bundler Semantics across Ecosystems**:
> - **React Native / Expo / Metro**: Metro bundlers natively resolve platform extensions: `.ios.tsx`, `.android.tsx`, `.windows.tsx`, and `.native.tsx`. Note that Metro and Expo **do not recognize `.mobile.tsx`** by default — they parse `.native.tsx` for cross-mobile code (shared by iOS and Android). Therefore, in React Native/Expo, use `Card.native.tsx` as the mobile-family unit, while reserving `Card.ios.tsx` and `Card.android.tsx` for OS-specific variances.
> - **C# / .NET MAUI**: MSBuild and .NET MAUI conventions support the `_X.Mobile.cs` family pattern (with custom or multi-targeting compile includes) alongside `X.Windows.cs`, `X.Android.cs`, and `X.iOS.cs`.
> - **Language & Tooling Sensitivity**: Platform naming must strictly adhere to the host build tool's resolution engine (e.g., Metro platform extensions vs. Go build tags vs. Rust target_os attributes).
```typescript
// Card.types.ts - Neutral Contract
import React from 'react';

export interface CardProps {
  readonly title: string;
  readonly children: React.ReactNode;
  readonly onCardPress?: () => void;
}
```

```tsx
// Card.windows.tsx - Desktop Windows Implementation (Hover + WinUI Pointer)
import React from 'react';
import { View, Text, Pressable } from 'react-native';
import { CardProps } from './Card.types';

export const Card: React.FC<CardProps> = ({ title, children, onCardPress }) => (
  <Pressable 
    onPress={onCardPress} 
    style={({ hovered }) => ({ 
      borderColor: hovered ? '#2386C8' : '#334155',
      padding: 16,
      cursor: 'pointer'
    })}
  >
    <Text style={{ fontSize: 18, fontWeight: '700' }}>{title}</Text>
    {children}
  </Pressable>
);
```

```tsx
// Card.native.tsx - Shared Mobile Implementation for iOS & Android (Touch + Haptic)
import React from 'react';
import { Pressable, Text } from 'react-native';
import * as Haptics from 'expo-haptics';
import { CardProps } from './Card.types';

export const Card: React.FC<CardProps> = ({ title, children, onCardPress }) => {
  const handlePress = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light).catch(() => {});
    onCardPress?.();
  };

  return (
    <Pressable 
      onPress={handlePress}
      style={({ pressed }) => ({
        transform: [{ scale: pressed ? 0.97 : 1.0 }],
        padding: 12
      })}
    >
      <Text style={{ fontSize: 16, fontWeight: '600' }}>{title}</Text>
      {children}
    </Pressable>
  );
};
```

---

### 2. Go (Multi-Platform Build Tags)

#### ❌ Anti-Pattern: Leaking OS specifics into shared service
```go
// credential_store.go
package credentials

import "runtime"

func StoreToken(key, token string) error {
    if runtime.GOOS == "windows" {
        // Direct Windows DPAPI or Registry call
        return storeWindowsDPAPI(key, token)
    } else if runtime.GOOS == "darwin" {
        // Direct macOS Keychain exec call
        return storeKeychain(key, token)
    }
    return storePlaintext(key, token)
}
```

#### ✅ Polyglot Standard: Build Tag Split (`store.go`, `store_windows.go`, `store_darwin.go`, `store_linux.go`)
```go
// store.go - Neutral Contract
package credentials

import "context"

type SecureStore interface {
    StoreToken(ctx context.Context, key, secret string) error
    RetrieveToken(ctx context.Context, key string) (string, error)
}
```

```go
// store_windows.go
//go:build windows

package credentials

import "context"

type winDPAPIStore struct{}

func NewSecureStore() SecureStore {
    return &winDPAPIStore{}
}

func (s *winDPAPIStore) StoreToken(ctx context.Context, key, secret string) error {
    // Windows CryptProtectData native call
    return nil
}

func (s *winDPAPIStore) RetrieveToken(ctx context.Context, key string) (string, error) {
    return "", nil
}
```

```go
// store_darwin.go
//go:build darwin

package credentials

import "context"

type keychainStore struct{}

func NewSecureStore() SecureStore {
    return &keychainStore{}
}

func (s *keychainStore) StoreToken(ctx context.Context, key, secret string) error {
    // macOS Security framework binding
    return nil
}

func (s *keychainStore) RetrieveToken(ctx context.Context, key string) (string, error) {
    return "", nil
}
```

---

### 3. Rust (Conditional Compilation & Trait Implementations)

#### ❌ Anti-Pattern: Giant Match Statement with In-Body CFG
```rust
// In shared core
pub fn get_machine_identifier() -> String {
    #[cfg(target_os = "windows")]
    {
        // 50 lines of Windows Win32 Registry APIs
        "win-id".to_string()
    }
    #[cfg(target_os = "android")]
    {
        // 50 lines of Android JNI calls
        "android-id".to_string()
    }
}
```

#### ✅ Polyglot Standard: OS Modules behind Trait Isolation
```rust
// identifier/mod.rs
pub trait MachineIdentifierProvider: Send + Sync {
    fn get_id(&self) -> Result<String, IdentificationError>;
}

#[cfg(target_os = "windows")]
mod windows;
#[cfg(target_os = "windows")]
pub use windows::WindowsIdentifierProvider as DefaultProvider;

#[cfg(any(target_os = "android", target_os = "ios"))]
mod mobile;
#[cfg(any(target_os = "android", target_os = "ios"))]
pub use mobile::MobileIdentifierProvider as DefaultProvider;
```

---

### 4. C# / .NET MAUI (Partial-Class Family Suffix Pattern)

#### ❌ Anti-Pattern: In-body `#if WINDOWS` in shared ViewModel
```csharp
public class ProjectCardViewModel
{
    public void OpenProject()
    {
#if WINDOWS
        Process.Start(new ProcessStartInfo("cmd", $"/c start {Url}"));
#elif ANDROID
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(Url));
        Android.App.Application.Context.StartActivity(intent);
#endif
    }
}
```

#### ✅ Polyglot Standard: Partial Classes with Multi-Targeting
```csharp
// UrlLauncher.cs - Neutral contract
public static partial class UrlLauncher
{
    public static partial void Open(string url);
}

// UrlLauncher.Windows.cs
public static partial class UrlLauncher
{
    public static partial void Open(string url)
    {
        Windows.System.Launcher.LaunchUriAsync(new Uri(url)).AsTask();
    }
}

// _UrlLauncher.Mobile.cs (Shared by Android & iOS)
internal static partial class UrlLauncher
{
    private static void OpenMobileUri(string url)
    {
        Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Uri(url));
    }
}

// UrlLauncher.Android.cs
public static partial class UrlLauncher
{
    public static partial void Open(string url) => OpenMobileUri(url);
}
```

---

## Pattern 2: Asymmetric Capabilities (Desktop vs Mobile)

### Architectural Principle
When a feature natively exists only on one platform family (e.g. System Tray Icon on Desktop, Biometric FaceID prompt on Mobile), avoid scattered null-checks or runtime exceptions. Encapsulate the feature in an explicit optional capability container.

### 1. TypeScript / Web & React Native
```typescript
export interface Capability<T> {
  readonly isSupported: boolean;
  readonly value: T | null;
  execute(action: (instance: T) => void): void;
}

export const createCapability = <T>(instance: T | null): Capability<T> => ({
  isSupported: instance !== null,
  value: instance,
  execute: (action) => {
    if (instance !== null) action(instance);
  }
});
```

### 2. C# / .NET
```csharp
public sealed class PlatformCapability<T> where T : class
{
    public T? Instance { get; }
    public bool IsSupported => Instance != null;

    public PlatformCapability(T? instance) => Instance = instance;

    public static PlatformCapability<T> WindowsOnly(Func<T> factory) =>
        CurrentPlatform.IsWindows ? new PlatformCapability<T>(factory()) : new PlatformCapability<T>(null);

    public void Execute(Action<T> action)
    {
        if (Instance != null) action(Instance);
    }
}
```

---

## Pattern 3: View Barrel Layout Swapping

### Architectural Principle
Desktop and mobile layouts have fundamentally different layout hierarchies and density requirements. Swapping visibility via CSS/XAML `display: none` or `IsVisible="false"` still instantiates and retains inactive DOM/Virtual DOM trees. Use dynamic container shells that resolve distinct layout trees.

### 1. TypeScript / React Native & Web (View Barrel Grouper with `getPlatform`)

```tsx
// ProjectCard.tsx - Grouper / Host Shell
// Does NOT inspect window dimensions or perform platform checks itself; delegates to getPlatform()
import React from 'react';
import { ProjectCardDesktopLayout } from './layouts/ProjectCardDesktopLayout';
import { ProjectCardMobileLayout } from './layouts/ProjectCardMobileLayout';
import { Project } from './Project.types';
import { getPlatform, PlatformType } from '@/core/platform/getPlatform';

interface ProjectCardProps {
  readonly project: Project;
}

export const ProjectCard: React.FC<ProjectCardProps> = ({ project }) => {
  const platform = getPlatform();

  // Dynamic layout swap: renders strictly one layout tree based on platform resolution
  return platform === PlatformType.Desktop ? (
    <ProjectCardDesktopLayout project={project} />
  ) : (
    <ProjectCardMobileLayout project={project} />
  );
};
```

### 2. Flutter (Widget Builder Resolution with `getPlatform`)
```dart
class ProjectCard extends StatelessWidget {
  final Project project;
  const ProjectCard({super.key, required this.project});

  @override
  Widget build(BuildContext context) {
    // Dynamic Layout Barrel resolution via platform helper
    final platform = PlatformResolver.getPlatform();
    return platform == PlatformType.desktop
        ? ProjectCardDesktopLayout(project: project)
        : ProjectCardMobileLayout(project: project);
  }
}
```

### 3. .NET MAUI / XAML (View Barrel Shell via `PlatformSelect`)
```csharp
public partial class ProjectCard : ContentView
{
    public ProjectCard()
    {
        InitializeComponent();
        Content = PlatformSelect.For<Func<View>>(
            windows: () => new ProjectCardWindowsLayout(),
            android: () => new ProjectCardMobileLayout(),
            ios: () => new ProjectCardMobileLayout()
        )();
    }
}
```

---

## Pattern 4: 3-Tier Unit Specialization Hierarchy

### Architectural Principle
Avoid duplicating UI behaviors, debouncing timers, confirmation popups, and formatting logic. Decompose features into three distinct layers:
1. **Tier 1 (Core Primitive):** Framework-agnostic algorithm, timer, or resolver.
2. **Tier 2 (Base Unit):** Reusable component with configurable parameters.
3. **Tier 3 (Domain Specialization):** Application-specific unit with fixed text, icons, and commands.

### Comparative Example: Confirmation Dialog

| Tier | Concept | TypeScript / React | C# / .NET MAUI | Flutter |
|---|---|---|---|---|
| **Tier 1 (Primitive)** | Modal Host | `Portal` / `NativeModalBridge` | `ModalPresenter` | `showDialog` / `showModalBottomSheet` |
| **Tier 2 (Base Unit)** | Confirmation Box | `<ConfirmationModal title message onConfirm onCancel />` | `ConfirmationBox` | `ConfirmationDialog` |
| **Tier 3 (Specialization)**| Exit Confirmation | `<ExitConfirmationModal onExit rememberChoice />` | `ExitConfirmationBox` | `ExitConfirmationDialog` |
