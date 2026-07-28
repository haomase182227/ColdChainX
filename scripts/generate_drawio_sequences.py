import xml.etree.ElementTree as ET
import html
import uuid
import sys
import json

def generate_drawio(diagrams_config, output_file):
    mxfile = ET.Element('mxfile', host="Electron", compressed="false", version="28.2.8")
    
    for diag_index, diag in enumerate(diagrams_config):
        diagram_name = diag['title']
        diagram = ET.SubElement(mxfile, 'diagram', id=str(uuid.uuid4()), name=diagram_name)
        
        msg_count = sum(1 for s in diag['steps'] if s['type'] in ('msg', 'alt_start', 'alt_else', 'alt_end'))
        required_height = max(1169, 300 + msg_count * 60)
        required_width = max(827, len(diag['participants']) * 240 + 100)
        
        mxGraphModel = ET.SubElement(diagram, 'mxGraphModel', dx="1000", dy="1000", grid="1", gridSize="10", guides="1", tooltips="1", connect="1", arrows="1", fold="1", page="1", pageScale="1", pageWidth=str(required_width), pageHeight=str(required_height), background="#ffffff", math="0", shadow="0")
        root = ET.SubElement(mxGraphModel, 'root')
        
        ET.SubElement(root, 'mxCell', id="0")
        ET.SubElement(root, 'mxCell', id="1", parent="0")
        
        # Title (Top-Left)
        title_id = "title_" + str(uuid.uuid4()).split('-')[0]
        title_cell = ET.SubElement(root, 'mxCell', id=title_id, value=html.escape(diagram_name), style="text;html=1;align=left;verticalAlign=middle;whiteSpace=wrap;fontSize=24;fontStyle=1;", parent="1", vertex="1", connectable="0")
        ET.SubElement(title_cell, 'mxGeometry', x="40", y="20", width="800", height="40", **{"as": "geometry"})
        
        # Participants Layout
        x_start = 80
        x_gap = 210
        y_start = 120
        lifeline_length = msg_count * 50 + 100
        
        participants_map = {}
        for i, p in enumerate(diag['participants']):
            p_id = "p_" + str(i)
            x = x_start + i * x_gap
            participants_map[p['id']] = x
            
            if p.get('type') == 'actor':
                style = "shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;outlineConnect=0;fillColor=#ffffff;strokeColor=#333333;fontSize=14;fontStyle=1;"
                geom = {'x': str(x-20), 'y': str(y_start-40), 'width': "40", 'height': "80"}
            elif p.get('type') == 'db':
                style = "shape=cylinder3;whiteSpace=wrap;html=1;boundedLbl=1;backgroundOutline=1;size=15;fillColor=#e1d5e7;strokeColor=#9673a6;strokeWidth=1.5;fontSize=14;fontStyle=1;"
                geom = {'x': str(x-60), 'y': str(y_start-30), 'width': "120", 'height': "60"}
            elif p.get('type') == 'screen':
                style = "rounded=1;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;strokeWidth=1.5;fontSize=14;fontStyle=1;align=center;verticalAlign=middle;spacing=4;"
                geom = {'x': str(x-80), 'y': str(y_start-25), 'width': "160", 'height': "50"}
            elif p.get('type') == 'thirdparty':
                style = "ellipse;shape=cloud;whiteSpace=wrap;html=1;fillColor=#f8cecc;strokeColor=#b85450;strokeWidth=1.5;fontSize=14;fontStyle=1;align=center;verticalAlign=middle;"
                geom = {'x': str(x-70), 'y': str(y_start-35), 'width': "140", 'height': "70"}
            else:
                style = "rounded=0;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;strokeWidth=1.5;fontSize=14;fontStyle=1;align=center;verticalAlign=middle;spacing=4;"
                geom = {'x': str(x-90), 'y': str(y_start-25), 'width': "180", 'height': "50"}
                
            p_cell = ET.SubElement(root, 'mxCell', id=p_id, value=html.escape(p['name']), style=style, parent="1", vertex="1")
            ET.SubElement(p_cell, 'mxGeometry', **geom, **{"as": "geometry"})
            
            end_id = "lend_" + str(i)
            end_cell = ET.SubElement(root, 'mxCell', id=end_id, value="", style="opacity=0;fillOpacity=0;strokeOpacity=0;", parent="1", vertex="1", connectable="0")
            ET.SubElement(end_cell, 'mxGeometry', x=str(x), y=str(y_start + lifeline_length), width="1", height="1", **{"as": "geometry"})
            
            line_id = "lline_" + str(i)
            line_cell = ET.SubElement(root, 'mxCell', id=line_id, value="", style="edgeStyle=none;html=1;dashed=1;dashPattern=8 6;endArrow=none;startArrow=none;strokeColor=#777777;strokeWidth=1.2;", parent="1", source=p_id, target=end_id, edge="1")
            ET.SubElement(line_cell, 'mxGeometry', relative="1", **{"as": "geometry"})
            
        # First pass to find activation bounds
        activations = {pid: {'min': 99999, 'max': 0} for pid in participants_map}
        y_cursor = y_start + 60
        
        for step in diag['steps']:
            if step['type'] == 'msg':
                if step['from'] in activations:
                    activations[step['from']]['min'] = min(activations[step['from']]['min'], y_cursor)
                    activations[step['from']]['max'] = max(activations[step['from']]['max'], y_cursor)
                if step['to'] in activations:
                    activations[step['to']]['min'] = min(activations[step['to']]['min'], y_cursor)
                    activations[step['to']]['max'] = max(activations[step['to']]['max'], y_cursor)
                y_cursor += 45
            elif step['type'] in ('alt_start', 'alt_else'):
                y_cursor += 30
            elif step['type'] == 'alt_end':
                y_cursor += 20
                
        # Draw Activation Bars (except for Actor)
        for i, p in enumerate(diag['participants']):
            if p.get('type') == 'actor': continue
            pid = p['id']
            bounds = activations[pid]
            if bounds['max'] > bounds['min']:
                act_id = f"act_{i}"
                x = participants_map[pid] - 5
                y = bounds['min'] - 10
                h = (bounds['max'] - bounds['min']) + 20
                act_cell = ET.SubElement(root, 'mxCell', id=act_id, value="", style="html=1;points=[];perimeter=orthogonalPerimeter;fillColor=#e1d5e7;strokeColor=#9673a6;", parent="1", vertex="1")
                ET.SubElement(act_cell, 'mxGeometry', x=str(x), y=str(y), width="10", height=str(h), **{"as": "geometry"})

        # Draw steps
        y_cursor = y_start + 60
        step_id_counter = 0
        alt_stack = []
        
        for step in diag['steps']:
            step_id_counter += 1
            if step['type'] == 'msg':
                if step['from'] not in participants_map or step['to'] not in participants_map: continue
                
                # Offset X slightly if aiming at activation bar
                source_x = participants_map[step['from']]
                target_x = participants_map[step['to']]
                if next((p.get('type') for p in diag['participants'] if p['id'] == step['from']), None) != 'actor':
                    source_x += (5 if target_x > source_x else -5)
                if next((p.get('type') for p in diag['participants'] if p['id'] == step['to']), None) != 'actor':
                    target_x += (-5 if target_x > source_x else 5)
                    
                is_return = step.get('return', False)
                is_self = source_x == target_x
                
                sp_id = f"msg_{step_id_counter}_sp"
                sp_cell = ET.SubElement(root, 'mxCell', id=sp_id, value="", style="opacity=0;", parent="1", vertex="1", connectable="0")
                ET.SubElement(sp_cell, 'mxGeometry', x=str(source_x), y=str(y_cursor), width="1", height="1", **{"as": "geometry"})
                
                if is_self:
                    edge_id = f"msg_{step_id_counter}_edge"
                    style = "edgeStyle=orthogonalEdgeStyle;html=1;align=left;spacingLeft=2;endArrow=block;rounded=0;endFill=1;strokeColor=#222222;strokeWidth=1.2;"
                    e_cell = ET.SubElement(root, 'mxCell', id=edge_id, value=html.escape(step['label']), style=style, parent="1", source=sp_id, edge="1")
                    ET.SubElement(e_cell, 'mxGeometry', relative="1", **{"as": "geometry"}).append(
                        ET.fromstring(f'<Array as="points"><mxPoint x="{source_x+40}" y="{y_cursor}"/><mxPoint x="{source_x+40}" y="{y_cursor+25}"/><mxPoint x="{source_x}" y="{y_cursor+25}"/></Array>')
                    )
                    y_cursor += 45
                else:
                    tp_id = f"msg_{step_id_counter}_tp"
                    tp_cell = ET.SubElement(root, 'mxCell', id=tp_id, value="", style="opacity=0;", parent="1", vertex="1", connectable="0")
                    ET.SubElement(tp_cell, 'mxGeometry', x=str(target_x), y=str(y_cursor), width="1", height="1", **{"as": "geometry"})
                    
                    edge_id = f"msg_{step_id_counter}_edge"
                    style = "edgeStyle=none;html=1;endArrow=block;endFill=1;strokeColor=#222222;strokeWidth=1.35;"
                    if is_return:
                        style = "edgeStyle=none;html=1;dashed=1;endArrow=open;endFill=0;strokeColor=#555555;strokeWidth=1.2;"
                    
                    e_cell = ET.SubElement(root, 'mxCell', id=edge_id, value="", style=style, parent="1", source=sp_id, target=tp_id, edge="1")
                    ET.SubElement(e_cell, 'mxGeometry', relative="1", **{"as": "geometry"})
                    
                    lbl_id = f"msg_{step_id_counter}_lbl"
                    l_cell = ET.SubElement(root, 'mxCell', id=lbl_id, value=html.escape(step['label']), style="text;html=1;align=center;verticalAlign=bottom;fontSize=12;", parent=edge_id, vertex="1", connectable="0")
                    ET.SubElement(l_cell, 'mxGeometry', relative="1", y="-5", **{"as": "geometry"})
                    
                    y_cursor += 45
                
            elif step['type'] == 'alt_start':
                alt_stack.append({'y': y_cursor, 'label': step.get('label', 'alt'), 'id': f"alt_{step_id_counter}"})
                y_cursor += 30
            elif step['type'] == 'alt_else':
                alt = alt_stack[-1]
                min_x = min(participants_map.values()) - 40
                max_x = max(participants_map.values()) + 40
                l_id = f"alt_else_{step_id_counter}"
                l_cell = ET.SubElement(root, 'mxCell', id=l_id, value="", style="endArrow=none;dashed=1;html=1;strokeWidth=1.2;strokeColor=#000000;dashPattern=4 4;", parent="1", edge="1")
                geom = ET.SubElement(l_cell, 'mxGeometry', width="50", height="50", relative="1", **{"as": "geometry"})
                geom.append(ET.fromstring(f'<mxPoint x="{min_x}" y="{y_cursor}" as="sourcePoint"/>'))
                geom.append(ET.fromstring(f'<mxPoint x="{max_x}" y="{y_cursor}" as="targetPoint"/>'))
                
                if 'label' in step:
                    txt_id = f"alt_else_txt_{step_id_counter}"
                    txt_cell = ET.SubElement(root, 'mxCell', id=txt_id, value=f"[{html.escape(step['label'])}]", style="text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=middle;whiteSpace=wrap;rounded=0;fontSize=12;fontStyle=1", parent="1", vertex="1")
                    ET.SubElement(txt_cell, 'mxGeometry', x=str(min_x + 5), y=str(y_cursor), width="300", height="20", **{"as": "geometry"})
                y_cursor += 30
            elif step['type'] == 'alt_end':
                if alt_stack:
                    alt = alt_stack.pop()
                    min_x = min(participants_map.values()) - 40
                    max_x = max(participants_map.values()) + 40
                    box_width = max_x - min_x
                    box_height = y_cursor - alt['y'] + 10
                    
                    box_cell = ET.Element('mxCell', id=alt['id'], value=html.escape(alt['label']), style="shape=umlFrame;whiteSpace=wrap;html=1;width=60;height=20;boundedLbl=1;verticalAlign=middle;align=left;spacingLeft=5;fontSize=12;fontStyle=1;fillColor=none;", parent="1", vertex="1")
                    ET.SubElement(box_cell, 'mxGeometry', x=str(min_x), y=str(alt['y']), width=str(box_width), height=str(box_height), **{"as": "geometry"})
                    root.insert(2, box_cell)
                    y_cursor += 20
        
        # Figure Caption (Bottom-Center)
        cap_id = "cap_" + str(uuid.uuid4()).split('-')[0]
        cap_cell = ET.SubElement(root, 'mxCell', id=cap_id, value=f"Figure {diag_index+1}: {html.escape(diagram_name)}", style="text;html=1;align=center;verticalAlign=middle;whiteSpace=wrap;fontSize=14;fontStyle=2;", parent="1", vertex="1", connectable="0")
        ET.SubElement(cap_cell, 'mxGeometry', x="0", y=str(y_start + lifeline_length + 20), width=str(required_width), height="30", **{"as": "geometry"})

    xml_str = '<?xml version="1.0" encoding="UTF-8"?>\n' + ET.tostring(mxfile, encoding="unicode")
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write(xml_str)
    print(f"Generated {output_file} successfully.")

if __name__ == '__main__':
    with open(sys.argv[1], 'r', encoding='utf-8') as f:
        config = json.load(f)
    generate_drawio(config, sys.argv[2])
