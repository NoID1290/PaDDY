import glob
import re

files = glob.glob('Views/*.xaml') + glob.glob('Controls/*.xaml') + glob.glob('Themes/*.xaml') + ['App.xaml']

for file in set(files):
    try:
        with open(file, 'r', encoding='utf-8') as f:
            content = f.read()
            
        # Strip all Triggers blocks
        content = re.sub(r'<ControlTemplate\.Triggers>.*?</ControlTemplate\.Triggers>', '', content, flags=re.DOTALL)
        content = re.sub(r'<Border\.Triggers>.*?</Border\.Triggers>', '', content, flags=re.DOTALL)
        content = re.sub(r'<Style\.Triggers>.*?</Style\.Triggers>', '', content, flags=re.DOTALL)
        
        # Also remove any DataTemplate.Triggers if they exist
        content = re.sub(r'<DataTemplate\.Triggers>.*?</DataTemplate\.Triggers>', '', content, flags=re.DOTALL)
        
        with open(file, 'w', encoding='utf-8') as f:
            f.write(content)
    except FileNotFoundError:
        pass
