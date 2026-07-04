import glob

files = glob.glob('Views/*.xaml') + glob.glob('Controls/*.xaml') + glob.glob('Themes/*.xaml') + ['App.xaml', 'MainWindow.xaml']

for file in set(files):
    try:
        with open(file, 'r', encoding='utf-8') as f:
            content = f.read()
            
        new_content = content.replace('DynamicResource', 'ThemeResource')
        
        if new_content != content:
            with open(file, 'w', encoding='utf-8') as f:
                f.write(new_content)
    except FileNotFoundError:
        pass
