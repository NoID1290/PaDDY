import glob
import re

files = glob.glob('Views/*.xaml')

for file in set(files):
    try:
        with open(file, 'r', encoding='utf-8') as f:
            content = f.read()
            
        # Strip WindowChrome completely
        content = re.sub(r'<shell:WindowChrome\.WindowChrome>.*?</shell:WindowChrome\.WindowChrome>', '', content, flags=re.DOTALL)
        content = re.sub(r'<WindowChrome\.WindowChrome>.*?</WindowChrome\.WindowChrome>', '', content, flags=re.DOTALL)
        
        # Strip ClipToBounds naked attributes
        content = re.sub(r'\bClipToBounds\b(?!\s*=)', '', content)
        
        with open(file, 'w', encoding='utf-8') as f:
            f.write(content)
    except FileNotFoundError:
        pass
