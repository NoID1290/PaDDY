import re

with open('Themes/AppTheme.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

# Remove Triggers entirely
content = re.sub(r'<ControlTemplate\.Triggers>.*?</ControlTemplate\.Triggers>', '', content, flags=re.DOTALL)
content = re.sub(r'<Style\.Triggers>.*?</Style\.Triggers>', '', content, flags=re.DOTALL)

with open('Themes/AppTheme.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
