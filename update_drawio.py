import xml.etree.ElementTree as ET
import uuid
import datetime

def generate_id():
    return str(uuid.uuid4())[:8]

def create_entity(parent, id, value, x, y, w=120, h=60):
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': f'<b>{value}</b>',
        'style': 'rounded=0;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;',
        'vertex': '1',
        'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(x), 'y': str(y), 'width': str(w), 'height': str(h), 'as': 'geometry'})

def create_relationship(parent, id, value, x, y, w=100, h=60):
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': value,
        'style': 'rhombus;whiteSpace=wrap;html=1;fillColor=#f8cecc;strokeColor=#b85450;',
        'vertex': '1',
        'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(x), 'y': str(y), 'width': str(w), 'height': str(h), 'as': 'geometry'})

def create_edge(parent, id, source, target, label=''):
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': label,
        'style': 'endArrow=none;html=1;rounded=0;labelBackgroundColor=#ffffff;',
        'edge': '1',
        'parent': '1',
        'source': source,
        'target': target
    })
    geo = ET.SubElement(cell, 'mxGeometry', {'relative': '1', 'as': 'geometry'})

def create_area(parent, id, label, x, y, w, h):
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': label,
        'style': 'rounded=1;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#c8c8c8;dashed=1;dashPattern=10 10;strokeWidth=1.2;align=left;verticalAlign=top;spacingLeft=10;spacingTop=10;fontColor=#888888;fontSize=14;fontStyle=1',
        'vertex': '1',
        'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(x), 'y': str(y), 'width': str(w), 'height': str(h), 'as': 'geometry'})

tree = ET.parse('docs/Conceptual-Data-Model.drawio')
root = tree.getroot()

# Ensure compressed=false
root.set('compressed', 'false')

new_diagram = ET.Element('diagram', {
    'id': 'page-0-overview',
    'name': '0 - Overall Conceptual Data Model'
})

graph_model = ET.SubElement(new_diagram, 'mxGraphModel', {
    'dx': '1600', 'dy': '1200', 'grid': '1', 'gridSize': '10', 'guides': '1',
    'tooltips': '1', 'connect': '1', 'arrows': '1', 'fold': '1', 'page': '1',
    'pageScale': '1', 'pageWidth': '1600', 'pageHeight': '1200', 'math': '0', 'shadow': '0'
})

root_element = ET.SubElement(graph_model, 'root')
ET.SubElement(root_element, 'mxCell', {'id': '0'})
ET.SubElement(root_element, 'mxCell', {'id': '1', 'parent': '0'})

# Areas
create_area(root_element, 'area1', 'Access & People', 50, 50, 500, 150)
create_area(root_element, 'area2', 'Commercial & Billing', 650, 50, 500, 150)
create_area(root_element, 'area3', 'Warehouse & Inventory', 50, 300, 500, 150)
create_area(root_element, 'area4', 'Orders & Transport', 650, 300, 800, 150)
create_area(root_element, 'area5', 'Trip & Exceptions', 50, 550, 1100, 150)
create_area(root_element, 'area6', 'Fleet & Telemetry', 50, 800, 850, 150)

# Entities
create_entity(root_element, 'e_role', 'Role', 100, 100)
create_entity(root_element, 'e_user', 'User', 400, 100)
create_entity(root_element, 'e_cust', 'Customer', 700, 100)
create_entity(root_element, 'e_inv', 'Invoice', 1000, 100)
create_entity(root_element, 'e_wh', 'Warehouse', 100, 350)
create_entity(root_element, 'e_lpn', 'LPN', 400, 350)
create_entity(root_element, 'e_order', 'Transport Order', 700, 350)
create_entity(root_element, 'e_route', 'RouteMaster', 1000, 350)
create_entity(root_element, 'e_loc', 'Location', 1300, 350)
create_entity(root_element, 'e_conf', 'LpnDeliveryConfirmation', 100, 600)
create_entity(root_element, 'e_alert', 'AlertLog', 400, 850)
create_entity(root_element, 'e_trip', 'MasterTrip', 700, 600)
create_entity(root_element, 'e_inc', 'IncidentReport', 1000, 600)
create_entity(root_element, 'e_veh', 'Vehicle', 100, 850)
create_entity(root_element, 'e_drv', 'Driver', 700, 850)

