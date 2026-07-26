import xml.etree.ElementTree as ET
import math
import random
import uuid

# Define domains and their center targets
domains = {
    'Access': {'center': (400, 400), 'color': '#f5f5f5'},
    'Reference': {'center': (1400, 400), 'color': '#f5f5f5'},
    'Commercial': {'center': (2400, 400), 'color': '#f5f5f5'},
    'Warehouse': {'center': (400, 1200), 'color': '#f5f5f5'},
    'Transport': {'center': (1400, 1200), 'color': '#f5f5f5'},
    'Exceptions': {'center': (400, 2000), 'color': '#f5f5f5'},
}

# Define entities
entities = {
    # Access
    'Role': 'Access', 'User': 'Access', 'ChatMessage': 'Access', 'Permission': 'Access',
    'Notification': 'Access', 'MessageType': 'Access', 'NotificationTemplate': 'Access',
    # Reference
    'ServiceCatalog': 'Reference', 'PricingMatrix': 'Reference', 'ComplianceZoningRule': 'Reference',
    'SystemConfig': 'Reference', 'WeightTier': 'Reference',
    # Commercial
    'Customer': 'Commercial', 'Location': 'Commercial', 'TransportOrder': 'Commercial', 'Invoice': 'Commercial',
    'CustomerContract': 'Commercial', 'Quotation': 'Commercial', 'OrderDimension': 'Commercial',
    'InvoiceLine': 'Commercial', 'ContractAppendix': 'Commercial', 'TransportDocument': 'Commercial',
    'Claim': 'Commercial', 'ClaimEvidence': 'Commercial', 'GeoFence': 'Commercial',
    # Warehouse
    'Warehouse': 'Warehouse', 'InboundAsn': 'Warehouse', 'WarehouseReceipt': 'Warehouse', 'Lpn': 'Warehouse',
    'PenaltyBill': 'Warehouse', 'InboundReturnSlip': 'Warehouse', 'OutboundOrder': 'Warehouse', 'OutboundOrderItem': 'Warehouse',
    # Transport
    'RouteMaster': 'Transport', 'RouteStop': 'Transport', 'RouteSchedule': 'Transport', 'MasterTrip': 'Transport',
    'Vehicle': 'Transport', 'TripStop': 'Transport', 'TripStopEvent': 'Transport', 'Seal': 'Transport',
    'TripDriver': 'Transport', 'Driver': 'Transport', 'DetentionCharge': 'Transport', 'ExpenseAdvance': 'Transport',
    'ExpenseReceipt': 'Transport', 'DriverLicense': 'Transport', 'DriverWorkLog': 'Transport',
    'MaintenanceTicket': 'Transport', 'VehicleDocument': 'Transport', 'VehicleOdometerLog': 'Transport',
    # Exceptions
    'IotDevice': 'Exceptions', 'TelemetryLog': 'Exceptions', 'AlertLog': 'Exceptions', 'IncidentReport': 'Exceptions',
    'IncidentEvidence': 'Exceptions', 'LpnDeliveryConfirmation': 'Exceptions', 'DeliveryEpod': 'Exceptions',
    'ReturnedItem': 'Exceptions'
}

