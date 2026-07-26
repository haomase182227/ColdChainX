import xml.etree.ElementTree as ET
import sys
import math

files = ['docs/Sequence-Diagram-Dispatch.drawio', 'docs/Sequence-Diagram-IncidentReports.drawio']

def update_style(style_str, updates):
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
    
    for d in root.findall('diagram'):
        d_model = d.find('mxGraphModel')
        if d_model is None: continue
        root_el = d_model.find('root')
        if root_el is None: continue
        
        # 1. Extract lifelines (p_*)
        lifelines = []
        for cell in root_el.findall('mxCell'):
            cid = cell.get('id')
            if cid and cid.startswith('p_'):
                geo = cell.find('mxGeometry')
                if geo is not None:
                    x = float(geo.get('x', 0))
                    w = float(geo.get('width', 100))
                    lifelines.append({'id': cid, 'x': x, 'w': w, 'center': x + w/2})
                    
        if not lifelines: continue
        
        lifelines.sort(key=lambda item: item['center'])
        N = len(lifelines)
        # Increase horizontal spacing
        spacing = 250
        new_centers = [150 + i * spacing for i in range(N)]
        center_map = {lifelines[i]['id']: new_centers[i] for i in range(N)}
        
        def get_closest_lifeline(old_x):
            closest_id = None
            min_dist = 999999
            for ll in lifelines:
                dist = abs(ll['center'] - old_x)
                if dist < min_dist:
                    min_dist = dist
                    closest_id = ll['id']
            if not closest_id: return old_x
            closest_old_c = [ll['center'] for ll in lifelines if ll['id'] == closest_id][0]
            closest_new_c = center_map[closest_id]
            return closest_new_c + (old_x - closest_old_c)

        # 2. Vertical Scaling
        Y0 = 120
        SCALE_Y = 1.8
        def scale_y(old_y):
            if old_y > Y0:
                return Y0 + (old_y - Y0) * SCALE_Y
            return old_y

        max_y = 0

        # Transform geometries
        for cell in root_el.findall('mxCell'):
            cid = cell.get('id', '')
            style = cell.get('style', '')
            geo = cell.find('mxGeometry')
            
            if geo is not None:
                # Handle waypoints first
                arr = geo.find('Array')
                if arr is not None:
                    for pt in arr.findall('mxPoint'):
                        px = float(pt.get('x', 0))
                        py = float(pt.get('y', 0))
                        pt.set('x', str(get_closest_lifeline(px)))
                        pt.set('y', str(scale_y(py)))

                # Handle main geometry
                if cid.startswith('p_'):
                    new_c = center_map[cid]
                    w = float(geo.get('width', 100))
                    geo.set('x', str(new_c - w/2))
                    
                elif cid.startswith('lend_') or cid.startswith('msg_'):
                    if cid.endswith('_lbl'):
                        # Message labels have relative Y, do NOT scale their Y!
                        pass
                    elif cid.endswith('_edge'):
                        # Message edges: enforce orthogonal routing
                        cell.set('style', update_style(style, {'edgeStyle': 'orthogonalEdgeStyle', 'rounded': '0'}))
                    else:
                        # Points
                        old_x = float(geo.get('x', 0))
                        old_y = float(geo.get('y', 0))
                        geo.set('x', str(get_closest_lifeline(old_x)))
                        geo.set('y', str(scale_y(old_y)))
                        
                elif cid.startswith('act_'):
                    old_x = float(geo.get('x', 0))
                    old_y = float(geo.get('y', 0))
                    w = float(geo.get('width', 10))
                    h = float(geo.get('height', 10))
                    
                    old_c = old_x + w/2
                    new_c = get_closest_lifeline(old_c)
                    geo.set('x', str(new_c - w/2))
                    
                    geo.set('y', str(scale_y(old_y)))
                    # Scale height
                    # If it crosses Y0, only scale the part below Y0. But usually act_ starts below Y0.
                    if old_y >= Y0:
                        geo.set('height', str(h * SCALE_Y))
                    else:
                        geo.set('height', str(h))

                elif cid.startswith('frame_'):
                    old_x = float(geo.get('x', 0))
                    old_y = float(geo.get('y', 0))
                    w = float(geo.get('width', 0))
                    h = float(geo.get('height', 0))
                    new_x = get_closest_lifeline(old_x)
                    new_right = get_closest_lifeline(old_x + w)
                    geo.set('x', str(new_x))
                    geo.set('width', str(new_right - new_x))
                    geo.set('y', str(scale_y(old_y)))
                    if old_y >= Y0:
                        geo.set('height', str(h * SCALE_Y))
                        
                # Update max_y (ignore cap_ for now)
                if not cid.startswith('cap_'):
                    y = float(geo.get('y', 0))
                    h = float(geo.get('height', 0))
                    if y + h > max_y:
                        max_y = y + h
                        
        # Second pass for Cap, Title and Page size
        page_width = max(1169, new_centers[-1] + 200)
        page_height = max(827, max_y + 100)
        d_model.set('pageWidth', str(math.ceil(page_width)))
        d_model.set('pageHeight', str(math.ceil(page_height)))
        
        for cell in root_el.findall('mxCell'):
            cid = cell.get('id', '')
            geo = cell.find('mxGeometry')
            if cid.startswith('cap_') and geo is not None:
                geo.set('y', str(max_y + 40))
                geo.set('width', str(math.ceil(page_width)))
            elif cid.startswith('title_') and geo is not None:
                geo.set('x', '20')
                geo.set('y', '20')

    tree.write(file, encoding='utf-8', xml_declaration=True)
print("Scaling complete")