# Relationships
create_relationship(root_element, 'r1', 'has role', 250, 100)
create_relationship(root_element, 'r2', 'billed in', 850, 100)
create_relationship(root_element, 'r3', 'places', 700, 225)
create_relationship(root_element, 'r4', 'stores', 250, 350)
create_relationship(root_element, 'r5', 'contains', 550, 350)
create_relationship(root_element, 'r6', 'assigned to', 850, 350)
create_relationship(root_element, 'r7', 'stops at', 1150, 350)
create_relationship(root_element, 'r8', 'followed by', 850, 475)
create_relationship(root_element, 'r9', 'transported in', 550, 475)
create_relationship(root_element, 'r10', 'records', 400, 600)
create_relationship(root_element, 'r11', 'has outcome', 250, 475)
create_relationship(root_element, 'r12', 'experiences', 850, 600)
create_relationship(root_element, 'r13', 'assigned to', 400, 725)
create_relationship(root_element, 'r14', 'driven by', 700, 725)
create_relationship(root_element, 'r15', 'triggers', 550, 725)

# Edges for R1
create_edge(root_element, 'e1_a', 'e_role', 'r1', 'M')
create_edge(root_element, 'e1_b', 'r1', 'e_user', 'N')

# Edges for R2
create_edge(root_element, 'e2_a', 'e_cust', 'r2', '1')
create_edge(root_element, 'e2_b', 'r2', 'e_inv', 'N')

# Edges for R3
create_edge(root_element, 'e3_a', 'e_cust', 'r3', '1')
create_edge(root_element, 'e3_b', 'r3', 'e_order', 'N')

# Edges for R4
create_edge(root_element, 'e4_a', 'e_wh', 'r4', '1')
create_edge(root_element, 'e4_b', 'r4', 'e_lpn', 'N')

# Edges for R5
create_edge(root_element, 'e5_a', 'e_order', 'r5', '1')
create_edge(root_element, 'e5_b', 'r5', 'e_lpn', 'N')

# Edges for R6
create_edge(root_element, 'e6_a', 'e_order', 'r6', 'N')
create_edge(root_element, 'e6_b', 'r6', 'e_route', '1')

# Edges for R7
create_edge(root_element, 'e7_a', 'e_route', 'r7', 'M')
create_edge(root_element, 'e7_b', 'r7', 'e_loc', 'N')

# Edges for R8
create_edge(root_element, 'e8_a', 'e_route', 'r8', '1')
create_edge(root_element, 'e8_b', 'r8', 'e_trip', 'N')

# Edges for R9
create_edge(root_element, 'e9_a', 'e_lpn', 'r9', 'N')
create_edge(root_element, 'e9_b', 'r9', 'e_trip', '1')

# Edges for R10
create_edge(root_element, 'e10_a', 'e_trip', 'r10', '1')
create_edge(root_element, 'e10_b', 'r10', 'e_conf', 'N')

# Edges for R11
create_edge(root_element, 'e11_a', 'e_lpn', 'r11', '1')
create_edge(root_element, 'e11_b', 'r11', 'e_conf', '1')

# Edges for R12
create_edge(root_element, 'e12_a', 'e_trip', 'r12', '1')
create_edge(root_element, 'e12_b', 'r12', 'e_inc', 'N')

# Edges for R13
create_edge(root_element, 'e13_a', 'e_trip', 'r13', 'N')
create_edge(root_element, 'e13_b', 'r13', 'e_veh', '1')

# Edges for R14
create_edge(root_element, 'e14_a', 'e_trip', 'r14', 'M')
create_edge(root_element, 'e14_b', 'r14', 'e_drv', 'N')

# Edges for R15
create_edge(root_element, 'e15_a', 'e_trip', 'r15', '1')
create_edge(root_element, 'e15_b', 'r15', 'e_alert', 'N')

root.insert(0, new_diagram)

tree.write('docs/Conceptual-Data-Model.drawio', encoding='utf-8', xml_declaration=True)
print("Updated successfully.")
