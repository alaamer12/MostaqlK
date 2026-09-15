# Template: Universal Component Decoupling & Platform Suffix Split

Use this template structure when splitting a tightly-coupled component containing platform divergences into isolated contracts and platform-specific implementations.

---

### Step 1: Define the Neutral Contract (`<ComponentName>.contract`)

#### TypeScript / React Native (`<ComponentName>.types.ts`)
```typescript
import React from 'react';

export interface <ComponentName>Props {
  readonly id: string;
  readonly title: string;
  readonly isEnabled?: boolean;
  readonly onAction?: () => void;
  readonly children?: React.ReactNode;
}
```

#### Go (`<component_name>.go`)
```go
package <package_name>

import "context"

type <ComponentName>Service interface {
    ExecuteAction(ctx context.Context, id string) error
    GetStatus(ctx context.Context) (string, error)
}
```

#### C# / .NET (`<ComponentName>.cs`)
```csharp
namespace <AppNamespace>.<Module>;

public partial class <ComponentName>
{
    public string Title { get; set; } = string.Empty;

    // Partial extension points implemented in OS-specific files
    partial void InitializePlatformBehavior();
    partial void CleanupPlatformBehavior();

    public <ComponentName>()
    {
        InitializePlatformBehavior();
    }
}
```

---

### Step 2: Implement the Desktop / Host Platform (`<ComponentName>.desktop`)

#### TypeScript (`<ComponentName>.windows.tsx` or `<ComponentName>.desktop.tsx`)
```tsx
import React from 'react';
import { View, Text, Pressable } from 'react-native';
import { <ComponentName>Props } from './<ComponentName>.types';

export const <ComponentName>: React.FC<<ComponentName>Props> = ({ title, onAction, children }) => {
  return (
    <Pressable
      onPress={onAction}
      style={({ hovered }) => ({
        cursor: 'pointer',
        borderWidth: 1,
        borderColor: hovered ? '#2386C8' : '#475569',
        padding: 16
      })}
    >
      <Text style={{ fontWeight: '700' }}>{title}</Text>
      {children}
    </Pressable>
  );
};
```

#### Go (`<component_name>_windows.go`)
```go
//go:build windows

package <package_name>

import "context"

type winImplementation struct{}

func New<ComponentName>Service() <ComponentName>Service {
    return &winImplementation{}
}

func (s *winImplementation) ExecuteAction(ctx context.Context, id string) error {
    // Native Win32 / WinRT execution
    return nil
}

func (s *winImplementation) GetStatus(ctx context.Context) (string, error) {
    return "running-windows", nil
}
```

#### C# (`<ComponentName>.Windows.cs`)
```csharp
#if WINDOWS
namespace <AppNamespace>.<Module>;

public partial class <ComponentName>
{
    partial void InitializePlatformBehavior()
    {
        // Wire WinUI 3 PointerEntered, PointerExited, and XamlRoot handlers
    }

    partial void CleanupPlatformBehavior()
    {
        // Detach native WinUI event handlers
    }
}
#endif
```

---

### Step 3: Implement Shared Mobile Family (`_<ComponentName>.mobile` / `<ComponentName>.native`)

> **Note on Environment Naming Sensitivity**:
> In .NET MAUI / MSBuild, shared mobile family logic is conventionally prefixed/named `_<ComponentName>.Mobile.cs`. In React Native / Expo, the Metro bundler does not parse `.mobile.tsx`; instead, it expects `<ComponentName>.native.tsx` for shared native platforms (iOS + Android). Always tailor the file naming to the underlying framework's resolution mechanism.

#### React Native / Expo (`<ComponentName>.native.tsx`)
```tsx
import React from 'react';
import { Pressable, Text } from 'react-native';
import * as Haptics from 'expo-haptics';
import { <ComponentName>Props } from './<ComponentName>.types';

export const <ComponentName>: React.FC<<ComponentName>Props> = ({ title, onAction, children }) => {
  const handlePress = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light).catch(() => {});
    onAction?.();
  };

  return (
    <Pressable
      onPress={handlePress}
      style={({ pressed }) => ({
        transform: [{ scale: pressed ? 0.97 : 1.0 }],
        padding: 12
      })}
    >
      <Text style={{ fontWeight: '600' }}>{title}</Text>
      {children}
    </Pressable>
  );
};
```

#### C# (`_<ComponentName>.Mobile.cs`)
```csharp
#if ANDROID || IOS || MACCATALYST
namespace <AppNamespace>.<Module>;

public partial class <ComponentName>
{
    private void TriggerMobileTouchHaptic()
    {
        try
        {
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(
                Microsoft.Maui.Devices.HapticFeedbackType.Click);
        }
        catch
        {
            // Device lacks vibration actuator
        }
    }
}
#endif
```