# Define relationships (Source, Target, Name, CardSrc, CardTgt)
rels_def = [
    ('Role', 'Permission', 'grants', 'M', 'N'),
    ('User', 'Role', 'assigned', 'M', 'N'),
    ('User', 'ChatMessage', 'sends', '1', 'N'),
    ('User', 'Notification', 'receives', '1', 'N'),
    ('NotificationTemplate', 'Notification', 'generates', '1', 'N'),
    ('MessageType', 'NotificationTemplate', 'classifies', '1', 'N'),

    ('Customer', 'CustomerContract', 'holds', '1', 'N'),
    ('CustomerContract', 'ContractAppendix', 'amended by', '1', 'N'),
    ('Customer', 'Quotation', 'gets', '1', 'N'),
    ('Customer', 'Invoice', 'billed', '1', 'N'),
    ('Invoice', 'InvoiceLine', 'contains', '1', 'N'),

    ('Customer', 'TransportOrder', 'places', '1', 'N'),
    ('TransportOrder', 'OrderDimension', 'described by', '1', '1'),
    ('TransportOrder', 'OutboundOrder', 'creates', '1', 'N'),
    ('OutboundOrder', 'OutboundOrderItem', 'contains', '1', 'N'),
    
    ('Location', 'GeoFence', 'defines', '1', 'N'),
    ('Location', 'Warehouse', 'located at', '1', '1'),
    ('Location', 'TransportOrder', 'origin/dest', '1', 'N'),
    
    ('Warehouse', 'WarehouseReceipt', 'recorded at', '1', 'N'),
    ('Warehouse', 'InboundAsn', 'receives', '1', 'N'),
    ('Warehouse', 'Lpn', 'stores', '1', 'N'),
    ('TransportOrder', 'Lpn', 'contains', '1', 'N'),
    ('Lpn', 'InboundReturnSlip', 'returns', '1', 'N'),
    ('Lpn', 'PenaltyBill', 'incurs', '1', 'N'),

    ('RouteMaster', 'Location', 'stops at', 'M', 'N'),
    ('RouteMaster', 'RouteStop', 'contains', '1', 'N'),
    ('RouteMaster', 'RouteSchedule', 'offers', '1', 'N'),
    ('RouteMaster', 'MasterTrip', 'followed by', '1', 'N'),

    ('MasterTrip', 'Vehicle', 'uses', 'N', '1'),
    ('MasterTrip', 'TripStop', 'contains', '1', 'N'),
    ('TripStop', 'TripStopEvent', 'records', '1', 'N'),
    ('MasterTrip', 'TransportDocument', 'documented', '1', 'N'),
    ('MasterTrip', 'Seal', 'secured by', '1', 'N'),
    ('MasterTrip', 'TripDriver', 'staffed by', '1', 'N'),
    ('Driver', 'TripDriver', 'assigned', '1', 'N'),

    ('Vehicle', 'VehicleDocument', 'has', '1', 'N'),
    ('Vehicle', 'MaintenanceTicket', 'maintained', '1', 'N'),
    ('Vehicle', 'VehicleOdometerLog', 'records', '1', 'N'),
    ('Vehicle', 'IotDevice', 'equipped', '1', '1'),

    ('Driver', 'DriverLicense', 'holds', '1', 'N'),
    ('Driver', 'DriverWorkLog', 'records', '1', 'N'),

    ('TripDriver', 'ExpenseAdvance', 'receives', '1', 'N'),
    ('ExpenseAdvance', 'ExpenseReceipt', 'cleared by', '1', 'N'),
    ('TripStop', 'DetentionCharge', 'incurs', '1', 'N'),

    ('MasterTrip', 'IncidentReport', 'experiences', '1', 'N'),
    ('IncidentReport', 'IncidentEvidence', 'supported by', '1', 'N'),
    ('TransportOrder', 'Claim', 'concerns', '1', 'N'),
    ('Claim', 'ClaimEvidence', 'supported by', '1', 'N'),

    ('IotDevice', 'TelemetryLog', 'produces', '1', 'N'),
    ('MasterTrip', 'AlertLog', 'triggers', '1', 'N'),

    ('Lpn', 'LpnDeliveryConfirmation', 'outcome', '1', '1'),
    ('MasterTrip', 'LpnDeliveryConfirmation', 'records', '1', 'N'),
    ('LpnDeliveryConfirmation', 'DeliveryEpod', 'signed by', '1', '1'),
    ('DeliveryEpod', 'ReturnedItem', 'records', '1', 'N'),
]

nodes = {}
edges = []

# Initialize nodes
for e, d in entities.items():
    nodes[e] = {
        'type': 'entity',
        'label': e,
        'domain': d,
        'x': domains[d]['center'][0] + random.uniform(-100, 100),
        'y': domains[d]['center'][1] + random.uniform(-100, 100),
        'vx': 0, 'vy': 0,
        'w': 140, 'h': 60
    }

for idx, r in enumerate(rels_def):
    src, tgt, name, c_src, c_tgt = r
    if src not in nodes or tgt not in nodes:
        continue
    rel_id = f'R_{idx}'
    # Diamond's domain is mid-point of src and tgt domains ideally, but let's assign to src's domain
    domain = entities[src]
    nodes[rel_id] = {
        'type': 'rel',
        'label': name,
        'domain': domain,
        'x': (nodes[src]['x'] + nodes[tgt]['x']) / 2 + random.uniform(-20, 20),
        'y': (nodes[src]['y'] + nodes[tgt]['y']) / 2 + random.uniform(-20, 20),
        'vx': 0, 'vy': 0,
        'w': 100, 'h': 60
    }
    edges.append({'source': src, 'target': rel_id, 'label': c_src})
    edges.append({'source': rel_id, 'target': tgt, 'label': c_tgt})

# Force-directed layout
ITERATIONS = 500
AREA = 3000 * 3000
k = math.sqrt(AREA / len(nodes)) * 1.5

