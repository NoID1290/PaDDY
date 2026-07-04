import glob
import re

files = glob.glob('Views/*.xaml') + glob.glob('Controls/*.xaml') + glob.glob('Themes/*.xaml') + ['App.xaml', 'MainWindow.xaml']

for file in set(files):
    try:
        with open(file, 'r', encoding='utf-8') as f:
            content = f.read()
            
        new_content = re.sub(r'\s+IsCancel="True"', '', content)
        new_content = re.sub(r'\s+IsDefault="True"', '', new_content)
        
        if new_content != content:
            with open(file, 'w', encoding='utf-8') as f:
                f.write(new_content)
    except FileNotFoundError:
        pass
