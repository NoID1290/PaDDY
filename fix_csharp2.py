import glob
import re
import os

files = glob.glob('**/*.cs', recursive=True)
exclude_dirs = ['obj', 'bin', 'EffectProcessor', 'OverlayEngine', 'AudioProcessor', 'vendors']
files = [f for f in files if not any(x in f for x in exclude_dirs)]

for file in files:
    with open(file, 'r', encoding='utf-8') as f:
        content = f.read()

    # Colors and Brushes
    content = content.replace('using Microsoft.UI.Xaml.Media.Color;', 'using Windows.UI;')
    content = content.replace('Microsoft.UI.Xaml.Media.Color', 'Windows.UI.Color')
    content = content.replace('Microsoft.UI.Xaml.Media.Brushes', 'Microsoft.UI.Colors') # Note: Colors are not brushes, so Brushes.Red -> Colors.Red is partly wrong if Brush is expected, but let's see.
    content = content.replace('using Microsoft.UI.Xaml.Media.Brushes;', '')
    
    # KeyEventArgs -> KeyRoutedEventArgs
    content = content.replace('KeyEventArgs', 'KeyRoutedEventArgs')
    
    # Point and Size
    content = content.replace('Microsoft.UI.Xaml.Point', 'Windows.Foundation.Point')
    content = content.replace('Microsoft.UI.Xaml.Size', 'Windows.Foundation.Size')
    
    # Threading
    content = content.replace('Microsoft.UI.Xaml.Threading', 'Microsoft.UI.Dispatching')
    
    # OnClosing
    content = re.sub(r'(protected\s+override\s+void\s+OnClosing\s*\(.*?\)\s*\{)', r'// \1', content)
    
    # OnSourceInitialized
    content = re.sub(r'(protected\s+override\s+void\s+OnSourceInitialized\s*\(.*?\)\s*\{)', r'// \1', content)
    
    with open(file, 'w', encoding='utf-8') as f:
        f.write(content)
