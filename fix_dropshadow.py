import glob
import re

files = glob.glob('Views/*.xaml') + glob.glob('Controls/*.xaml') + glob.glob('Themes/*.xaml') + ['App.xaml', 'MainWindow.xaml']

for file in set(files):
    try:
        with open(file, 'r', encoding='utf-8') as f:
            content = f.read()
            
        new_content = re.sub(r'<DropShadowEffect[^>]*?/>', '', content)
        new_content = re.sub(r'<DropShadowEffect[^>]*?>.*?</DropShadowEffect>', '', new_content, flags=re.DOTALL)
        # Remove any .Effect property elements like <Grid.Effect>...</Grid.Effect> or <UIElement.Effect>...</UIElement.Effect>
        new_content = re.sub(r'<[\w:]+\.Effect>.*?</[\w:]+\.Effect>', '', new_content, flags=re.DOTALL)
        # Remove storyboards animating DropShadowEffect
        new_content = re.sub(r'<DoubleAnimation[^>]*?DropShadowEffect[^>]*?/>', '', new_content)
        new_content = re.sub(r'<DoubleAnimation[^>]*?DropShadowEffect[^>]*?>.*?</DoubleAnimation>', '', new_content, flags=re.DOTALL)
        
        if new_content != content:
            with open(file, 'w', encoding='utf-8') as f:
                f.write(new_content)
    except FileNotFoundError:
        pass
