import re, sys

path = r"s:\VScodeProjects\Paddy-dev\Views\MainWindow.xaml.cs"

with open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

original = content

# 1. Replace System.Windows.Media.Animation usages with WinUI 3 equivalents
content = content.replace(
    "System.Windows.Media.Animation.DoubleAnimation",
    "Microsoft.UI.Xaml.Media.Animation.DoubleAnimation"
)
content = content.replace(
    "System.Windows.Media.Animation.CubicEase",
    "Microsoft.UI.Xaml.Media.Animation.CubicEase"
)
content = content.replace(
    "System.Windows.Media.Animation.EasingMode.EaseInOut",
    "Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut"
)
content = content.replace(
    "System.Windows.Media.Animation.EasingMode.",
    "Microsoft.UI.Xaml.Media.Animation.EasingMode."
)

# 2. Replace System.Windows.Media.Color.FromArgb with Windows.UI.Color.FromArgb
content = content.replace(
    "System.Windows.Media.Color.FromArgb(",
    "Windows.UI.Color.FromArgb("
)
content = content.replace(
    "(System.Windows.Media.Color)Microsoft.UI.ColorHelper.ConvertFromString(",
    "(Windows.UI.Color)Microsoft.UI.ColorHelper.ConvertFromString("
)

# 3. Replace System.Windows.MessageBox calls
#    The WPF overload that takes (Window owner, ...) needs the owner stripped
#    Replace MessageBoxButton.YesNo -> use Win32 MessageBox (MB_YESNO = 0x04)
#    Replace MessageBoxButton.OK -> MB_OK = 0x00
#    This is complex — stub them with ShowMessageBox for now and add TODO
# Simple approach: replace "System.Windows.MessageBox.Show(" with a call to ShowMessageBox or NativeMessageBox

# Pattern 1: MessageBox.Show(this, ..., MessageBoxButton.YesNo, ...) -> ShowMessageBox helper
content = re.sub(
    r'var result = System\.Windows\.MessageBox\.Show\(\s*this,\s*\$?"(.*?)",\s*"(.*?)",\s*MessageBoxButton\.YesNo.*?\);',
    lambda m: (
        '// TODO: Replace with ContentDialog for full WinUI 3 compat\n'
        '            var result = MessageBoxW(System.IntPtr.Zero,\n'
        '                $"' + m.group(1) + '",\n'
        '                "' + m.group(2) + '",\n'
        '                0x04 | 0x30) == 6 ? MessageBoxResult.Yes : MessageBoxResult.No; // MB_YESNO|MB_ICONWARNING'
    ),
    content,
    flags=re.DOTALL
)

# Simple: replace all remaining System.Windows.MessageBox.Show(this, ... with ShowMessageBox(...)
content = re.sub(
    r'System\.Windows\.MessageBox\.Show\(\s*this,\s*\$?"((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)",\s*MessageBoxButton\.\w+,\s*MessageBoxImage\.\w+\);',
    lambda m: 'ShowMessageBox($"' + m.group(1) + '", "' + m.group(2) + '");',
    content
)

# Simple: replace System.Windows.MessageBox.Show( with ShowMessageBox(
content = re.sub(
    r'System\.Windows\.MessageBox\.Show\(',
    'ShowMessageBox(',
    content
)

# 4. Remove MessageBoxButton.YesNo / MessageBoxButton.OK / MessageBoxImage references
#    Leave them as-is if they were already replaced above; remaining ones get stubbed

# 5. Replace Dispatcher.BeginInvoke(new Action(() => {...}), DispatcherPriority.Render)
#    -> DispatcherQueue.TryEnqueue(() => {...})
content = re.sub(
    r'Dispatcher\.BeginInvoke\(new Action\(',
    'DispatcherQueue.TryEnqueue(() =>\n            (',
    content
)
# Remove the DispatcherPriority trailing argument
content = re.sub(
    r'\}\s*\)\s*,\s*System\.Windows\.Threading\.DispatcherPriority\.\w+\)\s*;',
    '}));',
    content
)

# 6. Replace _overlayEngine method calls and events with no-ops / comments
content = re.sub(
    r'_overlayEngine\.DiagnosticEvent \+= OverlayEngine_DiagnosticEvent;.*\n',
    '// _overlayEngine.DiagnosticEvent stubbed for WinUI 3 migration\n',
    content
)
content = re.sub(
    r'_overlayEngine\.Initialize\(BuildOverlayOptions\(\)\);\s*\n',
    '// _overlayEngine.Initialize stubbed\n',
    content
)
content = re.sub(
    r'if \(_settings\.OverlayEnabled && _settings\.AppLoopbackProcessId != 0\)\s*\{[^}]*\}\s*\n',
    '// Overlay initialization stubbed\n',
    content,
    flags=re.DOTALL
)
content = re.sub(
    r'_overlayEngine\.\w+\([^)]*\);',
    '/* _overlayEngine stubbed */',
    content
)
content = re.sub(
    r'if \(_overlayEngine\.State == OverlayEngineState\.\w+ \|\| _overlayEngine\.State == OverlayEngineState\.\w+\)',
    'if (true) // Overlay engine stubbed',
    content
)
content = re.sub(
    r'_overlayEngine\.DiagnosticEvent -= OverlayEngine_DiagnosticEvent;',
    '// _overlayEngine.DiagnosticEvent -= stubbed',
    content
)
content = re.sub(
    r'_overlayEngine\.Dispose\(\);',
    '// _overlayEngine.Dispose() stubbed',
    content
)

# 7. Replace SystemCommands.CloseWindow(this) -> Close()
content = content.replace(
    "SystemCommands.CloseWindow(this);",
    "this.Close();"
)

# 8. Replace BeginAnimation (WPF) with a simple no-op comment
content = re.sub(
    r'ConfigPanelBorder\.BeginAnimation\(MaxHeightProperty, anim\);',
    '// TODO: Replace WPF BeginAnimation with WinUI 3 Storyboard animation\n            ConfigPanelBorder.MaxHeight = target;',
    content
)

# 9. MessageBoxResult references cleanup - these are WPF types that no longer exist
content = re.sub(r'\bMessageBoxResult\.\w+\b', '6', content)  # 6 = IDYES
content = re.sub(r'\bMessageBoxButton\.\w+\b', '0x04', content)
content = re.sub(r'\bMessageBoxImage\.\w+\b', '0x30', content)

# Count changes
changed_lines = sum(1 for a, b in zip(original.splitlines(), content.splitlines()) if a != b)
added = content.count('\n') - original.count('\n')
print(f"Changed ~{changed_lines} lines, net +{added} lines")

with open(path, "w", encoding="utf-8") as f:
    f.write(content)

print("Done.")
