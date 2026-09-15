# Template: Universal View Barrel Layout Swapping

Use this template when refactoring a composite block component, card, or page to prevent desktop layout DOM overhead and unneeded view trees on mobile devices.

---

### Step 1: Create the Container / Host Shell

#### TypeScript / React & React Native (`<Component>.tsx`)
```tsx
import React from 'react';
import { <Component>DesktopLayout } from './layouts/<Component>DesktopLayout';
import { <Component>MobileLayout } from './layouts/<Component>MobileLayout';
import { <Component>Props } from './<Component>.types';
import { getPlatform, PlatformType } from '@/core/platform/getPlatform';

/**
 * Grouper / Host Shell:
 * Does NOT inspect window dimensions or perform ad-hoc checks directly.
 * Instead, delegates platform resolution to canonical getPlatform() / platformSelect().
 */
export const <Component>: React.FC<<Component>Props> = (props) => {
  const platform = getPlatform();

  // Dynamic layout swap: renders strictly one layout tree based on platform
  return platform === PlatformType.Desktop ? (
    <<Component>DesktopLayout {...props} />
  ) : (
    <<Component>MobileLayout {...props} />
  );
};
```

> **Canonical Platform Selector (`getPlatform.ts` / `platformSelect.ts`)**:
> Never perform ad-hoc window measurement (`useWindowDimensions`) or direct OS sniffing inside the grouper component itself. The grouper component must delegate to a centralized platform utility:
> ```ts
> // getPlatform.ts
> export enum PlatformType {
>   Desktop = 'desktop',
>   Mobile = 'mobile',
> }
>
> export const getPlatform = (): PlatformType => {
>   // Centralized resolution (e.g. build target, user agent, or container capability)
>   return process.env.TARGET_PLATFORM === 'desktop' ? PlatformType.Desktop : PlatformType.Mobile;
> };
>
> export const platformSelect = <T>(options: { desktop: () => T; mobile: () => T }): T => {
>   return getPlatform() === PlatformType.Desktop ? options.desktop() : options.mobile();
> };
> ```

#### Flutter (`<component>.dart`)
```dart
import 'package:flutter/material.dart';
import 'layouts/<component>_desktop_layout.dart';
import 'layouts/<component>_mobile_layout.dart';

class <Component> extends StatelessWidget {
  final <Model> data;
  const <Component>({super.key, required this.data});

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth >= 720) {
          return <Component>DesktopLayout(data: data);
        }
        return <Component>MobileLayout(data: data);
      },
    );
  }
}
```

#### .NET MAUI / XAML (`<Component>.xaml` & `<Component>.xaml.cs`)
```xml
<!-- <Component>.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="<AppNamespace>.Views.<Component>">
    <!-- Clean host shell container -->
</ContentView>
```

```csharp
// <Component>.xaml.cs
namespace <AppNamespace>.Views;

public partial class <Component> : ContentView
{
    public <Component>()
    {
        InitializeComponent();

        // Resolves layout factory at compile/startup time
        Content = PlatformSelect.For<Func<View>>(
            windows: () => new Layouts.<Component>WindowsLayout(),
            android: () => new Layouts.<Component>MobileLayout(),
            ios: () => new Layouts.<Component>MobileLayout(),
            macCatalyst: () => new Layouts.<Component>WindowsLayout()
        )();
    }
}
```

---

### Step 2: Implement the Desktop Layout (`Layouts/<Component>DesktopLayout`)
- Optimized for horizontal screen real estate (4+ columns, fixed sidebar, data tables).
- Mouse-driven hover states, cursor pointer, right-click context menus.

### Step 3: Implement the Mobile Layout (`Layouts/<Component>MobileLayout`)
- Optimized for vertical thumb reachability (single-column flow, bottom action sheets).
- Touch-driven feedback (scale compression `0.97`, native haptics, horizontal swipe-to-reveal).
