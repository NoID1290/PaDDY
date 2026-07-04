import glob
import re

files = glob.glob('Views/*.xaml')

for file in set(files):
    try:
        with open(file, 'r', encoding='utf-8') as f:
            content = f.read()
            
        # Strip Window properties that crash WinUI 3
        content = re.sub(r'Title=".*?"', '', content)
        content = re.sub(r'Height="\d+"', '', content)
        content = re.sub(r'Width="\d+"', '', content)
        content = re.sub(r'AllowsTransparency=".*?"', '', content)
        content = re.sub(r'WindowStyle=".*?"', '', content)
        content = re.sub(r'ResizeMode=".*?"', '', content)
        content = re.sub(r'WindowStartupLocation=".*?"', '', content)
        content = re.sub(r'Topmost=".*?"', '', content)
        content = re.sub(r'ShowInTaskbar=".*?"', '', content)
        content = re.sub(r'Background="\{ThemeResource WindowBgBrush\}"', '', content)
        content = re.sub(r'Foreground="\{ThemeResource PrimaryTextBrush\}"', '', content)
        
        with open(file, 'w', encoding='utf-8') as f:
            f.write(content)
    except FileNotFoundError:
        pass
