import xml.etree.ElementTree as ET
import glob
import re

files = ['docs/Sequence-Diagram-Dispatch.drawio', 'docs/Sequence-Diagram-IncidentReports.drawio']

def update_style(style_str, updates, force_participant=False):
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
            
    for k, v in updates.items():
        styles[k] = v
        
    if force_participant:
        styles.pop('shape', None)
        shapes = [s for s in shapes if s not in ['ellipse', 'rhombus', 'cloud']]
        
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
    root.set('compressed', 'false')
    
    for d in root.findall('diagram'):
        d_model = d.find('mxGraphModel')
        if d_model is None: continue
        root_el = d_model.find('root')
        if root_el is None: continue
        
        # We already mapped the coordinates perfectly.
        # Now we just need to ensure the styles are fully updated.
        
        for cell in root_el.findall('mxCell'):
            cid = cell.get('id', '')
            style = cell.get('style', '')
            
            if cid.startswith('p_'):
                is_actor = ('shape=umlActor' in style)
                if is_actor:
                    cell.set('style', update_style(style, {'fillColor': '#ffffff', 'strokeColor': '#000000', 'fontColor': '#000000', 'strokeWidth': '1'}))
                else:
                    cell.set('style', update_style(style, {'fillColor': '#ffffff', 'strokeColor': '#000000', 'fontColor': '#000000', 'rounded': '1', 'strokeWidth': '1', 'align': 'center'}, force_participant=True))
                    
    tree.write(file, encoding='utf-8', xml_declaration=True)
print("Formatting complete")