for i in range(ITERATIONS):
    # Calculate repulsive forces
    for u_id, u in nodes.items():
        u['vx'] = 0
        u['vy'] = 0
        for v_id, v in nodes.items():
            if u_id != v_id:
                dx = u['x'] - v['x']
                dy = u['y'] - v['y']
                dist = math.hypot(dx, dy)
                if dist < 0.1: dist = 0.1
                if dist < 400:  # Only repel if close
                    repulse = (k * k) / dist
                    u['vx'] += (dx / dist) * repulse
                    u['vy'] += (dy / dist) * repulse

    # Calculate attractive forces (edges)
    for e in edges:
        u = nodes[e['source']]
        v = nodes[e['target']]
        dx = v['x'] - u['x']
        dy = v['y'] - u['y']
        dist = math.hypot(dx, dy)
        if dist < 0.1: dist = 0.1
        attract = (dist * dist) / k
        force_x = (dx / dist) * attract
        force_y = (dy / dist) * attract
        u['vx'] += force_x
        u['vy'] += force_y
        v['vx'] -= force_x
        v['vy'] -= force_y

    # Calculate attractive forces to domain centers
    for u_id, u in nodes.items():
        cx, cy = domains[u['domain']]['center']
        dx = cx - u['x']
        dy = cy - u['y']
        dist = math.hypot(dx, dy)
        if dist > 0.1:
            attract = dist / 2.0  # Spring to center
            u['vx'] += (dx / dist) * attract
            u['vy'] += (dy / dist) * attract

    # Apply forces
    temp = max(100 * (1 - i / ITERATIONS), 1)
    for u in nodes.values():
        disp = math.hypot(u['vx'], u['vy'])
        if disp > 0.1:
            u['x'] += (u['vx'] / disp) * min(disp, temp)
            u['y'] += (u['vy'] / disp) * min(disp, temp)

# Now generate XML
def create_cell(parent, id, value, x, y, w, h, style, type='vertex'):
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': f'<b>{value}</b>' if type == 'entity' else value,
        'style': style,
        'vertex': '1',
        'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(int(x)), 'y': str(int(y)), 'width': str(w), 'height': str(h), 'as': 'geometry'})
    return cell

def create_edge(parent, id, source, target, label):
    # Orthogonal edge routing helps minimize overlaps by avoiding entities!
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': label,
        'style': 'endArrow=none;html=1;rounded=0;edgeStyle=orthogonalEdgeStyle;labelBackgroundColor=#ffffff;fontStyle=1',
        'edge': '1',
        'parent': '1',
        'source': source,
        'target': target
    })
    ET.SubElement(cell, 'mxGeometry', {'relative': '1', 'as': 'geometry'})

tree = ET.parse('docs/Conceptual-Data-Model.drawio')
root = tree.getroot()
root.set('compressed', 'false')

diagrams = root.findall('diagram')
for d in diagrams:
    if d.attrib.get('name') == '0 - Overall Conceptual Data Model':
        root.remove(d)

new_diagram = ET.Element('diagram', {'id': 'page-0-overview', 'name': '0 - Overall Conceptual Data Model'})
graph_model = ET.SubElement(new_diagram, 'mxGraphModel', {
    'dx': '3000', 'dy': '3000', 'grid': '1', 'gridSize': '10', 'guides': '1',
    'tooltips': '1', 'connect': '1', 'arrows': '1', 'fold': '1', 'page': '1',
    'pageScale': '1', 'pageWidth': '3000', 'pageHeight': '3000', 'math': '0', 'shadow': '0'
})
root_el = ET.SubElement(graph_model, 'root')
ET.SubElement(root_el, 'mxCell', {'id': '0'})
ET.SubElement(root_el, 'mxCell', {'id': '1', 'parent': '0'})

# Create domain background areas
# We'll calculate the bounding box of each domain's nodes to draw the area
for d_name, d_info in domains.items():
    d_nodes = [n for n in nodes.values() if n['domain'] == d_name]
    if not d_nodes: continue
    min_x = min(n['x'] for n in d_nodes) - 100
    max_x = max(n['x'] + n['w'] for n in d_nodes) + 100
    min_y = min(n['y'] for n in d_nodes) - 100
    max_y = max(n['y'] + n['h'] for n in d_nodes) + 100
    
    cell = ET.SubElement(root_el, 'mxCell', {
        'id': f'area_{d_name}',
        'value': d_name,
        'style': 'rounded=1;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#aaaaaa;dashed=1;dashPattern=10 10;strokeWidth=2;align=left;verticalAlign=top;spacingLeft=10;spacingTop=10;fontColor=#666666;fontSize=24;fontStyle=1',
        'vertex': '1',
        'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(int(min_x)), 'y': str(int(min_y)), 'width': str(int(max_x - min_x)), 'height': str(int(max_y - min_y)), 'as': 'geometry'})


# Create Nodes
for nid, n in nodes.items():
    style = 'rounded=0;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;fontSize=14;' if n['type'] == 'entity' else 'rhombus;whiteSpace=wrap;html=1;fillColor=#f8cecc;strokeColor=#b85450;fontSize=12;'
    create_cell(root_el, nid, n['label'], n['x'], n['y'], n['w'], n['h'], style, n['type'])

# Create Edges
for idx, e in enumerate(edges):
    create_edge(root_el, f'edge_{idx}', e['source'], e['target'], e['label'])

root.insert(0, new_diagram)
tree.write('docs/Conceptual-Data-Model.drawio', encoding='utf-8', xml_declaration=True)
print("Updated drawio with Force Directed Layout successfully!")
