import os
import glob

files = glob.glob('Views/*.xaml.cs') + glob.glob('Controls/*.xaml.cs')

for file in files:
    with open(file, 'r', encoding='utf-8') as f:
        content = f.read()

    # Generic WinUI 3 replacements
    content = content.replace('using System.Windows;', 'using Microsoft.UI.Xaml;')
    content = content.replace('using System.Windows.Forms;', '')
    content = content.replace('using System.Windows.Media;', 'using Microsoft.UI.Xaml.Media;')
    content = content.replace('using System.Windows.Input;', 'using Microsoft.UI.Xaml.Input;')
    content = content.replace('using System.Windows.Controls;', 'using Microsoft.UI.Xaml.Controls;')
    content = content.replace('using System.Windows.Controls.Primitives;', 'using Microsoft.UI.Xaml.Controls.Primitives;')
    content = content.replace('using System.Windows.Shapes;', 'using Microsoft.UI.Xaml.Shapes;')

    # Event signature updates
    content = content.replace('RoutedPropertyChangedEventArgs<double>', 'RangeBaseValueChangedEventArgs')
    content = content.replace('System.Windows.Input.MouseButtonEventArgs', 'PointerRoutedEventArgs')
    
    # DialogResult logic injection for specific windows
    if 'class SettingsWindow : Window' in content and 'DialogResult' not in content:
        content = content.replace('public partial class SettingsWindow : Window\n    {', 'public partial class SettingsWindow : Window\n    {\n        public bool? DialogResult { get; set; }')
    
    if 'class EffectsWindow : Window' in content and 'DialogResult' not in content:
        content = content.replace('public partial class EffectsWindow : Window\n{', 'public partial class EffectsWindow : Window\n{\n    public bool? DialogResult { get; set; }')
        
    if 'class AudioEditorWindow : Window' in content and 'DialogResult' not in content:
        content = content.replace('public partial class AudioEditorWindow : Window\n    {', 'public partial class AudioEditorWindow : Window\n    {\n        public bool? DialogResult { get; set; }')

    if 'class RenameDialog : Window' in content and 'DialogResult' not in content:
        content = content.replace('public partial class RenameDialog : Window\n    {', 'public partial class RenameDialog : Window\n    {\n        public bool? DialogResult { get; set; }')

    with open(file, 'w', encoding='utf-8') as f:
        f.write(content)
