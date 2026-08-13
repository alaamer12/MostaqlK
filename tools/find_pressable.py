import os
import re
import sys

# Patterns to look for in XAML files
XAML_INTERACTIVE_TAGS = [
    r'<Button\b', r'<ImageButton\b', r'<CheckBox\b', r'<RadioButton\b', 
    r'<Switch\b', r'<AppToggle\b', r'<Picker\b', r'<Entry\b', r'<SearchBar\b',
    r'<SwipeItem\b', r'<MenuFlyoutItem\b', r'<ToolbarItem\b',
    r'<TapGestureRecognizer\b', r'<PointerGestureRecognizer\b', r'<GestureRecognizer\b',
]

XAML_INTERACTIVE_ATTRS = [
    r'Clicked="', r'Tapped="', r'Command="', r'Command=\{Binding', r'IsToggled="', r'IsChecked="', r'SelectedIndex="',
]

CS_INTERACTIVE_PATTERNS = [
    r'\.Clicked\s*\+=', r'\.Tapped\s*\+=', r'\.GestureRecognizers\.Add',
    r'new TapGestureRecognizer', r'new PointerGestureRecognizer',
    r'ICommand', r'RelayCommand', r'AsyncRelayCommand',
]

def find_pressable_components(root_dir):
    interactive_components = set()
    results = []
    
    # Pass 1: Find components that DEFINE interaction
    for root, dirs, files in os.walk(root_dir):
        if any(skip in root for skip in ['bin', 'obj', '.git', '.venv', 'node_modules']):
            continue
            
        for file in files:
            file_path = os.path.join(root, file)
            class_name = os.path.splitext(file)[0]
            
            is_interactive = False
            if file.endswith('.xaml'):
                with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                    content = f.read()
                    if any(re.search(p, content) for p in XAML_INTERACTIVE_TAGS + XAML_INTERACTIVE_ATTRS):
                        is_interactive = True
            
            elif file.endswith('.cs'):
                with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                    content = f.read()
                    if any(re.search(p, content) for p in CS_INTERACTIVE_PATTERNS):
                        is_interactive = True
            
            if is_interactive:
                interactive_components.add(class_name)

    # Pass 2: Find usages of interactive components and standard elements
    for root, dirs, files in os.walk(root_dir):
        if any(skip in root for skip in ['bin', 'obj', '.git', '.venv', 'node_modules']):
            continue
            
        for file in files:
            if not file.endswith('.xaml'):
                continue
                
            file_path = os.path.join(root, file)
            with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                content = f.read()
                
                # Find all tags
                tags = re.finditer(r'<([a-zA-Z0-9_:]+)\b', content)
                for tag_match in tags:
                    tag_full = tag_match.group(1)
                    tag_name = tag_full.split(':')[-1]
                    
                    # Is it a known interactive element or a custom interactive component?
                    is_candidate = False
                    if tag_name in ['Button', 'ImageButton', 'CheckBox', 'RadioButton', 'Switch', 'AppToggle', 'Picker', 'Label', 'Image', 'Border', 'Grid', 'StackLayout', 'VerticalStackLayout', 'HorizontalStackLayout']:
                        # For generic containers, only if they have explicit interaction attributes in the snippet
                        snippet_end = content.find('>', tag_match.end())
                        snippet = content[tag_match.start():snippet_end+1]
                        if any(re.search(p, snippet) for p in XAML_INTERACTIVE_ATTRS + ['TapGestureRecognizer']):
                            is_candidate = True
                    elif tag_name in interactive_components:
                        is_candidate = True
                        snippet_end = content.find('>', tag_match.end())
                        snippet = content[tag_match.start():snippet_end+1]
                    else:
                        continue
                        
                    if is_candidate:
                        # Improved check: look further for behaviors if it's a multi-line tag
                        full_snippet = snippet
                        if snippet.endswith('/>'):
                            pass
                        else:
                            # Search for closing tag to get the full body
                            closing_tag = f'</{tag_full}>'
                            closing_pos = content.find(closing_tag, tag_match.start())
                            if closing_pos != -1:
                                full_snippet = content[tag_match.start():closing_pos + len(closing_tag)]
                        
                        has_effect = 'PressableEffect' in full_snippet or 'SidebarNavRowStyle' in full_snippet or 'AppButtonStyle' in full_snippet
                        line_no = content.count('\n', 0, tag_match.start()) + 1
                        
                        results.append({
                            'file': file_path,
                            'line': line_no,
                            'type': 'XAML',
                            'component': tag_name,
                            'has_effect': has_effect,
                            'snippet': snippet.strip().replace('\n', ' ')
                        })
                            
    return results, interactive_components

if __name__ == "__main__":
    if sys.platform == "win32":
        import io
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

    project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
    print(f"Tracing interactive components in: {project_root}")
    
    pressables, registry = find_pressable_components(project_root)
    
    print(f"\nInteractive Registry ({len(registry)}): {', '.join(sorted(registry))}\n")
    
    missing_count = 0
    for p in pressables:
        if not p['has_effect']:
            missing_count += 1
            status = "[MISSING EFFECT] "
            print(f"{status}[{p['component']}] {p['file']}:{p['line']}")
            print(f"  Snippet: {p['snippet']}")
            print("-" * 60)
    
    print(f"\nAudit Summary:")
    print(f"Total interactive candidates found: {len(pressables)}")
    print(f"Candidates missing PressableEffect: {missing_count}")
