import json
import subprocess
import copy
import os

with open("obj/Debug/net10.0-windows10.0.19041.0/win-x64/input.json", "r", encoding="utf-8") as f:
    data = json.load(f)

compiler_path = r"C:\Users\90lec\.nuget\packages\microsoft.windowsappsdk\1.6.241114003\tools\net472\XamlCompiler.exe"

# Select AboutWindow.xaml
page = [p for p in data["XamlPages"] if "AboutWindow.xaml" in p["FullPath"]][0]

page_data = copy.deepcopy(data)
page_data["XamlApplications"] = []
page_data["XamlPages"] = [page]

with open("obj/test_input.json", "w", encoding="utf-8") as f:
    json.dump(page_data, f)

if os.path.exists("obj/test_output.json"):
    os.remove("obj/test_output.json")

print("Running XamlCompiler on AboutWindow.xaml...")
res = subprocess.run([compiler_path, "obj/test_input.json", "obj/test_output.json"], capture_output=True, text=True)
print(f"Exit code: {res.returncode}")

if os.path.exists("obj/test_output.json"):
    with open("obj/test_output.json", "r", encoding="utf-8") as f:
        out_content = f.read()
    print("test_output.json exists! Content length:", len(out_content))
    try:
        parsed = json.loads(out_content)
        print(json.dumps(parsed, indent=2)[:500])
    except Exception:
        print(out_content[:500])
else:
    print("test_output.json does not exist!")
