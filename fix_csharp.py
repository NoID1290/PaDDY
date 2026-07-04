import glob
import re

files = glob.glob('**/*.cs', recursive=True)
exclude_dirs = ['obj', 'bin', 'EffectProcessor', 'OverlayEngine', 'AudioProcessor', 'vendors']
files = [f for f in files if not any(x in f for x in exclude_dirs)]

for file in files:
    with open(file, 'r', encoding='utf-8') as f:
        content = f.read()

    # Replace namespaces
    content = content.replace('using System.Windows;', 'using Microsoft.UI.Xaml;\nusing Windows.Foundation;')
    content = content.replace('using System.Windows.Controls;', 'using Microsoft.UI.Xaml.Controls;')
    content = content.replace('using System.Windows.Controls.Primitives;', 'using Microsoft.UI.Xaml.Controls.Primitives;')
    content = content.replace('using System.Windows.Data;', 'using Microsoft.UI.Xaml.Data;')
    content = content.replace('using System.Windows.Documents;', 'using Microsoft.UI.Xaml.Documents;')
    content = content.replace('using System.Windows.Input;', 'using Microsoft.UI.Xaml.Input;')
    content = content.replace('using System.Windows.Media;', 'using Microsoft.UI.Xaml.Media;')
    content = content.replace('using System.Windows.Media.Imaging;', 'using Microsoft.UI.Xaml.Media.Imaging;')
    content = content.replace('using System.Windows.Media.Effects;', '') # Not in WinUI 3
    content = content.replace('using System.Windows.Navigation;', 'using Microsoft.UI.Xaml.Navigation;')
    content = content.replace('using System.Windows.Shapes;', 'using Microsoft.UI.Xaml.Shapes;')
    content = content.replace('using System.Windows.Threading;', 'using Microsoft.UI.Dispatching;')
    content = content.replace('using System.Windows.Interop;', '') # Not in WinUI 3
    
    # Replace inline namespace usage
    content = content.replace('System.Windows.Input.', 'Microsoft.UI.Xaml.Input.')
    content = content.replace('System.Windows.Controls.', 'Microsoft.UI.Xaml.Controls.')
    content = content.replace('System.Windows.', 'Microsoft.UI.Xaml.')
    
    # Specific type fixes
    content = content.replace('DispatcherTimer', 'Microsoft.UI.Dispatching.DispatcherQueueTimer')
    
    with open(file, 'w', encoding='utf-8') as f:
        f.write(content)
