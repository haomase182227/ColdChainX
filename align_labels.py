import xml.etree.ElementTree as ET

files = [
    'docs/Sequence-Diagram-Authentication-User-Management.drawio',
    'docs/Sequence-Diagram-Dispatch.drawio',
    'docs/Sequence-Diagram-IncidentReports.drawio'
]

def update_style(style_str):
    if not style_str: return style_str
    parts = style_str.split(';')
    styles = {}
    shapes = []
    for p in parts:
        if not p: continue
        if '=' in p:
            k, v = p.split('=', 1)
            styles[k] = v
        else:
            shapes.append(p)
    
    styles['align'] = 'center'
    styles['verticalAlign'] = 'bottom'
    
    out = []
    for s in shapes: out.append(s)
    for k, v in styles.items(): out.append(f"{k}={v}")
    return ';'.join(out) + ';'

for file in files:
    try:
        tree = ET.parse(file)
    except Exception as e:
        print(f"Failed to parse {file}: {e}")
        continue
    root = tree.getroot()
    changed = 0
    for cell in root.iter('mxCell'):
        # Identify message labels by checking if parent ends with _edge
        # Or if id ends with _lbl
        pid = cell.get('parent', '')
        cid = cell.get('id', '')
        style = cell.get('style', '')
        if (pid.endswith('_edge') or cid.endswith('_lbl')) and 'text;' in style:
            cell.set('style', update_style(style))
            geo = cell.find('mxGeometry')
            if geo is not None:
                # Ensure it's centered and 10px above
                geo.set('x', '0')
                geo.set('y', '-10')
                changed += 1

    tree.write(file, encoding='utf-8', xml_declaration=True)
    print(f"Updated {changed} labels in {file}")

